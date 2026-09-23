using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hamster;

public abstract record ClaudeEvent;

/// <param name="Detail">What the tool works on, e.g. the file it reads or the command it runs.</param>
public sealed record ToolUse(string Id, string Name, string Detail = "") : ClaudeEvent
{
    public bool IsWeb => Name is "WebSearch" or "WebFetch";

    public string Description => Detail.Length > 0 ? $"{Name}: {Detail.ReplaceLineEndings(" ")}" : Name;
}

public sealed record ToolResult(string ToolUseId) : ClaudeEvent;

public sealed record PermissionRequest(string RequestId, string ToolName, JsonObject Input) : ClaudeEvent
{
    public string Details => string.Join(Environment.NewLine, Input.Select(property => $"{property.Key}: {property.Value}"));
}

public sealed record CancelRequest(string RequestId) : ClaudeEvent;

/// <param name="Cost">Claude's estimate in USD, accumulated over the whole session.</param>
public sealed record ClaudeResult(string? SessionId, string Text, bool IsError, decimal? Cost = null) : ClaudeEvent;

/// <summary>Claude Code's stream-json protocol (one JSON object per line on stdin/stdout).</summary>
public static class ClaudeProtocol
{
    public static string[] Arguments(string? sessionId, string model, string effort, string permissionMode) =>
    [
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
        "--permission-prompt-tool", "stdio", "--permission-mode", permissionMode,
        // Only the user's own settings: an allow rule or hook written into the workspace must not silence the permission bubbles.
        "--setting-sources", "user",
        "--model", model, "--effort", effort,
        .. (sessionId is null ? Array.Empty<string>() : ["--resume", sessionId]),
        // A fixed list, so nothing runs without a permission bubble: the tools that ask first, and the ones that only read or list.
        // Left out: Skill (a skill's allowed-tools skip the bubble), the tools that act without asking (CronCreate, CronDelete,
        // ScheduleWakeup, PushNotification, RemoteTrigger, SendMessage), and AskUserQuestion, which the pet has no UI for.
        "--tools", "Read,Glob,Grep,Bash,PowerShell,Edit,Write,NotebookEdit,WebSearch,WebFetch,Agent,Monitor,ToolSearch,EnterPlanMode,ExitPlanMode,EnterWorktree,ExitWorktree,Workflow,TaskStop,ListAgents,CronList,ReportFindings,ShareOnboardingGuide",
    ];

    // The input field that says most about what a tool does, in order of preference.
    static readonly string[] DetailFields = ["file_path", "notebook_path", "command", "url", "query", "pattern", "description"];

    public static IEnumerable<ClaudeEvent> Parse(string line)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return [];
        }
        if (message is null)
            return [];

        return (string?)message["type"] switch
        {
            "assistant" => ContentBlocks(message, "tool_use").Select(block => new ToolUse((string)block["id"]!, (string)block["name"]!, Detail(block["input"]))),
            "user" => ContentBlocks(message, "tool_result").Select(block => new ToolResult((string)block["tool_use_id"]!)),
            "control_request" when (string?)message["request"]?["subtype"] == "can_use_tool" => [ToPermissionRequest(message)],
            "control_cancel_request" => [new CancelRequest((string)message["request_id"]!)],
            "result" => [new ClaudeResult(
                (string?)message["session_id"],
                (string?)message["result"] ?? (string?)message["subtype"] ?? "",
                (bool?)message["is_error"] ?? false,
                (decimal?)message["total_cost_usd"])],
            _ => [],
        };
    }

    public static string UserMessage(string prompt, IReadOnlyList<ImageAttachment> images) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = Content(prompt, images) },
        ["parent_tool_use_id"] = null,
        ["session_id"] = "",
    }.ToJsonString();

    static JsonNode Content(string prompt, IReadOnlyList<ImageAttachment> images) =>
        images.Count == 0
            ? JsonValue.Create(prompt)
            : new JsonArray(
            [
                new JsonObject { ["type"] = "text", ["text"] = prompt },
                .. images.Select(image => new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data) },
                }),
            ]);

    /// <summary>Asks claude to stop the turn; it still sends the result, with what the turn cost so far.</summary>
    public const string Interrupt = """{"type":"control_request","request_id":"interrupt","request":{"subtype":"interrupt"}}""";

    public static string Allow(PermissionRequest request) =>
        Response(request.RequestId, new JsonObject { ["behavior"] = "allow", ["updatedInput"] = request.Input.DeepClone() });

    public static string Deny(PermissionRequest request) =>
        Response(request.RequestId, new JsonObject { ["behavior"] = "deny", ["message"] = "Brugeren afviste." });

    static string Response(string requestId, JsonObject body) => new JsonObject
    {
        ["type"] = "control_response",
        ["response"] = new JsonObject { ["subtype"] = "success", ["request_id"] = requestId, ["response"] = body },
    }.ToJsonString();

    static IEnumerable<JsonNode> ContentBlocks(JsonNode message, string type) =>
        message["message"]?["content"] is JsonArray content
            ? content.OfType<JsonNode>().Where(block => (string?)block["type"] == type)
            : [];

    static string Detail(JsonNode? input) =>
        DetailFields.Select(field => input is JsonObject fields && fields[field] is JsonValue value && value.TryGetValue(out string? text) ? text : null)
            .FirstOrDefault(text => text is not null) ?? "";

    static PermissionRequest ToPermissionRequest(JsonNode message)
    {
        var request = message["request"]!;
        return new PermissionRequest(
            (string)message["request_id"]!,
            (string?)request["display_name"] ?? (string)request["tool_name"]!,
            request["input"]?.DeepClone() as JsonObject ?? new JsonObject());
    }
}
