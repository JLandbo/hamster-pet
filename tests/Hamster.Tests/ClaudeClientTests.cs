using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Hamster.Tests;

public sealed class ClaudeClientTests : IDisposable
{
    const string Permission = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","input":{"url":"https://example.com"}}}""";
    const string Withdrawal = """{"type":"control_cancel_request","request_id":"req-1"}""";
    const string WebSearch = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{}}]}}""";
    const string SearchDone = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"..."}]}}""";
    const string Stopped = """{"type":"result","subtype":"error_during_execution","is_error":true,"session_id":"session-1","total_cost_usd":0.2}""";
    const string Result = """{"type":"result","subtype":"success","is_error":false,"result":"Svar","session_id":"session-1"}""";

    readonly string settingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    static CancellationToken Token => TestContext.Current.CancellationToken;

    JsonFile<ClaudeSettings> Store() => new(settingsFile, ClaudeSettings.Default);

    public void Dispose() => File.Delete(settingsFile);

    [Fact]
    public void Settings_WhenNothingSaved_ThenOpus55WithXhighInManualMode()
    {
        // Act
        var client = new ClaudeClient("workspace", Store());

        // Assert
        Assert.Equal(new ClaudeSettings("claude-opus-5-5", "xhigh", "default"), client.Settings);
    }

    [Fact]
    public void Settings_WhenChanged_ThenTheNextStartUsesThem()
    {
        // Arrange
        new ClaudeClient("workspace", Store()).Settings = new("claude-sonnet-5", "low", "plan");

        // Act
        var restarted = new ClaudeClient("workspace", Store());

        // Assert
        Assert.Equal(new ClaudeSettings("claude-sonnet-5", "low", "plan"), restarted.Settings);
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
    public async Task ConverseAsync_WhenPermissionWithdrawn_ThenCancelsTheQuestionRightAwayAndAnswersNothing()
    {
        // Arrange
        var listener = new FakeListener(allow: null);
        var input = new StringWriter();

        // Act
        await ClaudeClient.ConverseAsync(Output(Permission, Withdrawal, WebSearch, Result), input, "hej", [], listener, Token);

        // Assert
        Assert.True(listener.QuestionCancelledBeforeNextTool);
        Assert.Single(Lines(input));
    }

    [Fact]
    public async Task ConverseAsync_WhenResultArrivesWhileAsking_ThenCancelsTheQuestionAndAnswersNothing()
    {
        // Arrange
        var listener = new FakeListener(allow: null);
        var input = new StringWriter();

        // Act
        await ClaudeClient.ConverseAsync(Output(Permission, Result), input, "hej", [], listener, Token);

        // Assert
        Assert.True(listener.Question.IsCancellationRequested);
        Assert.Single(Lines(input));
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

    [Fact(Timeout = 5_000)]
    public async Task ConverseAsync_WhenCancelled_ThenAsksClaudeToStopAndReturnsItsResult()
    {
        // Arrange
        var output = new LineReader();
        var input = new StringWriter();
        using var stop = new CancellationTokenSource();
        var conversing = ClaudeClient.ConverseAsync(output, input, "hej", [], new FakeListener(), stop.Token);

        // Act
        stop.Cancel();
        output.Add(Stopped);
        var result = await conversing.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClaudeProtocol.Interrupt, Lines(input)[^1]);
        Assert.Equal(new ClaudeResult("session-1", "error_during_execution", IsError: true, Cost: 0.2m), result);
    }

    static StringReader Output(params string[] lines) => new(string.Join('\n', lines));

    static string[] Lines(StringWriter input) => input.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    sealed class LineReader : TextReader
    {
        readonly Channel<string> lines = Channel.CreateUnbounded<string>();

        public void Add(string line) => lines.Writer.TryWrite(line);

        public override async Task<string?> ReadLineAsync() => await lines.Reader.ReadAsync();
    }

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
            // Cancelled inline (unlike Task.Delay), so whatever happens to a cancelled question happens before ConverseAsync returns.
            var never = new TaskCompletionSource<bool>();
            using var registration = cancellationToken.Register(() => never.TrySetCanceled(cancellationToken));
            return await never.Task;
        }
    }
}
