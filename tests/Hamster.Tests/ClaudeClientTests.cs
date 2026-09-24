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
    public void Remember_WhenClaudeReportsAMode_ThenTheNextStartUsesIt()
    {
        // Arrange
        var client = new ClaudeClient("workspace", "instructions.txt", Store());

        // Act
        client.Remember(client.Settings with { PermissionMode = "plan" });

        // Assert
        Assert.Equal("plan", new ClaudeClient("workspace", "instructions.txt", Store()).Settings.PermissionMode);
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
}
