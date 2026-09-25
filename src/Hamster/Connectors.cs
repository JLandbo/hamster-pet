using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;

namespace Hamster;

public sealed record ConnectorField(string Label, bool Secret);

public sealed record Connector(string Title, string Name, string Url, Uri? TokenPage, IReadOnlyList<ConnectorField> Fields, Func<IReadOnlyList<string>, string>? Authorization, bool CanBeReadOnly = false, string? ClaudeAiName = null)
{
    public bool Installable => Authorization is not null;

    public McpServer? FindIn(IEnumerable<McpServer> servers, bool claudeAi = false) =>
        servers.Where(server => (!claudeAi || server.FromClaudeAi) && Uri.TryCreate(server.Url, UriKind.Absolute, out var url) && url.Host == new Uri(Url).Host)
            .OrderByDescending(server => server.IsUsable)
            .ThenByDescending(server => server.Name == Name)
            .FirstOrDefault();

    public IReadOnlyList<string> AddArguments(IReadOnlyList<string> values, bool allowWrite) =>
    [
        "mcp", "add", "--scope", "user", "--transport", "http", Name, Url, "--header", $"Authorization: {Authorization!(values)}",
        .. (CanBeReadOnly && !allowWrite ? ["--header", $"{Connectors.ReadOnlyHeader}: true"] : Array.Empty<string>()),
    ];
}

public sealed class Connectors(IClaudeClient claude, Action restart, Func<bool> restartPending)
{
    public const string ReadOnlyHeader = "X-MCP-Readonly";

    public static readonly IReadOnlyList<Connector> All =
    [
        new("Atlassian Rovo", "hamster-atlassian-rovo", "https://mcp.atlassian.com/v2/mcp", new("https://id.atlassian.com/manage-profile/security/api-tokens"),
            [new("E-mail", Secret: false), new("API-token", Secret: true)],
            values => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{values[0]}:{values[1]}"))}",
            ClaudeAiName: "claude.ai Atlassian Rovo"),
        new("GitHub", "hamster-github", "https://api.githubcopilot.com/mcp/", new("https://github.com/settings/personal-access-tokens"),
            [new("Token", Secret: true)],
            values => $"Bearer {values[0]}",
            CanBeReadOnly: true),
        new("Microsoft 365", "hamster-microsoft-365", "https://microsoft365.mcp.claude.com/mcp", null, [], null,
            ClaudeAiName: "claude.ai Microsoft 365"),
    ];

    public static JsonArray AllowedServers() => [.. All.Select(connector => new JsonObject { ["serverUrl"] = $"https://{new Uri(connector.Url).Host}/*" })];

    public static JsonArray DeniedServers(IReadOnlyCollection<string> claudeAi) =>
        [.. All.Select(connector => !claudeAi.Contains(connector.Name) ? connector.ClaudeAiName : connector.Installable ? connector.Name : null)
            .OfType<string>().Select(name => new JsonObject { ["serverName"] = name })];

    public bool RestartPending => restartPending();

    public bool UsesClaudeAi(Connector connector) => claude.Settings.ClaudeAiConnectors?.Contains(connector.Name) == true;

    public void UseClaudeAi(Connector connector, bool use)
    {
        var others = (claude.Settings.ClaudeAiConnectors ?? []).Where(name => name != connector.Name);
        claude.Settings = claude.Settings with { ClaudeAiConnectors = use ? [.. others, connector.Name] : [.. others] };
        restart();
    }

    public async Task<IReadOnlyList<McpServer>> ServersAsync() => ClaudeProtocol.McpServers(await claude.RequestAsync(ClaudeProtocol.McpStatus()));

    public Task ToggleAsync(McpServer server, bool enabled) => claude.RequestAsync(ClaudeProtocol.McpToggle(server.Name, enabled));

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

public sealed class FieldInput(ConnectorField field)
{
    public ConnectorField Field { get; } = field;
    public string Value { get; set; } = "";
}

public sealed class ConnectorRow(Connector connector, bool usesClaudeAi = false) : INotifyPropertyChanged
{
    const int ClaudeAiChecks = 5;

    int claudeAiMisses;
    bool restartPending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Connector Connector { get; } = connector;
    public IReadOnlyList<FieldInput> Inputs { get; private set; } = NewInputs(connector);
    public McpServer? Server { get; private set; }
    public bool Known { get; private set; }
    public bool Idle { get; private set; } = true;
    public bool Editing { get; private set; }
    public bool Restarting { get; private set; }
    public bool AllowWrite { get; set; }
    public bool UsesClaudeAi { get; private set; } = usesClaudeAi;
    public string? Problem { get; private set; }
    public bool Installed => Server?.IsUsable == true;
    public bool CanInstall => Known && !Installed && !Restarting && !UsesClaudeAi && Connector.Installable;
    public bool CanToggle => Installed && Connector.Installable;
    public bool ShowForm => CanInstall || Editing;
    public bool CanEdit => Installed && Server?.Name == Connector.Name && !UsesClaudeAi;
    public bool HasToken => Installed && Server?.HasToken == true;
    public bool On => Server?.Status != "disabled";
    public bool ClaudeAiMissing => claudeAiMisses >= ClaudeAiChecks;

    public string State => !Known ? "Henter…"
        : Restarting && !Installed ? "Installeret – bliver aktiv, når Claude er genstartet"
        : UsesClaudeAi && Server is null && restartPending ? "Skifter til claude.ai, når Claude er genstartet"
        : UsesClaudeAi && Server is null ? "Ikke fundet på claude.ai"
        : !UsesClaudeAi && !Connector.Installable ? "Slået fra"
        : Server?.Status switch
        {
            null => "Ikke installeret",
            "connected" => "Forbundet",
            "pending" => "Forbinder…",
            "disabled" => "Slået fra",
            "needs-auth" => "Mangler login",
            "failed" => $"Fejl: {Server.Error}",
            var status => status,
        };

    public void Show(IReadOnlyList<McpServer> servers, bool restartPending = false)
    {
        (Server, Known, this.restartPending) = (Connector.FindIn(servers, UsesClaudeAi), true, restartPending);
        Restarting &= !Installed && Server?.Name != Connector.Name;
        claudeAiMisses = UsesClaudeAi && Server is null && !restartPending ? claudeAiMisses + 1 : 0;
        Changed();
    }

    public void UseClaudeAi(bool use)
    {
        (UsesClaudeAi, Editing, claudeAiMisses, Problem) = (use, false, 0, null);
        Changed();
    }

    public void Edit()
    {
        Editing = !Editing;
        Changed();
    }

    public void Start()
    {
        (Idle, Problem) = (false, null);
        Changed();
    }

    public void Finish(string? problem)
    {
        (Idle, Problem) = (true, problem);
        Changed();
    }

    public void AwaitRestart()
    {
        (Restarting, Editing, AllowWrite, Inputs) = (true, false, false, NewInputs(Connector));
        Changed();
    }

    static FieldInput[] NewInputs(Connector connector) => [.. connector.Fields.Select(field => new FieldInput(field))];

    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
