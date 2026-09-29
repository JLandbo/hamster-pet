using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hamster.Core.Claude;
using Hamster.Core.Connections;
using Hamster.Core.Languages;
using Hamster.Core.Storage;

namespace Hamster.Core.Feeds;

public sealed class Subscriptions(Connectors connectors, ClaudeFetcher fetcher, IWebSession web, JsonFile<SubscriptionSettings> store)
{
    readonly Dictionary<string, IssueRef?> _epicsOfTasks = [];
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public const string JiraSource = "Jira";
    public const string GitHubSource = "GitHub";
    static string NotLoggedIn => Strings.Of("Tasks.NotLoggedIn");
    static readonly IReadOnlyDictionary<string, string> _noColumns = new Dictionary<string, string>();
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static readonly IReadOnlyList<GitHubFeed> GitHubFeeds =
    [
        new("review", "is:open is:pr review-requested:@me archived:false", ["search_pull_requests"]),
        new("authored", "is:open is:pr author:@me archived:false", ["search_pull_requests"]),
        new("assigned", "is:open assignee:@me archived:false", ["search_issues", "search_pull_requests"]),
    ];

    public static Connector Jira { get; } = Connectors.All.Single(connector => connector.Name == "hamster-atlassian-rovo");
    public static Connector GitHub { get; } = Connectors.All.Single(connector => connector.Name == "hamster-github");

    public event Action? Changed;

    public SubscriptionSettings Settings
    {
        get;
        set
        {
            field = value;
            store.Save(value);
            Changed?.Invoke();
        }
    } = store.Load();

    public static bool IsFetchError(Exception exception)
    {
        return exception is InvalidOperationException or OperationCanceledException or IOException or UnauthorizedAccessException or HttpRequestException or JsonException
            or Win32Exception or COMException;
    }

    public IEnumerable<(string Source, string Group)> Slots
    {
        get
        {
            var settings = Settings;
            if (connectors.SourceOf(Jira) != ConnectorSource.Off)
            {
                foreach (var column in settings.Chosen.Where(board => FeedApi.BoardJql(board, settings.JiraColumns) is not null).SelectMany(board => board.Columns)
                    .Select(column => column.Name).Where(settings.JiraColumns.Contains).Distinct())
                {
                    yield return (JiraSource, column);
                }
            }
            if (connectors.SourceOf(GitHub) != ConnectorSource.Off)
            {
                foreach (var feed in GitHubFeeds.Where(feed => settings.GitHub.Contains(feed.Key)))
                {
                    yield return (GitHubSource, feed.Key);
                }
            }
        }
    }

    public IReadOnlyList<Choice> BoardChoicesFor(Connector connector) => connector == Jira
        ? [.. Settings.JiraBoards.Select(board => new Choice(board.Key, board.Name, Settings.ChosenBoards.Contains(board.Key), ChoiceKind.Board))]
        : [];

    public IReadOnlyList<Choice> ChoicesFor(Connector connector) => connector == Jira
            ? [.. Settings.Chosen.SelectMany(board => board.Columns).Select(column => column.Name).Distinct()
                .Select(name => new Choice(name, name, Settings.JiraColumns.Contains(name), ChoiceKind.Column))]
            : connector == GitHub
                ? [.. GitHubFeeds.Select(feed => new Choice(feed.Key, feed.Title, Settings.GitHub.Contains(feed.Key), ChoiceKind.GitHub))]
                : [];

    public async Task ChooseAsync(Choice choice, bool chosen)
    {
        var board = choice.Kind == ChoiceKind.Board ? await ConfiguredAsync(Settings.JiraBoards.Single(board => board.Key == choice.Key), chosen) : null;
        Settings = choice.Kind switch
        {
            ChoiceKind.Board => Settings.WithBoard(board!, chosen),
            ChoiceKind.Column => Settings.WithColumn(choice.Key, chosen),
            _ => Settings.WithGitHub(choice.Key, chosen),
        };
    }

    public string? LoginSiteOf(Connector connector) => Settings.LoginSites.GetValueOrDefault(connector.Name);

    public string? KnownSiteOf(Connector connector) => LoginSiteOf(connector) ?? (connector == Jira ? Settings.JiraBoards.FirstOrDefault()?.Site : null);

    public void RememberLogin(Connector connector, string site) => Settings = Settings with { LoginSites = new Dictionary<string, string>(Settings.LoginSites) { [connector.Name] = site } };

    public async Task<string?> CheckLoginAsync(Connector connector)
    {
        if (LoginSiteOf(connector) is not { } site)
        {
            return null;
        }
        if (await web.IsLoggedInAsync(site, connector.Login!))
        {
            return site;
        }
        Settings = Settings.WithoutLogin(connector.Name);
        return null;
    }

