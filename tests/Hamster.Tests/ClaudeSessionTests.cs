using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Hamster.Tests;

public sealed class ClaudeSessionTests
{
    const string Permission = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","input":{"url":"https://example.com"}}}""";
    const string Withdrawal = """{"type":"control_cancel_request","request_id":"req-1"}""";
    const string WebSearch = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{}}]}}""";
    const string SearchDone = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"..."}]}}""";
    const string RateLimit = """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.03},"seven_day":{"utilization":0.59}}}}""";
    const string Started = """{"type":"command_lifecycle","command_uuid":"id-1","state":"started"}""";
    const string Tasks = """{"type":"system","subtype":"background_tasks_changed","tasks":[]}""";
    const string Status = """{"type":"system","subtype":"status","status":null,"permissionMode":"plan"}""";
    const string Result = """{"type":"result","subtype":"success","is_error":false,"result":"Svar","session_id":"session-1","user_message_uuids":["id-1"]}""";

    static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_WhenCalled_ThenWritesTheUserMessageWithItsId()
    {
        // Arrange
        var input = new StringWriter();

        // Act
        await new ClaudeSession(Output(), input, new FakeListener()).SendAsync("id-1", "hej", []);

        // Assert
        Assert.Equal(ClaudeProtocol.UserMessage("hej", [], "id-1"), Lines(input)[0]);
    }

    [Fact]
    public async Task ReadAsync_WhenSeveralTurnsEnd_ThenReportsEveryResult()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(Started, Result, Started, Result), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(2, listener.Results.Count);
    }

    [Fact]
    public async Task ReadAsync_WhenTurnStarts_ThenTellsWhichMessage()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(Started), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["id-1"], listener.Turns);
    }

    [Fact]
    public async Task ReadAsync_WhenModeChanges_ThenTellsTheNewMode()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(Status), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["plan"], listener.Modes);
    }

    [Fact]
    public async Task ReadAsync_WhenBackgroundTasksChange_ThenTellsHowManyRun()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(Tasks), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([0], listener.Tasks);
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "deny")]
    public async Task ReadAsync_WhenPermissionRequested_ThenSendsUsersAnswer(bool allowed, string behavior)
    {
        // Arrange
        var input = new StringWriter();

        // Act
        await new ClaudeSession(Output(Permission, Result), input, new FakeListener(allowed)).ReadAsync();

        // Assert
        Assert.Equal(behavior, (string?)JsonNode.Parse(Lines(input)[0])!["response"]!["response"]!["behavior"]);
    }

    [Fact]
    public async Task ReadAsync_WhenPermissionWithdrawn_ThenCancelsTheQuestionRightAwayAndAnswersNothing()
    {
        // Arrange
        var listener = new FakeListener(allow: null);
        var input = new StringWriter();

        // Act
        await new ClaudeSession(Output(Permission, Withdrawal, WebSearch), input, listener).ReadAsync();

        // Assert
        Assert.True(listener.QuestionCancelledBeforeNextTool);
        Assert.Empty(Lines(input));
    }

    [Fact(Timeout = 5_000)]
    public async Task ReadAsync_WhenResultArrivesWhileAsking_ThenKeepsAsking()
    {
        // Arrange
        var listener = new FakeListener(allow: null);
        var output = new LineReader();
        var reading = new ClaudeSession(output, new StringWriter(), listener).ReadAsync();

        // Act
        output.Add(Permission);
        output.Add(Result);
        await listener.FirstResult.WaitAsync(Token);

        // Assert
        Assert.False(listener.Question.IsCancellationRequested);
        output.End();
        await reading.WaitAsync(Token);
    }

    [Fact]
    public async Task ReadAsync_WhenOutputEnds_ThenCancelsOpenQuestions()
    {
        // Arrange
        var listener = new FakeListener(allow: null);

        // Act
        await new ClaudeSession(Output(Permission), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.True(listener.Question.IsCancellationRequested);
    }

    [Fact]
    public async Task ReadAsync_WhenToolStartsAndFinishes_ThenNotifiesListener()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(WebSearch, SearchDone), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([new ToolUse("toolu_1", "WebSearch")], listener.Started);
        Assert.Equal([new ToolResult("toolu_1")], listener.Finished);
    }

    [Fact]
    public async Task ReadAsync_WhenRateLimitArrives_ThenReportsUsage()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(RateLimit), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([new Usage(0.03, 0.59)], listener.Usages);
    }

    [Fact]
    public async Task ReadAsync_WhenDetached_ThenIgnoresTheRestButCountsResults()
    {
        // Arrange
        var listener = new FakeListener();
        var session = new ClaudeSession(Output(Started, Result), new StringWriter(), listener);
        session.Detach();

        // Act
        await session.ReadAsync();

        // Assert
        Assert.Equal((0, 0, 1), (listener.Turns.Count, listener.Results.Count, session.Results));
    }

    [Fact(Timeout = 5_000)]
    public async Task RequestAsync_WhenClaudeAnswers_ThenReturnsTheResponse()
    {
        // Arrange
        var (output, input) = (new LineReader(), new LineWriter());
        var session = new ClaudeSession(output, input, new FakeListener());
        _ = session.ReadAsync();
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, Patience);

        // Act
        output.Add(Reply(await input.NextAsync(), """{"subtype":"success","response":{"mcpServers":[]}}"""));

        // Assert
        Assert.True((await asking.WaitAsync(Token))?["mcpServers"] is JsonArray);
    }

    [Fact(Timeout = 5_000)]
    public async Task RequestAsync_WhenAnotherRequestIsAnsweredFirst_ThenWaitsForItsOwnReply()
    {
        // Arrange
        var (output, input) = (new LineReader(), new LineWriter());
        var session = new ClaudeSession(output, input, new FakeListener());
        _ = session.ReadAsync();
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, Patience);
        var request = await input.NextAsync();

        // Act
        output.Add("""{"type":"control_response","response":{"subtype":"success","request_id":"other","response":{}}}""");
        output.Add(Reply(request, """{"subtype":"success","response":{"mcpServers":[]}}"""));

        // Assert
        Assert.True((await asking.WaitAsync(Token))?["mcpServers"] is JsonArray);
    }

    [Fact(Timeout = 5_000)]
    public async Task RequestAsync_WhenClaudeRefuses_ThenThrowsItsError()
    {
        // Arrange
        var (output, input) = (new LineReader(), new LineWriter());
        var session = new ClaudeSession(output, input, new FakeListener());
        _ = session.ReadAsync();
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_toggle" }, Patience);

        // Act
        output.Add(Reply(await input.NextAsync(), """{"subtype":"error","error":"Server not found: x"}"""));

        // Assert
        Assert.Equal("Server not found: x", (await Assert.ThrowsAsync<InvalidOperationException>(() => asking.WaitAsync(Token))).Message);
    }

    [Fact(Timeout = 5_000)]
    public async Task RequestAsync_WhenOutputEndsBeforeTheAnswer_ThenIsCancelled()
    {
        // Arrange
        var (output, input) = (new LineReader(), new LineWriter());
        var session = new ClaudeSession(output, input, new FakeListener());
        var reading = session.ReadAsync();
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, Patience);
        await input.NextAsync();

        // Act
        output.End();
        await reading.WaitAsync(Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(Token));
    }

    [Fact(Timeout = 5_000)]
    public async Task RequestAsync_WhenClaudeDoesNotAnswerInTime_ThenFails()
    {
        // Arrange
        var session = new ClaudeSession(new LineReader(), new LineWriter(), new FakeListener());

        // Act
        var asking = () => session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, TimeSpan.Zero).WaitAsync(Token);

        // Assert
        Assert.Equal("claude svarede ikke.", (await Assert.ThrowsAsync<InvalidOperationException>(asking)).Message);
    }

    static string Reply(string request, string response)
    {
        var reply = JsonNode.Parse(response)!.AsObject();
        reply["request_id"] = (string?)JsonNode.Parse(request)!["request_id"];
        return new JsonObject { ["type"] = "control_response", ["response"] = reply }.ToJsonString();
    }

    static StringReader Output(params string[] lines) => new(string.Join('\n', lines));

    static string[] Lines(StringWriter input) => input.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    sealed class LineReader : TextReader
    {
        readonly Channel<string> lines = Channel.CreateUnbounded<string>();

        public void Add(string line) => lines.Writer.TryWrite(line);

        public void End() => lines.Writer.Complete();

        public override async Task<string?> ReadLineAsync() =>
            await lines.Reader.WaitToReadAsync() && lines.Reader.TryRead(out var line) ? line : null;
    }

    sealed class LineWriter : TextWriter
    {
        readonly Channel<string> lines = Channel.CreateUnbounded<string>();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value) => lines.Writer.TryWrite(value!.TrimEnd('\n'));

        public ValueTask<string> NextAsync() => lines.Reader.ReadAsync(Token);
    }

    sealed class FakeListener(bool? allow = true) : IClaudeListener
    {
        readonly TaskCompletionSource firstResult = new();

        public CancellationToken Question { get; private set; }
        public List<string?> Turns { get; } = [];
        public List<ToolUse> Started { get; } = [];
        public List<ToolResult> Finished { get; } = [];
        public List<Usage> Usages { get; } = [];
        public List<string> Modes { get; } = [];
        public List<int> Tasks { get; } = [];
        public List<ClaudeResult> Results { get; } = [];
        public bool QuestionCancelledBeforeNextTool { get; private set; }
        public Task FirstResult => firstResult.Task;

        public void TurnStarted(string? messageId) => Turns.Add(messageId);

        public void ToolStarted(ToolUse tool)
        {
            Started.Add(tool);
            QuestionCancelledBeforeNextTool = Question.IsCancellationRequested;
        }

        public void ToolFinished(ToolResult result) => Finished.Add(result);

        public void UsageReported(Usage usage) => Usages.Add(usage);

        public void ModeChanged(string mode) => Modes.Add(mode);

        public void BackgroundTasksChanged(int count) => Tasks.Add(count);

        public void ResultReceived(ClaudeResult result)
        {
            Results.Add(result);
            firstResult.TrySetResult();
        }

        public void Exited(string error)
        {
        }

        public async Task<bool> AskPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
        {
            Question = cancellationToken;
            if (allow is { } answer)
                return answer;
            var never = new TaskCompletionSource<bool>();
            using var registration = cancellationToken.Register(() => never.TrySetCanceled(cancellationToken));
            return await never.Task;
        }
    }
}
