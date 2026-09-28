using System.Text.Json.Nodes;

namespace Hamster.Tests;

public class ClaudeFetcherTests
{
    static Connector Atlassian => Connectors.All.Single(connector => connector.Name == "hamster-atlassian-rovo");

    static ToolCall Search(string query) => new("search", new JsonObject { ["query"] = query });

    [Fact]
    public async Task ToolFinished_WhenEveryToolAnswered_ThenReturnsTheirTexts()
    {
        // Arrange
        var call = Search("a");
        var results = new ClaudeFetcher.ToolResults([call], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"a"}"""));

        // Act
        results.ToolFinished(new ToolResult("toolu_1", "{}"));

        // Assert
        Assert.Equal([(call, "{}")], await results.Done);
    }

    [Fact]
    public async Task ToolFinished_WhenClaudeAnswersInAnotherOrder_ThenPairsEachResultWithItsCall()
    {
        // Arrange
        var (first, second) = (Search("a"), Search("b"));
        var results = new ClaudeFetcher.ToolResults([first, second], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"b"}"""));
        results.ToolStarted(new ToolUse("toolu_2", "mcp__x__search", Input: """{"query":"a"}"""));

        // Act
        results.ToolFinished(new ToolResult("toolu_1", "B"));
        results.ToolFinished(new ToolResult("toolu_2", "A"));

        // Assert
        Assert.Equal([(second, "B"), (first, "A")], await results.Done);
    }

    [Fact]
    public async Task ToolFinished_WhenAnAlteredCallFinishesFirst_ThenStillPairsTheExactOneCorrectly()
    {
        // Arrange
        var (first, second) = (Search("a"), Search("b"));
        var results = new ClaudeFetcher.ToolResults([first, second], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"b "}"""));
        results.ToolStarted(new ToolUse("toolu_2", "mcp__x__search", Input: """{"query":"a"}"""));

        // Act
        results.ToolFinished(new ToolResult("toolu_1", "B"));
        results.ToolFinished(new ToolResult("toolu_2", "A"));

        // Assert
        Assert.Equal([(first, "A"), (second, "B")], await results.Done);
    }

    [Fact]
    public async Task ResultReceived_WhenClaudeRepeatsOneCallInsteadOfTheOther_ThenFails()
    {
        // Arrange
        var results = new ClaudeFetcher.ToolResults([Search("a"), Search("b")], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"a"}"""));
        results.ToolStarted(new ToolUse("toolu_2", "mcp__x__search", Input: """{"query":"a"}"""));
        results.ToolFinished(new ToolResult("toolu_1", "A"));
        results.ToolFinished(new ToolResult("toolu_2", "A"));

        // Act
        results.ResultReceived(new ClaudeResult(null, "ok", IsError: false));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => results.Done);
    }

    [Fact]
    public async Task ResultReceived_WhenTwoCallsOfOneToolWereAltered_ThenFailsInsteadOfGuessing()
    {
        // Arrange
        var results = new ClaudeFetcher.ToolResults([Search("a"), Search("b")], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"a "}"""));
        results.ToolStarted(new ToolUse("toolu_2", "mcp__x__search", Input: """{"query":"b "}"""));
        results.ToolFinished(new ToolResult("toolu_1", "A"));
        results.ToolFinished(new ToolResult("toolu_2", "B"));

        // Act
        results.ResultReceived(new ClaudeResult(null, "ok", IsError: false));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => results.Done);
    }

    [Fact]
    public async Task ToolFinished_WhenTheErrorIsAResultClaudeSavedToAFile_ThenKeepsTheResult()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(file, """{"issues":[]}""");
        var call = Search("a");
        var results = new ClaudeFetcher.ToolResults([call], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"a"}"""));

        // Act
        results.ToolFinished(new ToolResult("toolu_1", $"Error: result (201.710 characters) exceeds maximum allowed tokens. Output has been saved to {file}", IsError: true));
        var done = await results.Done;
        File.Delete(file);

        // Assert
        Assert.Equal(call, Assert.Single(done).Call);
    }

    [Theory]
    [InlineData("  claude: not logged in \n", "claude: not logged in")]
    [InlineData("", "claude stoppede uventet.")]
    public async Task ReasonAsync_WhenClaudeStopped_ThenUsesWhatItWroteToStderr(string stderr, string expected)
    {
        // Act
        var reason = await ClaudeFetcher.ReasonAsync(Task.FromResult(stderr));

        // Assert
        Assert.Equal(expected, reason);
    }

    [Fact]
    public async Task ToolFinished_WhenTheToolFails_ThenFailsWithItsMessage()
    {
        // Arrange
        var results = new ClaudeFetcher.ToolResults([Search("a")], "mcp__x__");
        results.ToolStarted(new ToolUse("toolu_1", "mcp__x__search", Input: """{"query":"a"}"""));

        // Act
        results.ToolFinished(new ToolResult("toolu_1", """{"error":true,"message":"Filteret findes ikke."}""", IsError: true));

        // Assert
        Assert.Equal("Filteret findes ikke.", (await Assert.ThrowsAsync<InvalidOperationException>(() => results.Done)).Message);
    }

    [Theory]
    [InlineData(true, "You've hit your limit", "You've hit your limit")]
    [InlineData(false, "ok", "claude kaldte ikke værktøjerne.")]
    public async Task ResultReceived_WhenAToolDidNotAnswer_ThenFails(bool isError, string text, string expected)
    {
        // Arrange
        var results = new ClaudeFetcher.ToolResults([Search("a")], "mcp__x__");

        // Act
        results.ResultReceived(new ClaudeResult(null, text, isError));

        // Assert
        Assert.Equal(expected, (await Assert.ThrowsAsync<InvalidOperationException>(() => results.Done)).Message);
    }

    [Theory]
    [InlineData("claude.ai Atlassian Rovo", "mcp__claude_ai_Atlassian_Rovo__")]
    [InlineData("hamster-github", "mcp__hamster-github__")]
    public void ToolPrefix_WhenServerIsNamed_ThenMatchesClaudesToolNames(string serverName, string expected)
    {
        // Act
        var prefix = ClaudeFetcher.ToolPrefix(serverName);

        // Assert
        Assert.Equal(expected, prefix);
    }

    [Fact]
    public void FullText_WhenClaudeSavedTheResultToAFile_ThenReadsTheFile()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(file, """{"issues":[]}""");
        var text = $"<persisted-output>\nOutput too large (50.2KB). Full output saved to: {file}\n\nPreview (first 2KB):\n{{\"issues\"\n</persisted-output>";

        // Act
        var full = ClaudeFetcher.FullText(text);
        File.Delete(file);

        // Assert
        Assert.Equal("""{"issues":[]}""", full);
    }

    [Fact]
    public void FullText_WhenTheResultWasTooLargeForClaude_ThenReadsTheSavedFile()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(file, """{"issues":[]}""");
        var text = $"""
            Error: result (201.710 characters) exceeds maximum allowed tokens. Output has been saved to {file}
            Format: JSON
            """;

        // Act
        var full = ClaudeFetcher.FullText(text);
        File.Delete(file);

        // Assert
        Assert.Equal("""{"issues":[]}""", full);
    }

    [Fact]
    public void FullText_WhenTheResultIsInline_ThenKeepsIt()
    {
        // Act
        var full = ClaudeFetcher.FullText("""{"issues":[]}""");

        // Assert
        Assert.Equal("""{"issues":[]}""", full);
    }

    [Fact]
    public void Arguments_WhenCalled_ThenAllowsOnlyTheToolsAndTheServerAsked()
    {
        // Arrange
        ToolCall[] calls = [new("searchJiraIssuesUsingJql", new JsonObject()), new("searchJiraIssuesUsingJql", new JsonObject())];

        // Act
        var arguments = ClaudeFetcher.Arguments(Atlassian, "claude.ai Atlassian Rovo", calls);

        // Assert
        var settings = JsonNode.Parse(arguments[Array.IndexOf(arguments, "--settings") + 1])!;
        Assert.Equal(("mcp__claude_ai_Atlassian_Rovo__searchJiraIssuesUsingJql", "https://mcp.atlassian.com/*", "hamster-atlassian-rovo"),
            (arguments[Array.IndexOf(arguments, "--allowedTools") + 1], (string?)settings["allowedMcpServers"]![0]!["serverUrl"], (string?)settings["deniedMcpServers"]![0]!["serverName"]));
    }
}
