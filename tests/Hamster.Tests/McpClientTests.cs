using System.Text.Json.Nodes;

namespace Hamster.Tests;

public class McpClientTests
{
    [Theory]
    [InlineData("text/event-stream", "event: message\ndata: {\"id\":1}\n\ndata: {\"id\":2}\n\n")]
    [InlineData("application/json", "{\"id\":2}")]
    public void Reply_WhenServerAnswers_ThenReadsTheLastMessage(string mediaType, string body)
    {
        // Act
        var reply = McpClient.Reply(mediaType, body);

        // Assert
        Assert.Equal(2, (int?)reply?["id"]);
    }

    [Fact]
    public void Reply_WhenBodyIsEmpty_ThenNothing()
    {
        // Act
        var reply = McpClient.Reply("application/json", "");

        // Assert
        Assert.Null(reply);
    }

    [Fact]
    public void ToolText_WhenToolAnswers_ThenJoinsItsText()
    {
        // Arrange
        var reply = JsonNode.Parse("""{"result":{"content":[{"type":"text","text":"{\"items\":"},{"type":"text","text":"[]}"}]}}""");

        // Act
        var text = McpClient.ToolText(reply);

        // Assert
        Assert.Equal("""{"items":[]}""", text);
    }

    [Theory]
    [InlineData("""{"result":{"isError":true,"content":[{"type":"text","text":"{\"error\":true,\"message\":\"Ingen adgang\"}"}]}}""")]
    [InlineData("""{"result":{"isError":true,"content":[{"type":"text","text":"Ingen adgang"}]}}""")]
    [InlineData("""{"error":{"code":-32602,"message":"Ingen adgang"}}""")]
    public void ToolText_WhenToolFails_ThenThrowsItsMessage(string json)
    {
        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => McpClient.ToolText(JsonNode.Parse(json)));

        // Assert
        Assert.Equal("Ingen adgang", exception.Message);
    }

    [Fact]
    public void FromClaudeConfig_WhenServerIsInstalled_ThenReturnsUrlAndHeaders()
    {
        // Arrange
        const string config = """{"mcpServers":{"hamster-github":{"type":"http","url":"https://api.githubcopilot.com/mcp/","headers":{"Authorization":"Bearer x","X-MCP-Readonly":"true"}}}}""";

        // Act
        var endpoint = McpEndpoint.FromClaudeConfig(config, "hamster-github");

        // Assert
        Assert.Equal(("https://api.githubcopilot.com/mcp/", "Bearer x", "true"), (endpoint?.Url, endpoint?.Headers["Authorization"], endpoint?.Headers["X-MCP-Readonly"]));
    }

    [Theory]
    [InlineData("""{"mcpServers":{"other":{"url":"https://example.com"}}}""")]
    [InlineData("""{"projects":{}}""")]
    public void FromClaudeConfig_WhenServerIsMissing_ThenNothing(string config)
    {
        // Act
        var endpoint = McpEndpoint.FromClaudeConfig(config, "hamster-github");

        // Assert
        Assert.Null(endpoint);
    }
}
