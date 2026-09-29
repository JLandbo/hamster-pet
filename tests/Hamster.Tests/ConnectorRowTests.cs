namespace Hamster.Tests;

public sealed class ConnectorRowTests
{
    static Connector Atlassian => Connectors.All.Single(connector => connector.Title == "Atlassian Rovo");
    static Connector GitHub => Connectors.All.Single(connector => connector.Title == "GitHub");
    static Connector Microsoft365 => Connectors.All.Single(connector => connector.Title == "Microsoft 365");

    [Theory]
    [InlineData(null, "Site:", "Log ind…")]
    [InlineData("https://aciesdk.atlassian.net", "Logget ind på aciesdk.atlassian.net", "Log ud")]
    public void ShowLogin_WhenLoginIsChosen_ThenSaysWhereTheHamsterIsLoggedIn(string? site, string state, string label)
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.Login);

        // Act
        row.ShowLogin(site);

        // Assert
        Assert.Equal((state, label), (row.State, row.LoginLabel));
    }

    [Theory]
    [InlineData(null, "Tjekker login på aciesdk.atlassian.net…")]
    [InlineData(false, "Kunne ikke tjekke login på aciesdk.atlassian.net")]
    public void ShowLogin_WhenTheLoginIsNotConfirmed_ThenDoesNotSayLoggedIn(bool? confirmed, string state)
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.Login);

        // Act
        row.ShowLogin("https://aciesdk.atlassian.net", confirmed);

        // Assert
        Assert.Equal(state, row.State);
    }

    [Fact]
    public void Show_WhenMicrosoft365IsOff_ThenOffersNothingToInstall()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365, ConnectorSource.Off);

        // Act
        row.Show([]);

        // Assert
        Assert.Equal((false, false, "Slået fra"), (row.CanInstall, row.CanEdit, row.State));
    }

    [Fact]
    public void Show_WhenMicrosoft365IsConnectedThroughClaudeAi_ThenSaysSo()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365, ConnectorSource.ClaudeAi);

        // Act
        row.Show([new McpServer("claude.ai Microsoft 365", "connected", "https://microsoft365.mcp.claude.com/mcp", null, FromClaudeAi: true)]);

        // Assert
        Assert.Equal((false, "Forbundet via claude.ai"), (row.CanEdit, row.State));
    }

    [Fact]
    public void Show_WhenOff_ThenIgnoresItsServers()
    {
        // Arrange
        var row = new ConnectorRow(GitHub, ConnectorSource.Off);

        // Act
        row.Show([new McpServer("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal((null, false, "Slået fra"), (row.Server, row.CanInstall, row.State));
    }

    [Fact]
    public void Show_WhenSwitchingIsPending_ThenOffersNoInstall()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);

        // Act
        row.Show([], restartPending: true);

        // Assert
        Assert.Equal((false, "Skifter, når Claude er genstartet"), (row.CanInstall, row.State));
    }

    [Fact]
    public void CanEdit_WhenClaudeAiIsChosen_ThenOffersNoToken()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.ClaudeAi);

        // Act
        row.Show([new McpServer("claude.ai Atlassian Rovo", "connected", "https://mcp.atlassian.com/v1/mcp", null, FromClaudeAi: true)]);

        // Assert
        Assert.Equal((false, "Forbundet via claude.ai"), (row.CanEdit, row.State));
    }

    [Fact]
    public void Show_WhenTheHamstersOwnServerIsConnected_ThenSaysItUsesYourToken()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);

        // Act
        row.Show([new McpServer("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal((true, "Forbundet med dit token"), (row.CanEdit, row.State));
    }

    [Theory]
    [InlineData(ConnectorSource.Off, false)]
    [InlineData(ConnectorSource.Hamster, true)]
    [InlineData(ConnectorSource.ClaudeAi, true)]
    public void HasChoices_WhenSourceIsChosen_ThenOnlyWhenOn(ConnectorSource source, bool expected)
    {
        // Arrange
        var row = new ConnectorRow(GitHub, source);

        // Act
        row.ShowChoices([new Choice("review", "Review anmodet af mig", false, ChoiceKind.GitHub)], [], canFetch: false, null);

        // Assert
        Assert.Equal(expected, row.HasChoices);
    }

    [Fact]
    public void Show_WhenClaudeAiIsUsed_ThenOnlyCountsClaudeAiServers()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.ClaudeAi);

        // Act
        row.Show([new McpServer("hamster-atlassian-rovo", "connected", "https://mcp.atlassian.com/v2/mcp", null), new McpServer("claude.ai Atlassian Rovo", "connected", "https://mcp.atlassian.com/v1/mcp", null, FromClaudeAi: true)]);

        // Assert
        Assert.Equal("claude.ai Atlassian Rovo", row.Server?.Name);
    }

    [Fact]
    public void Show_WhenClaudeAiStaysMissing_ThenGivesUp()
    {
        // Arrange
        var row = new ConnectorRow(GitHub, ConnectorSource.ClaudeAi);
        for (var i = 0; i < 4; i++)
        {
            row.Show([]);
        }

        // Act
        row.Show([]);

        // Assert
        Assert.True(row.ClaudeAiMissing);
    }

    [Fact]
    public void Show_WhenClaudeAiIsMissingFourTimes_ThenKeepsWaiting()
    {
        // Arrange
        var row = new ConnectorRow(GitHub, ConnectorSource.ClaudeAi);
        for (var i = 0; i < 3; i++)
        {
            row.Show([]);
        }

        // Act
        row.Show([]);

        // Assert
        Assert.False(row.ClaudeAiMissing);
    }

    [Fact]
    public void Show_WhenARestartIsPending_ThenWaitsForIt()
    {
        // Arrange
        var row = new ConnectorRow(Microsoft365, ConnectorSource.ClaudeAi);
        for (var i = 0; i < 4; i++)
        {
            row.Show([], restartPending: true);
        }

        // Act
        row.Show([], restartPending: true);

        // Assert
        Assert.Equal((false, "Skifter, når Claude er genstartet"), (row.ClaudeAiMissing, row.State));
    }

    [Fact]
    public void SetSource_WhenSwitchedAgain_ThenForgetsTheOldProblem()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);
        row.Finish("Ikke fundet på claude.ai.");

        // Act
        row.SetSource(ConnectorSource.ClaudeAi);

        // Assert
        Assert.Null(row.Problem);
    }

    [Fact]
    public void SetSource_WhenSwitchedToHamster_ThenWaitsForFreshStatusBeforeOfferingInstall()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.ClaudeAi);
        row.Show([new McpServer("claude.ai Atlassian Rovo", "connected", "https://mcp.atlassian.com/v1/mcp", null, FromClaudeAi: true)]);

        // Act
        row.SetSource(ConnectorSource.Hamster);

        // Assert
        Assert.Equal((false, "Henter…"), (row.CanInstall, row.State));
    }

    [Fact]
    public void Show_WhenClaudeAiIsUsedButNotFound_ThenSaysSoWithoutOfferingInstall()
    {
        // Arrange
        var row = new ConnectorRow(Atlassian, ConnectorSource.ClaudeAi);

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
        Assert.Equal((false, "Gemt – aktiveres, når Claude er genstartet"), (row.CanInstall, row.State));
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
        Assert.Equal((true, "Tokenet blev afvist"), (row.CanInstall, row.State));
    }
}