    public async Task LogoutAsync()
    {
        await web.LogoutAsync();
        Settings = Settings with { LoginSites = new Dictionary<string, string>() };
    }

    public async Task FetchJiraBoardsAsync()
    {
        var (sites, get) = await JiraRestAsync();
        List<JiraBoard> found = [];
        foreach (var site in sites)
        {
            for (var start = 0; ; start += 50)
            {
                var (boards, last) = FeedApi.BoardPage(await get($"{site.TrimEnd('/')}/rest/agile/1.0/board?startAt={start}&maxResults=50"), site);
                found.AddRange(boards);
                if (last)
                {
                    break;
                }
            }
        }
        if (found.Count == 0)
        {
            throw new InvalidOperationException(Strings.Of("Settings.NoBoards"));
        }
        var chosen = Settings.ChosenBoards;
        Dictionary<string, IReadOnlyDictionary<string, string>> jiraFields = new(Settings.JiraFields);
        foreach (var site in sites)
        {
            jiraFields[site] = FeedApi.JiraFieldsOf(await get($"{site.TrimEnd('/')}/rest/api/3/field"));
        }
        JiraBoard[] merged = [.. FeedApi.Merged(found, Settings.JiraBoards)];
        await Parallel.ForEachAsync(Enumerable.Range(0, merged.Length).Where(i => chosen.Contains(merged[i].Key)), async (i, _) =>
            merged[i] = FeedApi.Configured(merged[i], await get(FeedApi.ConfigurationUrl(merged[i]))));
        Settings = Settings with { JiraBoards = merged, JiraFields = jiraFields };
    }

    IReadOnlyDictionary<string, string> FieldsFor(string site) => Settings.JiraFields.GetValueOrDefault(site) ?? FeedApi.DefaultFields;

    public async Task<IReadOnlyList<FeedItem>> FetchJiraAsync()
    {
        var settings = Settings;
        var source = connectors.SourceOf(Jira);
        var searches = settings.Chosen.Select(board => (Board: board, Jql: FeedApi.BoardJql(board, settings.JiraColumns))).Where(search => search.Jql is not null).ToArray();
        if (searches.Length == 0 || source == ConnectorSource.Off)
        {
            return [];
        }
        var columns = FeedApi.ColumnsByStatus(settings.Chosen);
        if (source == ConnectorSource.Login && await CheckLoginAsync(Jira) is null)
        {
            throw new InvalidOperationException(NotLoggedIn);
        }
        var results = await SearchJiraAsync(source, [.. searches.Select(search => (search.Board.Site, search.Jql!)).Distinct()]);
        FeedItem[] items = [.. results.SelectMany(result => FeedApi.JiraItems(result.Text, result.Site, columns, FieldsFor(result.Site))).DistinctBy(item => item.Url)];
        await LookUpEpicsAsync(source, [.. FeedApi.TasksWithUnknownEpic(items).Where(task => !_epicsOfTasks.ContainsKey(task.Url))]);
        return FeedApi.WithEpics(items, _epicsOfTasks);
    }

    async Task LookUpEpicsAsync(ConnectorSource source, IssueRef[] tasks)
    {
        if (tasks.Length == 0)
        {
            return;
        }
        try
        {
            var found = (await SearchJiraAsync(source, [.. FeedApi.LookupSearches(tasks)])).SelectMany(result => FeedApi.JiraItems(result.Text, result.Site, _noColumns)).ToArray();
            foreach (var task in tasks)
            {
                _epicsOfTasks[task.Url] = found.FirstOrDefault(item => item.Url == task.Url)?.Parent is { Kind: IssueKind.Epic } epic ? epic : null;
            }
        }
        catch (Exception exception) when (IsFetchError(exception))
        {
        }
    }

    async Task<IReadOnlyList<(string Site, string Text)>> SearchJiraAsync(ConnectorSource source, IReadOnlyList<(string Site, string Jql)> searches)
    {
        if (source != ConnectorSource.Login)
        {
            return [.. (await CallAllPagesAsync(Jira, [.. searches.Select(search => FeedApi.JiraSearch(search.Site, search.Jql, FieldsFor(search.Site).Values))], FeedApi.NextJiraPage))
                .Select(result => ((string)result.Call.Arguments["cloudId"]!, result.Text))];
        }
        Dictionary<string, Func<string, Task<string>>> readers = [];
        foreach (var site in searches.Select(search => search.Site).Distinct())
        {
            readers[site] = await web.ReaderAsync(site);
        }
        var pages = new IReadOnlyList<string>[searches.Count];
        var customFields = searches.Select(search => FieldsFor(search.Site).Values).ToArray();
        await Parallel.ForEachAsync(Enumerable.Range(0, searches.Count), async (i, _) => pages[i] = await FeedApi.PagesAsync(readers[searches[i].Site], searches[i].Site, searches[i].Jql, customFields[i]));
        return [.. searches.Zip(pages).SelectMany(found => found.Second.Select(page => (found.First.Site, page)))];
    }

