using System.Text;
using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class ConnectorsTests
{
    static Connector Atlassian => Connectors.All.Single(connector => connector.Title == "Atlassian Rovo");
    static Connector GitHub => Connectors.All.Single(connector => connector.Title == "GitHub");

    [Fact]
    public void Servers_WhenClaudeReportsStatus_ThenReadsNameStatusUrlAndError()
    {
        // Arrange
        var status = JsonNode.Parse("""{"mcpServers":[{"name":"claude.ai Atlassian Rovo","status":"connected","config":{"type":"claudeai-proxy","url":"https://mcp.atlassian.com/v1/mcp","id":"x"}},{"name":"hamster-github","status":"failed","config":{"type":"http","url":"https://api.githubcopilot.com/mcp/","headers":{"Authorization":"Bearer x","X-MCP-Readonly":"true"}},"error":"401"}]}""")!.AsObject();

        // Act
        var servers = Connectors.Servers(status);

        // Assert
        Assert.Equal(
            [new McpServer("claude.ai Atlassian Rovo", "connected", "https://mcp.atlassian.com/v1/mcp", null), new McpServer("hamster-github", "failed", "https://api.githubcopilot.com/mcp/", "401", ReadOnly: true, HasToken: true)],
            servers);
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
        row.Show(status is null ? null : new McpServer("hamster-github", status, "https://api.githubcopilot.com/mcp/", null));

        // Assert
        Assert.Equal(expected, row.CanInstall);
    }

    [Fact]
    public void Show_WhenInstalledButClaudeIsNotRestartedYet_ThenDoesNotOfferInstallAgain()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);
        row.Show(null);
        row.AwaitRestart();

        // Act
        row.Show(null);

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
        row.Show(new McpServer(name, "connected", "https://api.githubcopilot.com/mcp/", null));

        // Assert
        Assert.Equal(expected, row.CanEdit);
    }

    [Fact]
    public void State_WhenGitHubIsReadOnly_ThenSaysSo()
    {
        // Arrange
        var row = new ConnectorRow(GitHub);

        // Act
        row.Show(new McpServer("hamster-github", "connected", "https://api.githubcopilot.com/mcp/", null, ReadOnly: true));

        // Assert
        Assert.Equal("Forbundet · kun læse", row.State);
    }
}
