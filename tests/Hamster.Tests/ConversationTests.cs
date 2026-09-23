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
        Assert.Equal((ChatStatus.Error, true), (chat.Status, chat.Answer.Contains("pipe brudt")));
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
    public async Task AskPermissionAsync_WhenUserAllows_ThenClaudeGetsYes()
    {
        // Arrange
        claude.Reply = async (listener, _) =>
            new ClaudeResult("session-1", $"{await listener.AskPermissionAsync(Request(), CancellationToken.None)}", IsError: false);
        var sending = conversation.SendAsync("hej");
        var waiting = conversation.IsWaitingForUser;

        // Act
        conversation.Chats[0].Requests[0].Respond(true);
        await sending;

        // Assert
        Assert.Equal((true, "True"), (waiting, conversation.Chats[0].Answer));
    }

    [Fact]
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking!);

        // Assert
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

    [Fact]
    public async Task Reset_WhenRunning_ThenCancelsClaudeAndEndsEmpty()
    {
        // Arrange
        // Cancelled on the calling thread, like on the app's UI thread, so the run ends inside Reset.
        claude.Reply = (_, cancellationToken) =>
        {
            var reply = new TaskCompletionSource<ClaudeResult>();
            cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken));
            return reply.Task;
        };
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Reset();
        await sending;

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

    [Fact]
    public async Task Delete_WhenChatIsRunning_ThenCancelsClaude()
    {
        // Arrange
        claude.Reply = (_, cancellationToken) =>
        {
            var reply = new TaskCompletionSource<ClaudeResult>();
            cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken));
            return reply.Task;
        };
        var sending = conversation.SendAsync("hej");

        // Act
        conversation.Delete(conversation.Chats[0]);
        await sending;

        // Assert
        Assert.Equal((false, 0), (conversation.IsBusy, conversation.Chats.Count));
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
