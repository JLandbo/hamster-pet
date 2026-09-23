using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ConversationTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly FakeClaude claude = new();
    readonly Conversation conversation;

    public ConversationTests() => conversation = new Conversation(claude, Store());

    JsonFile<SavedChats> Store() => new(Path.Combine(directory, "chats.json"), SavedChats.Empty);

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
    public async Task SendAsync_WhenCalledAgain_ThenResumesTheSession()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], claude.Sessions);
    }

    [Fact]
    public async Task SendAsync_WhenResumeFails_ThenNextMessageStartsNewSession()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult(null, "No conversation found", IsError: true));
        await conversation.SendAsync("igen");

        // Act
        await conversation.SendAsync("forfra");

        // Assert
        Assert.Equal([null, "session-1", null], claude.Sessions);
    }

    [Fact]
    public async Task SendAsync_WhenClaudeFails_ThenChatShowsError()
    {
        // Arrange
        claude.Reply = (_, _) => Task.FromException<ClaudeResult>(new IOException("pipe brudt"));

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
    public async Task Constructor_WhenChatsWereSaved_ThenRestoresChatsAndSession()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(claude, Store());
        await restarted.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1"], claude.Sessions);
        Assert.Equal(["hej", "igen"], restarted.Chats.Select(chat => chat.Prompt));
    }

    [Fact]
    public void AskPermissionAsync_WhenAsked_ThenWaitsForUser()
    {
        // Arrange
        claude.Reply = async (listener, _) =>
            new ClaudeResult("session-1", $"{await listener.AskPermissionAsync(Request(), CancellationToken.None)}", IsError: false);

        // Act
        _ = conversation.SendAsync("hej");

        // Assert
        Assert.True(conversation.IsWaitingForUser);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenUserAllows_ThenClaudeGetsYes()
    {
        // Arrange
        claude.Reply = async (listener, _) =>
            new ClaudeResult("session-1", $"{await listener.AskPermissionAsync(Request(), CancellationToken.None)}", IsError: false);
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Chats[0].Requests[0].Respond(true);
        await sending.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("True", conversation.Chats[0].Answer);
    }

    [Fact(Timeout = 5_000)]
    public async Task AskPermissionAsync_WhenClaudeWithdraws_ThenRequestIsRemoved()
    {
        // Arrange
        using var withdrawal = new CancellationTokenSource();
        Task<bool>? asking = null;
        claude.Reply = (listener, _) =>
        {
            asking = listener.AskPermissionAsync(Request(), withdrawal.Token);
            return new TaskCompletionSource<ClaudeResult>().Task;
        };
        _ = conversation.SendAsync("hej");

        // Act
        withdrawal.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking!).WaitAsync(TestContext.Current.CancellationToken);
        Assert.Empty(conversation.Chats[0].Requests);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenForgetsChatsAndSession()
    {
        // Arrange
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();
        await conversation.SendAsync("forfra");

        // Assert
        Assert.Equal([null, null], claude.Sessions);
        Assert.Single(conversation.Chats);
    }

    [Fact(Timeout = 5_000)]
    public async Task Reset_WhenRunning_ThenCancelsClaudeAndEndsEmpty()
    {
        // Arrange
        claude.Reply = (_, cancellationToken) => UntilStopped(cancellationToken, Stopped);
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Reset();
        await sending.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, 0), (conversation.IsBusy, conversation.Chats.Count));
    }

    [Fact]
    public void IsCurrent_WhenChatIsRunning_ThenTrue()
    {
        // Arrange
        claude.Reply = (_, _) => new TaskCompletionSource<ClaudeResult>().Task;
        _ = conversation.SendAsync("hej");

        // Act
        var current = conversation.IsCurrent(conversation.Chats[0], DateTime.UtcNow);

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
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Fejl", IsError: true));
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
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false, Cost: 0.36m));
        await conversation.SendAsync("hej");
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false, Cost: 0.5m));

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal(0.5m, conversation.Cost);
    }

    [Fact]
    public async Task Constructor_WhenCostWasSaved_ThenRestoresCost()
    {
        // Arrange
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false, Cost: 0.36m));
        await conversation.SendAsync("hej");

        // Act
        var restarted = new Conversation(claude, Store());

        // Assert
        Assert.Equal(0.36m, restarted.Cost);
    }

    [Fact]
    public async Task Reset_WhenCalled_ThenCostIsZero()
    {
        // Arrange
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false, Cost: 0.36m));
        await conversation.SendAsync("hej");

        // Act
        conversation.Reset();

        // Assert
        Assert.Equal(0m, conversation.Cost);
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

    [Fact(Timeout = 5_000)]
    public async Task Delete_WhenChatIsRunning_ThenCancelsClaude()
    {
        // Arrange
        claude.Reply = (_, cancellationToken) => UntilStopped(cancellationToken, Stopped);
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Delete(conversation.Chats[0]);
        await sending.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, 0), (conversation.IsBusy, conversation.Chats.Count));
    }

    [Fact]
    public async Task SendAsync_WhenRunEndsDuringWebSearch_ThenStopsBrowsingWeb()
    {
        // Arrange
        var browsing = false;
        claude.Reply = (listener, _) =>
        {
            listener.ToolStarted(new ToolUse("toolu_1", "WebSearch"));
            browsing = conversation.IsBrowsingWeb;
            return Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false));
        };

        // Act
        await conversation.SendAsync("søg");

        // Assert
        Assert.Equal((true, false), (browsing, conversation.IsBrowsingWeb));
    }

    [Fact]
    public async Task ToolStarted_WhenRunning_ThenChatShowsWhatClaudeDid()
    {
        // Arrange
        claude.Reply = (listener, _) =>
        {
            listener.ToolStarted(new ToolUse("toolu_1", "Read", "Mood.cs"));
            listener.ToolStarted(new ToolUse("toolu_2", "Bash", "git log"));
            return Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false));
        };

        // Act
        await conversation.SendAsync("hvad er nyt?");

        // Assert
        Assert.Equal(["Read: Mood.cs", "Bash: git log"], conversation.Chats[0].Activity);
    }

    [Fact]
    public async Task Delete_WhenAnotherChatIsRunning_ThenRunningChatIsNotSaved()
    {
        // Arrange
        await conversation.SendAsync("færdig");
        claude.Reply = (_, _) => new TaskCompletionSource<ClaudeResult>().Task;
        _ = conversation.SendAsync("kører");

        // Act
        conversation.Delete(conversation.Chats[0]);

        // Assert
        Assert.Empty(new Conversation(claude, Store()).Chats);
    }

    [Fact(Timeout = 5_000)]
    public async Task Cancel_WhenRunning_ThenChatIsInterruptedAndKeepsTheCost()
    {
        // Arrange
        claude.Reply = (_, cancellationToken) => UntilStopped(cancellationToken, Stopped);
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Cancel();
        await sending.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        var chat = Assert.Single(conversation.Chats);
        Assert.Equal(("Afbrudt.", ChatStatus.Error, 0.2m), (chat.Answer, chat.Status, conversation.Cost));
    }

    [Fact(Timeout = 5_000)]
    public async Task Cancel_WhenClaudeHadToBeKilled_ThenNextMessageResumesTheSession()
    {
        // Arrange
        await conversation.SendAsync("hej");
        claude.Reply = (_, cancellationToken) => UntilStopped(cancellationToken, new ClaudeResult(null, "claude stoppede uventet", IsError: true));
        var sending = conversation.SendAsync("stop");
        conversation.Cancel();
        await sending.WaitAsync(TestContext.Current.CancellationToken);
        claude.Reply = (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false));

        // Act
        await conversation.SendAsync("igen");

        // Assert
        Assert.Equal([null, "session-1", "session-1"], claude.Sessions);
    }

    [Fact(Timeout = 5_000)]
    public async Task Cancel_WhenAnswerAlreadyArrived_ThenKeepsTheAnswer()
    {
        // Arrange
        claude.Reply = (_, cancellationToken) => UntilStopped(cancellationToken, new ClaudeResult("session-1", "Svar", IsError: false));
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Cancel();
        await sending.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(("Svar", ChatStatus.Done), (conversation.Chats[0].Answer, conversation.Chats[0].Status));
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
        var prompt = Conversation.WithAttachments("Hvad ser du?", [], ["screenshot.png"]);

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

    static PermissionRequest Request() => new("req-1", "Bash", new JsonObject { ["command"] = "dir" });

    static readonly ClaudeResult Stopped = new("session-1", "error_during_execution", IsError: true, Cost: 0.2m);

    // Like ClaudeClient, stopping ends the turn with claude's result for it. It completes on the stopping thread, so nothing
    // runs concurrently with the rest of Reset or Delete; in the app the UI thread gives the same guarantee.
    static Task<ClaudeResult> UntilStopped(CancellationToken cancellationToken, ClaudeResult result)
    {
        var reply = new TaskCompletionSource<ClaudeResult>();
        cancellationToken.Register(() => reply.TrySetResult(result));
        return reply.Task;
    }

    sealed class FakeClaude : IClaudeClient
    {
        public Func<IClaudeListener, CancellationToken, Task<ClaudeResult>> Reply { get; set; } =
            (_, _) => Task.FromResult(new ClaudeResult("session-1", "Svar", IsError: false));
        public List<string?> Sessions { get; } = [];
        public List<ImageAttachment> Images { get; } = [];

        public Task<ClaudeResult> SendAsync(string prompt, IReadOnlyList<ImageAttachment> images, string? sessionId, IClaudeListener listener,
            CancellationToken cancellationToken)
        {
            Sessions.Add(sessionId);
            Images.AddRange(images);
            return Reply(listener, cancellationToken);
        }
    }
}
