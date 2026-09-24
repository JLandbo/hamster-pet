using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hamster;

public abstract record ClaudeEvent;

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

public sealed record ClaudeResult(string? SessionId, string Text, bool IsError, decimal? Cost = null) : ClaudeEvent
{
    public bool NeedsLogin => IsError && Text.Contains("/login");
}

public sealed record Usage(double FiveHour, double SevenDay) : ClaudeEvent;

public static class ClaudeProtocol
{
    public const string PetInstructions = "Appen viser selv de kilder, du har søgt i og hentet. Skriv derfor ikke en kilde- eller kildeliste-sektion i svaret. Nævn ikke MCP-servere eller connectors, der mangler godkendelse, medmindre brugeren beder om noget, der kræver dem.";

    public static string[] Arguments(string? sessionId, ClaudeSettings settings, string instructions = "") =>
    [
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
        "--permission-prompt-tool", "stdio", "--permission-mode", settings.PermissionMode,
        "--setting-sources", "user",
        "--settings", """{"permissions":{"ask":["Skill"]}}""",
        "--model", settings.Model, "--effort", settings.Effort,
        .. (sessionId is null ? Array.Empty<string>() : ["--resume", sessionId]),
        "--append-system-prompt", string.IsNullOrWhiteSpace(instructions) ? PetInstructions : $"{PetInstructions}\n\n{instructions}",
        "--tools", "Read,Glob,Grep,Bash,PowerShell,Edit,Write,NotebookEdit,WebSearch,WebFetch,Agent,ToolSearch,EnterPlanMode,ExitPlanMode,Workflow,TaskStop,ListAgents,CronList,ReportFindings,Skill",
    ];

    static readonly string[] DetailFields = ["file_path", "notebook_path", "command", "url", "query", "pattern", "skill", "description"];

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
            "rate_limit_event" => UsageOf(message["rate_limit_info"]?["unifiedWindows"]),
            "result" => [new ClaudeResult(
                (string?)message["session_id"],
                ResultText(message),
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

    static string ResultText(JsonNode message) =>
        (string?)message["result"]
        ?? (message["errors"] is JsonArray { Count: > 0 } errors ? string.Join('\n', errors.Select(error => (string?)error)) : null)
        ?? (string?)message["subtype"]
        ?? "";

    static IEnumerable<ClaudeEvent> UsageOf(JsonNode? windows) =>
        (double?)windows?["five_hour"]?["utilization"] is { } fiveHour && (double?)windows?["seven_day"]?["utilization"] is { } sevenDay
            ? [new Usage(fiveHour, sevenDay)]
            : [];

    static PermissionRequest ToPermissionRequest(JsonNode message)
    {
        var request = message["request"]!;
        return new PermissionRequest(
            (string)message["request_id"]!,
            (string?)request["display_name"] ?? (string)request["tool_name"]!,
            request["input"]?.DeepClone() as JsonObject ?? new JsonObject());
    }
}
