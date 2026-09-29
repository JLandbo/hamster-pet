using System.Text;
using System.Text.Json.Nodes;
using Hamster.Core.Claude;
using Hamster.Core.Languages;

namespace Hamster.Core.Connections;

public enum ConnectorSource { Off, Hamster, ClaudeAi, Login }

public sealed record ConnectorField(string Key, bool Secret)
{
    public string Label => Key switch
    {
        "email" => Strings.Of("Settings.Email"),
        "api-token" => Strings.Of("Settings.ApiToken"),
        "token" => Strings.Of("Settings.Token"),
        _ => Key,
    };
}

public sealed record WebLogin(string SiteSuffix, string CheckPath)
{
    public string? SiteOf(Uri page) => page.Scheme == Uri.UriSchemeHttps && page.Host.EndsWith(SiteSuffix, StringComparison.OrdinalIgnoreCase) ? page.GetLeftPart(UriPartial.Authority) : null;

    public string? SiteFrom(string input) => Uri.TryCreate(input.Trim().Contains("://") ? input.Trim() : $"https://{input.Trim()}", UriKind.Absolute, out var url) && url.Host.Length > 0
            ? SiteOf(new Uri($"https://{(url.Host.Contains('.') ? url.Host : url.Host + SiteSuffix)}"))
            : null;
}

public sealed record ConnectorProblem(string Title, McpServer? Server)
{
    public string Text => $"{Title}: {(Server is null ? Connector.MissingOnClaudeAi : Connector.StateOf(Server))}";
}

public sealed record Connector(string Title, string Name, string Url, Uri? TokenPage, IReadOnlyList<ConnectorField> Fields, Func<IReadOnlyList<string>, string>? Authorization,
    bool CanBeReadOnly = false, string? ClaudeAiName = null, WebLogin? Login = null)
{
    public static string MissingOnClaudeAi => Strings.Of("Settings.MissingOnClaudeAi");

    public bool Installable => Authorization is not null;

    public string ServerUrlPattern => $"https://{new Uri(Url).Host}/*";

    public static string StateOf(McpServer server) => server.Status switch
    {
        "connected" => Strings.Of("Settings.Connected"),
        "pending" => Strings.Of("Settings.Connecting"),
        "disabled" => Strings.Of("Settings.TurnedOff"),
        "needs-auth" => Strings.Of("Settings.NeedsLogin"),
        "failed" => Strings.Format("Settings.Failed", server.Error),
        var status => status,
    };

    public ConnectorProblem? ProblemIn(IEnumerable<McpServer> servers, bool claudeAi) => FindIn(servers, claudeAi) switch
        {
            null => claudeAi ? new(Title, null) : null,
            { Status: "needs-auth" or "failed" } server => new(Title, server),
            _ => null,
        };

    public McpServer? FindIn(IEnumerable<McpServer> servers, bool claudeAi = false) => claudeAi
            ? servers.Where(server => server.FromClaudeAi && Uri.TryCreate(server.Url, UriKind.Absolute, out var url) && url.Host == new Uri(Url).Host)
                .OrderByDescending(server => server.IsUsable).FirstOrDefault()
            : servers.FirstOrDefault(server => !server.FromClaudeAi && server.Name == Name);

    public IReadOnlyList<string> AddArguments(IReadOnlyList<string> values, bool allowWrite) => [
        "mcp", "add", "--scope", "user", "--transport", "http", Name, Url, "--header", $"Authorization: {Authorization!(values)}",
        .. (CanBeReadOnly && !allowWrite ? ["--header", $"{Connectors.ReadOnlyHeader}: true"] : Array.Empty<string>()),
    ];
}

public sealed class Connectors(IClaudeClient claude, Action restart, Func<bool> restartPending)
{
    public const string ReadOnlyHeader = "X-MCP-Readonly";
    public const int ClaudeAiChecks = 5;
    public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    public static readonly IReadOnlyList<Connector> All =
    [
        new("Atlassian Rovo", "hamster-atlassian-rovo", "https://mcp.atlassian.com/v2/mcp", new("https://id.atlassian.com/manage-profile/security/api-tokens"),
            [new("email", Secret: false), new("api-token", Secret: true)],
            values => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{values[0]}:{values[1]}"))}",
            ClaudeAiName: "claude.ai Atlassian Rovo",
            Login: new(".atlassian.net", "/rest/api/3/myself")),
        new("GitHub", "hamster-github", "https://api.githubcopilot.com/mcp/", new("https://github.com/settings/personal-access-tokens"),
            [new("token", Secret: true)],
            values => $"Bearer {values[0]}",
            CanBeReadOnly: true),
        new("Microsoft 365", "hamster-microsoft-365", "https://microsoft365.mcp.claude.com/mcp", null, [], null,
            ClaudeAiName: "claude.ai Microsoft 365"),
    ];

    public static JsonArray AllowedServers() => [.. All.Select(connector => new JsonObject { ["serverUrl"] = connector.ServerUrlPattern })];

