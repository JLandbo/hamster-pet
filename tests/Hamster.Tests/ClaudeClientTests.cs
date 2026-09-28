namespace Hamster.Tests;

public sealed class ClaudeClientTests : IDisposable
{
    readonly string settingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
    readonly string workspace = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");

    static CancellationToken Token => TestContext.Current.CancellationToken;

    JsonFile<ClaudeSettings> Store() => new(settingsFile, ClaudeSettings.Default);

    public void Dispose()
    {
        File.Delete(settingsFile);
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace);
        }
    }

    [Fact]
    public void StartAsync_WhenClaudeCannotStart_ThenTheCallerGoesOnAndTheStartFails() => UiThread.Run(() =>
    {
        // Arrange
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var client = new ClaudeClient(workspace, "findes-ikke.txt", Store());
        client.Settings = client.Settings with { WorkingDirectory = Path.Combine(workspace, "findes-ikke") };

        // Act
        var starting = client.StartAsync(null, new ClaudeSessionTests.FakeListener());
        var runningAtOnce = client.IsRunning;
        UiThread.Until(() => starting.IsCompleted && !client.IsRunning);

        // Assert
        Assert.Equal((true, typeof(Win32Exception)), (runningAtOnce, starting.Exception?.InnerException?.GetType()));
    });

    [Fact]
    public async Task SendAsync_WhenClaudeIsStillStarting_ThenEverythingIsSentInOrderOnceItRuns()
    {
        // Arrange
        var streams = new TaskCompletionSource<(TextReader, TextWriter)>();
        var input = new ClaudeSessionTests.LineWriter();
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        client.Attach(new ClaudeSession(streams.Task, new ClaudeSessionTests.FakeListener()));

        // Act
        _ = client.SendAsync("id-1", "hej", []);
        client.Choose(client.Settings, ClaudeProtocol.SetEffort("high"));
        client.Withdraw("id-1");
        streams.SetResult((new StringReader(""), input));

        // Assert
        Assert.Contains("hej", await input.NextAsync());
        Assert.Contains("\"high\"", await input.NextAsync());
        Assert.Contains("cancel_async_message", await input.NextAsync());
    }

    [Fact]
    public async Task End_WhenClaudeIsStillStarting_ThenOnlyTheEndIsSent()
    {
        // Arrange
        var streams = new TaskCompletionSource<(TextReader, TextWriter)>();
        var input = new ClaudeSessionTests.LineWriter();
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        client.Attach(new ClaudeSession(streams.Task, new ClaudeSessionTests.FakeListener()));
        _ = client.SendAsync("id-1", "hej", []);

        // Act
        client.End();
        streams.SetResult((new StringReader(""), input));

        // Assert
        Assert.Contains("end_session", await input.NextAsync());
    }

    [Fact]
    public async Task End_WhenAnEarlierMessageIsStillBeingWritten_ThenReturnsAtOnceAndEndsAfterIt()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        using var input = new ClaudeSessionTests.LineWriter(blocked: true);
        var session = new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener());
        client.Attach(session);
        _ = session.SendAsync("stor besked");

        // Act
        await Task.Run(client.End, Token).WaitAsync(TimeSpan.FromSeconds(1), Token);
        input.Open();

        // Assert
        Assert.Equal("stor besked", await input.NextAsync());
        Assert.Contains("end_session", await input.NextAsync());
        await Assert.ThrowsAsync<ChannelClosedException>(input.NextAsync);
    }

    [Fact]
    public async Task Interrupt_WhenAPromptIsStillBeingWritten_ThenReturnsAtOnce()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        using var input = new ClaudeSessionTests.LineWriter(blocked: true);
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));
        _ = client.SendAsync("id-1", "hej", []);

        // Act
        await Task.Run(client.Interrupt, Token).WaitAsync(TimeSpan.FromSeconds(1), Token);
        input.Open();

        // Assert
        Assert.Contains("hej", await input.NextAsync());
        Assert.Contains("interrupt", await input.NextAsync());
    }

    [Fact]
    public async Task Choose_WhenAPromptIsStillBeingWritten_ThenReturnsAtOnce()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        using var input = new ClaudeSessionTests.LineWriter(blocked: true);
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));
        _ = client.SendAsync("id-1", "hej", []);

        // Act
        await Task.Run(() => client.Choose(client.Settings, ClaudeProtocol.SetEffort("high")), Token).WaitAsync(TimeSpan.FromSeconds(1), Token);
        input.Open();

        // Assert
        Assert.Contains("hej", await input.NextAsync());
        Assert.Contains("\"high\"", await input.NextAsync());
    }

    [Fact]
    public void Settings_WhenNothingSaved_ThenOpus55WithXhighInManualMode()
    {
        // Act
        var client = new ClaudeClient("workspace", "instructions.txt", Store());

        // Assert
        Assert.Equal(new ClaudeSettings("claude-opus-5-5", "xhigh", "default"), client.Settings);
    }

    [Fact]
    public void Settings_WhenChanged_ThenTheNextStartUsesThem()
    {
        // Arrange
        new ClaudeClient("workspace", "instructions.txt", Store()).Settings = new("claude-sonnet-5", "low", "plan", @"C:\projekt");

        // Act
        var restarted = new ClaudeClient("workspace", "instructions.txt", Store());

        // Assert
        Assert.Equal(new ClaudeSettings("claude-sonnet-5", "low", "plan", @"C:\projekt"), restarted.Settings);
    }

    [Fact]
    public void Choose_WhenClaudeIsNotRunning_ThenTheNextStartUsesTheChoice()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());

        // Act
        client.Choose(client.Settings with { Effort = "low" }, ClaudeProtocol.SetEffort("low"));

        // Assert
        Assert.Equal("low", new ClaudeClient("workspace", "instructions.txt", Store()).Settings.Effort);
    }

    [Fact]
    public async Task Choose_WhenClaudeRunsWithTheSameEffort_ThenStillSendsIt()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        var input = new ClaudeSessionTests.LineWriter();
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));

        // Act
        client.Choose(client.Settings, ClaudeProtocol.SetEffort(client.Settings.Effort));

        // Assert
        Assert.Contains("\"effortLevel\":\"xhigh\"", await input.NextAsync());
    }

    [Fact]
    public async Task Settings_WhenClaudeRuns_ThenSendsNothing()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        var input = new ClaudeSessionTests.LineWriter();
        var session = new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener());
        client.Attach(session);

        // Act
        client.Settings = client.Settings with { PermissionMode = "plan" };
        await session.SendAsync("slut");

        // Assert
        Assert.Equal("slut", await input.NextAsync());
    }

    [Fact]
    public async Task Withdraw_WhenAPromptIsStillBeingWritten_ThenReturnsAtOnceAndKeepsTheOrder()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        using var input = new ClaudeSessionTests.LineWriter(blocked: true);
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));
        _ = client.SendAsync("id-1", "hej", []);

        // Act
        await Task.Run(() => client.Withdraw("id-1"), Token).WaitAsync(TimeSpan.FromSeconds(1), Token);
        input.Open();

        // Assert
        Assert.Contains("hej", await input.NextAsync());
        Assert.Contains("cancel_async_message", await input.NextAsync());
    }


    [Fact]
    public async Task SendAsync_WhenNotStarted_ThenFailsWithInvalidOperation()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());

        // Act
        var sending = () => client.SendAsync("id-1", "hej", []);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(sending);
    }

    [Fact]
    public async Task RequestAsync_WhenNotStarted_ThenFailsWithInvalidOperation()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());

        // Act
        var asking = () => client.RequestAsync(new JsonObject { ["subtype"] = "mcp_status" });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(asking);
    }
}