    async Task<IReadOnlyList<(ToolCall Call, string Text)>> CallAllPagesAsync(Connector connector, IReadOnlyList<ToolCall> calls, Func<ToolCall, string, ToolCall?> next)
    {
        List<(ToolCall Call, string Text)> all = [];
        for (var pending = calls; pending.Count > 0;)
        {
            var found = await CallAsync(connector, pending);
            all.AddRange(found);
            pending = [.. found.Select(result => next(result.Call, result.Text)).OfType<ToolCall>()];
        }
        return all;
    }

    public async Task<IReadOnlyList<FeedItem>> FetchGitHubAsync()
    {
        GitHubFeed[] feeds = [.. GitHubFeeds.Where(feed => Settings.GitHub.Contains(feed.Key))];
        return feeds.Length > 0 && connectors.SourceOf(GitHub) != ConnectorSource.Off
            ? [.. (await CallAllPagesAsync(GitHub, [.. feeds.SelectMany(feed => feed.Tools.Select(tool => FeedApi.GitHubSearch(tool, feed.Query)))], FeedApi.NextGitHubPage))
                .SelectMany(found => FeedApi.GitHubItems(found.Text, feeds.First(feed => feed.Query == (string?)found.Call.Arguments["query"]).Key)).DistinctBy(item => item.Url)]
            : [];
    }

    static async Task<McpEndpoint?> OwnEndpointAsync(Connector connector) => File.Exists(McpEndpoint.ClaudeConfigFile)
        ? McpEndpoint.FromClaudeConfig(await File.ReadAllTextAsync(McpEndpoint.ClaudeConfigFile), connector.Name)
        : null;

    async Task<JiraBoard> ConfiguredAsync(JiraBoard board, bool chosen)
    {
        return chosen && board.FilterId is null ? FeedApi.Configured(board, await (await JiraGetAsync())(FeedApi.ConfigurationUrl(board))) : board;
    }

    async Task<Func<string, Task<string>>> JiraGetAsync()
    {
        if (connectors.SourceOf(Jira) == ConnectorSource.Login)
        {
            return await web.ReaderAsync(LoginSiteOf(Jira) ?? throw new InvalidOperationException(NotLoggedIn));
        }
        var authorization = (await OwnEndpointAsync(Jira))?.Headers.GetValueOrDefault("Authorization")
            ?? throw new InvalidOperationException(Strings.Of("Settings.NeedsTokenOrLogin"));
        return url => GetWithTokenAsync(url, authorization);
    }

    async Task<(IReadOnlyList<string> Sites, Func<string, Task<string>> Get)> JiraRestAsync()
    {
        if (connectors.SourceOf(Jira) == ConnectorSource.Login)
        {
            return await CheckLoginAsync(Jira) is { } site ? ([site], await web.ReaderAsync(site)) : throw new InvalidOperationException(NotLoggedIn);
        }
        var get = await JiraGetAsync();
        string[] sites = [.. FeedApi.JiraSites((await CallAsync(Jira, [new("getAccessibleAtlassianResources", new JsonObject())])).Single().Text).Where(FeedApi.IsJiraSite)];
        return sites.Length > 0 ? (sites, get) : throw new InvalidOperationException(Strings.Of("Settings.NoJiraSites"));
    }

    static async Task<string> GetWithTokenAsync(string url, string authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Task.Run(() => _http.SendAsync(request));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync()
            : throw new InvalidOperationException(Strings.Format("Settings.JiraRejectedToken", (int)response.StatusCode, response.ReasonPhrase));
    }

    async Task<IReadOnlyList<(ToolCall Call, string Text)>> CallAsync(Connector connector, IReadOnlyList<ToolCall> calls) => connectors.SourceOf(connector) switch
    {
        ConnectorSource.ClaudeAi => await fetcher.CallAsync(connector, connector.ClaudeAiName ?? connector.FindIn(await connectors.ServersAsync(), claudeAi: true)?.Name
            ?? throw new InvalidOperationException($"{connector.Title}: {Connector.MissingOnClaudeAi}"), calls),
        ConnectorSource.Hamster => await OwnEndpointAsync(connector) is { } endpoint
            ? [.. calls.Zip(await McpClient.CallAsync(endpoint, calls))]
            : throw new InvalidOperationException(Strings.Format("Tasks.NotInstalled", connector.Title)),
        _ => throw new InvalidOperationException(Strings.Format("Tasks.TurnedOff", connector.Title)),
    };
}
