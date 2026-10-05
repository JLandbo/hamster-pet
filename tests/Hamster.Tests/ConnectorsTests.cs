using System.Text;
using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ConnectorsTests : IDisposable
{
    readonly string _settingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    static Connector Atlassian => Connectors.All.Single(connector => connector.Title == "Atlassian Rovo");
    static Connector GitHub => Connectors.All.Single(connector => connector.Title == "GitHub");

    public void Dispose() => File.Delete(_settingsFile);

    Connectors NewConnectors(Action restart) => new(new ClaudeClient("workspace", "instructions.txt", new JsonFile<ClaudeSettings>(_settingsFile, ClaudeSettings.Default)), restart, () => false);

    [Fact]
    public void Fields_WhenShown_ThenTheirLabelsComeFromTheLanguage()
    {
        // Act
        var labels = Connectors.All.SelectMany(connector => connector.Fields).Select(field => field.Label);

        // Assert
        Assert.Equal(["E-mail", "API-token", "Token"], labels);
    }

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
    public void AllowedServers_WhenTheUserHasALocalServer_ThenAllowsItsCommand()
    {
        // Act
        var allowed = Connectors.AllowedServers("""{"mcpServers":{"hoboman":{"type":"stdio","command":"C:\\Hoboman\\hoboman-cli.exe","args":["mcp"]}}}""");

        // Assert
        Assert.Equal("""{"serverCommand":["C:\\Hoboman\\hoboman-cli.exe","mcp"]}""", allowed[^1]!.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"mcpServers":{"hamster-github":{"type":"http","url":"https://api.githubcopilot.com/mcp/"}}}""")]
    [InlineData("""{"mcpServers":{"odd":{"command":"tool.exe","args":[1]}}}""")]
    [InlineData("""{"mcpServers":{"twice":{"command":"a.exe"},"twice":{"command":"b.exe"}}}""")]
    public void AllowedServers_WhenThereIsNoValidLocalServer_ThenAllowsOnlyTheConnectors(string? claudeConfig)
    {
        // Act
        var allowed = Connectors.AllowedServers(claudeConfig);

        // Assert
        Assert.Equal(Connectors.All.Count, allowed.Count);
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
        Assert.Equal([expected], problems.Select(problem => problem.Text));
    }

    [Fact]
    public async Task ProblemsAsync_WhenTheSameProblemIsFoundAgain_ThenItIsEqual()
    {
        // Arrange
        var claude = new StatusClaude(Status(("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null, false)));
        var connectors = new Connectors(claude, () => { }, () => false);

        // Act
        var (first, second) = (await connectors.ProblemsAsync(TimeSpan.Zero), await connectors.ProblemsAsync(TimeSpan.Zero));

        // Assert
        Assert.Equal(Assert.Single(first), Assert.Single(second));
    }

    [Fact]
    public async Task ProblemsAsync_WhenTheProblemChanges_ThenItIsAnotherProblem()
    {
        // Arrange
        var needsLogin = new StatusClaude(Status(("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null, false)));
        var failed = new StatusClaude(Status(("hamster-github", "failed", "https://api.githubcopilot.com/mcp/", "401", false)));

        // Act
        var (first, second) = (await new Connectors(needsLogin, () => { }, () => false).ProblemsAsync(TimeSpan.Zero), await new Connectors(failed, () => { }, () => false).ProblemsAsync(TimeSpan.Zero));

        // Assert
        Assert.NotEqual(Assert.Single(first), Assert.Single(second));
    }

    [Fact]
    public async Task ProblemsAsync_WhenAConnectorHasLoggedInSinceItLostItsLogin_ThenReconnectsItAndReportsNoProblem()
    {
        // Arrange
        var claude = new StatusClaude(
            Status(("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null, false)),
            Status(("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null, false)));

        // Act
        var problems = await new Connectors(claude, () => { }, () => false).ProblemsAsync(TimeSpan.Zero);

        // Assert
        Assert.Empty(problems);
        Assert.Equal(["hamster-github"], claude.Sent.Where(request => (string?)request["subtype"] == "mcp_reconnect").Select(request => (string?)request["serverName"]));
    }

    [Fact]
    public async Task ReconnectAsync_WhenTheConnectorWasTriedBefore_ThenLeavesIt()
    {
        // Arrange
        var claude = new StatusClaude();
        var connectors = new Connectors(claude, () => { }, () => false);
        McpServer[] servers = [new("hamster-github", "needs-auth", "https://api.githubcopilot.com/mcp/", null)];
        HashSet<string> reconnected = [];
        await connectors.ReconnectAsync(servers, reconnected);

        // Act
        await connectors.ReconnectAsync(servers, reconnected);

        // Assert
        Assert.Single(claude.Sent);
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
        Assert.Equal(("Microsoft 365: Ikke fundet på claude.ai", Connectors.ClaudeAiChecks), (Assert.Single(problems).Text, claude.Requests));
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
            return Task.FromResult((string?)request["subtype"] == "mcp_status" ? statuses[Math.Min(Requests++, statuses.Length - 1)] : null);
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