    public static JsonArray DeniedServers(ClaudeSettings settings) => [.. All.SelectMany(connector => SourceOf(connector, settings) switch
            {
                ConnectorSource.Hamster => new[] { connector.ClaudeAiName },
                ConnectorSource.ClaudeAi or ConnectorSource.Login => [connector.Installable ? connector.Name : null],
                _ => [connector.ClaudeAiName, connector.Installable ? connector.Name : null],
            })
            .OfType<string>().Select(name => new JsonObject { ["serverName"] = name })];

    public static ConnectorSource SourceOf(Connector connector, ClaudeSettings settings) => settings.ClaudeAiConnectors?.Contains(connector.Name) == true ? ConnectorSource.ClaudeAi
        : settings.LoginConnectors?.Contains(connector.Name) == true && connector.Login is not null ? ConnectorSource.Login
        : settings.OffConnectors?.Contains(connector.Name) == true || !connector.Installable ? ConnectorSource.Off
        : ConnectorSource.Hamster;

    public bool RestartPending => restartPending();

    public ConnectorSource SourceOf(Connector connector) => SourceOf(connector, claude.Settings);

    public bool UsesClaudeAi(Connector connector) => SourceOf(connector) is ConnectorSource.ClaudeAi or ConnectorSource.Login;

    public void SetSource(Connector connector, ConnectorSource source)
    {
        string[] Others(IReadOnlyList<string>? names) => [.. (names ?? []).Where(name => name != connector.Name)];
        var denied = DeniedServers(claude.Settings);
        claude.Settings = claude.Settings with
        {
            ClaudeAiConnectors = [.. Others(claude.Settings.ClaudeAiConnectors), .. source == ConnectorSource.ClaudeAi ? [connector.Name] : Array.Empty<string>()],
            OffConnectors = [.. Others(claude.Settings.OffConnectors), .. source == ConnectorSource.Off && connector.Installable ? [connector.Name] : Array.Empty<string>()],
            LoginConnectors = [.. Others(claude.Settings.LoginConnectors), .. source == ConnectorSource.Login ? [connector.Name] : Array.Empty<string>()],
        };
        if (!JsonNode.DeepEquals(denied, DeniedServers(claude.Settings)))
        {
            restart();
        }
    }

    public async Task<IReadOnlyList<McpServer>> ServersAsync() => ClaudeProtocol.McpServers(await claude.RequestAsync(ClaudeProtocol.McpStatus()));

    public async Task<IReadOnlyList<ConnectorProblem>> ProblemsAsync(TimeSpan interval)
    {
        if (RestartPending)
        {
            return [];
        }
        HashSet<string> reconnected = [];
        for (var check = 1; ; check++)
        {
            var servers = await ServersAsync();
            await EnableAsync(servers);
            if (await ReconnectAsync(servers, reconnected))
            {
                servers = await ServersAsync();
            }
            var problems = All.Where(connector => SourceOf(connector) != ConnectorSource.Off)
                .Select(connector => (Problem: connector.ProblemIn(servers, UsesClaudeAi(connector)), Missing: UsesClaudeAi(connector) && connector.FindIn(servers, claudeAi: true) is null))
                .Where(found => found.Problem is not null).ToArray();
            if (check == ClaudeAiChecks || !servers.Any(server => server.Status == "pending") && !problems.Any(found => found.Missing))
            {
                return [.. problems.Select(found => found.Problem!)];
            }
            await Task.Delay(interval);
        }
    }

    // Claude keeps a connector that lost its login in that state until asked to reconnect, so a login made since is only picked up this way.
    public async Task<bool> ReconnectAsync(IReadOnlyList<McpServer> servers, HashSet<string> reconnected)
    {
        McpServer[] broken = [.. UsedServers(servers).Where(server => server.Status is "needs-auth" or "failed" && !reconnected.Contains(server.Name))];
        foreach (var server in broken)
        {
            reconnected.Add(server.Name);
            try
            {
                await claude.RequestAsync(ClaudeProtocol.McpReconnect(server.Name));
            }
            catch (InvalidOperationException)
            {
            }
        }
        return broken.Length > 0;
    }

    IEnumerable<McpServer> UsedServers(IReadOnlyList<McpServer> servers) => All.Where(connector => SourceOf(connector) != ConnectorSource.Off)
        .Select(connector => connector.FindIn(servers, UsesClaudeAi(connector))).OfType<McpServer>();

    public async Task EnableAsync(IReadOnlyList<McpServer> servers)
    {
        foreach (var server in UsedServers(servers).Where(server => server.Status == "disabled"))
        {
            await claude.RequestAsync(ClaudeProtocol.McpToggle(server.Name, true));
        }
    }

    public async Task InstallAsync(Connector connector, IReadOnlyList<string> values, bool allowWrite)
    {
        try
        {
            await ClaudeClient.RunCommandAsync(["mcp", "remove", "--scope", "user", connector.Name]);
        }
        catch (InvalidOperationException)
        {
        }
        await ClaudeClient.RunCommandAsync(connector.AddArguments(values, allowWrite));
        restart();
    }
}
