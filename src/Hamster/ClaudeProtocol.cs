using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hamster;

public abstract record ClaudeEvent;

public sealed record ToolUse(string Id, string Name, string Detail = "", string? ParentId = null, string? Input = null) : ClaudeEvent
{
    public bool IsWeb => Name is "WebSearch" or "WebFetch";

    public string Description => Detail.Length > 0 ? $"{Name}: {Detail.ReplaceLineEndings(" ")}" : Name;
}

public sealed record ToolResult(string ToolUseId, string Text = "", bool IsError = false) : ClaudeEvent;

public sealed record PermissionRequest(string RequestId, string ToolName, JsonObject Input, string? ToolUseId = null, JsonArray? Suggestions = null) : ClaudeEvent
{
    public string Details => string.Join(Environment.NewLine, Input.Select(property => $"{property.Key}: {property.Value}"));

    public string? AlwaysScope => Suggestions is { Count: > 0 } suggestions ? string.Join(", ", suggestions.OfType<JsonObject>().SelectMany(Scope)) : null;

    static IEnumerable<string> Scope(JsonObject suggestion) =>
        suggestion["rules"] is JsonArray rules
            ? rules.OfType<JsonObject>().Select(rule => (string?)rule["ruleContent"] is { } content ? $"{(string?)rule["toolName"]}({content})" : (string?)rule["toolName"] ?? "")
            : suggestion["directories"] is JsonArray directories
                ? directories.Select(directory => $"mappen {(string?)directory}")
                : [];
}

public sealed record CancelRequest(string RequestId) : ClaudeEvent;

public sealed record TurnStarted(string? MessageId) : ClaudeEvent;

public sealed record ModeChanged(string Mode) : ClaudeEvent;

public sealed record BackgroundTasksChanged(int Count) : ClaudeEvent;

public sealed record ControlReply(string RequestId, JsonObject? Response, string? Error) : ClaudeEvent;

public sealed record McpServer(string Name, string Status, string? Url, string? Error, bool FromClaudeAi = false)
{
    public bool IsUsable => Status is "connected" or "pending" or "disabled";
}

public sealed record ClaudeResult(string? SessionId, string Text, bool IsError, decimal? Cost = null, IReadOnlyList<string>? Answers = null) : ClaudeEvent
{
    public bool NeedsLogin => IsError && Text.Contains("/login");
}

public sealed record Usage(double FiveHour, double SevenDay) : ClaudeEvent;

public static class ClaudeProtocol
{
    public const string PetInstructions = "Appen viser selv de kilder, du har søgt i og hentet. Skriv derfor ikke en kilde- eller kildeliste-sektion i svaret. Nævn ikke MCP-servere eller connectors, der mangler godkendelse, medmindre brugeren beder om noget, der kræver dem. Når brugeren beder om en påmindelse, så brug CronCreate med en prompt, der beder dig om kun at skrive påmindelsen til brugeren, når den affyres.";

    public static string[] Arguments(string? sessionId, ClaudeSettings settings, string instructions = "") =>
    [
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
        "--permission-prompt-tool", "stdio", "--permission-mode", settings.PermissionMode,
        "--setting-sources", "user",
        "--settings", SettingsJson(settings),
        "--model", settings.Model, "--effort", settings.Effort,
        .. (sessionId is null ? Array.Empty<string>() : ["--resume", sessionId]),
        "--append-system-prompt", string.IsNullOrWhiteSpace(instructions) ? PetInstructions : $"{PetInstructions}\n\n{instructions}",
        "--tools", "Read,Glob,Grep,Bash,PowerShell,Edit,Write,NotebookEdit,WebSearch,WebFetch,Agent,ToolSearch,EnterPlanMode,ExitPlanMode,Workflow,TaskStop,ListAgents,CronList,CronCreate,CronDelete,ReportFindings,Skill",
    ];

    static string SettingsJson(ClaudeSettings settings) => new JsonObject
    {
        ["permissions"] = new JsonObject { ["ask"] = new JsonArray("Skill", "CronCreate") },
        ["allowedMcpServers"] = Connectors.AllowedServers(),
        ["deniedMcpServers"] = Connectors.DeniedServers(settings),
    }.ToJsonString();

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
        if (message is not JsonObject)
            return [];

