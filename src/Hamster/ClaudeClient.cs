using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hamster;

public interface IClaudeListener
{
    void ToolStarted(ToolUse tool);
    void ToolFinished(ToolResult result);
    Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken);
}

public interface IClaudeClient
{
    Task<ClaudeResult> SendAsync(string prompt, IReadOnlyList<ImageAttachment> images, string? sessionId, IClaudeListener listener, CancellationToken cancellationToken);
}

public sealed class ClaudeClient(string workingDirectory) : IClaudeClient
{
    // Full model id rather than the "opus" alias, so a newer Opus doesn't replace it silently.
    public const string DefaultModel = "claude-opus-5-5";
    public const string DefaultEffort = "xhigh";
    public const string DefaultPermissionMode = "default";
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public string Model { get; set; } = DefaultModel;
    public string Effort { get; set; } = DefaultEffort;
    public string PermissionMode { get; set; } = DefaultPermissionMode;

    public async Task<ClaudeResult> SendAsync(string prompt, IReadOnlyList<ImageAttachment> images, string? sessionId, IClaudeListener listener, CancellationToken cancellationToken)
    {
        using var process = Start(sessionId);
        // Killed only if it doesn't stop when asked to: killing right away would lose what the stopped turn cost.
        using var killer = new CancellationTokenSource();
        using var kill = killer.Token.Register(() => TryKill(process));
        using var stop = cancellationToken.Register(() => killer.CancelAfter(StopTimeout));
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        ClaudeResult? result;
        try
        {
            result = await ConverseAsync(process.StandardOutput, process.StandardInput, prompt, images, listener, cancellationToken);
        }
        finally
        {
            // claude keeps waiting for input until stdin closes, also when something above failed.
            process.StandardInput.Close();
        }
        await process.WaitForExitAsync();
        if (result is not null)
            return result;

        var error = (await errors).Trim();
        return new ClaudeResult(SessionId: null, error.Length > 0 ? error : $"claude stoppede uventet (exit code {process.ExitCode}).", IsError: true);
    }

    public static async Task<ClaudeResult?> ConverseAsync(TextReader output, TextWriter input, string prompt, IReadOnlyList<ImageAttachment> images,
        IClaudeListener listener, CancellationToken cancellationToken)
    {
        input = TextWriter.Synchronized(input);
        var pending = new ConcurrentDictionary<string, CancellationTokenSource>();
        // Off the UI thread: with images the message is large, and the write blocks until claude reads it.
        await Task.Run(() => Send(input, ClaudeProtocol.UserMessage(prompt, images)));
        using var interrupt = cancellationToken.Register(() => Interrupt(input));
        try
        {
            // No token: after Stop, claude's result still comes and carries the turn's cost.
            while (await output.ReadLineAsync() is { } line)
            {
                foreach (var message in ClaudeProtocol.Parse(line))
                {
                    switch (message)
                    {
                        case ToolUse tool:
                            listener.ToolStarted(tool);
                            break;
                        case ToolResult toolResult:
                            listener.ToolFinished(toolResult);
                            break;
                        case PermissionRequest request:
                            var withdrawal = pending[request.RequestId] = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            // Not awaited: Claude may withdraw the request while the user is still deciding.
                            _ = AnswerAsync(request, listener, input, pending, withdrawal.Token);
                            break;
                        case CancelRequest cancel when pending.TryRemove(cancel.RequestId, out var withdrawn):
                            withdrawn.Cancel();
                            break;
                        case ClaudeResult result:
                            return result;
                    }
                }
            }
            return null;
        }
        finally
        {
            foreach (var unanswered in pending.Values)
                unanswered.Cancel();
        }
    }

    static async Task AnswerAsync(PermissionRequest request, IClaudeListener listener, TextWriter input,
        ConcurrentDictionary<string, CancellationTokenSource> pending, CancellationToken cancellationToken)
    {
        try
        {
            var allowed = await listener.AskPermissionAsync(request, cancellationToken);
            if (pending.TryRemove(request.RequestId, out _))
                Send(input, allowed ? ClaudeProtocol.Allow(request) : ClaudeProtocol.Deny(request));
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Withdrawn, cancelled, or claude already exited - nobody is waiting for the answer.
        }
    }

    static void Interrupt(TextWriter input)
    {
        try
        {
            Send(input, ClaudeProtocol.Interrupt);
        }
        catch (IOException)
        {
            // claude already exited.
        }
    }

    // One Write per message, so the synchronized writer never interleaves two messages.
    static void Send(TextWriter input, string json)
    {
        input.Write(json + '\n');
        input.Flush();
    }

    Process Start(string? sessionId)
    {
        Directory.CreateDirectory(workingDirectory);
        return Process.Start(new ProcessStartInfo("claude", ClaudeProtocol.Arguments(sessionId, Model, Effort, PermissionMode))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Encoding.UTF8 would prefix stdin with a BOM and break claude's first JSON line.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        })!;
    }

    static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Already exited.
        }
    }
}
