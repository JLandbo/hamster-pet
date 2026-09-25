using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Hamster;

public interface IClaudeListener
{
    void TurnStarted(string? messageId);
    void ToolStarted(ToolUse tool);
    void ToolFinished(ToolResult result);
    Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken);
    void UsageReported(Usage usage);
    void ModeChanged(string mode);
    void BackgroundTasksChanged(int count);
    void ResultReceived(ClaudeResult result);
    void Exited(string error);
}

public interface IClaudeClient
{
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
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(1);

    (Process Process, ClaudeSession Session)? current;
    int starts;
    ClaudeSettings settings = store.Load();

    public ClaudeSettings Settings
    {
        get => settings;
        set
        {
            var old = settings;
            Remember(value);
            if (current is var (_, session))
                foreach (var change in ClaudeProtocol.Changes(old, value))
                    TrySend(session, change);
        }
    }

    public void Remember(ClaudeSettings value)
    {
        settings = value;
        store.Save(value);
    }

    public bool IsRunning => current is not null;

    public async Task StartAsync(string? sessionId, IClaudeListener listener)
    {
        End();
        var start = ++starts;
        var instructions = File.Exists(instructionsFile) ? await File.ReadAllTextAsync(instructionsFile) : "";
        if (start != starts)
            return;
        var process = Start(sessionId, instructions);
        var session = new ClaudeSession(process.StandardOutput, process.StandardInput, listener);
        current = (process, session);
        _ = RunAsync(process, session, listener);
    }

    public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) =>
        current is var (_, session) ? session.SendAsync(id, prompt, images) : throw new InvalidOperationException("claude kører ikke.");

    public Task<JsonObject?> RequestAsync(JsonObject request) =>
        current is var (_, session) ? session.RequestAsync(request, RequestTimeout) : throw new InvalidOperationException("claude kører ikke.");

    public void Interrupt()
    {
        if (current is not var (process, session))
            return;
        var results = session.Results;
        TrySend(session, ClaudeProtocol.Interrupt());
        _ = KillAfterAsync(process, () => session.Results == results);
    }

    public void Withdraw(string id)
    {
        if (current is var (_, session))
            TrySend(session, ClaudeProtocol.Withdraw(id));
    }

    public void End()
    {
        if (current is not var (process, session))
            return;
        current = null;
        session.Detach();
        TrySend(session, ClaudeProtocol.EndSession());
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        _ = KillAfterAsync(process, () => true);
    }

    async Task RunAsync(Process process, ClaudeSession session, IClaudeListener listener)
    {
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
            if (current?.Process != process)
                return;
            current = null;
            listener.Exited(error.Length > 0 ? error : $"claude stoppede uventet (exit code {process.ExitCode}).");
        }
    }

    static async Task KillAfterAsync(Process process, Func<bool> stuck)
    {
        await Task.Delay(StopTimeout);
        if (stuck())
            TryKill(process);
    }

    static void TrySend(ClaudeSession session, string json)
    {
        try
        {
            session.Send(json);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
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
