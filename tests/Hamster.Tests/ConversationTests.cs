using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ConversationTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly FakeClaude _claude = new();
    readonly Conversation _conversation;
    static readonly ClaudeResult _answered = new("session-1", "Svar", IsError: false);
    static readonly ClaudeResult _stopped = new("session-1", "error_during_execution", IsError: true, Cost: 0.2m);
    static readonly ClaudeResult _cannotResume = new("session-1", "No conversation found", IsError: true);

    public ConversationTests() => _conversation = new Conversation(_claude, Store());

    JsonFile<SavedChats> Store() => new(Path.Combine(_directory, "chats.json"), SavedChats.Empty);

    JsonFile<SavedChats> Folder() => new(Path.Combine(_directory, "folder.json"), SavedChats.Empty);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_WhenClaudeAnswers_ThenChatIsDone()
    {
        // Act
        await _conversation.SendAsync("hej");

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal(("Svar", ChatStatus.Done), (chat.Answer, chat.Status));
    }

    [Fact]
    public async Task PartialMessageReceived_WhenClaudeWrites_ThenShowsDraftUntilTheFinalAnswerArrives()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        var chat = Assert.Single(_conversation.Chats);

        // Act
        _claude.Listener.PartialMessageStarted(_claude.Ids[0]);
        _claude.Listener.PartialMessageReceived("Delvist ");
        _claude.Listener.PartialMessageReceived("svar");
        chat.RefreshDisplayAnswer();
        var draft = chat.DisplayAnswer;
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal(("Delvist svar", "Svar", "Svar", ChatStatus.Done), (draft, chat.Answer, chat.DisplayAnswer, chat.Status));
        Assert.Equal("Svar", new Conversation(new FakeClaude(), Store()).Chats.Single().Answer);
    }

    [Fact]
    public async Task AssistantTextReceived_WhenClaudeWritesAroundAToolCall_ThenKeepsBothMessagesWithoutDuplicatingTheResult()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        var chat = Assert.Single(_conversation.Chats);

        // Act
        _claude.Listener.PartialMessageStarted(_claude.Ids[0]);
        _claude.Listener.PartialMessageReceived("Jeg undersøger det.");
        _claude.Listener.AssistantTextReceived("Jeg undersøger det.");
        _claude.Listener.PartialMessageStarted(_claude.Ids[0]);
        _claude.Listener.PartialMessageReceived("Her er svaret.");
        _claude.Listener.AssistantTextReceived("Her er svaret.");
        _claude.Listener.ResultReceived(_answered with { Text = "Her er svaret.", Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal(("Jeg undersøger det.\n\nHer er svaret.", ChatStatus.Done), (chat.Answer, chat.Status));
    }

    [Fact]
    public async Task AssistantTextReceived_WhenPartialMessagesAreDisabled_ThenKeepsTheAnswerHiddenUntilTheTurnFinishes()
    {
        // Arrange
        _claude.Settings = _claude.Settings with { EnablePartialMessages = false };
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        var chat = Assert.Single(_conversation.Chats);

        // Act
        _claude.Listener.AssistantTextReceived("Hele svaret");
        var whileBusy = chat.DisplayAnswer;
        _claude.Listener.ResultReceived(_answered with { Text = "Hele svaret", Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Matches(@"^Tygger… \d+ s$", whileBusy);
        Assert.Equal(("Hele svaret", "Hele svaret", ChatStatus.Done), (chat.Answer, chat.DisplayAnswer, chat.Status));
    }

    [Fact]
    public async Task PartialMessageStarted_WhenFollowUpWasSent_ThenUsesTheSharedChat()
    {
        // Arrange
        _claude.Reply = Silent;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        _claude.Listener.TurnStarted(_claude.Ids[0]);

        // Act
        _claude.Listener.PartialMessageStarted(_claude.Ids[1]);
        _claude.Listener.PartialMessageReceived("Kladde til første");
        foreach (var item in _conversation.Chats)
        {
            item.RefreshDisplayAnswer();
        }

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal((2, "første\n\nanden", "Kladde til første"), (chat.Prompts.Count, chat.DisplayPrompt, chat.DisplayAnswer));
    }

    [Fact]
    public async Task ResultReceived_WhenFollowUpNeedsAnotherTurn_ThenKeepsTheChatBusyAndAppendsBothAnswers()
    {
        // Arrange
        _claude.Reply = Silent;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        var chat = Assert.Single(_conversation.Chats);

        // Act
        _claude.Listener.TurnStarted(_claude.Ids[0]);
        _claude.Listener.PartialMessageStarted(_claude.Ids[0]);
        _claude.Listener.PartialMessageReceived("Første svar");
        _claude.Listener.AssistantTextReceived("Første svar");
        _claude.Listener.ResultReceived(_answered with { Text = "Første svar", Answers = [_claude.Ids[0]] });
        var afterFirst = (chat.Answer, chat.Status, _conversation.IsBusy);
        _claude.Listener.TurnStarted(_claude.Ids[1]);
        _claude.Listener.PartialMessageStarted(_claude.Ids[1]);
        _claude.Listener.PartialMessageReceived("Andet svar");
        _claude.Listener.AssistantTextReceived("Andet svar");
        _claude.Listener.ResultReceived(_answered with { Text = "Andet svar", Answers = [_claude.Ids[1]] });

        // Assert
        Assert.Equal(("Første svar", ChatStatus.Busy, true), afterFirst);
        Assert.Equal(("Første svar\n\nAndet svar", ChatStatus.Done, false), (chat.Answer, chat.Status, _conversation.IsBusy));
    }

    [Fact]
    public async Task ResultReceived_WhenFollowUpStillWaits_ThenSavesOnlyTheCompletedPromptAndAnswer()
    {
        // Arrange
        _claude.Reply = Silent;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        _claude.Listener.TurnStarted(_claude.Ids[0]);

        // Act
        _claude.Listener.ResultReceived(_answered with { Text = "Første svar", Answers = [_claude.Ids[0]] });
        var restarted = new Conversation(new FakeClaude(), Store());

        // Assert
        var saved = Assert.Single(restarted.Chats);
        Assert.Equal(["første"], saved.Prompts.Select(prompt => prompt.Text));
        Assert.Equal(("Første svar", ChatStatus.Done), (saved.Answer, saved.Status));
    }

    [Fact]
    public async Task SendAsync_WhenFollowUpCannotBeSent_ThenKeepsTheRunningChatAndShowsASeparateError()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");
        _claude.Reply = (_, _) => throw new IOException("pipe brudt");

        // Act
        await _conversation.SendAsync("anden");

        // Assert
        Assert.Equal(2, _conversation.Chats.Count);
        Assert.Equal(("første", ChatStatus.Busy), (_conversation.Chats[0].DisplayPrompt, _conversation.Chats[0].Status));
        Assert.Equal(("anden", ChatStatus.Error), (_conversation.Chats[1].DisplayPrompt, _conversation.Chats[1].Status));
    }

    [Fact]
    public async Task SendAsync_WhenTheFirstSendFailsAfterAFollowUpWasSent_ThenWithdrawsTheFollowUp()
    {
        // Arrange
        var firstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _claude.Reply = Silent;
        _claude.Sending = _ => _claude.Ids.Count == 1 ? firstSend.Task : Task.CompletedTask;

        // Act
        var sendingFirst = _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        firstSend.SetException(new IOException("pipe brudt"));
        await sendingFirst;

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal([_claude.Ids[1]], _claude.Withdrawn);
        Assert.Equal((2, ChatStatus.Error, false), (chat.Prompts.Count, chat.Status, _conversation.IsBusy));
    }

    [Fact]
    public async Task SendAsync_WhenATitleIsGiven_ThenTheChatShowsItButClaudeGetsThePrompt()
    {
        // Act
        await _conversation.SendAsync("Giv mig et overblik over mine sager.", title: "Dagens overblik");

        // Assert
        var chat = _conversation.Chats.Single();
        Assert.Equal(("Dagens overblik", "Giv mig et overblik over mine sager.", "Giv mig et overblik over mine sager."), (chat.DisplayPrompt, chat.Prompt, _claude.Prompts.Single()));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeIsStillStarting_ThenTheMessagesKeepTheirOrder()
    {
        // Arrange
        var started = new TaskCompletionSource();
        _claude.Launched = started.Task;

        // Act
        var first = _conversation.SendAsync("første");
        var second = _conversation.SendAsync("anden");
        started.SetResult();
        await first;
        await second;

        // Assert
        Assert.Equal(["første", "anden"], _claude.Prompts);
    }

    [Fact]
    public async Task Cancel_WhenClaudeIsStillStarting_ThenTheMessageIsSentAndWithdrawn()
    {
        // Arrange
        var started = new TaskCompletionSource();
        (_claude.Launched, _claude.Reply) = (started.Task, Silent);
        var sending = _conversation.SendAsync("hej");

        // Act
        _conversation.Cancel();
        started.SetResult();
        await sending;

        // Assert
        Assert.Equal((_claude.Ids.Single(), "Afbrudt."), (_claude.Withdrawn.Single(), _conversation.Chats.Single().Answer));
    }

    [Fact]
    public async Task Cancel_WhenNoTurnHasStarted_ThenWithdrawsTheMessage()
    {
        // Arrange
        _claude.Reply = Silent;
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Cancel();

        // Assert
        Assert.Equal((_claude.Ids[0], "Afbrudt.", false), (_claude.Withdrawn.Single(), _conversation.Chats.Single().Answer, _conversation.IsBusy));
    }

    [Fact]
    public async Task SendAsync_WhenCalledTwice_ThenBothGoToTheSameClaude()
    {
        // Act
        await _conversation.SendAsync("hej");
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null], _claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenBusy_ThenSendsRightAway()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");

        // Act
        await _conversation.SendAsync("anden");

        // Assert
        Assert.Equal(2, _claude.Ids.Count);
    }

    [Fact]
    public async Task StartAsync_WhenSessionWasSaved_ThenResumesIt()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        var restarted = new Conversation(_claude, Store());

        // Act
        await restarted.StartAsync();

        // Assert
        Assert.Equal([null, "session-1"], _claude.Starts);
    }

    [Fact]
    public async Task Switch_WhenTheTargetWasUsedBefore_ThenShowsItsChatsAndResumesItsSession()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        var folder = Folder();
        folder.Save(new SavedChats("session-folder", [new ChatRecord("mappe", "svar", ChatStatus.Done)]));

        // Act
        _conversation.Switch(() => folder);

        // Assert
        Assert.Equal(("mappe", "session-folder"), (Assert.Single(_conversation.Chats).Prompt, _claude.Starts[^1]));
    }

    [Fact]
    public async Task Switch_WhenSwitchingBack_ThenTheFirstChatsAndSessionComeBack()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _conversation.Switch(Folder);

        // Act
        _conversation.Switch(Store);

        // Assert
        Assert.Equal(("hej", "session-1"), (Assert.Single(_conversation.Chats).Prompt, _claude.Starts[^1]));
    }

    [Fact]
    public async Task Switch_WhenTheChatChangedAfterItsTurn_ThenTheChangeIsKept()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _conversation.ToolStarted(new ToolUse("toolu_late", "Bash", "dotnet test"));
        _conversation.Switch(Folder);

        // Act
        _conversation.Switch(Store);

        // Assert
        Assert.Equal(["Bash: dotnet test"], Assert.Single(_conversation.Chats).Commands.Lines);
    }

    [Fact]
    public async Task Switch_WhenTheTargetIsChosen_ThenTheCurrentChatsAreAlreadySaved()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _conversation.ToolStarted(new ToolUse("toolu_late", "Bash", "dotnet test"));
        IReadOnlyList<string>? saved = null;

        // Act
        _conversation.Switch(() =>
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
        await _conversation.SendAsync("hej");
        _conversation.Switch(() => null);
        await _conversation.SendAsync("midlertidig");

        // Act
        _conversation.Switch(Store);

        // Assert
        Assert.Equal("hej", Assert.Single(_conversation.Chats).Prompt);
    }

    [Fact]
    public async Task Switch_WhenThePlaceIsOwnedElsewhere_ThenStartsAnEmptyTemporaryChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Switch(() => null);

        // Assert
        Assert.Equal((true, 0), (_conversation.IsTemporary, _conversation.Chats.Count));
        Assert.Equal([null, null], _claude.Starts);
    }

    [Fact]
    public void Switch_WhenTheTargetHasOldUsage_ThenKeepsTheCurrentUsage()
    {
        // Arrange
        _conversation.UsageReported(new Usage(0.5, 0.2));
        var folder = Folder();
        folder.Save(SavedChats.Empty with { Usage = new Usage(0.1, 0.1) });

        // Act
        _conversation.Switch(() => folder);

        // Assert
        Assert.Equal(new Usage(0.5, 0.2), _conversation.Usage);
    }

    [Fact]
    public async Task StartAsync_WhenClaudeStarts_ThenSaysSo()
    {
        // Arrange
        var started = 0;
        _conversation.Started += () => started++;

        // Act
        await _conversation.StartAsync();

        // Assert
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeMustStart_ThenSaysSoOnce()
    {
        // Arrange
        var started = 0;
        _conversation.Started += () => started++;

        // Act
        await _conversation.SendAsync("hej");
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task ResultReceived_WhenClaudeCannotResume_ThenWaitingChatShowsWhy()
    {
        // Arrange
        var restarted = await RestartedWithWaitingChat();

        // Act
        _claude.Listener.ResultReceived(_cannotResume);

        // Assert
        Assert.Equal(("No conversation found", ChatStatus.Error), (restarted.Chats[1].Answer, restarted.Chats[1].Status));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeCouldNotResume_ThenStartsANewSession()
    {
        // Arrange
        var restarted = await RestartedWithWaitingChat();
        _claude.Listener.ResultReceived(_cannotResume);
        _claude.IsRunning = false;
        _claude.Reply = Answer;

        // Act
        await restarted.SendAsync("forfra");

        // Assert
        Assert.Equal([null, "session-1", null], _claude.Starts);
    }

    [Fact]
    public async Task ResultReceived_WhenClaudeCannotResumeBeforeAnyMessage_ThenAddsNoChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        var restarted = new Conversation(_claude, Store());
        await restarted.StartAsync();

        // Act
        _claude.Listener.ResultReceived(_cannotResume);

        // Assert
        Assert.Single(restarted.Chats);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeFails_ThenChatShowsError()
    {
        // Arrange
        _claude.Reply = (_, _) => throw new IOException("pipe brudt");

        // Act
        await _conversation.SendAsync("hej");

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal(ChatStatus.Error, chat.Status);
        Assert.Contains("pipe brudt", chat.Answer);
    }

    [Fact]
    public async Task SendAsync_WhenMoreThanMaxChats_ThenKeepsTheNewest()
    {
        // Act
        for (var i = 0; i <= Conversation.MaxChats; i++)
        {
            await _conversation.SendAsync($"{i}");
        }

        // Assert
        Assert.Equal((Conversation.MaxChats, "1"), (_conversation.Chats.Count, _conversation.Chats[0].Prompt));
    }

    [Fact]
    public async Task Constructor_WhenChatsWereSaved_ThenRestoresChats()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(_claude, Store());

        // Assert
        var chat = Assert.Single(restarted.Chats);
        Assert.Equal(("hej", "Svar", ChatStatus.Done), (chat.Prompt, chat.Answer, chat.Status));
    }

    [Fact]
    public async Task Constructor_WhenACompletedChatHadFollowUps_ThenRestoresThemInTheSameChat()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        _claude.Listener.ResultReceived(_answered with { Answers = [.. _claude.Ids] });

        // Act
        var restarted = new Conversation(new FakeClaude(), Store());

        // Assert
        var chat = Assert.Single(restarted.Chats);
        Assert.Equal(["første", "anden"], chat.Prompts.Select(prompt => prompt.Text));
        Assert.Equal(("første\n\nanden", "Svar"), (chat.DisplayPrompt, chat.Answer));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenAsked_ThenWaitsForUser()
    {
        // Act
        await Asking();

        // Assert
        Assert.True(_conversation.IsWaitingForUser);
    }

    [Theory(Timeout = 5_000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AskPermissionAsync_WhenUserAnswers_ThenClaudeGetsTheAnswer(bool allowed)
    {
        // Arrange
        var asking = await Asking();

        // Act
        _conversation.Chats[0].Requests[0].Respond(allowed);

        // Assert
        Assert.Equal(allowed ? PermissionAnswer.Allow : PermissionAnswer.Deny, await asking.WaitAsync(Token));
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenUserAllowsAlways_ThenClaudeIsToldSo()
    {
        // Arrange
        var asking = await Asking();

        // Act
        _conversation.Chats[0].Requests[0].RespondAlways();

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
        _ = _conversation.AskPermissionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(expected, _conversation.Chats.Single().Requests.Single().CanAllowAlways);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenUserDenies_ThenCommandsShowIt()
    {
        // Arrange
        var asking = await Asking();

        // Act
        _conversation.Chats[0].Requests[0].Respond(false);
        await asking.WaitAsync(Token);

        // Assert
        Assert.Equal(["Afvist: Bash"], _conversation.Chats[0].Commands.Lines);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenClaudeWithdraws_ThenRequestIsRemoved()
    {
        // Arrange
        using var withdrawal = new CancellationTokenSource();
        Task<PermissionAnswer>? asking = null;
        _claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            asking = listener.AskPermissionAsync(Request(), withdrawal.Token);
        };
        await _conversation.SendAsync("hej");

        // Act
        withdrawal.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking!).WaitAsync(Token);
        Assert.Empty(_conversation.Chats[0].Requests);
    }

    [Fact(Timeout = 5_000)]
    public async Task Delete_WhenChatAsksPermission_ThenClaudeGetsNo()
    {
        // Arrange
        var asking = await Asking();

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Equal(PermissionAnswer.Deny, await asking.WaitAsync(Token));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenNoTurnIsRunning_ThenAsksInTheLatestChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _ = _conversation.AskPermissionAsync(Request(), CancellationToken.None);

        // Assert
        Assert.Single(_conversation.Chats[0].Requests);
    }

    [Fact]
    public async Task ToolStarted_WhenSubagentWorksAfterTheAnswer_ThenShowsInTheChatThatStartedIt()
    {
        // Arrange
        await StartedSubagent();

        // Act
        _claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir", ParentId: "toolu_agent"));

        // Assert
        Assert.Equal(["Agent: Undersøg", "Bash: dir"], _conversation.Chats[0].Commands.Lines);
    }

    [Fact]
    public async Task AskPermissionAsync_WhenSubagentAsksAfterTheAnswer_ThenAsksInTheChatThatStartedIt()
    {
        // Arrange
        await StartedSubagent();
        _claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir", ParentId: "toolu_agent"));

        // Act
        _ = _conversation.AskPermissionAsync(Request() with { ToolUseId = "toolu_2" }, CancellationToken.None);

        // Assert
        Assert.Equal((1, 0), (_conversation.Chats[0].Requests.Count, _conversation.Chats[1].Requests.Count));
    }

    [Fact]
    public async Task AskPermissionAsync_WhenTheChatThatStartedTheSubagentIsDeleted_ThenAsksInTheLatestChat()
    {
        // Arrange
        await StartedSubagent();
        _conversation.Delete(_conversation.Chats[0]);

        // Act
        _ = _conversation.AskPermissionAsync(Request() with { ToolUseId = "toolu_agent" }, CancellationToken.None);

        // Assert
        Assert.Single(_conversation.Chats[0].Requests);
    }

    [Fact]
    public async Task ResultReceived_WhenBackgroundTaskFinishes_ThenShowsTheAnswerInANewChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _claude.Listener.TurnStarted(null);
        _claude.Listener.ResultReceived(new ClaudeResult("session-1", "Opgaven er færdig.", IsError: false));

        // Assert
        var chat = _conversation.Chats[^1];
        Assert.Equal((Conversation.BackgroundPrompt, "Opgaven er færdig.", ChatStatus.Done), (chat.Prompt, chat.Answer, chat.Status));
    }

    [Fact]
    public async Task ToolStarted_WhenBackgroundTurnWorks_ThenItsChatShowsTheCommands()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _claude.Listener.TurnStarted(null);

        // Act
        _claude.Listener.ToolStarted(new ToolUse("toolu_2", "Bash", "dir"));

        // Assert
        Assert.Equal(Conversation.BackgroundPrompt, _conversation.Chats[^1].Prompt);
        Assert.Equal(["Bash: dir"], _conversation.Chats[^1].Commands.Lines);
    }

    [Fact]
    public async Task PartialMessageReceived_WhenClaudeStartsATurnByItself_ThenTellsTheWindowAboutTheNewChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _claude.Listener.TurnStarted(null);
        var changed = false;
        _conversation.Changed += () => changed = true;

        // Act
        _claude.Listener.PartialMessageReceived("Opgaven er ");

        // Assert
        Assert.Equal((Conversation.BackgroundPrompt, true), (_conversation.Chats[^1].Prompt, changed));
    }

    [Fact]
    public async Task ToolStarted_WhenABackgroundTurnHasEnded_ThenAddsNoChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _claude.Listener.TurnStarted(null);
        _claude.Listener.ResultReceived(new ClaudeResult("session-1", "Opgaven er færdig.", IsError: false));

        // Act
        _claude.Listener.ToolStarted(new ToolUse("toolu_9", "Read", "a.cs"));

        // Assert
        Assert.Equal(2, _conversation.Chats.Count);
    }

    [Fact]
    public async Task ResultReceived_WhenMessagesWereAnsweredTogether_ThenOneChatContainsBothPromptsAndTheAnswer()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");

        // Act
        _claude.Listener.ResultReceived(_answered with { Answers = [.. _claude.Ids] });

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal(["første", "anden"], chat.Prompts.Select(prompt => prompt.Text));
        Assert.Equal(("Svar", ChatStatus.Done), (chat.Answer, chat.Status));
        Assert.False(_conversation.IsBusy);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenForgetsChatsAndStartsANewSession()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Reset();

        // Assert
        Assert.Equal([null, null], _claude.Starts);
        Assert.Empty(_conversation.Chats);
    }

    [Fact]
    public async Task SendAsync_WhenClear_ThenStartsANewSessionWithoutSendingIt()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        await _conversation.SendAsync("/clear");

        // Assert
        Assert.Equal([null, null], _claude.Starts);
        Assert.Single(_claude.Ids);
        Assert.Empty(_conversation.Chats);
    }

    [Fact]
    public async Task Reset_WhenRunning_ThenIsNoLongerBusy()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Reset();

        // Assert
        Assert.False(_conversation.IsBusy);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenCostIsZero()
    {
        // Arrange
        _claude.Reply = Answering(_answered with { Cost = 0.36m });
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Reset();

        // Assert
        Assert.Equal(0m, _conversation.Cost);
    }

    [Fact]
    public async Task IsCurrent_WhenChatIsRunning_ThenTrue()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        var current = _conversation.IsCurrent(_conversation.Chats[0], showLastResponse: false, DateTime.MaxValue);

        // Assert
        Assert.True(current);
    }

    [Fact]
    public async Task IsCurrent_WhenChatAsksPermission_ThenTrueAfterTheChatsWereHidden()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _ = _conversation.AskPermissionAsync(Request(), CancellationToken.None);

        // Act
        var current = _conversation.IsCurrent(_conversation.Chats[0], showLastResponse: false, DateTime.MaxValue);

        // Assert
        Assert.True(current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsCurrent_WhenAnswered_ThenUsesLastResponseVisibility(bool showLastResponse)
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        var current = _conversation.IsCurrent(_conversation.Chats[0], showLastResponse, DateTime.MinValue);

        // Assert
        Assert.Equal(showLastResponse, current);
    }

    [Fact]
    public async Task IsCurrent_WhenANewerChatWasAnswered_ThenOlderChatIsHidden()
    {
        // Arrange
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");

        // Act
        var current = _conversation.IsCurrent(_conversation.Chats[0], showLastResponse: true, DateTime.MinValue);

        // Assert
        Assert.False(current);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public async Task IsCurrent_WhenAMessageIsSentAfterTheAnswer_ThenTrueOnlyWithinTwoSeconds(int sentSecondsLater, bool expected)
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        var current = _conversation.IsCurrent(_conversation.Chats[0], showLastResponse: true, _conversation.AnsweredAt.AddSeconds(sentSecondsLater));

        // Assert
        Assert.Equal(expected, current);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(5, false)]
    public async Task AnsweredWithin_WhenAnswered_ThenTrueOnlyWithinTheTime(int secondsLater, bool expected)
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        var answered = _conversation.AnsweredWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow.AddSeconds(secondsLater));

        // Assert
        Assert.Equal(expected, answered);
    }

    [Fact]
    public async Task AnsweredWithin_WhenClaudeFailed_ThenFalse()
    {
        // Arrange
        _claude.Reply = Answering(new ClaudeResult("session-1", "Fejl", IsError: true));
        await _conversation.SendAsync("hej");

        // Act
        var answered = _conversation.AnsweredWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow);

        // Assert
        Assert.False(answered);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeReportsCost_ThenCostIsTheSessionTotal()
    {
        // Arrange
        _claude.Reply = Answering(_answered with { Cost = 0.36m });
        await _conversation.SendAsync("hej");
        _claude.Reply = Answering(_answered with { Cost = 0.5m });

        // Act
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal(0.5m, _conversation.Cost);
    }

    [Fact]
    public async Task ResultReceived_WhenTheSameSessionReportsLess_ThenKeepsTheHigherCost()
    {
        // Arrange
        _claude.Reply = Answering(_answered with { Cost = 0.5m });
        await _conversation.SendAsync("hej");
        _claude.Reply = Answering(_answered with { Cost = 0.3m });

        // Act
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal(0.5m, _conversation.Cost);
    }

    [Fact]
    public async Task Constructor_WhenCostWasSaved_ThenRestoresCost()
    {
        // Arrange
        _claude.Reply = Answering(_answered with { Cost = 0.36m });
        await _conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(_claude, Store());

        // Assert
        Assert.Equal(0.36m, restarted.Cost);
    }

    [Fact]
    public async Task Delete_WhenCalled_ThenChatIsGoneAfterRestart()
    {
        // Arrange
        await _conversation.SendAsync("slet mig");
        await _conversation.SendAsync("behold mig");

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Equal(["behold mig"], new Conversation(_claude, Store()).Chats.Select(chat => chat.Prompt));
    }

    [Fact]
    public async Task Delete_WhenChatIsRunning_ThenInterruptsClaude()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Equal((1, 0), (_claude.Interrupts, _conversation.Chats.Count));
    }

    [Fact]
    public async Task Delete_WhenRunningChatHasAFollowUp_ThenInterruptsTheTurnAndWithdrawsTheFollowUp()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");

        // Act
        _conversation.Delete(Assert.Single(_conversation.Chats));

        // Assert
        Assert.Equal([_claude.Ids[1]], _claude.Withdrawn);
        Assert.Equal((1, 0), (_claude.Interrupts, _conversation.Chats.Count));
    }

    [Fact]
    public async Task ResultReceived_WhenAReminderFires_ThenShowsItInANewChat()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _claude.Listener.TurnStarted("reminder-id");

        // Act
        _claude.Listener.ResultReceived(new ClaudeResult("session-1", "Husk at drikke vand!", IsError: false));

        // Assert
        Assert.Equal((Conversation.BackgroundPrompt, "Husk at drikke vand!"), (_conversation.Chats[^1].Prompt, _conversation.Chats[^1].Answer));
    }

    [Fact]
    public async Task Delete_WhenAnotherChatIsRunning_ThenRunningChatIsNotSaved()
    {
        // Arrange
        await _conversation.SendAsync("færdig");
        _claude.Reply = Started;
        await _conversation.SendAsync("kører");

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Empty(new Conversation(_claude, Store()).Chats);
    }

    [Fact]
    public async Task ResultReceived_WhenRunEndsDuringWebSearch_ThenStopsBrowsingWeb()
    {
        // Arrange
        var browsing = false;
        _claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_1", "WebSearch"));
            browsing = _conversation.IsBrowsingWeb;
            listener.ResultReceived(_answered with { Answers = [id] });
        };

        // Act
        await _conversation.SendAsync("søg");

        // Assert
        Assert.Equal((true, false), (browsing, _conversation.IsBrowsingWeb));
    }

    [Fact]
    public async Task ToolStarted_WhenRunning_ThenChatShowsCommandsAndSources()
    {
        // Arrange
        _claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_1", "Read", "Mood.cs"));
            listener.ToolStarted(new ToolUse("toolu_2", "WebSearch", "hamstere"));
            listener.ToolStarted(new ToolUse("toolu_3", "Bash", "git log"));
        };

        // Act
        await _conversation.SendAsync("hvad er nyt?");

        // Assert
        Assert.Equal(["Read: Mood.cs", "Bash: git log"], _conversation.Chats[0].Commands.Lines);
        Assert.Equal(["WebSearch: hamstere"], _conversation.Chats[0].Sources.Lines);
    }

    [Fact]
    public async Task SendAsync_WhenNotLoggedIn_ThenChatOffersLogin()
    {
        // Arrange
        _claude.Reply = Answering(new ClaudeResult("session-1", "Not logged in · Please run /login", IsError: true));

        // Act
        await _conversation.SendAsync("hej");

        // Assert
        Assert.Equal((true, "Du er ikke logget ind i claude."), (_conversation.Chats[0].NeedsLogin, _conversation.Chats[0].Answer));
    }

    [Fact]
    public async Task Constructor_WhenUsageWasSaved_ThenRestoresUsage()
    {
        // Arrange
        _claude.Reply = (listener, id) =>
        {
            listener.UsageReported(new Usage(0.03, 0.59));
            Answer(listener, id);
        };
        await _conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(_claude, Store());

        // Assert
        Assert.Equal(new Usage(0.03, 0.59), restarted.Usage);
    }

    [Fact]
    public async Task Cancel_WhenRunning_ThenChatIsInterruptedAndKeepsTheCost()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Cancel();
        _claude.Listener.ResultReceived(_stopped with { Answers = [_claude.Ids[0]] });

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal((1, "Afbrudt.", ChatStatus.Error, 0.2m), (_claude.Interrupts, chat.Answer, chat.Status, _conversation.Cost));
    }

    [Fact]
    public async Task Cancel_WhenRunningChatHasAFollowUp_ThenWithdrawsTheFollowUpAndFinishesOnce()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");

        // Act
        _conversation.Cancel();
        _claude.Listener.ResultReceived(_stopped with { Answers = [_claude.Ids[0]] });

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal([_claude.Ids[1]], _claude.Withdrawn);
        Assert.Equal((2, "Afbrudt.", ChatStatus.Error, false), (chat.Prompts.Count, chat.Answer, chat.Status, _conversation.IsBusy));
    }

    [Fact]
    public async Task Cancel_WhenNothingRuns_ThenDoesNotInterrupt()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Cancel();

        // Assert
        Assert.Equal(0, _claude.Interrupts);
    }

    [Fact]
    public async Task Cancel_WhenAnswerAlreadyArrived_ThenKeepsTheAnswer()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Cancel();
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal(("Svar", ChatStatus.Done), (_conversation.Chats[0].Answer, _conversation.Chats[0].Status));
    }

    [Fact]
    public async Task Exited_WhenClaudeDies_ThenWaitingChatShowsWhy()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _claude.Listener.Exited("claude stoppede uventet");

        // Assert
        Assert.Equal(("claude stoppede uventet", ChatStatus.Error), (_conversation.Chats[0].Answer, _conversation.Chats[0].Status));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeHasExited_ThenResumesTheSession()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _claude.IsRunning = false;
        _claude.Listener.Exited("claude stoppede uventet");

        // Act
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], _claude.Starts);
    }

    [Fact]
    public async Task Exited_WhenClaudeWasKilledAfterStop_ThenChatSaysInterrupted()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Cancel();

        // Act
        _claude.Listener.Exited("killed");

        // Assert
        Assert.Equal("Afbrudt.", _conversation.Chats[0].Answer);
    }

    [Fact]
    public void BackgroundTasksChanged_WhenTasksRun_ThenCountsThem()
    {
        // Act
        _conversation.BackgroundTasksChanged(2);

        // Assert
        Assert.Equal(2, _conversation.BackgroundTasks);
    }

    [Fact]
    public void Exited_WhenBackgroundTasksRan_ThenNoneRunAnymore()
    {
        // Arrange
        _conversation.BackgroundTasksChanged(2);

        // Act
        _conversation.Exited("claude stoppede uventet");

        // Assert
        Assert.Equal(0, _conversation.BackgroundTasks);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task FailedWithin_WhenAnswered_ThenTrueOnlyForErrors(bool isError, bool expected)
    {
        // Arrange
        _claude.Reply = Answering(new ClaudeResult("session-1", "Svar", isError));
        await _conversation.SendAsync("hej");

        // Act
        var failed = _conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow);

        // Assert
        Assert.Equal(expected, failed);
    }

    [Fact]
    public async Task ResultReceived_WhenNoChatGetsTheAnswer_ThenAnsweredAtStays()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        var answeredAt = _conversation.AnsweredAt;

        // Act
        _claude.Listener.ResultReceived(new ClaudeResult("session-1", "", IsError: false));

        // Assert
        Assert.Equal(answeredAt, _conversation.AnsweredAt);
    }

    [Fact]
    public void BackgroundTasksChanged_WhenCalled_ThenTellsTheWindow()
    {
        // Arrange
        var changed = false;
        _conversation.Changed += () => changed = true;

        // Act
        _conversation.BackgroundTasksChanged(1);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void Reset_WhenBackgroundTasksRan_ThenNoneRunAnymore()
    {
        // Arrange
        _conversation.BackgroundTasksChanged(2);

        // Act
        _conversation.Reset();

        // Assert
        Assert.Equal(0, _conversation.BackgroundTasks);
    }

    [Fact]
    public async Task Reset_WhenAnswered_ThenNothingIsNew()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Reset();

        // Assert
        Assert.Equal(default(DateTime), _conversation.AnsweredAt);
    }

    [Fact]
    public async Task Delete_WhenTheAnsweredChatIsDeleted_ThenNothingIsNew()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Equal(default(DateTime), _conversation.AnsweredAt);
    }

    [Fact]
    public async Task ResultReceived_WhenUserStopped_ThenIsNeitherSadNorNew()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Cancel();

        // Act
        _claude.Listener.ResultReceived(_stopped with { Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal((false, default(DateTime)), (_conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow), _conversation.AnsweredAt));
    }

    [Fact]
    public async Task Delete_WhenAnotherChatIsDeleted_ThenTheAnswerStaysNew()
    {
        // Arrange
        await _conversation.SendAsync("første");
        await _conversation.SendAsync("anden");
        var answeredAt = _conversation.AnsweredAt;

        // Act
        _conversation.Delete(_conversation.Chats[0]);

        // Assert
        Assert.Equal(answeredAt, _conversation.AnsweredAt);
    }

    [Fact]
    public async Task ResultReceived_WhenTheRunningChatWasDeleted_ThenNothingIsNew()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Delete(_conversation.Chats[0]);

        // Act
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal(default(DateTime), _conversation.AnsweredAt);
    }

    [Fact]
    public async Task Exited_WhenClaudeWasKilledAfterStop_ThenIsNeitherSadNorNew()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Cancel();

        // Act
        _claude.Listener.Exited("killed");

        // Assert
        Assert.Equal((false, default(DateTime)), (_conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow), _conversation.AnsweredAt));
    }

    [Fact]
    public async Task Exited_WhenFollowUpWaits_ThenTheSharedChatShowsTheFailure()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("kører");
        await _conversation.SendAsync("venter");

        // Act
        _claude.Listener.Exited("claude stoppede uventet");

        // Assert
        var chat = Assert.Single(_conversation.Chats);
        Assert.Equal((2, ChatStatus.Error, true), (chat.Prompts.Count, chat.Status, _conversation.IsCurrent(chat, showLastResponse: true, DateTime.MinValue)));
    }

    [Fact]
    public async Task Exited_WhenClaudeDies_ThenIsSad()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");

        // Act
        _claude.Listener.Exited("claude stoppede uventet");

        // Assert
        Assert.True(_conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow));
    }

    [Fact]
    public async Task SendAsync_WhenClaudeFails_ThenIsSad()
    {
        // Arrange
        _claude.Reply = (_, _) => throw new IOException("pipe brudt");

        // Act
        await _conversation.SendAsync("hej");

        // Assert
        Assert.True(_conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow));
    }

    [Theory]
    [InlineData(true, -10, true)]
    [InlineData(true, 10, false)]
    [InlineData(false, -10, false)]
    public async Task FailedSince_WhenAnswered_ThenTrueOnlyForErrorsNotSeenYet(bool isError, int seenSecondsLater, bool expected)
    {
        // Arrange
        _claude.Reply = Answering(new ClaudeResult("session-1", "Svar", isError));
        await _conversation.SendAsync("hej");

        // Act
        var failed = _conversation.FailedSince(DateTime.UtcNow.AddSeconds(seenSecondsLater));

        // Assert
        Assert.Equal(expected, failed);
    }

    [Fact]
    public async Task FailedWithin_WhenTheTimeHasPassed_ThenFalse()
    {
        // Arrange
        _claude.Reply = Answering(new ClaudeResult("session-1", "Fejl", IsError: true));
        await _conversation.SendAsync("hej");

        // Act
        var failed = _conversation.FailedWithin(TimeSpan.FromSeconds(4), DateTime.UtcNow.AddSeconds(5));

        // Assert
        Assert.False(failed);
    }

    [Fact]
    public void ModeChanged_WhenClaudeSwitchesMode_ThenTellsTheWindow()
    {
        // Arrange
        string? mode = null;
        _conversation.PermissionModeChanged += changed => mode = changed;

        // Act
        _conversation.ModeChanged("plan");

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
        await _conversation.SendAsync("Hvad ser du?", [image]);

        // Assert
        Assert.Equal([image], _claude.Images);
    }

    [Fact]
    public async Task Restart_WhenIdle_ThenStartsClaudeAgainWithTheSession()
    {
        // Arrange
        await _conversation.SendAsync("hej");

        // Act
        _conversation.Restart();

        // Assert
        Assert.Equal([null, "session-1"], _claude.Starts);
    }

    [Fact]
    public async Task Restart_WhenBusy_ThenKeepsClaudeRunningWhenTheTurnEnds()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Restart();

        // Act
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });

        // Assert
        Assert.Equal([null], _claude.Starts);
    }

    [Fact]
    public async Task Restart_WhenBackgroundTasksRun_ThenKeepsClaudeRunning()
    {
        // Arrange
        await _conversation.SendAsync("hej");
        _conversation.BackgroundTasksChanged(1);

        // Act
        _conversation.Restart();

        // Assert
        Assert.Equal([null], _claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenARestartWaitsWhileBusy_ThenDoesNotRestart()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Restart();

        // Act
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null], _claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenTheWaitingRestartIsDone_ThenDoesNotRestartAgain()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Restart();
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });
        _claude.Reply = Answer;
        await _conversation.SendAsync("igen");

        // Act
        await _conversation.SendAsync("tredje");

        // Assert
        Assert.Equal([null, "session-1"], _claude.Starts);
    }

    [Fact]
    public async Task SendAsync_WhenARestartWaits_ThenStartsClaudeAgain()
    {
        // Arrange
        _claude.Reply = Started;
        await _conversation.SendAsync("hej");
        _conversation.Restart();
        _claude.Listener.ResultReceived(_answered with { Answers = [_claude.Ids[0]] });

        // Act
        await _conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], _claude.Starts);
    }

    async Task<Task<PermissionAnswer>> Asking()
    {
        Task<PermissionAnswer>? asking = null;
        _claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            asking = listener.AskPermissionAsync(Request(), CancellationToken.None);
        };
        await _conversation.SendAsync("hej");
        return asking!;
    }

    async Task<Conversation> RestartedWithWaitingChat()
    {
        await _conversation.SendAsync("hej");
        var restarted = new Conversation(_claude, Store());
        _claude.IsRunning = false;
        _claude.Reply = Silent;
        await restarted.SendAsync("igen");
        return restarted;
    }

    async Task StartedSubagent()
    {
        _claude.Reply = (listener, id) =>
        {
            listener.TurnStarted(id);
            listener.ToolStarted(new ToolUse("toolu_agent", "Agent", "Undersøg"));
            listener.ResultReceived(_answered with { Answers = [id] });
        };
        await _conversation.SendAsync("første");
        _claude.Reply = Answer;
        await _conversation.SendAsync("anden");
    }

    static PermissionRequest Request() => new("req-1", "Bash", new JsonObject { ["command"] = "dir" });

    static Action<IClaudeListener, string> Answering(ClaudeResult result) => (listener, id) =>
    {
        listener.TurnStarted(id);
        listener.ResultReceived(result with { Answers = [id] });
    };

    static void Answer(IClaudeListener listener, string id) => Answering(_answered)(listener, id);

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
        public Task Launched { get; set; } = Task.CompletedTask;
        public Func<string, Task>? Sending { get; set; }
        public ClaudeSettings Settings { get; set; } = ClaudeSettings.Default;

        public Task StartAsync(string? sessionId, IClaudeListener listener)
        {
            Starts.Add(sessionId);
            (Listener, IsRunning) = (listener, true);
            return Launched;
        }

        public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images)
        {
            Ids.Add(id);
            Prompts.Add(prompt);
            Images.AddRange(images);
            Reply(Listener, id);
            return Sending?.Invoke(id) ?? Task.CompletedTask;
        }

        public Task<JsonObject?> RequestAsync(JsonObject request) => Task.FromResult<JsonObject?>(null);

        public void Interrupt() => Interrupts++;

        public void Withdraw(string id) => Withdrawn.Add(id);

        public void End() => IsRunning = false;
    }
}
