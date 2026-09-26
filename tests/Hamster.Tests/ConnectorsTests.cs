using System.Text;
using System.Text.Json.Nodes;

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

    [Theory]
    [InlineData(ConnectorSource.ClaudeAi)]
    [InlineData(ConnectorSource.Off)]
    [InlineData(ConnectorSource.Login)]
    public void SetSource_WhenChosen_ThenRemembersItAndRestarts(ConnectorSource source)
    {
        // Arrange
        var restarts = 0;

        // Act
        NewConnectors(() => restarts++).SetSource(Atlassian, source);

        // Assert
        Assert.Equal((source, 1), (NewConnectors(() => { }).SourceOf(Atlassian), restarts));
    }

    [Fact]
    public void SetSource_WhenSwitchedBetweenClaudeAiAndLogin_ThenDoesNotRestartClaude()
    {
        // Arrange
        var restarts = 0;
        var connectors = NewConnectors(() => restarts++);
        connectors.SetSource(Atlassian, ConnectorSource.ClaudeAi);

        // Act
        connectors.SetSource(Atlassian, ConnectorSource.Login);

        // Assert
        Assert.Equal((ConnectorSource.Login, 1), (connectors.SourceOf(Atlassian), restarts));
    }

    [Fact]
    public void SourceOf_WhenLoginIsChosenForAConnectorWithoutLogin_ThenUsesTheHamster()
    {
        // Act
        var source = Connectors.SourceOf(GitHub, ClaudeSettings.Default with { LoginConnectors = ["hamster-github"] });

        // Assert
        Assert.Equal(ConnectorSource.Hamster, source);
    }

    [Theory]
    [InlineData("aciesdk", "https://aciesdk.atlassian.net")]
    [InlineData(" aciesdk.atlassian.net ", "https://aciesdk.atlassian.net")]
    [InlineData("https://aciesdk.atlassian.net/jira/your-work", "https://aciesdk.atlassian.net")]
    [InlineData("example.com", null)]
    [InlineData("", null)]
    public void SiteFrom_WhenTheUserTypesTheSite_ThenMakesItsAddress(string input, string? expected)
    {
        // Act
        var site = Atlassian.Login!.SiteFrom(input);

        // Assert
        Assert.Equal(expected, site);
    }

    [Theory]
    [InlineData("https://aciesdk.atlassian.net/jira/your-work", "https://aciesdk.atlassian.net")]
    [InlineData("https://start.atlassian.com/", null)]
    [InlineData("http://aciesdk.atlassian.net/", null)]
    public void SiteOf_WhenTheLoginWindowShowsAPage_ThenFindsTheJiraSite(string page, string? expected)
    {
        // Act
        var site = Atlassian.Login!.SiteOf(new Uri(page));

        // Assert
        Assert.Equal(expected, site);
    }

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
    public void SetSource_WhenChangedAgain_ThenKeepsTheOthers()
    {
        // Arrange
        var connectors = NewConnectors(() => { });
        connectors.SetSource(GitHub, ConnectorSource.Off);
        connectors.SetSource(Atlassian, ConnectorSource.ClaudeAi);

        // Act
        connectors.SetSource(GitHub, ConnectorSource.Hamster);

        // Assert
        Assert.Equal((ConnectorSource.Hamster, ConnectorSource.ClaudeAi), (connectors.SourceOf(GitHub), connectors.SourceOf(Atlassian)));
    }

    [Theory]
    [InlineData("Atlassian Rovo", ConnectorSource.Hamster)]
    [InlineData("Microsoft 365", ConnectorSource.Off)]
    public void SourceOf_WhenNothingIsChosen_ThenUsesTheHamsterWhenItCan(string title, ConnectorSource expected)
    {
        // Act
        var source = Connectors.SourceOf(Connectors.All.Single(connector => connector.Title == title), ClaudeSettings.Default);

        // Assert
        Assert.Equal(expected, source);
    }

    [Fact]
    public void FindIn_WhenSeveralClaudeAiServersUseTheHost_ThenPrefersOneThatWorks()
    {
        // Arrange
        McpServer[] servers = [new("claude.ai Atlassian", "needs-auth", "https://mcp.atlassian.com/v2/mcp", null, FromClaudeAi: true), new("claude.ai Atlassian Rovo", "disabled", "https://mcp.atlassian.com/v1/mcp", null, FromClaudeAi: true)];

        // Act
        var server = Atlassian.FindIn(servers, claudeAi: true);

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
    public void FindIn_WhenOnlyAnotherServerUsesTheHost_ThenFindsNothing()
    {
        // Arrange
        McpServer[] servers = [new("plugin:atlassian:atlassian", "connected", "https://mcp.atlassian.com/v2/mcp", null)];

        // Act
        var server = Atlassian.FindIn(servers);

        // Assert
        Assert.Null(server);
    }

    [Fact]
    public void FindIn_WhenClaudeAiIsOff_ThenIgnoresClaudeAiServers()
    {
        // Arrange
        McpServer[] servers = [new("claude.ai GitHub", "connected", "https://api.githubcopilot.com/mcp/", null, FromClaudeAi: true)];

        // Act
        var server = GitHub.FindIn(servers);

        // Assert
        Assert.Null(server);
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
        var denied = Connectors.DeniedServers(ClaudeSettings.Default);

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosen_ThenBlocksTheHamstersOwn()
    {
        // Act
        var denied = Connectors.DeniedServers(ClaudeSettings.Default with { ClaudeAiConnectors = ["hamster-atlassian-rovo"] });

        // Assert
        Assert.Equal("""[{"serverName":"hamster-atlassian-rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosenForGitHub_ThenBlocksTheHamstersGitHub()
    {
        // Act
        var denied = Connectors.DeniedServers(ClaudeSettings.Default with { ClaudeAiConnectors = ["hamster-github"] });

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"},{"serverName":"hamster-github"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenClaudeAiIsChosenForMicrosoft365_ThenBlocksNothingForIt()
    {
        // Act
        var denied = Connectors.DeniedServers(ClaudeSettings.Default with { ClaudeAiConnectors = ["hamster-microsoft-365"] });

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenLoginIsChosen_ThenClaudeUsesTheClaudeAiConnector()
    {
        // Act
        var denied = Connectors.DeniedServers(ClaudeSettings.Default with { LoginConnectors = ["hamster-atlassian-rovo"] });

        // Assert
        Assert.Equal("""[{"serverName":"hamster-atlassian-rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
    }

    [Fact]
    public void DeniedServers_WhenAConnectorIsOff_ThenBlocksBothOfIts()
    {
        // Act
        var denied = Connectors.DeniedServers(ClaudeSettings.Default with { OffConnectors = ["hamster-atlassian-rovo"] });

        // Assert
        Assert.Equal("""[{"serverName":"claude.ai Atlassian Rovo"},{"serverName":"hamster-atlassian-rovo"},{"serverName":"claude.ai Microsoft 365"}]""", denied.ToJsonString());
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
        var row = new ConnectorRow(GitHub, ConnectorSource.ClaudeAi);
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
        var row = new ConnectorRow(Microsoft365, ConnectorSource.ClaudeAi);
        for (var i = 0; i < 4; i++)
            row.Show([], restartPending: true);

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

    [Theory]
    [InlineData("needs-auth", null, "GitHub: Mangler login")]
    [InlineData("failed", "401", "GitHub: Fejl: 401")]
    public async Task ProblemsAsync_WhenAConnectorIsBroken_ThenNamesIt(string status, string? error, string expected)
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", status, "https://api.githubcopilot.com/mcp/", error, false)));

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Equal([expected], problems);
    }

    [Fact]
    public async Task EnableAsync_WhenAUsedConnectorIsDisabledInClaude_ThenTurnsItBackOn()
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null, false)));
        var connectors = new Connectors(claude, () => { }, () => false);

        // Act
        await connectors.EnableAsync([new McpServer("hamster-github", "disabled", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Equal(("mcp_toggle", "hamster-github", true), ((string?)Assert.Single(claude.Sent)["subtype"], (string?)claude.Sent[0]["serverName"], (bool?)claude.Sent[0]["enabled"]));
    }

    [Fact]
    public async Task EnableAsync_WhenTheDisabledConnectorIsOff_ThenLeavesItOff()
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null, false)))
        {
            Settings = ClaudeSettings.Default with { OffConnectors = ["hamster-github"] },
        };
        var connectors = new Connectors(claude, () => { }, () => false);

        // Act
        await connectors.EnableAsync([new McpServer("hamster-github", "disabled", "https://api.githubcopilot.com/mcp/", null)]);

        // Assert
        Assert.Empty(claude.Sent);
    }

    [Fact]
    public async Task ProblemsAsync_WhenARestartIsPending_ThenReportsNothingYet()
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null, false)));

        // Act
        var problems = await new Connectors(claude, () => { }, () => true).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public async Task ProblemsAsync_WhenABrokenConnectorIsOff_ThenNoProblems()
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null, false)))
        {
            Settings = ClaudeSettings.Default with { OffConnectors = ["hamster-github"] },
        };

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("connected")]
    [InlineData("disabled")]
    public async Task ProblemsAsync_WhenAConnectorWorksOrIsTurnedOff_ThenNoProblems(string status)
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", status, "https://api.githubcopilot.com/mcp/", null, false)));

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public async Task ProblemsAsync_WhenStillConnecting_ThenWaitsUntilItIsConnected()
    {
        // Arrange
        var claude = new StatusClaude(
            Status(("hamster-github", "pending", "https://api.githubcopilot.com/mcp/", null, false)),
            Status(("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null, false)));

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Equal((0, 2), (problems.Count, claude.Requests));
    }

    [Fact]
    public async Task ProblemsAsync_WhenTheClaudeAiConnectorNeverShowsUp_ThenSaysSoAfterTheLastCheck()
    {
        // Arrange
        var claude = new StatusClaude(Status()) { Settings = ClaudeSettings.Default with { ClaudeAiConnectors = ["hamster-microsoft-365"] } };

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Equal(("Microsoft 365: Ikke fundet på claude.ai", Connectors.ClaudeAiChecks), (Assert.Single(problems), claude.Requests));
    }

    [Fact]
    public async Task ProblemsAsync_WhenTheClaudeAiConnectorShowsUpLate_ThenNoProblems()
    {
        // Arrange
        var claude = new StatusClaude(Status(), Status(("claude.ai Microsoft 365", "connected", "https://microsoft365.mcp.claude.com/mcp", null, true)))
        {
            Settings = ClaudeSettings.Default with { ClaudeAiConnectors = ["hamster-microsoft-365"] },
        };

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Empty(problems);
    }

    static JsonObject Status(params (string Name, string Status, string Url, string? Error, bool ClaudeAi)[] servers) => new()
    {
        ["mcpServers"] = new JsonArray([.. servers.Select(server => new JsonObject
        {
            ["name"] = server.Name,
            ["status"] = server.Status,
            ["config"] = new JsonObject { ["url"] = server.Url },
            ["error"] = server.Error,
            ["source"] = server.ClaudeAi ? "claudeai" : null,
        })]),
    };

    sealed class StatusClaude(params JsonObject[] statuses) : IClaudeClient
    {
        public int Requests { get; private set; }
        public List<JsonObject> Sent { get; } = [];
        public ClaudeSettings Settings { get; set; } = ClaudeSettings.Default;
        public bool IsRunning => true;

        public Task StartAsync(string? sessionId, IClaudeListener listener) => Task.CompletedTask;

        public Task SendAsync(string id, string prompt, IReadOnlyList<ImageAttachment> images) => Task.CompletedTask;

        public Task<JsonObject?> RequestAsync(JsonObject request)
        {
            Sent.Add(request);
            return Task.FromResult<JsonObject?>(statuses[Math.Min(Requests++, statuses.Length - 1)]);
        }

        public void Interrupt()
        {
        }

        public void Withdraw(string id)
        {
        }

        public void End()
        {
        }
    }
}
