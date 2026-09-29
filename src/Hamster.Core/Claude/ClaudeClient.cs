using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Hamster.Core.Chats;
using Hamster.Core.Languages;
using Hamster.Core.Storage;

namespace Hamster.Core.Claude;

public interface IClaudeListener
{
    void TurnStarted(string? messageId);
    void ToolStarted(ToolUse tool);
    void ToolFinished(ToolResult result);
    Task<PermissionAnswer> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken);
    void UsageReported(Usage usage);
    void ModeChanged(string mode);
    void BackgroundTasksChanged(int count);
    void ResultReceived(ClaudeResult result);
    void Exited(string error);
}

public interface IClaudeClient
{
    ClaudeSettings Settings { get; set; }
    bool IsRunning { get; }
    Task StartAsync(string? sessionId, IClaudeListener listener);
    Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images);
    Task<JsonObject?> RequestAsync(JsonObject request);
    void Interrupt();
    void Withdraw(string id);
    void End();
}

public sealed class ClaudeClient(string workspace, string instructionsFile, JsonFile<ClaudeSettings> store) : IClaudeClient
{
    static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan _requestTimeout = TimeSpan.FromMinutes(1);

    (Task<Process?> Starting, ClaudeSession Session)? _current;

    public ClaudeSettings Settings
    {
        get;
        set
        {
            field = value;
            store.Save(value);
        }
    } = store.Load();

    public void Choose(ClaudeSettings value, string control)
    {
        Settings = value;
        if (_current is var (_, session))
        {
            _ = session.SendAsync(control);
        }
    }

    internal void Attach(ClaudeSession session) => _current = (Task.FromResult<Process?>(null), session);

    public bool IsRunning => _current is not null;

    public Task StartAsync(string? sessionId, IClaudeListener listener)
    {
        End();
        var starting = Task.Run<Process?>(() => Start(sessionId));
        var session = new ClaudeSession(StreamsAsync(starting), listener);
        _current = (starting, session);
        _ = RunAsync(starting, session, listener);
        return starting;
    }

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) => _current is var (_, session)
        ? session.SendAsync(id, prompt, images)
        : throw new InvalidOperationException(Strings.Of("Claude.NotRunning"));

    public Task<JsonObject?> RequestAsync(JsonObject request) => _current is var (_, session)
        ? session.RequestAsync(request, _requestTimeout)
        : throw new InvalidOperationException(Strings.Of("Claude.NotRunning"));

    public void Interrupt()
    {
        if (_current is not var (starting, session))
        {
            return;
        }
        var results = session.Results;
        _ = session.SendAsync(ClaudeProtocol.Interrupt());
        _ = KillAfterAsync(starting, () => session.Results == results);
    }

    public void Withdraw(string id)
    {
        if (_current is var (_, session))
        {
            _ = session.SendAsync(ClaudeProtocol.Withdraw(id));
        }
    }

    public void End()
    {
        if (_current is not var (starting, session))
        {
            return;
        }
        _current = null;
        session.Detach();
        _ = session.SendAsync(ClaudeProtocol.EndSession());
        _ = session.CloseAsync();
        _ = KillAfterAsync(starting, () => true);
    }

    async Task RunAsync(Task<Process?> starting, ClaudeSession session, IClaudeListener listener)
    {
        Process process;
        try
        {
            process = (await starting)!;
        }
        catch (Exception)
        {
            if (_current?.Starting == starting)
            {
                _current = null;
            }
            return;
        }
        using (process)
        {
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                await session.ReadAsync();
            }
            catch (Exception)
            {
                TryKill(process);
            }
            await process.WaitForExitAsync();
            var error = (await errors).Trim();
            if (_current?.Starting != starting)
            {
                return;
            }
            _current = null;
            listener.Exited(error.Length > 0 ? error : Strings.Format("Claude.StoppedWithExitCode", process.ExitCode));
        }
    }

    static async Task<(TextReader Output, TextWriter Input)> StreamsAsync(Task<Process?> starting)
    {
        var process = (await starting)!;
        return (process.StandardOutput, process.StandardInput);
    }

    static async Task KillAfterAsync(Task<Process?> starting, Func<bool> stuck)
    {
        await Task.Delay(_stopTimeout);
        if (stuck() && starting is { IsCompletedSuccessfully: true, Result: { } process })
        {
            TryKill(process);
        }
    }

    Process Start(string? sessionId)
    {
        Directory.CreateDirectory(workspace);
        var instructions = File.Exists(instructionsFile) ? File.ReadAllText(instructionsFile) : "";
        var arguments = ClaudeProtocol.Arguments(sessionId, Settings, instructions);
        return StartProcess(arguments, Settings.WorkingDirectory ?? workspace);
    }

    internal static Process StartProcess(IReadOnlyList<string> arguments, string workingDirectory)
    {
        return Process.Start(new ProcessStartInfo("claude", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        })!;
    }

    public static async Task RunCommandAsync(IReadOnlyList<string> arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("claude", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException((await errors).Trim() is { Length: > 0 } error ? error : (await output).Trim());
        }
    }

    internal static void TryKill(Process process)
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