        return (string?)message["type"] switch
        {
            "assistant" => ContentBlocks(message, "tool_use").Select(block =>
                new ToolUse((string)block["id"]!, (string)block["name"]!, Detail(block["input"]), (string?)message["parent_tool_use_id"], block["input"]?.ToJsonString())),
            "user" => ContentBlocks(message, "tool_result").Select(block => new ToolResult((string)block["tool_use_id"]!, ContentText(block["content"]), (bool?)block["is_error"] == true)),
            "command_lifecycle" when (string?)message["state"] == "started" => [new TurnStarted((string?)message["command_uuid"])],
            "system" when (string?)message["subtype"] == "init" => [new TurnStarted(null)],
            "system" when (string?)message["subtype"] == "status" && (string?)message["permissionMode"] is { } mode => [new ModeChanged(mode)],
            "system" when (string?)message["subtype"] == "background_tasks_changed" && message["tasks"] is JsonArray tasks => [new BackgroundTasksChanged(tasks.Count)],
            "control_request" when (string?)message["request"]?["subtype"] == "can_use_tool" => [ToPermissionRequest(message)],
            "control_cancel_request" => [new CancelRequest((string)message["request_id"]!)],
            "control_response" when message["response"] is JsonObject response && (string?)response["request_id"] is { } id =>
                [new ControlReply(id, response["response"] as JsonObject, (string?)response["subtype"] == "error" ? (string?)response["error"] ?? "" : null)],
            "rate_limit_event" => UsageOf(message["rate_limit_info"]?["unifiedWindows"]),
            "result" => [new ClaudeResult(
                (string?)message["session_id"],
                ResultText(message),
                (bool?)message["is_error"] ?? false,
                (decimal?)message["total_cost_usd"],
                Answers(message))],
            _ => [],
        };
    }

    public static string UserMessage(string prompt, IReadOnlyList<ImageAttachment> images, string id) => new JsonObject
    {
        ["type"] = "user",
        ["uuid"] = id,
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

    public static string Interrupt() => Control(new JsonObject { ["subtype"] = "interrupt" });

    public static string EndSession() => Control(new JsonObject { ["subtype"] = "end_session" });

    public static string Withdraw(string id) => Control(new JsonObject { ["subtype"] = "cancel_async_message", ["message_uuid"] = id });

    public static JsonObject McpStatus() => new() { ["subtype"] = "mcp_status" };

    public static JsonObject McpToggle(string serverName, bool enabled) => new() { ["subtype"] = "mcp_toggle", ["serverName"] = serverName, ["enabled"] = enabled };

    public static IReadOnlyList<McpServer> McpServers(JsonObject? status) =>
        status?["mcpServers"] is JsonArray servers
            ? [.. servers.OfType<JsonObject>().Where(server => (string?)server["name"] is not null).Select(server => new McpServer(
                (string)server["name"]!,
                (string?)server["status"] ?? "",
                (string?)server["config"]?["url"],
                (string?)server["error"],
                (string?)server["source"] == "claudeai"))]
            : [];

    public static string SetModel(string model) => Control(new JsonObject { ["subtype"] = "set_model", ["model"] = model });

    public static string SetEffort(string effort) => Control(new JsonObject { ["subtype"] = "apply_flag_settings", ["settings"] = new JsonObject { ["effortLevel"] = effort } });

    public static string SetPermissionMode(string mode) => Control(new JsonObject { ["subtype"] = "set_permission_mode", ["mode"] = mode });

    public static string Allow(PermissionRequest request, bool always = false)
    {
        var body = new JsonObject { ["behavior"] = "allow", ["updatedInput"] = request.Input.DeepClone() };
        if (always && request.Suggestions is { } suggestions)
            body["updatedPermissions"] = new JsonArray([.. suggestions.OfType<JsonObject>().Select(ForThisSession)]);
        return Response(request.RequestId, body);
    }

    static JsonNode ForThisSession(JsonObject suggestion)
    {
        var copy = suggestion.DeepClone().AsObject();
        copy["destination"] = "session";
        return copy;
    }

    public static string Deny(PermissionRequest request) =>
        Response(request.RequestId, new JsonObject { ["behavior"] = "deny", ["message"] = "Brugeren afviste." });

    public static string Control(JsonObject request, string id) => new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = request,
    }.ToJsonString();

    static string Control(JsonObject request) => Control(request, Guid.NewGuid().ToString());

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

    static string ContentText(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue(out string? text) => text,
        JsonArray blocks => string.Concat(blocks.Select(block => (string?)block?["text"])),
        _ => "",
    };

    static string ResultText(JsonNode message) =>
        (string?)message["result"]
        ?? (message["errors"] is JsonArray { Count: > 0 } errors ? string.Join('\n', errors.Select(error => (string?)error)) : null)
        ?? (string?)message["subtype"]
        ?? "";

    static IReadOnlyList<string>? Answers(JsonNode message) =>
        message["user_message_uuids"] is JsonArray ids ? [.. ids.Select(id => (string)id!)]
        : (string?)message["user_message_uuid"] is { } id ? [id]
        : null;

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
            request["input"]?.DeepClone() as JsonObject ?? new JsonObject(),
            (string?)request["tool_use_id"],
            request["permission_suggestions"] is JsonArray suggestions
                ? new JsonArray([.. suggestions.OfType<JsonObject>().Where(suggestion => (string?)suggestion["type"] != "setMode").Select(suggestion => suggestion.DeepClone())])
                : null);
    }
}
