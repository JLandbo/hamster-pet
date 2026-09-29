using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Hamster.Tests;

public sealed class ClaudeSessionTests
{
    const string _permission = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","input":{"url":"https://example.com"}}}""";
    const string _withdrawal = """{"type":"control_cancel_request","request_id":"req-1"}""";
    const string _webSearch = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{}}]}}""";
    const string _searchDone = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"..."}]}}""";
    const string _rateLimit = """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.03},"seven_day":{"utilization":0.59}}}}""";
    const string _started = """{"type":"command_lifecycle","command_uuid":"id-1","state":"started"}""";
    const string _partialStarted = """{"type":"stream_event","user_message_uuid":"id-1","event":{"type":"message_start"}}""";
    const string _partialText = """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Hej"}}}""";
    const string _assistantText = """{"type":"assistant","message":{"content":[{"type":"text","text":"Hej med dig"}]}}""";
    const string _tasks = """{"type":"system","subtype":"background_tasks_changed","tasks":[]}""";
    const string _status = """{"type":"system","subtype":"status","status":null,"permissionMode":"plan"}""";
    const string _result = """{"type":"result","subtype":"success","is_error":false,"result":"Svar","session_id":"session-1","user_message_uuids":["id-1"]}""";

    static readonly TimeSpan _patience = TimeSpan.FromMinutes(1);

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
    public async Task SendAsync_WhenAnEarlierWriteFailed_ThenStillWrites()
    {
        // Arrange
        var input = new BrokenOnceWriter();
        var session = new ClaudeSession(Output(), input, new FakeListener());
        var failed = session.SendAsync("a");

        // Act
        await session.SendAsync("b");

        // Assert
        Assert.Equal((true, "b\n"), (failed.IsFaulted, input.ToString()));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeCouldNotStart_ThenFailsWithTheReason()
    {
        // Arrange
        var streams = new TaskCompletionSource<(TextReader, TextWriter)>();
        var sending = new ClaudeSession(streams.Task, new FakeListener()).SendAsync("id-1", "hej", []);

        // Act
        streams.SetException(new Win32Exception("Mappenavnet er ugyldigt"));

        // Assert
        Assert.Equal("Mappenavnet er ugyldigt", (await Assert.ThrowsAsync<InvalidOperationException>(() => sending)).Message);
    }

    [Fact]
    public async Task ReadAsync_WhenSeveralTurnsEnd_ThenReportsEveryResult()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_started, _result, _started, _result), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(2, listener.Results.Count);
    }

    [Fact]
    public async Task ReadAsync_WhenTurnStarts_ThenTellsWhichMessage()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_started), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["id-1"], listener.Turns);
    }

    [Fact]
    public async Task ReadAsync_WhenPartialMessageArrives_ThenForwardsItsStartAndText()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_partialStarted, _partialText), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["id-1"], listener.PartialStarts);
        Assert.Equal(["Hej"], listener.PartialTexts);
    }

    [Fact]
    public async Task ReadAsync_WhenAssistantTextCompletes_ThenForwardsTheCompleteText()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_assistantText), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["Hej med dig"], listener.AssistantTexts);
    }

    [Fact]
    public async Task ReadAsync_WhenModeChanges_ThenTellsTheNewMode()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_status), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal(["plan"], listener.Modes);
    }

    [Fact]
    public async Task ReadAsync_WhenBackgroundTasksChange_ThenTellsHowManyRun()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_tasks), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([0], listener.Tasks);
    }

    [Theory]
    [InlineData(PermissionAnswer.Allow, "allow")]
    [InlineData(PermissionAnswer.Deny, "deny")]
    public async Task ReadAsync_WhenPermissionRequested_ThenSendsUsersAnswer(PermissionAnswer answer, string behavior)
    {
        // Arrange
        var input = new LineWriter();

        // Act
        await new ClaudeSession(Output(_permission, _result), input, new FakeListener(answer)).ReadAsync();
        var sent = await input.NextAsync();

        // Assert
        Assert.Equal(behavior, (string?)JsonNode.Parse(sent)!["response"]!["response"]!["behavior"]);
    }

    [Fact]
    public async Task ReadAsync_WhenUserAllowsAlways_ThenGrantsClaudesSuggestionsForThisSessionOnly()
    {
        // Arrange
        const string permission = """{"type":"control_request","request_id":"req-2","request":{"subtype":"can_use_tool","tool_name":"Write","input":{"file_path":"a.txt"},"permission_suggestions":[{"type":"addRules","rules":[{"toolName":"Write"}],"behavior":"allow","destination":"localSettings"}]}}""";
        var input = new LineWriter();

        // Act
        await new ClaudeSession(Output(permission, _result), input, new FakeListener(PermissionAnswer.AllowAlways)).ReadAsync();
        var sent = await input.NextAsync();

        // Assert
        var granted = JsonNode.Parse(sent)!["response"]!["response"]!["updatedPermissions"]!;
        Assert.Equal("""[{"type":"addRules","rules":[{"toolName":"Write"}],"behavior":"allow","destination":"session"}]""", granted.ToJsonString());
    }

    [Fact]
    public async Task ReadAsync_WhenPermissionWithdrawn_ThenCancelsTheQuestionRightAwayAndAnswersNothing()
    {
        // Arrange
        var listener = new FakeListener(answer: null);
        var input = new LineWriter();
        var session = new ClaudeSession(Output(_permission, _withdrawal, _webSearch), input, listener);

        // Act
        await session.ReadAsync();
        await session.SendAsync("slut");

        // Assert
        Assert.True(listener.QuestionCancelledBeforeNextTool);
        Assert.Equal("slut", await input.NextAsync());
    }

    [Fact(Timeout = 5_000)]
    public async Task ReadAsync_WhenResultArrivesWhileAsking_ThenKeepsAsking()
    {
        // Arrange
        var listener = new FakeListener(answer: null);
        var output = new LineReader();
        var reading = new ClaudeSession(output, new StringWriter(), listener).ReadAsync();

        // Act
        output.Add(_permission);
        output.Add(_result);
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
        var listener = new FakeListener(answer: null);

        // Act
        await new ClaudeSession(Output(_permission), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.True(listener.Question.IsCancellationRequested);
    }

    [Fact]
    public async Task ReadAsync_WhenToolStartsAndFinishes_ThenNotifiesListener()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_webSearch, _searchDone), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([new ToolUse("toolu_1", "WebSearch", Input: "{}")], listener.Started);
        Assert.Equal([new ToolResult("toolu_1", "...")], listener.Finished);
    }

    [Fact]
    public async Task ReadAsync_WhenRateLimitArrives_ThenReportsUsage()
    {
        // Arrange
        var listener = new FakeListener();

        // Act
        await new ClaudeSession(Output(_rateLimit), new StringWriter(), listener).ReadAsync();

        // Assert
        Assert.Equal([new Usage(0.03, 0.59)], listener.Usages);
    }

    [Fact]
    public async Task ReadAsync_WhenDetached_ThenIgnoresTheRestButCountsResults()
    {
        // Arrange
        var listener = new FakeListener();
        var session = new ClaudeSession(Output(_started, _result), new StringWriter(), listener);
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
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, _patience);

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
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, _patience);
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
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_toggle" }, _patience);

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
        var asking = session.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" }, _patience);
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
        readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public void Add(string line) => _lines.Writer.TryWrite(line);

        public void End() => _lines.Writer.Complete();

        public override async Task<string?> ReadLineAsync() => await _lines.Reader.WaitToReadAsync() && _lines.Reader.TryRead(out var line) ? line : null;
    }

    sealed class BrokenOnceWriter : StringWriter
    {
        bool _broken;

        public override void Write(string? value)
        {
            if (!_broken)
            {
                _broken = true;
                throw new IOException();
            }
            base.Write(value);
        }
    }
}
