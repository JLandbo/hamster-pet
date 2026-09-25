using System.Text.Json.Nodes;

namespace Hamster.Tests;

public class ClaudeProtocolTests
{
    const string PermissionLine = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","display_name":"Fetch","input":{"url":"https://example.com","prompt":"Titel?"},"description":"https://example.com","tool_use_id":"toolu_1"}}""";

    static PermissionRequest Permission => (PermissionRequest)ClaudeProtocol.Parse(PermissionLine).Single();

    [Fact]
    public void Parse_WhenCanUseToolRequest_ThenReturnsPermissionRequest()
    {
        // Act
        var events = ClaudeProtocol.Parse(PermissionLine);

        // Assert
        var request = Assert.IsType<PermissionRequest>(Assert.Single(events));
        Assert.Equal(("req-1", "Fetch", "url: https://example.com" + Environment.NewLine + "prompt: Titel?", "toolu_1"),
            (request.RequestId, request.ToolName, request.Details, request.ToolUseId));
    }

    [Fact]
    public void Parse_WhenCanUseToolRequestHasNoDisplayName_ThenToolNameIsShown()
    {
        // Arrange
        const string line = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"WebFetch","input":{},"tool_use_id":"toolu_1"}}""";

        // Act
        var request = (PermissionRequest)ClaudeProtocol.Parse(line).Single();

        // Assert
        Assert.Equal("WebFetch", request.ToolName);
    }

    [Fact]
    public void Parse_WhenAssistantUsesTool_ThenReturnsToolUse()
    {
        // Arrange
        const string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"Jeg søger."},{"type":"tool_use","id":"toolu_1","name":"WebSearch","input":{}}]}}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ToolUse("toolu_1", "WebSearch")], events);
    }

    [Theory]
    [InlineData("""{"file_path":"C:\\hamster\\Mood.cs","offset":10}""", @"C:\hamster\Mood.cs")]
    [InlineData("""{"command":"git log","description":"Viser historik"}""", "git log")]
    [InlineData("""{"url":"https://example.com","prompt":"Titel?"}""", "https://example.com")]
    [InlineData("""{"skill":"flow-next:prime"}""", "flow-next:prime")]
    [InlineData("""{"todos":[]}""", "")]
    public void Parse_WhenToolHasInput_ThenDetailIsWhatItWorksOn(string input, string expected)
    {
        // Arrange
        var line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"Tool","input":""" + input + "}]}}";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal(expected, Assert.IsType<ToolUse>(Assert.Single(events)).Detail);
    }

    [Theory]
    [InlineData("", "Read")]
    [InlineData("git status\ngit log", "Read: git status git log")]
    public void Description_WhenToolUsed_ThenOneLineWithNameAndDetail(string detail, string expected)
    {
        // Act
        var description = new ToolUse("toolu_1", "Read", detail).Description;

        // Assert
        Assert.Equal(expected, description);
    }

    [Theory]
    [InlineData("WebSearch", true)]
    [InlineData("WebFetch", true)]
    [InlineData("Bash", false)]
    public void IsWeb_WhenToolNamed_ThenOnlyWebToolsCount(string name, bool expected)
    {
        // Act
        var isWeb = new ToolUse("toolu_1", name).IsWeb;

        // Assert
        Assert.Equal(expected, isWeb);
    }

    [Fact]
    public void Parse_WhenToolResult_ThenReturnsToolResult()
    {
        // Arrange
        const string line = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"Example Domain"}]}}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ToolResult("toolu_1")], events);
    }

    [Fact]
    public void Parse_WhenResult_ThenReturnsTextAndSession()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"Hej!","session_id":"session-1"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ClaudeResult("session-1", "Hej!", IsError: false)], events);
    }

    [Fact]
    public void Parse_WhenResultHasCost_ThenReturnsIt()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s1","total_cost_usd":0.4970334}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal(0.4970334m, Assert.IsType<ClaudeResult>(Assert.Single(events)).Cost);
    }

    [Fact]
    public void Parse_WhenRateLimitEvent_ThenReturnsUsage()
    {
        // Arrange
        const string line = """{"type":"rate_limit_event","rate_limit_info":{"unifiedWindows":{"five_hour":{"utilization":0.03},"seven_day":{"utilization":0.59}}}}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new Usage(0.03, 0.59)], events);
    }

    [Fact]
    public void Parse_WhenRateLimitEventHasNoWindows_ThenReturnsNothing()
    {
        // Arrange
        const string line = """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("Not logged in · Please run /login", true, true)]
    [InlineData("Invalid API key · Please run /login", true, true)]
    [InlineData("Se /login-siden i appen", false, false)]
    [InlineData("Reached maximum number of turns (1)", true, false)]
    public void NeedsLogin_WhenResultArrives_ThenOnlyForLoginErrors(string text, bool isError, bool expected)
    {
        // Act
        var needsLogin = new ClaudeResult("session-1", text, isError).NeedsLogin;

        // Assert
        Assert.Equal(expected, needsLogin);
    }

    [Fact]
    public void Parse_WhenErrorResultHasErrors_ThenTextIsTheErrors()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"error_max_turns","is_error":true,"session_id":"s1","errors":["Reached maximum number of turns (1)"]}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal("Reached maximum number of turns (1)", Assert.IsType<ClaudeResult>(Assert.Single(events)).Text);
    }

    [Fact]
    public void Parse_WhenErrorResultWithoutText_ThenFallsBackToSubtype()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"error_during_execution","is_error":true,"session_id":"session-1"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ClaudeResult("session-1", "error_during_execution", IsError: true)], events);
    }

    [Fact]
    public void Parse_WhenCancelRequest_ThenReturnsCancelRequest()
    {
        // Act
        var events = ClaudeProtocol.Parse("""{"type":"control_cancel_request","request_id":"req-1"}""");

        // Assert
        Assert.Equal([new CancelRequest("req-1")], events);
    }

    [Theory]
    [InlineData("""{"type":"system","subtype":"thinking_tokens","session_id":"s1"}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("not json")]
    public void Parse_WhenIrrelevantLine_ThenReturnsNothing(string line)
    {
        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void Allow_WhenCalled_ThenEchoesRequestAndInput()
    {
        // Act
        var line = ClaudeProtocol.Allow(Permission);

        // Assert
        Assert.Equal("""{"type":"control_response","response":{"subtype":"success","request_id":"req-1","response":{"behavior":"allow","updatedInput":{"url":"https://example.com","prompt":"Titel?"}}}}""", line);
    }

    [Fact]
    public void Deny_WhenCalled_ThenReturnsDenyBehavior()
    {
        // Act
        var line = ClaudeProtocol.Deny(Permission);

        // Assert
        Assert.Equal("""{"type":"control_response","response":{"subtype":"success","request_id":"req-1","response":{"behavior":"deny","message":"Brugeren afviste."}}}""", line);
    }

    [Fact]
    public void UserMessage_WhenCalled_ThenMatchesWireFormat()
    {
        // Act
        var line = ClaudeProtocol.UserMessage("hej", [], "id-1");

        // Assert
        Assert.Equal("""{"type":"user","uuid":"id-1","message":{"role":"user","content":"hej"},"parent_tool_use_id":null,"session_id":""}""", line);
    }

    [Fact]
    public void UserMessage_WhenImageAttached_ThenSendsItInsideTheMessage()
    {
        // Act
        var line = ClaudeProtocol.UserMessage("hej", [new ImageAttachment("a.png", "image/png", [1, 2, 3])], "id-1");

        // Assert
        Assert.Equal("""{"type":"user","uuid":"id-1","message":{"role":"user","content":[{"type":"text","text":"hej"},{"type":"image","source":{"type":"base64","media_type":"image/png","data":"AQID"}}]},"parent_tool_use_id":null,"session_id":""}""", line);
    }

    [Fact]
    public void UserMessage_WhenPromptHasNewlines_ThenStaysOnOneLine()
    {
        // Act
        var line = ClaudeProtocol.UserMessage("hej\næblegrød", [], "id-1");

        // Assert
        Assert.DoesNotContain('\n', line);
        Assert.Equal("hej\næblegrød", (string?)JsonNode.Parse(line)!["message"]!["content"]);
    }

    [Fact]
    public void Arguments_WhenNoSession_ThenStartsNewSessionWithHostPermissions()
    {
        // Act
        var arguments = ClaudeProtocol.Arguments(null, ClaudeSettings.Default);

        // Assert
        Assert.Equal(
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
             "--permission-prompt-tool", "stdio", "--permission-mode", "default", "--setting-sources", "user",
             "--settings", """{"permissions":{"ask":["Skill","CronCreate"]}}""",
             "--model", "claude-opus-5-5", "--effort", "xhigh", "--append-system-prompt", ClaudeProtocol.PetInstructions,
             "--tools", "Read,Glob,Grep,Bash,PowerShell,Edit,Write,NotebookEdit,WebSearch,WebFetch,Agent,ToolSearch,EnterPlanMode,ExitPlanMode,Workflow,TaskStop,ListAgents,CronList,CronCreate,CronDelete,ReportFindings,Skill"],
            arguments);
    }

    [Fact]
    public void Arguments_WhenInstructionsGiven_ThenAppendsThemAfterThePetsOwn()
    {
        // Act
        var arguments = ClaudeProtocol.Arguments(null, ClaudeSettings.Default, "Svar kort.");

        // Assert
        Assert.Equal($"{ClaudeProtocol.PetInstructions}\n\nSvar kort.", arguments[Array.IndexOf(arguments, "--append-system-prompt") + 1]);
    }

    [Fact]
    public void Arguments_WhenSessionExists_ThenResumesIt()
    {
        // Act
        var arguments = ClaudeProtocol.Arguments("session-1", ClaudeSettings.Default);

        // Assert
        Assert.Equal("session-1", arguments[Array.IndexOf(arguments, "--resume") + 1]);
    }

    [Fact]
    public void Arguments_WhenModelAndEffortChosen_ThenPassesThem()
    {
        // Act
        var arguments = ClaudeProtocol.Arguments(null, ClaudeSettings.Default with { Model = "claude-sonnet-5", Effort = "low" });

        // Assert
        Assert.Equal(("claude-sonnet-5", "low"),
            (arguments[Array.IndexOf(arguments, "--model") + 1], arguments[Array.IndexOf(arguments, "--effort") + 1]));
    }

    [Fact]
    public void Arguments_WhenAutoModeChosen_ThenPassesIt()
    {
        // Act
        var arguments = ClaudeProtocol.Arguments(null, ClaudeSettings.Default with { PermissionMode = "auto" });

        // Assert
        Assert.Equal("auto", arguments[Array.IndexOf(arguments, "--permission-mode") + 1]);
    }

    [Fact]
    public void Parse_WhenSubagentUsesTool_ThenToolKnowsItsParent()
    {
        // Arrange
        const string line = """{"type":"assistant","parent_tool_use_id":"toolu_agent","message":{"content":[{"type":"tool_use","id":"toolu_2","name":"Read","input":{"file_path":"a.cs"}}]}}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ToolUse("toolu_2", "Read", "a.cs", "toolu_agent")], events);
    }

    [Fact]
    public void Parse_WhenCommandStarts_ThenTurnStartsForThatMessage()
    {
        // Arrange
        const string line = """{"type":"command_lifecycle","command_uuid":"id-1","state":"started","session_id":"session-1"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new TurnStarted("id-1")], events);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("completed")]
    public void Parse_WhenCommandIsQueuedOrCompleted_ThenReturnsNothing(string state)
    {
        // Arrange
        var line = $$"""{"type":"command_lifecycle","command_uuid":"id-1","state":"{{state}}"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void Parse_WhenInit_ThenTurnStartsWithoutMessage()
    {
        // Arrange
        const string line = """{"type":"system","subtype":"init","session_id":"session-1"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new TurnStarted(null)], events);
    }

    [Fact]
    public void Parse_WhenStatusReportsMode_ThenModeChanged()
    {
        // Arrange
        const string line = """{"type":"system","subtype":"status","status":null,"permissionMode":"plan"}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new ModeChanged("plan")], events);
    }

    [Fact]
    public void Parse_WhenBackgroundTasksChange_ThenCountsTheRunningTasks()
    {
        // Arrange
        const string line = """{"type":"system","subtype":"background_tasks_changed","tasks":[{"task_id":"b1","task_type":"local_bash","description":"sleep"}]}""";

        // Act
        var events = ClaudeProtocol.Parse(line);

        // Assert
        Assert.Equal([new BackgroundTasksChanged(1)], events);
    }

    [Fact]
    public void Parse_WhenResultAnswersMessages_ThenListsThem()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"Svar","session_id":"session-1","user_message_uuid":"id-1","user_message_uuids":["id-1","id-2"]}""";

        // Act
        var result = (ClaudeResult)ClaudeProtocol.Parse(line).Single();

        // Assert
        Assert.Equal(["id-1", "id-2"], result.Answers);
    }

    [Fact]
    public void Parse_WhenResultAnswersNoMessage_ThenAnswersIsNull()
    {
        // Arrange
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"Opgaven er færdig.","session_id":"session-1","origin":{"kind":"task-notification"}}""";

        // Act
        var result = (ClaudeResult)ClaudeProtocol.Parse(line).Single();

        // Assert
        Assert.Null(result.Answers);
    }

    [Fact]
    public void Changes_WhenModelEffortAndModeChange_ThenSendsOneControlRequestEach()
    {
        // Act
        var changes = ClaudeProtocol.Changes(ClaudeSettings.Default, new ClaudeSettings("claude-sonnet-5", "low", "plan"));

        // Assert
        Assert.Equal(["set_model:claude-sonnet-5", "apply_flag_settings:low", "set_permission_mode:plan"], changes.Select(Summary));
    }

    [Fact]
    public void Changes_WhenOnlyTheFolderChanges_ThenSendsNothing()
    {
        // Act
        var changes = ClaudeProtocol.Changes(ClaudeSettings.Default, ClaudeSettings.Default with { WorkingDirectory = @"C:\projekt" });

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void Interrupt_WhenCalledTwice_ThenEachRequestHasItsOwnId()
    {
        // Act
        var (first, second) = (JsonNode.Parse(ClaudeProtocol.Interrupt())!, JsonNode.Parse(ClaudeProtocol.Interrupt())!);

        // Assert
        Assert.Equal(("interrupt", "interrupt"), ((string?)first["request"]!["subtype"], (string?)second["request"]!["subtype"]));
        Assert.NotEqual((string?)first["request_id"], (string?)second["request_id"]);
    }

    [Fact]
    public void EndSession_WhenCalled_ThenAsksClaudeToEndTheSession()
    {
        // Act
        var request = JsonNode.Parse(ClaudeProtocol.EndSession())!;

        // Assert
        Assert.Equal(("control_request", "end_session"), ((string?)request["type"], (string?)request["request"]!["subtype"]));
    }

    [Fact]
    public void Withdraw_WhenCalled_ThenAsksClaudeToDropTheQueuedMessage()
    {
        // Act
        var request = JsonNode.Parse(ClaudeProtocol.Withdraw("id-2"))!["request"]!;

        // Assert
        Assert.Equal(("cancel_async_message", "id-2"), ((string?)request["subtype"], (string?)request["message_uuid"]));
    }

    [Fact]
    public void Parse_WhenControlRequestSucceeds_ThenReturnsTheReply()
    {
        // Arrange
        const string line = """{"type":"control_response","response":{"subtype":"success","request_id":"req-1","response":{"mcpServers":[]}}}""";

        // Act
        var reply = Assert.IsType<ControlReply>(Assert.Single(ClaudeProtocol.Parse(line)));

        // Assert
        Assert.Equal(("req-1", true, (string?)null), (reply.RequestId, reply.Response?["mcpServers"] is JsonArray, reply.Error));
    }

    [Fact]
    public void Parse_WhenControlRequestFails_ThenReturnsTheError()
    {
        // Arrange
        const string line = """{"type":"control_response","response":{"subtype":"error","request_id":"req-1","error":"Server not found: x"}}""";

        // Act
        var reply = Assert.IsType<ControlReply>(Assert.Single(ClaudeProtocol.Parse(line)));

        // Assert
        Assert.Equal(("req-1", "Server not found: x"), (reply.RequestId, reply.Error));
    }

    static string Summary(string line)
    {
        var request = JsonNode.Parse(line)!["request"]!;
        return $"{(string?)request["subtype"]}:{(string?)(request["model"] ?? request["settings"]?["effortLevel"] ?? request["mode"])}";
    }
}
