using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hamster;

public sealed record ToolCall(string Name, JsonObject Arguments);

public sealed record McpEndpoint(string Url, IReadOnlyDictionary<string, string> Headers)
{
    public static string ClaudeConfigFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    public static McpEndpoint? FromClaudeConfig(string config, string name) =>
        JsonNode.Parse(config)?["mcpServers"]?[name] is JsonObject server && (string?)server["url"] is { } url
            ? new(url, server["headers"] is JsonObject headers ? headers.ToDictionary(header => header.Key, header => (string?)header.Value ?? "") : new Dictionary<string, string>())
            : null;
}

public static class McpClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<IReadOnlyList<string>> CallAsync(McpEndpoint endpoint, IReadOnlyList<ToolCall> calls)
    {
        var (_, session) = await PostAsync(endpoint, null, Request(0, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "hamster", ["version"] = "1" },
        }));
        await PostAsync(endpoint, session, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        var results = new string[calls.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, calls.Count), async (i, _) =>
        {
            var (reply, _) = await PostAsync(endpoint, session, Request(i + 1, "tools/call", new JsonObject { ["name"] = calls[i].Name, ["arguments"] = calls[i].Arguments.DeepClone() }));
            results[i] = ToolText(reply);
        });
        return results;
    }

    public static JsonNode? Reply(string? mediaType, string body) =>
        mediaType == "text/event-stream"
            ? body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()).LastOrDefault() is { } data ? JsonNode.Parse(data) : null
            : string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);

    public static string ToolText(JsonNode? reply)
    {
        if (reply?["error"] is { } error)
            throw new InvalidOperationException((string?)error["message"] ?? error.ToJsonString());
        var text = string.Concat(reply?["result"]?["content"]?.AsArray().Select(block => (string?)block?["text"]) ?? []);
        return (bool?)reply?["result"]?["isError"] == true ? throw new InvalidOperationException(MessageOf(text)) : text;
    }

    public static string MessageOf(string text)
    {
        try
        {
            return (string?)JsonNode.Parse(text)?["message"] ?? text;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return text;
        }
    }

    static JsonObject Request(int id, string method, JsonObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };

    static async Task<(JsonNode? Reply, string? Session)> PostAsync(McpEndpoint endpoint, string? session, JsonObject body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        foreach (var (name, value) in endpoint.Headers)
            request.Headers.TryAddWithoutValidation(name, value);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (session is not null)
            request.Headers.Add("Mcp-Session-Id", session);
        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}");
        return (Reply(response.Content.Headers.ContentType?.MediaType, text), response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : session);
    }
}
