using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ClaudeClientTests : IDisposable
{
    readonly string settingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    JsonFile<ClaudeSettings> Store() => new(settingsFile, ClaudeSettings.Default);

    public void Dispose() => File.Delete(settingsFile);

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
    public void Choose_WhenClaudeRunsWithTheSameEffort_ThenStillSendsIt()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        var input = new StringWriter();
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));

        // Act
        client.Choose(client.Settings, ClaudeProtocol.SetEffort(client.Settings.Effort));

        // Assert
        Assert.Contains("\"effortLevel\":\"xhigh\"", input.ToString());
    }

    [Fact]
    public void Settings_WhenClaudeRuns_ThenSendsNothing()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());
        var input = new StringWriter();
        client.Attach(new ClaudeSession(new StringReader(""), input, new ClaudeSessionTests.FakeListener()));

        // Act
        client.Settings = client.Settings with { PermissionMode = "plan" };

        // Assert
        Assert.Empty(input.ToString());
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
