using System.Text;

namespace Hamster.Tests;

public sealed class ConnectorsTests : IDisposable
{
    readonly string settingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    static Connector Atlassian => Connectors.All.Single(connector => connector.Title == "Atlassian Rovo");
    static Connector GitHub => Connectors.All.Single(connector => connector.Title == "GitHub");
    static Connector Microsoft365 => Connectors.All.Single(connector => connector.Title == "Microsoft 365");

    public void Dispose() => File.Delete(settingsFile);

    Connectors NewConnectors(Action restart) =>
        new(new ClaudeClient("workspace", "instructions.txt", new JsonFile<ClaudeSettings>(settingsFile, ClaudeSettings.Default)), restart, () => false);

    [Fact]
    public void UseClaudeAi_WhenTurnedOn_ThenRemembersItAndRestarts()
    {
        // Arrange
        var restarts = 0;

        // Act
        NewConnectors(() => restarts++).UseClaudeAi(GitHub, true);

        // Assert
        Assert.Equal((true, 1), (NewConnectors(() => { }).UsesClaudeAi(GitHub), restarts));
    }

    [Fact]
    public void UseClaudeAi_WhenTurnedOff_ThenKeepsTheOthers()
    {
        // Arrange
        var connectors = NewConnectors(() => { });
        connectors.UseClaudeAi(GitHub, true);
        connectors.UseClaudeAi(Atlassian, true);

        // Act
        connectors.UseClaudeAi(GitHub, false);

        // Assert
        Assert.Equal((false, true), (connectors.UsesClaudeAi(GitHub), connectors.UsesClaudeAi(Atlassian)));
    }

    [Fact]
    public void FindIn_WhenSeveralServersUseTheHost_ThenPrefersOneThatWorks()
    {
        // Arrange
        McpServer[] servers = [new("plugin:atlassian:atlassian", "needs-auth", "https://mcp.atlassian.com/v2/mcp", null), new("claude.ai Atlassian Rovo", "disabled", "https://mcp.atlassian.com/v1/mcp", null)];

        // Act
        var server = Atlassian.FindIn(servers);

        // Assert
        Assert.Equal("claude.ai Atlassian Rovo", server?.Name);
    }

    [Fact]
    public void FindIn_WhenEveryServerFails_ThenPrefersItsOwn()
    {
        // Arrange
        McpServer[] servers = [new("plugin:atlassian:atlassian", "needs-auth", "https://mcp.atlassian.com/v2/mcp", null), new("hamster-atlassian-rovo", "failed", "https://mcp.atlassian.com/v2/mcp", "401")];

        // Act
        var server = Atlassian.FindIn(servers);

        // Assert
        Assert.Equal("hamster-atlassian-rovo", server?.Name);
    }

    [Fact]
    public void FindIn_WhenNoServerUsesTheHost_ThenFindsNothing()
    {
        // Arrange
        McpServer[] servers = [new("claude.ai Linear", "connected", "https://mcp.linear.app/mcp", null), new("local", "connected", null, null)];

        // Act
        var server = GitHub.FindIn(servers);

        // Assert
        Assert.Null(server);
    }

    [Fact]
    public void AddArguments_WhenGitHub_ThenAddsItForTheUserAsReadOnly()
    {
        // Act
        var arguments = GitHub.AddArguments(["ghp_1"], allowWrite: false);

        // Assert
        Assert.Equal(["mcp", "add", "--scope", "user", "--transport", "http", "hamster-github", "https://api.githubcopilot.com/mcp/", "--header", "Authorization: Bearer ghp_1", "--header", "X-MCP-Readonly: true"], arguments);
    }

    [Fact]
    public void AddArguments_WhenWritingIsAllowed_ThenLeavesOutReadOnly()
    {
        // Act
        var arguments = GitHub.AddArguments(["ghp_1"], allowWrite: true);

        // Assert
        Assert.DoesNotContain("X-MCP-Readonly: true", arguments);
    }

    [Fact]
    public void AddArguments_WhenAtlassian_ThenAuthorizesWithEmailAndToken()
    {
        // Act
        var arguments = Atlassian.AddArguments(["mig@example.com", "abc"], allowWrite: false);

        // Assert
        Assert.Equal($"Authorization: Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("mig@example.com:abc"))}", arguments[^1]);
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsNotChosen_ThenBlocksTheClaudeAiConnector()
    {
        // Act
        var denied = Connectors.DeniedServers([]);

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosen_ThenBlocksTheHamstersOwn()
    {
        // Act
        var denied = Connectors.DeniedServers(["hamster-atlassian-rovo"]);

        // Assert
        Assert.Equal("""[{"serverName":"hamster-atlassian-rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosenForGitHub_ThenBlocksTheHamstersGitHub()
    {
        // Act
        var denied = Connectors.DeniedServers(["hamster-github"]);

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"},{"serverName":"hamster-github"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosenForMicrosoft365_ThenBlocksNothingForIt()
    {
        // Act
        var denied = Connectors.DeniedServers(["hamster-microsoft-365"]);

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"}]""", denied.ToJsonString());
    }

    [Fact]
    public void Show_WhenMicrosoft365IsOff_ThenOnlyTheClaudeAiSwitchIsOffered()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365);

        // Act
        row.Show([]);

        // Assert
        Assert.Equal((false, false, "Slået fra"), (row.CanInstall, row.CanToggle, row.State));
    }

