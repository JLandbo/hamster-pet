using System.Text.Json.Nodes;

namespace Hamster.Tests;

public class ClaudeClientTests
{
    const string Permission = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","input":{"url":"https://example.com"}}}""";
    const string Withdrawal = """{"type":"control_cancel_request","request_id":"req-1"}""";
    const string WebSearch = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{}}]}}""";
    const string SearchDone = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"..."}]}}""";
    const string Result = """{"type":"result","subtype":"success","is_error":false,"result":"Svar","session_id":"session-1"}""";

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_WhenCreated_ThenUsesOpus55WithXhighEffort()
    {
        // Act
        var client = new ClaudeClient("workspace");

        // Assert
        Assert.Equal(("claude-opus-5-5", "xhigh"), (client.Model, client.Effort));
    }

    [Fact]
    public void Constructor_WhenCreated_ThenUsesManualMode()
    {
        // Act
        var client = new ClaudeClient("workspace");

        // Assert
        Assert.Equal("default", client.PermissionMode);
    }

    [Fact]
    public async Task ConverseAsync_WhenStarted_ThenSendsPromptAsUserMessage()
    {
        // Arrange
        var input = new StringWriter();

        // Act
        await ClaudeClient.ConverseAsync(Output(Result), input, "hej", [], new FakeListener(), Token);

        // Assert
        Assert.Equal(ClaudeProtocol.UserMessage("hej", []), Lines(input)[0]);
    }

    [Fact]
    public async Task ConverseAsync_WhenResultArrives_ThenReturnsIt()
    {
        // Act
        var result = await ClaudeClient.ConverseAsync(Output(Result), new StringWriter(), "hej", [], new FakeListener(), Token);

        // Assert
        Assert.Equal(new ClaudeResult("session-1", "Svar", IsError: false), result);
    }

    [Fact]
    public async Task ConverseAsync_WhenOutputEndsWithoutResult_ThenReturnsNull()
    {
        // Act
        var result = await ClaudeClient.ConverseAsync(Output(), new StringWriter(), "hej", [], new FakeListener(), Token);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "deny")]
    public async Task ConverseAsync_WhenPermissionRequested_ThenSendsUsersAnswer(bool allowed, string behavior)
    {
        // Arrange
        var input = new StringWriter();

        // Act
        await ClaudeClient.ConverseAsync(Output(Permission, Result), input, "hej", [], new FakeListener(allowed), Token);

        // Assert
        Assert.Equal(behavior, (string?)JsonNode.Parse(Lines(input)[1])!["response"]!["response"]!["behavior"]);
    }

    [Fact]
    public async Task ConverseAsync_WhenPermissionWithdrawn_ThenCancelsTheQuestionRightAway()
    {
        // Arrange
        var listener = new FakeListener(allow: null);

        // Act
        await ClaudeClient.ConverseAsync(Output(Permission, Withdrawal, WebSearch, Result), new StringWriter(), "hej", [], listener, Token);

        // Assert
        Assert.True(listener.QuestionCancelledBeforeNextTool);
    }

    [Fact]
    public async Task ConverseAsync_WhenResultArrivesWhileAsking_ThenCancelsTheQuestion()
    {
        // Arrange
        var listener = new FakeListener(allow: null);

        // Act
        await ClaudeClient.ConverseAsync(Output(Permission, Result), new StringWriter(), "hej", [], listener, Token);

        // Assert
        Assert.True(listener.Question.IsCancellationRequested);
    }

    [Fact]
    public async Task ConverseAsync_WhenToolStartsAndFinishes_ThenNotifiesListener()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await ClaudeClient.ConverseAsync(Output(WebSearch, SearchDone, Result), new StringWriter(), "hej", [], listener, Token);

        // Assert
        Assert.Equal([new ToolUse("toolu_1", "WebSearch")], listener.Started);
        Assert.Equal([new ToolResult("toolu_1")], listener.Finished);
    }

    static StringReader Output(params string[] lines) => new(string.Join('\n', lines));

    static string[] Lines(StringWriter input) => input.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    sealed class FakeListener(bool? allow = true) : IClaudeListener
    {
        public CancellationToken Question { get; private set; }
        public List<ToolUse> Started { get; } = [];
        public List<ToolResult> Finished { get; } = [];
        public bool QuestionCancelledBeforeNextTool { get; private set; }

        public void ToolStarted(ToolUse tool)
        {
            Started.Add(tool);
            QuestionCancelledBeforeNextTool = Question.IsCancellationRequested;
        }

        public void ToolFinished(ToolResult result) => Finished.Add(result);

        public async Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
        {
            Question = cancellationToken;
            if (allow is { } answer)
                return answer;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }
    }
}
