using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hamster.Core.Connections;
using Hamster.Core.Languages;

namespace Hamster.Core.Claude;

public sealed partial class ClaudeFetcher(string workspace)
{
    const string _model = "claude-haiku-4-5-20251001";
    const string _instructions = "Du kalder præcis de værktøjer, brugeren beder om, med præcis de angivne argumenter, og svarer derefter kun ok.";
    static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan _answerTimeout = TimeSpan.FromMinutes(2);
    static readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan _errorWait = TimeSpan.FromSeconds(2);
    static string Stopped => Strings.Of("Claude.Stopped");

    public async Task<IReadOnlyList<(ToolCall Call, string Text)>> CallAsync(Connector connector, string serverName, IReadOnlyList<ToolCall> calls)
    {
        Directory.CreateDirectory(workspace);
        using var process = ClaudeClient.StartProcess(Arguments(connector, serverName, calls), workspace);
        var errors = process.StandardError.ReadToEndAsync();
        var results = new ToolResults(calls, ToolPrefix(serverName));
        var session = new ClaudeSession(process.StandardOutput, process.StandardInput, results);
        var reading = session.ReadAsync();
        try
        {
            await WaitUntilConnectedAsync(session, serverName);
            await session.SendAsync(Guid.NewGuid().ToString(), Prompt(calls), []);
            return await Task.WhenAny(results.Done, reading).WaitAsync(_answerTimeout) == results.Done
                ? [.. (await results.Done).Select(result => (result.Call, FullText(result.Text)))]
                : throw new InvalidOperationException(await ReasonAsync(errors));
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(Strings.Of("Claude.NoAnswer"));
        }
        catch (OperationCanceledException) when (reading.IsCompleted)
        {
            throw new InvalidOperationException(await ReasonAsync(errors));
        }
        finally
        {
            session.Detach();
            ClaudeClient.TryKill(process);
        }
    }

    public static async Task<string> ReasonAsync(Task<string> errors)
    {
        try
        {
            return (await errors.WaitAsync(_errorWait)).Trim() is { Length: > 0 } error ? error : Stopped;
        }
        catch (TimeoutException)
        {
            return Stopped;
        }
    }

    public static string[] Arguments(Connector connector, string serverName, IReadOnlyList<ToolCall> calls) => [
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
        "--model", _model, "--setting-sources", "user", "--no-session-persistence",
        "--settings", new JsonObject
        {
            ["allowedMcpServers"] = new JsonArray(new JsonObject { ["serverUrl"] = connector.ServerUrlPattern }),
            ["deniedMcpServers"] = new JsonArray(new JsonObject { ["serverName"] = connector.Name }),
        }.ToJsonString(),
        "--tools", "", "--system-prompt", _instructions,
        "--allowedTools", string.Join(",", calls.Select(call => ToolPrefix(serverName) + call.Name).Distinct()),
    ];

    public static string ToolPrefix(string serverName) => $"mcp__{NotAllowedInToolNames().Replace(serverName, "_")}__";

    public static string FullText(string text) => !text.TrimStart().StartsWith('{') && SavedFile().Match(text) is { Success: true } match && File.Exists(match.Groups[1].Value)
            ? File.ReadAllText(match.Groups[1].Value)
            : text;

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex NotAllowedInToolNames();

    [GeneratedRegex(@"saved to:? (.+?\.txt)")]
    private static partial Regex SavedFile();

    static string Prompt(IReadOnlyList<ToolCall> calls)
    {
        return $"Kald disse værktøjer med præcis disse argumenter:\n{string.Join('\n', calls.Select(call => $"- {call.Name}: {call.Arguments.ToJsonString()}"))}";
    }

    static async Task WaitUntilConnectedAsync(ClaudeSession session, string serverName)
    {
        var deadline = DateTime.UtcNow + _connectTimeout;
        while (true)
        {
            var server = ClaudeProtocol.McpServers(await session.RequestAsync(ClaudeProtocol.McpStatus(), _connectTimeout)).FirstOrDefault(server => server.Name == serverName);
            if (server?.Status == "connected")
            {
                return;
            }
            if (server?.Status is "failed" or "needs-auth")
            {
                throw new InvalidOperationException($"{serverName}: {Connector.StateOf(server)}");
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException(Strings.Format("Claude.NotConnected", serverName));
            }
            await Task.Delay(_checkInterval);
        }
    }

    public sealed class ToolResults(IReadOnlyList<ToolCall> calls, string prefix) : IClaudeListener
    {
        readonly Dictionary<string, ToolUse> _started = [];
        readonly List<(ToolUse Tool, string Text)> _finished = [];
        readonly TaskCompletionSource<IReadOnlyList<(ToolCall Call, string Text)>> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<(ToolCall Call, string Text)>> Done => _done.Task;

        public void ToolStarted(ToolUse tool)
        {
            if (calls.Any(call => prefix + call.Name == tool.Name))
            {
                _started[tool.Id] = tool;
            }
        }

        public void ToolFinished(ToolResult result)
        {
            if (!_started.Remove(result.ToolUseId, out var tool))
            {
                return;
            }
            if (result.IsError && FullText(result.Text) == result.Text)
            {
                _done.TrySetException(new InvalidOperationException(McpClient.MessageOf(result.Text)));
                return;
            }
            _finished.Add((tool, result.Text));
            var matched = Matched();
            if (matched.Count == calls.Count)
            {
                _done.TrySetResult(matched);
            }
        }

        public void ResultReceived(ClaudeResult result)
        {
            var matched = Matched();
            if (matched.Count < calls.Count)
            {
                _done.TrySetException(new InvalidOperationException(result.IsError && result.Text.Length > 0 ? result.Text : Strings.Of("Claude.NoToolCalls")));
            }
            else
            {
                _done.TrySetResult(matched);
            }
        }

        List<(ToolCall Call, string Text)> Matched()
        {
            List<ToolCall> open = [.. calls];
            List<(ToolCall Call, string Text)> matched = [];
            List<(ToolUse Tool, string Text)> unknown = [];
            foreach (var (tool, text) in _finished)
            {
                var arguments = tool.Input is null ? null : JsonNode.Parse(tool.Input);
                if (open.FirstOrDefault(call => prefix + call.Name == tool.Name && JsonNode.DeepEquals(call.Arguments, arguments)) is { } call)
                {
                    open.Remove(call);
                    matched.Add((call, text));
                }
                else if (!calls.Any(call => JsonNode.DeepEquals(call.Arguments, arguments)))
                {
                    unknown.Add((tool, text));
                }
            }
            foreach (var group in unknown.GroupBy(result => result.Tool.Name))
            {
                if (group.Count() == 1 && open.Where(call => prefix + call.Name == group.Key).ToArray() is [var only])
                {
                    open.Remove(only);
                    matched.Add((only, group.Single().Text));
                }
            }
            return matched;
        }

        public Task<PermissionAnswer> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken) => Task.FromResult(PermissionAnswer.Deny);

        public void TurnStarted(string? messageId)
        {
        }

        public void PartialMessageStarted(string? messageId)
        {
        }

        public void PartialMessageReceived(string text)
        {
        }

        public void UsageReported(Usage usage)
        {
        }

        public void ModeChanged(string mode)
        {
        }

        public void BackgroundTasksChanged(int count)
        {
        }

        public void Exited(string error)
        {
        }
    }
}
