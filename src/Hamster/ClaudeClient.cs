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
    void UsageReported(Usage usage);
}

public interface IClaudeClient
{
    Task<ClaudeResult> SendAsync(string prompt, IReadOnlyList<ImageAttachment> images, string? sessionId, IClaudeListener listener, CancellationToken cancellationToken);
}

public sealed class ClaudeClient(string workspace, string instructionsFile, JsonFile<ClaudeSettings> store) : IClaudeClient
{
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public ClaudeSettings Settings
    {
        get;
        set
        {
            field = value;
            store.Save(value);
        }
    } = store.Load();

    public async Task<ClaudeResult> SendAsync(string prompt, IReadOnlyList<ImageAttachment> images, string? sessionId, IClaudeListener listener, CancellationToken cancellationToken)
    {
        var instructions = File.Exists(instructionsFile) ? await File.ReadAllTextAsync(instructionsFile, CancellationToken.None) : "";
        using var process = Start(sessionId, instructions);
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
        await Task.Run(() => Send(input, ClaudeProtocol.UserMessage(prompt, images)));
        using var interrupt = cancellationToken.Register(() => Interrupt(input));
        try
        {
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
                            _ = AnswerAsync(request, listener, input, pending, withdrawal.Token);
                            break;
                        case CancelRequest cancel when pending.TryRemove(cancel.RequestId, out var withdrawn):
                            withdrawn.Cancel();
                            break;
                        case Usage usage:
                            listener.UsageReported(usage);
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
        }
    }

    static void Send(TextWriter input, string json)
    {
        input.Write(json + '\n');
        input.Flush();
    }

    Process Start(string? sessionId, string instructions)
    {
        Directory.CreateDirectory(workspace);
        var arguments = ClaudeProtocol.Arguments(sessionId, Settings, instructions);
        return Process.Start(new ProcessStartInfo("claude", arguments)
        {
            WorkingDirectory = Settings.WorkingDirectory ?? workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment = { ["CLAUDE_CODE_DISABLE_BACKGROUND_TASKS"] = "1" },
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
        }
    }
}
