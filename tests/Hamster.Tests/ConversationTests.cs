using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ConversationTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly FakeClaude claude = new();
    readonly Conversation conversation;

    public ConversationTests() => conversation = new Conversation(claude, Store());

    JsonFile<SavedChats> Store() => new(Path.Combine(directory, "chats.json"), SavedChats.Empty);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeAnswers_ThenChatIsDone()
    {
        // Act
        await conversation.SendAsync("hej");

        // Assert
        var chat = Assert.Single(conversation.Chats);
        Assert.Equal(("Svar", ChatStatus.Done), (chat.Answer, chat.Status));
    }

    [Fact]
    public async Task SendAsync_WhenATitleIsGiven_ThenTheChatShowsItButClaudeGetsThePrompt()
    {
        // Act
        await conversation.SendAsync("Giv mig et overblik over mine sager.", title: "Dagens overblik");

        // Assert
        var chat = conversation.Chats.Single();
        Assert.Equal(("Dagens overblik", "Giv mig et overblik over mine sager.", "Giv mig et overblik over mine sager."), (chat.DisplayPrompt, chat.Prompt, claude.Prompts.Single()));
    }

    [Fact]
    public async Task SendAsync_WhenCancelledWhileClaudeStarts_ThenSendsNothing()
    {
        // Arrange
        claude.Starting = conversation.Cancel;

        // Act
        await conversation.SendAsync("hej");

        // Assert
        Assert.Equal((0, "Afbrudt."), (claude.Ids.Count, conversation.Chats.Single().Answer));
    }

    [Fact]
    public async Task Cancel_WhenNoTurnHasStarted_ThenWithdrawsTheMessage()
    {
        // Arrange
        claude.Reply = Silent;
        await conversation.SendAsync("hej");

        // Act
        conversation.Cancel();

        // Assert
        Assert.Equal((claude.Ids[0], "Afbrudt.", false), (claude.Withdrawn.Single(), conversation.Chats.Single().Answer, conversation.IsBusy));
    }

    [Fact]
    public async Task SendAsync_WhenCalledTwice_ThenBothGoToTheSameClaude()
    {
        // Act
        await conversation.SendAsync("hej");
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null], claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenBusy_ThenSendsRightAway()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("første");

        // Act
        await conversation.SendAsync("anden");

        // Assert
        Assert.Equal(2, claude.Ids.Count);
    }

    [Fact]
    public async Task StartAsync_WhenSessionWasSaved_ThenResumesIt()
    {
        // Arrange
        await conversation.SendAsync("hej");
        var restarted = new Conversation(claude, Store());

        // Act
        await restarted.StartAsync();

        // Assert
        Assert.Equal([null, "session-1"], claude.Starts);
    }

    [Fact]
    public async Task Switch_WhenTheTargetWasUsedBefore_ThenShowsItsChatsAndResumesItsSession()
    {
        // Arrange
        await conversation.SendAsync("hej");
        var folder = new JsonFile<SavedChats>(Path.Combine(directory, "folder.json"), SavedChats.Empty);
        folder.Save(new SavedChats("session-folder", [new ChatRecord("mappe", "svar", ChatStatus.Done)]));

        // Act
        conversation.Switch(() => folder);

        // Assert
        Assert.Equal(("mappe", "session-folder"), (Assert.Single(conversation.Chats).Prompt, claude.Starts[^1]));
    }

    [Fact]
    public async Task Switch_WhenSwitchingBack_ThenTheFirstChatsAndSessionComeBack()
    {
        // Arrange
        await conversation.SendAsync("hej");
        conversation.Switch(() => new JsonFile<SavedChats>(Path.Combine(directory, "folder.json"), SavedChats.Empty));

        // Act
        conversation.Switch(Store);

        // Assert
        Assert.Equal(("hej", "session-1"), (Assert.Single(conversation.Chats).Prompt, claude.Starts[^1]));
    }

    [Fact]
    public async Task Switch_WhenTheChatChangedAfterItsTurn_ThenTheChangeIsKept()
    {
        // Arrange
        await conversation.SendAsync("hej");
        conversation.ToolStarted(new ToolUse("toolu_late", "Bash", "dotnet test"));
        conversation.Switch(() => new JsonFile<SavedChats>(Path.Combine(directory, "folder.json"), SavedChats.Empty));

        // Act
        conversation.Switch(Store);

        // Assert
        Assert.Equal(["Bash: dotnet test"], Assert.Single(conversation.Chats).Commands.Lines);
    }

    [Fact]
    public async Task Switch_WhenTheTargetIsChosen_ThenTheCurrentChatsAreAlreadySaved()
    {
        // Arrange
        await conversation.SendAsync("hej");
        conversation.ToolStarted(new ToolUse("toolu_late", "Bash", "dotnet test"));
        IReadOnlyList<string>? saved = null;

        // Act
        conversation.Switch(() =>
        {
            saved = Store().Load().Chats.Single().Commands;
            return null;
        });

        // Assert
        Assert.Equal(["Bash: dotnet test"], saved);
    }

    [Fact]
    public async Task Switch_WhenLeavingATemporaryChat_ThenItsChatsAreNotSaved()
    {
        // Arrange
        await conversation.SendAsync("hej");
        conversation.Switch(() => null);
        await conversation.SendAsync("midlertidig");

        // Act
        conversation.Switch(Store);

        // Assert
        Assert.Equal("hej", Assert.Single(conversation.Chats).Prompt);
    }

    [Fact]
    public async Task Switch_WhenThePlaceIsOwnedElsewhere_ThenStartsAnEmptyTemporaryChat()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Switch(() => null);

        // Assert
        Assert.Equal((true, 0), (conversation.IsTemporary, conversation.Chats.Count));
        Assert.Equal([null, null], claude.Starts);
    }

    [Fact]
    public void Switch_WhenTheTargetHasOldUsage_ThenKeepsTheCurrentUsage()
    {
        // Arrange
        conversation.UsageReported(new Usage(0.5, 0.2));
        var folder = new JsonFile<SavedChats>(Path.Combine(directory, "folder.json"), SavedChats.Empty);
        folder.Save(SavedChats.Empty with { Usage = new Usage(0.1, 0.1) });

        // Act
        conversation.Switch(() => folder);

        // Assert
        Assert.Equal(new Usage(0.5, 0.2), conversation.Usage);
    }

    [Fact]
    public async Task StartAsync_WhenClaudeStarts_ThenSaysSo()
    {
        // Arrange
        var started = 0;
        conversation.Started += () => started++;

        // Act
        await conversation.StartAsync();

        // Assert
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeMustStart_ThenSaysSoOnce()
    {
        // Arrange
        var started = 0;
        conversation.Started += () => started++;

        // Act
        await conversation.SendAsync("hej");
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task ResultReceived_WhenClaudeCannotResume_ThenWaitingChatShowsWhy()
    {
        // Arrange
        var restarted = await RestartedWithWaitingChat();

        // Act
        claude.Listener.ResultReceived(CannotResume);

        // Assert
        Assert.Equal(("No conversation found", ChatStatus.Error), (restarted.Chats[1].Answer, restarted.Chats[1].Status));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeCouldNotResume_ThenStartsANewSession()
    {
        // Arrange
        var restarted = await RestartedWithWaitingChat();
        claude.Listener.ResultReceived(CannotResume);
        claude.IsRunning = false;
        claude.Reply = Answer;

        // Act
        await restarted.SendAsync("forfra");

        // Assert
        Assert.Equal([null, "session-1", null], claude.Starts);
    }

    [Fact]
    public async Task ResultReceived_WhenClaudeCannotResumeBeforeAnyMessage_ThenAddsNoChat()
    {
        // Arrange
        await conversation.SendAsync("hej");
        var restarted = new Conversation(claude, Store());
        await restarted.StartAsync();

        // Act
        claude.Listener.ResultReceived(CannotResume);

        // Assert
        Assert.Single(restarted.Chats);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeFails_ThenChatShowsError()
    {
        // Arrange
        claude.Reply = (_, _) => throw new IOException("pipe brudt");

        // Act
        await conversation.SendAsync("hej");

        // Assert
        var chat = Assert.Single(conversation.Chats);
        Assert.Equal(ChatStatus.Error, chat.Status);
        Assert.Contains("pipe brudt", chat.Answer);
    }

    [Fact]
    public async Task SendAsync_WhenMoreThanMaxChats_ThenKeepsTheNewest()
    {
        // Act
        for (var i = 0; i <= Conversation.MaxChats; i++)
            await conversation.SendAsync($"{i}");

        // Assert
        Assert.Equal((Conversation.MaxChats, "1"), (conversation.Chats.Count, conversation.Chats[0].Prompt));
    }

    [Fact]
    public async Task Constructor_WhenChatsWereSaved_ThenRestoresChats()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(claude, Store());

        // Assert
        var chat = Assert.Single(restarted.Chats);
        Assert.Equal(("hej", "Svar", ChatStatus.Done), (chat.Prompt, chat.Answer, chat.Status));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenAsked_ThenWaitsForUser()
    {
        // Arrange
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            _ = listener.AskPermissionAsync(Request(), CancellationToken.None);
        };

        // Act
        await conversation.SendAsync("hej");

        // Assert
        Assert.True(conversation.IsWaitingForUser);
    }

    [Theory(Timeout = 5_000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AskPermissionAsync_WhenUserAnswers_ThenClaudeGetsTheAnswer(bool allowed)
    {
        // Arrange
        var asking = await Asking();

        // Act
        conversation.Chats[0].Requests[0].Respond(allowed);

        // Assert
        Assert.Equal(allowed ? PermissionAnswer.Allow : PermissionAnswer.Deny, await asking.WaitAsync(Token));
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenUserAllowsAlways_ThenClaudeIsToldSo()
    {
        // Arrange
        var asking = await Asking();

        // Act
        conversation.Chats[0].Requests[0].RespondAlways();

        // Assert
        Assert.Equal(PermissionAnswer.AllowAlways, await asking.WaitAsync(Token));
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("""[{"type":"addRules","rules":[{"toolName":"Bash","ruleContent":"git push:*"}],"behavior":"allow","destination":"localSettings"}]""", true)]
    public void AskPermissionAsync_WhenClaudeSuggestsRules_ThenOffersAllowAlwaysOnlyIfThereAreAny(string suggestions, bool expected)
    {
        // Arrange
        var request = Request() with { Suggestions = JsonNode.Parse(suggestions)!.AsArray() };

        // Act
        _ = conversation.AskPermissionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(expected, conversation.Chats.Single().Requests.Single().CanAllowAlways);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenUserDenies_ThenCommandsShowIt()
    {
        // Arrange
        var asking = await Asking();

        // Act
        conversation.Chats[0].Requests[0].Respond(false);
        await asking.WaitAsync(Token);

        // Assert
        Assert.Equal(["Afvist: Bash"], conversation.Chats[0].Commands.Lines);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenClaudeWithdraws_ThenRequestIsRemoved()
    {
        // Arrange
        using var withdrawal = new CancellationTokenSource();
        Task<PermissionAnswer>? asking = null;
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            asking = listener.AskPermissionAsync(Request(), withdrawal.Token);
        };
        await conversation.SendAsync("hej");

        // Act
        withdrawal.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking!).WaitAsync(Token);
        Assert.Empty(conversation.Chats[0].Requests);
    }

    [Fact(Timeout = 5_000)]
    public async Task Delete_WhenChatAsksPermission_ThenClaudeGetsNo()
    {
        // Arrange
        var asking = await Asking();

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Equal(PermissionAnswer.Deny, await asking.WaitAsync(Token));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenNoTurnIsRunning_ThenAsksInTheLatestChat()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        _ = conversation.AskPermissionAsync(Request(), CancellationToken.None);

        // Assert
        Assert.Single(conversation.Chats[0].Requests);
    }

    [Fact]
    public async Task ToolStarted_WhenSubagentWorksAfterTheAnswer_ThenShowsInTheChatThatStartedIt()
    {
        // Arrange
        await StartedSubagent();

        // Act
        claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir", ParentId: "toolu_agent"));

        // Assert
        Assert.Equal(["Agent: Undersøg", "Bash: dir"], conversation.Chats[0].Commands.Lines);
    }

    [Fact]
    public async Task AskPermissionAsync_WhenSubagentAsksAfterTheAnswer_ThenAsksInTheChatThatStartedIt()
    {
        // Arrange
        await StartedSubagent();
        claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir", ParentId: "toolu_agent"));

        // Act
        _ = conversation.AskPermissionAsync(Request() with { ToolUseId = "toolu_2" }, CancellationToken.None);

        // Assert
        Assert.Equal((1, 0), (conversation.Chats[0].Requests.Count, conversation.Chats[1].Requests.Count));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenTheChatThatStartedTheSubagentIsDeleted_ThenAsksInTheLatestChat()
    {
        // Arrange
        await StartedSubagent();
        conversation.Delete(conversation.Chats[0]);

        // Act
        _ = conversation.AskPermissionAsync(Request() with { ToolUseId = "toolu_agent" }, CancellationToken.None);

        // Assert
        Assert.Single(conversation.Chats[0].Requests);
    }

    [Fact]
    public async Task ResultReceived_WhenBackgroundTaskFinishes_ThenShowsTheAnswerInANewChat()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        claude.Listener.TurnStarted(null);
        claude.Listener.ResultReceived(new ClaudeResult("session-1", "Opgaven er færdig.", IsError: false));

        // Assert
        var chat = conversation.Chats[^1];
        Assert.Equal((Conversation.BackgroundPrompt, "Opgaven er færdig.", ChatStatus.Done), (chat.Prompt, chat.Answer, chat.Status));
    }

    [Fact]
    public async Task ToolStarted_WhenBackgroundTurnWorks_ThenItsChatShowsTheCommands()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.Listener.TurnStarted(null);

        // Act
        claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir"));

        // Assert
        Assert.Equal(Conversation.BackgroundPrompt, conversation.Chats[^1].Prompt);
        Assert.Equal(["Bash: dir"], conversation.Chats[^1].Commands.Lines);
    }

    [Fact]
    public async Task ToolStarted_WhenABackgroundTurnHasEnded_ThenAddsNoChat()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.Listener.TurnStarted(null);
        claude.Listener.ResultReceived(new ClaudeResult("session-1", "Opgaven er færdig.", IsError: false));

        // Act
        claude.Listener.ToolStarted(new ToolUse("toolu_9", "Read", "a.cs"));

        // Assert
        Assert.Equal(2, conversation.Chats.Count);
    }

    [Fact]
    public async Task ResultReceived_WhenMessagesWereAnsweredTogether_ThenTheFirstGetsTheAnswer()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("første");
        await conversation.SendAsync("anden");

        // Act
        claude.Listener.ResultReceived(Answered with { Answers = [.. claude.Ids] });

        // Assert
        Assert.Equal(["Svar", Conversation.AnsweredAbove], conversation.Chats.Select(chat => chat.Answer));
        Assert.False(conversation.IsBusy);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenForgetsChatsAndStartsANewSession()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();

        // Assert
        Assert.Equal([null, null], claude.Starts);
        Assert.Empty(conversation.Chats);
    }

    [Fact]
    public async Task SendAsync_WhenClear_ThenStartsANewSessionWithoutSendingIt()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        await conversation.SendAsync("/clear");

        // Assert
        Assert.Equal([null, null], claude.Starts);
        Assert.Single(claude.Ids);
        Assert.Empty(conversation.Chats);
    }

    [Fact]
    public async Task Reset_WhenRunning_ThenIsNoLongerBusy()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();

        // Assert
        Assert.False(conversation.IsBusy);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenCostIsZero()
    {
        // Arrange
        claude.Reply = Answering(Answered with { Cost = 0.36m });
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();

        // Assert
        Assert.Equal(0m, conversation.Cost);
    }

    [Fact]
    public async Task IsCurrent_WhenChatIsRunning_ThenTrue()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        var current = conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow);

        // Assert
        Assert.True(current);
    }

    [Fact]
    public async Task IsCurrent_WhenChatAsksPermission_ThenTrueLongAfterItsAnswer()
    {
        // Arrange
        await conversation.SendAsync("hej");
        _ = conversation.AskPermissionAsync(Request(), CancellationToken.None);

        // Act
        var current = conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.True(current);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public async Task IsCurrent_WhenAnswered_ThenShownFor30Seconds(int secondsLater, bool expected)
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        var current = conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow.AddSeconds(secondsLater));

        // Assert
        Assert.Equal(expected, current);
    }

    [Fact]
    public async Task IsCurrent_WhenANewerChatWasAnswered_ThenOlderChatIsHidden()
    {
        // Arrange
        await conversation.SendAsync("første");
        await conversation.SendAsync("anden");

        // Act
        var current = conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow);

        // Assert
        Assert.False(current);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(5, false)]
    public async Task AnsweredWithin_WhenAnswered_ThenTrueOnlyWithinTheTime(int secondsLater, bool expected)
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        var answered = conversation.AnsweredWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow.AddSeconds(secondsLater));

        // Assert
        Assert.Equal(expected, answered);
    }

    [Fact]
    public async Task AnsweredWithin_WhenClaudeFailed_ThenFalse()
    {
        // Arrange
        claude.Reply = Answering(new ClaudeResult("session-1", "Fejl", IsError: true));
        await conversation.SendAsync("hej");

        // Act
        var answered = conversation.AnsweredWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow);

        // Assert
        Assert.False(answered);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeReportsCost_ThenCostIsTheSessionTotal()
    {
        // Arrange
        claude.Reply = Answering(Answered with { Cost = 0.36m });
        await conversation.SendAsync("hej");
        claude.Reply = Answering(Answered with { Cost = 0.5m });

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal(0.5m, conversation.Cost);
    }

    [Fact]
    public async Task ResultReceived_WhenTheSameSessionReportsLess_ThenKeepsTheHigherCost()
    {
        // Arrange
        claude.Reply = Answering(Answered with { Cost = 0.5m });
        await conversation.SendAsync("hej");
        claude.Reply = Answering(Answered with { Cost = 0.3m });

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal(0.5m, conversation.Cost);
    }

    [Fact]
    public async Task Constructor_WhenCostWasSaved_ThenRestoresCost()
    {
        // Arrange
        claude.Reply = Answering(Answered with { Cost = 0.36m });
        await conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(claude, Store());

        // Assert
        Assert.Equal(0.36m, restarted.Cost);
    }

    [Fact]
    public async Task Delete_WhenCalled_ThenChatIsGoneAfterRestart()
    {
        // Arrange
        await conversation.SendAsync("slet mig");
        await conversation.SendAsync("behold mig");

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Equal(["behold mig"], new Conversation(claude, Store()).Chats.Select(chat => chat.Prompt));
    }

    [Fact]
    public async Task Delete_WhenChatIsRunning_ThenInterruptsClaude()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Equal((1, 0), (claude.Interrupts, conversation.Chats.Count));
    }

    [Fact]
    public async Task Delete_WhenChatWaitsForItsTurn_ThenWithdrawsItFromClaude()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("første");
        await conversation.SendAsync("anden");

        // Act
        conversation.Delete(conversation.Chats[1]);

        // Assert
        Assert.Equal([claude.Ids[1]], claude.Withdrawn);
        Assert.Equal(0, claude.Interrupts);
    }

    [Fact]
    public async Task ResultReceived_WhenAReminderFires_ThenShowsItInANewChat()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.Listener.TurnStarted("reminder-id");

        // Act
        claude.Listener.ResultReceived(new ClaudeResult("session-1", "Husk at drikke vand!", IsError: false));

        // Assert
        Assert.Equal((Conversation.BackgroundPrompt, "Husk at drikke vand!"), (conversation.Chats[^1].Prompt, conversation.Chats[^1].Answer));
    }

    [Fact]
    public async Task Delete_WhenAnotherChatIsRunning_ThenRunningChatIsNotSaved()
    {
        // Arrange
        await conversation.SendAsync("færdig");
        claude.Reply = Started;
        await conversation.SendAsync("kører");

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Empty(new Conversation(claude, Store()).Chats);
    }

    [Fact]
    public async Task ResultReceived_WhenRunEndsDuringWebSearch_ThenStopsBrowsingWeb()
    {
        // Arrange
        var browsing = false;
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_1", "WebSearch"));
            browsing = conversation.IsBrowsingWeb;
            listener.ResultReceived(Answered with { Answers = [id] });
        };

        // Act
        await conversation.SendAsync("søg");

        // Assert
        Assert.Equal((true, false), (browsing, conversation.IsBrowsingWeb));
    }

    [Fact]
    public async Task ToolStarted_WhenRunning_ThenChatShowsCommandsAndSources()
    {
        // Arrange
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_1", "Read", "Mood.cs"));
            listener.ToolStarted(new ToolUse("toolu_2", "WebSearch", "hamstere"));
            listener.ToolStarted(new ToolUse("toolu_3", "Bash", "git log"));
        };

        // Act
        await conversation.SendAsync("hvad er nyt?");

        // Assert
        Assert.Equal(["Read: Mood.cs", "Bash: git log"], conversation.Chats[0].Commands.Lines);
        Assert.Equal(["WebSearch: hamstere"], conversation.Chats[0].Sources.Lines);
    }

    [Fact]
    public async Task SendAsync_WhenNotLoggedIn_ThenChatOffersLogin()
    {
        // Arrange
        claude.Reply = Answering(new ClaudeResult("session-1", "Not logged in · Please run /login", IsError: true));

        // Act
        await conversation.SendAsync("hej");

        // Assert
        Assert.Equal((true, "Du er ikke logget ind i claude."), (conversation.Chats[0].NeedsLogin, conversation.Chats[0].Answer));
    }

    [Fact]
    public async Task Constructor_WhenUsageWasSaved_ThenRestoresUsage()
    {
        // Arrange
        claude.Reply = (listener, id) =>
        {
            listener.UsageReported(new Usage(0.03, 0.59));
            Answer(listener, id);
        };
        await conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(claude, Store());

        // Assert
        Assert.Equal(new Usage(0.03, 0.59), restarted.Usage);
    }

    [Fact]
    public async Task Cancel_WhenRunning_ThenChatIsInterruptedAndKeepsTheCost()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        conversation.Cancel();
        claude.Listener.ResultReceived(Stopped with { Answers = [claude.Ids[0]] });

        // Assert
        var chat = Assert.Single(conversation.Chats);
        Assert.Equal((1, "Afbrudt.", ChatStatus.Error, 0.2m), (claude.Interrupts, chat.Answer, chat.Status, conversation.Cost));
    }

    [Fact]
    public async Task Cancel_WhenNothingRuns_ThenDoesNotInterrupt()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Cancel();

        // Assert
        Assert.Equal(0, claude.Interrupts);
    }

    [Fact]
    public async Task Cancel_WhenAnswerAlreadyArrived_ThenKeepsTheAnswer()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        conversation.Cancel();
        claude.Listener.ResultReceived(Answered with { Answers = [claude.Ids[0]] });

        // Assert
        Assert.Equal(("Svar", ChatStatus.Done), (conversation.Chats[0].Answer, conversation.Chats[0].Status));
    }

    [Fact]
    public async Task Exited_WhenClaudeDies_ThenWaitingChatShowsWhy()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        claude.Listener.Exited("claude stoppede uventet");

        // Assert
        Assert.Equal(("claude stoppede uventet", ChatStatus.Error), (conversation.Chats[0].Answer, conversation.Chats[0].Status));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeHasExited_ThenResumesTheSession()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.IsRunning = false;
        claude.Listener.Exited("claude stoppede uventet");

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], claude.Starts);
    }

    [Fact]
    public async Task Exited_WhenClaudeWasKilledAfterStop_ThenChatSaysInterrupted()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Cancel();

        // Act
        claude.Listener.Exited("killed");

        // Assert
        Assert.Equal("Afbrudt.", conversation.Chats[0].Answer);
    }

    [Fact]
    public void BackgroundTasksChanged_WhenTasksRun_ThenCountsThem()
    {
        // Act
        conversation.BackgroundTasksChanged(2);

        // Assert
        Assert.Equal(2, conversation.BackgroundTasks);
    }

    [Fact]
    public void Exited_WhenBackgroundTasksRan_ThenNoneRunAnymore()
    {
        // Arrange
        conversation.BackgroundTasksChanged(2);

        // Act
        conversation.Exited("claude stoppede uventet");

        // Assert
        Assert.Equal(0, conversation.BackgroundTasks);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task FailedWithin_WhenAnswered_ThenTrueOnlyForErrors(bool isError, bool expected)
    {
        // Arrange
        claude.Reply = Answering(new ClaudeResult("session-1", "Svar", isError));
        await conversation.SendAsync("hej");

        // Act
        var failed = conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow);

        // Assert
        Assert.Equal(expected, failed);
    }

    [Fact]
    public async Task ResultReceived_WhenNoChatGetsTheAnswer_ThenAnsweredAtStays()
    {
        // Arrange
        await conversation.SendAsync("hej");
        var answeredAt = conversation.AnsweredAt;

        // Act
        claude.Listener.ResultReceived(new ClaudeResult("session-1", "", IsError: false));

        // Assert
        Assert.Equal(answeredAt, conversation.AnsweredAt);
    }

    [Fact]
    public void BackgroundTasksChanged_WhenCalled_ThenTellsTheWindow()
    {
        // Arrange
        var changed = false;
        conversation.Changed += () => changed = true;

        // Act
        conversation.BackgroundTasksChanged(1);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void Reset_WhenBackgroundTasksRan_ThenNoneRunAnymore()
    {
        // Arrange
        conversation.BackgroundTasksChanged(2);

        // Act
        conversation.Reset();

        // Assert
        Assert.Equal(0, conversation.BackgroundTasks);
    }

    [Fact]
    public async Task Reset_WhenAnswered_ThenNothingIsNew()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();

        // Assert
        Assert.Equal(default(DateTime), conversation.AnsweredAt);
    }

    [Fact]
    public async Task Delete_WhenTheAnsweredChatIsDeleted_ThenNothingIsNew()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Equal(default(DateTime), conversation.AnsweredAt);
    }

    [Fact]
    public async Task ResultReceived_WhenUserStopped_ThenIsNeitherSadNorNew()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Cancel();

        // Act
        claude.Listener.ResultReceived(Stopped with { Answers = [claude.Ids[0]] });

        // Assert
        Assert.Equal((false, default(DateTime)), (conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow), conversation.AnsweredAt));
    }

    [Fact]
    public async Task Delete_WhenAnotherChatIsDeleted_ThenTheAnswerStaysNew()
    {
        // Arrange
        await conversation.SendAsync("første");
        await conversation.SendAsync("anden");
        var answeredAt = conversation.AnsweredAt;

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Equal(answeredAt, conversation.AnsweredAt);
    }

    [Fact]
    public async Task ResultReceived_WhenTheRunningChatWasDeleted_ThenNothingIsNew()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Delete(conversation.Chats[0]);

        // Act
        claude.Listener.ResultReceived(Answered with { Answers = [claude.Ids[0]] });

        // Assert
        Assert.Equal(default(DateTime), conversation.AnsweredAt);
    }

    [Fact]
    public async Task Exited_WhenClaudeWasKilledAfterStop_ThenIsNeitherSadNorNew()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Cancel();

        // Act
        claude.Listener.Exited("killed");

        // Assert
        Assert.Equal((false, default(DateTime)), (conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow), conversation.AnsweredAt));
    }

    [Fact]
    public async Task Exited_WhenMessagesWait_ThenTheRunningChatIsTheOneShown()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("kører");
        await conversation.SendAsync("venter");

        // Act
        claude.Listener.Exited("claude stoppede uventet");

        // Assert
        Assert.Equal((true, false), (conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow), conversation.IsCurrent(conversation.Chats[1], DateTime.UtcNow)));
    }

    [Fact]
    public async Task Exited_WhenClaudeDies_ThenIsSad()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");

        // Act
        claude.Listener.Exited("claude stoppede uventet");

        // Assert
        Assert.True(conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeFails_ThenIsSad()
    {
        // Arrange
        claude.Reply = (_, _) => throw new IOException("pipe brudt");

        // Act
        await conversation.SendAsync("hej");

        // Assert
        Assert.True(conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow));
    }

    [Fact]
    public async Task FailedWithin_WhenTheTimeHasPassed_ThenFalse()
    {
        // Arrange
        claude.Reply = Answering(new ClaudeResult("session-1", "Fejl", IsError: true));
        await conversation.SendAsync("hej");

        // Act
        var failed = conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow.AddSeconds(5));

        // Assert
        Assert.False(failed);
    }

    [Fact]
    public void ModeChanged_WhenClaudeSwitchesMode_ThenTellsTheWindow()
    {
        // Arrange
        string? mode = null;
        conversation.PermissionModeChanged += changed => mode = changed;

        // Act
        conversation.ModeChanged("plan");

        // Assert
        Assert.Equal("plan", mode);
    }

    [Theory]
    [InlineData("Hvad er det?", "Hvad er det?")]
    [InlineData("", "Se på de vedhæftede filer.")]
    public void WithAttachments_WhenFilesAttached_ThenListsThemAfterTheText(string text, string expectedText)
    {
        // Act
        var prompt = Conversation.WithAttachments(text, ["a.pdf", "b.txt"], []);

        // Assert
        Assert.Equal($"{expectedText}\n\nVedhæftede filer:\na.pdf\nb.txt", prompt);
    }

    [Fact]
    public void WithAttachments_WhenNoFiles_ThenKeepsTheText()
    {
        // Act
        var prompt = Conversation.WithAttachments("hej", [], []);

        // Assert
        Assert.Equal("hej", prompt);
    }

    [Fact]
    public void WithAttachments_WhenImagesAttached_ThenNamesThem()
    {
        // Act
        var prompt = Conversation.WithAttachments("Hvad ser du?", [], [new ImageAttachment("screenshot.png", "image/png", [])]);

        // Assert
        Assert.Equal("Hvad ser du?\n\nVedhæftede billeder:\nscreenshot.png", prompt);
    }

    [Fact]
    public async Task SendAsync_WhenImagesAttached_ThenClaudeGetsThem()
    {
        // Arrange
        ImageAttachment image = new("screenshot.png", "image/png", [1, 2, 3]);

        // Act
        await conversation.SendAsync("Hvad ser du?", [image]);

        // Assert
        Assert.Equal([image], claude.Images);
    }

    async Task<Task<PermissionAnswer>> Asking()
    {
        Task<PermissionAnswer>? asking = null;
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            asking = listener.AskPermissionAsync(Request(), CancellationToken.None);
        };
        await conversation.SendAsync("hej");
        return asking!;
    }

    async Task<Conversation> RestartedWithWaitingChat()
    {
        await conversation.SendAsync("hej");
        var restarted = new Conversation(claude, Store());
        claude.IsRunning = false;
        claude.Reply = Silent;
        await restarted.SendAsync("igen");
        return restarted;
    }

    async Task StartedSubagent()
    {
        claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_agent", "Agent", "Undersøg"));
            listener.ResultReceived(Answered with { Answers = [id] });
        };
        await conversation.SendAsync("første");
        claude.Reply = Answer;
        await conversation.SendAsync("anden");
    }

    [Fact]
    public async Task Restart_WhenIdle_ThenStartsClaudeAgainWithTheSession()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Restart();

        // Assert
        Assert.Equal([null, "session-1"], claude.Starts);
    }

    [Fact]
    public async Task Restart_WhenBusy_ThenKeepsClaudeRunningWhenTheTurnEnds()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Restart();

        // Act
        claude.Listener.ResultReceived(Answered with { Answers = [claude.Ids[0]] });

        // Assert
        Assert.Equal([null], claude.Starts);
    }

    [Fact]
    public async Task Restart_WhenBackgroundTasksRun_ThenKeepsClaudeRunning()
    {
        // Arrange
        await conversation.SendAsync("hej");
        conversation.BackgroundTasksChanged(1);

        // Act
        conversation.Restart();

        // Assert
        Assert.Equal([null], claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenARestartWaitsWhileBusy_ThenDoesNotRestart()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Restart();

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null], claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenTheWaitingRestartIsDone_ThenDoesNotRestartAgain()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Restart();
        claude.Listener.ResultReceived(Answered with { Answers = [claude.Ids[0]] });
        claude.Reply = Answer;
        await conversation.SendAsync("igen");

        // Act
        await conversation.SendAsync("tredje");

        // Assert
        Assert.Equal([null, "session-1"], claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenARestartWaits_ThenStartsClaudeAgain()
    {
        // Arrange
        claude.Reply = Started;
        await conversation.SendAsync("hej");
        conversation.Restart();
        claude.Listener.ResultReceived(Answered with { Answers = [claude.Ids[0]] });

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], claude.Starts);
    }

    static PermissionRequest Request() => new("req-1", "Bash", new JsonObject { ["command"] = "dir" });

    static readonly ClaudeResult Answered = new("session-1", "Svar", IsError: false);

    static readonly ClaudeResult Stopped = new("session-1", "error_during_execution", IsError: true, Cost: 0.2m);

    static readonly ClaudeResult CannotResume = new("session-1", "No conversation found", IsError: true);

    static Action<IClaudeListener, string> Answering(ClaudeResult result) => (listener, id) =>
    {
        listener.TurnStarted(id);
        listener.ResultReceived(result with { Answers = [id] });
    };

    static void Answer(IClaudeListener listener, string id) => Answering(Answered)(listener, id);

    static void Started(IClaudeListener listener, string id) => listener.TurnStarted(id);

    static void Silent(IClaudeListener listener, string id)
    {
    }

    sealed class FakeClaude : IClaudeClient
    {
        public Action<IClaudeListener, string> Reply { get; set; } = Answer;
        public List<string?> Starts { get; } = [];
        public List<string> Ids { get; } = [];
        public List<string> Prompts { get; } = [];
        public List<ImageAttachment> Images { get; } = [];
        public List<string> Withdrawn { get; } = [];
        public int Interrupts { get; private set; }
        public bool IsRunning { get; set; }
        public IClaudeListener Listener { get; private set; } = null!;
        public Action? Starting { get; set; }
        public ClaudeSettings Settings { get; set; } = ClaudeSettings.Default;

        public Task StartAsync(string? sessionId, IClaudeListener listener)
        {
            Starts.Add(sessionId);
            (Listener, IsRunning) = (listener, true);
            Starting?.Invoke();
            return Task.CompletedTask;
        }

        public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images)
        {
            Ids.Add(id);
            Prompts.Add(prompt);
            Images.AddRange(images);
            Reply(Listener, id);
            return Task.CompletedTask;
        }

        public Task<JsonObject?> RequestAsync(JsonObject request) => Task.FromResult<JsonObject?>(null);

        public void Interrupt() => Interrupts++;

        public void Withdraw(string id) => Withdrawn.Add(id);

        public void End() => IsRunning = false;
    }
}