    [Fact]
    public void Show_WhenMicrosoft365IsConnectedThroughClaudeAi_ThenHasNoToggleOfItsOwn()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365, usesClaudeAi: true);

        // Act
        row.Show([new McpServer("claude.ai Microsoft 365", "connected", "https://microsoft365.mcp.claude.com/mcp", null, FromClaudeAi: true)]);

        // Assert
        Assert.Equal((false, "Forbundet"), (row.CanToggle, row.State));
    }

    [Fact]
    public void Show_WhenClaudeAiIsUsed_ThenOnlyCountsClaudeAiServers()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, usesClaudeAi: true);

        // Act
        row.Show([new McpServer("hamster-atlassian-rovo", "connected", "https://mcp.atlassian.com/v2/mcp", null), new McpServer("claude.ai Atlassian Rovo", "connected", "https://mcp.atlassian.com/v1/mcp", null, FromClaudeAi: true)]);

        // Assert
        Assert.Equal("claude.ai Atlassian Rovo", row.Server?.Name);
    }

    [Fact]
    public void Show_WhenClaudeAiStaysMissing_ThenGivesUp()
    {
        // Arrange
        var row = new ConnectorRow(GitHub, usesClaudeAi: true);
        for (var i = 0; i < 4; i++)
            row.Show([]);

        // Act
        row.Show([]);

        // Assert
        Assert.True(row.ClaudeAiMissing);
    }

    [Fact]
    public void Show_WhenClaudeAiIsMissingFourTimes_ThenKeepsWaiting()
    {
        // Arrange
        var row = new ConnectorRow(GitHub, usesClaudeAi: true);
        for (var i = 0; i < 3; i++)
            row.Show([]);

        // Act
        row.Show([]);

        // Assert
        Assert.False(row.ClaudeAiMissing);
    }

    [Fact]
    public void Show_WhenARestartIsPending_ThenWaitsForIt()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365, usesClaudeAi: true);
        for (var i = 0; i < 4; i++)
            row.Show([], restartPending: true);

        // Act
        row.Show([], restartPending: true);

        // Assert
        Assert.Equal((false, "Skifter til claude.ai, når Claude er genstartet"), (row.ClaudeAiMissing, row.State));
    }

    [Fact]
    public void UseClaudeAi_WhenSwitchedAgain_ThenForgetsTheOldProblem()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);
        row.Finish("Ikke fundet på claude.ai.");

        // Act
        row.UseClaudeAi(true);

        // Assert
        Assert.Null(row.Problem);
    }

    [Fact]
    public void Show_WhenClaudeAiIsUsedButNotFound_ThenSaysSoWithoutOfferingInstall()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, usesClaudeAi: true);

        // Act
        row.Show([]);

        // Assert
        Assert.Equal((false, "Ikke fundet på claude.ai"), (row.CanInstall, row.State));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("needs-auth", true)]
    [InlineData("failed", true)]
    [InlineData("disabled", false)]
    [InlineData("connected", false)]
    public void CanInstall_WhenTheServerHasStatus_ThenOnlyWhenItCannotWork(string? status, bool expected)
    {
        // Arrange
        var row = new ConnectorRow(GitHub);

        // Act
        row.Show(status is null ? Array.Empty<McpServer>() : [new McpServer("hamster-github", status, "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal(expected, row.CanInstall);
    }

    [Fact]
    public void Show_WhenInstalledButClaudeIsNotRestartedYet_ThenDoesNotOfferInstallAgain()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);
        row.Show([]);
        row.AwaitRestart();

        // Act
        row.Show([]);

        // Assert
        Assert.Equal((false, "Installeret – bliver aktiv, når Claude er genstartet"), (row.CanInstall, row.State));
    }

    [Theory]
    [InlineData("hamster-github", true)]
    [InlineData("claude.ai GitHub", false)]
    public void CanEdit_WhenInstalled_ThenOnlyTheHamstersOwnServer(string name, bool expected)
    {
        // Arrange
        var row = new ConnectorRow(GitHub);

        // Act
        row.Show([new McpServer(name, "connected", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal(expected, row.CanEdit);
    }

    [Fact]
    public void Show_WhenItsOwnServerFailsAfterTheRestart_ThenOffersInstallAgain()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);
        row.Show([]);
        row.AwaitRestart();

        // Act
        row.Show([new McpServer("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal((true, "Mangler login"), (row.CanInstall, row.State));
    }
}
