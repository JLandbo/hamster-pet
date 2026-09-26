using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Hamster;

public enum IssueKind { Other, Epic, Task, SubTask }

public sealed record IssueRef(string Key, string Title, IssueKind Kind, string Url);

public sealed record IssueTag(string Key, string Value);

public sealed record IssueDetails(string? Sprint, IReadOnlyList<IssueTag> Tags, string Description);

public sealed record FeedItem(string Source, string Group, string Id, string Title, string Url, string Repository = "", DateTimeOffset? Updated = null, bool Draft = false,
    IssueKind Kind = IssueKind.Other, IssueRef? Parent = null, IssueRef? Epic = null, IssueDetails? Details = null)
{
    public static readonly IComparer<string> KeyOrder = Comparer<string>.Create(CompareKeys);

    public IssueRef Ref => new(Id, Title, Kind, Url);

    public string DetailAt(DateTimeOffset now) =>
        string.Join(" · ", new[] { Repository, Draft ? "kladde" : "", Updated is { } updated ? Ago(now - updated) : "" }.Where(part => part.Length > 0));

    public static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "nu",
        { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min",
        { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} t",
        { TotalDays: < 2 } => "i går",
        _ => $"{(int)elapsed.TotalDays} dage",
    };

    static int CompareKeys(string? first, string? second)
    {
        var (a, b) = (Split(first ?? ""), Split(second ?? ""));
        var byPrefix = string.Compare(a.Prefix, b.Prefix, StringComparison.OrdinalIgnoreCase);
        return byPrefix != 0 ? byPrefix : a.Number.CompareTo(b.Number);
    }

    static (string Prefix, long Number) Split(string key)
    {
        var digits = key.Length - key.Reverse().TakeWhile(char.IsAsciiDigit).Count();
        return (key[..digits], long.TryParse(key[digits..], out var number) ? number : -1);
    }
}

public sealed record FeedRow(string Id, string Title, string Url, string Detail, IssueKind Kind = IssueKind.Other, int Depth = 0, bool Muted = false, IssueDetails? Details = null);

public sealed record FeedBox(FeedRow? Epic, IReadOnlyList<FeedRow> Rows);

public sealed record FeedGroup(string Title, IReadOnlyList<FeedBox> Boxes);

public sealed record FeedSource(string Title, string? Error, IReadOnlyList<FeedGroup> Groups);

public sealed record JiraColumn(string Name, IReadOnlyList<string> StatusIds);

public sealed record JiraBoard(int Id, string Name, string Site)
{
    public string? FilterId { get; init; }
    public IReadOnlyList<JiraColumn> Columns { get; init; } = [];

    [JsonIgnore]
    public string Key => $"{Site}#{Id}";
}

public sealed record GitHubFeed(string Key, string Title, string Query, IReadOnlyList<string> Tools);

public enum ChoiceKind { Column, Board, GitHub }

public sealed record Choice(string Key, string Title, bool Chosen, ChoiceKind Kind);

public sealed record SubscriptionSettings
{
    public static SubscriptionSettings Empty { get; } = new();

    public IReadOnlyList<JiraBoard> JiraBoards { get; init; } = [];
    public IReadOnlyList<string> ChosenBoards { get; init; } = [];
    public IReadOnlyList<string> JiraColumns { get; init; } = [];
    public IReadOnlyList<string> GitHub { get; init; } = [];
    public IReadOnlyDictionary<string, string> LoginSites { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> JiraFields { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public IEnumerable<JiraBoard> Chosen => JiraBoards.Where(board => ChosenBoards.Contains(board.Key));

    public SubscriptionSettings WithoutLogin(string connector) => this with { LoginSites = LoginSites.Where(login => login.Key != connector).ToDictionary() };

    public SubscriptionSettings WithBoard(JiraBoard board, bool chosen) => this with
    {
        JiraBoards = [.. JiraBoards.Select(other => other.Key == board.Key ? board : other)],
        ChosenBoards = Toggled(ChosenBoards, board.Key, chosen),
    };

    public SubscriptionSettings WithColumn(string name, bool chosen) => this with { JiraColumns = Toggled(JiraColumns, name, chosen) };

    public SubscriptionSettings WithGitHub(string key, bool chosen) => this with { GitHub = Toggled(GitHub, key, chosen) };

    static IReadOnlyList<string> Toggled(IReadOnlyList<string> keys, string key, bool chosen) => [.. keys.Where(other => other != key), .. chosen ? [key] : Array.Empty<string>()];
}

public sealed class Subscriptions(Connectors connectors, ClaudeFetcher fetcher, WebSession web, JsonFile<SubscriptionSettings> store)
{
    readonly Dictionary<string, IssueRef?> epicsOfTasks = [];
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public const string JiraSource = "Jira";
    public const string GitHubSource = "GitHub";
    const string NotLoggedIn = "Ikke logget ind. Log ind under Indstillinger.";
    const int PageSize = 100;
    const int DescriptionLength = 1500;
    static readonly MarkdownPipeline SourcePositions = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
    const string SprintName = "Sprint";
    static readonly string[] CustomFieldNames = ["Story Points", "Reviewer", "Tester", "Kundenavn"];
    static readonly IReadOnlyDictionary<string, string> DefaultFields = new Dictionary<string, string> { [SprintName] = "customfield_10020" };
    const int GitHubSearchLimit = 1000;
    static readonly IReadOnlyDictionary<string, string> NoColumns = new Dictionary<string, string>();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static readonly IReadOnlyList<GitHubFeed> GitHubFeeds =
    [
        new("review", "Review anmodet af mig", "is:open is:pr review-requested:@me archived:false", ["search_pull_requests"]),
        new("authored", "Mine åbne PR'er", "is:open is:pr author:@me archived:false", ["search_pull_requests"]),
        new("assigned", "Tildelt mig", "is:open assignee:@me archived:false", ["search_issues", "search_pull_requests"]),
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

    public static bool IsFetchError(Exception exception) =>
        exception is InvalidOperationException or OperationCanceledException or IOException or UnauthorizedAccessException or HttpRequestException or JsonException or Win32Exception or COMException;

    public IEnumerable<(string Source, string Group)> Slots
    {
        get
        {
            var settings = Settings;
            if (connectors.SourceOf(Jira) != ConnectorSource.Off)
                foreach (var column in settings.Chosen.Where(board => BoardJql(board, settings.JiraColumns) is not null).SelectMany(board => board.Columns)
                    .Select(column => column.Name).Where(settings.JiraColumns.Contains).Distinct())
                    yield return (JiraSource, column);
            if (connectors.SourceOf(GitHub) != ConnectorSource.Off)
                foreach (var feed in GitHubFeeds.Where(feed => settings.GitHub.Contains(feed.Key)))
                    yield return (GitHubSource, feed.Title);
        }
    }

    public IReadOnlyList<Choice> BoardChoicesFor(Connector connector) =>
        connector == Jira ? [.. Settings.JiraBoards.Select(board => new Choice(board.Key, board.Name, Settings.ChosenBoards.Contains(board.Key), ChoiceKind.Board))] : [];

    public IReadOnlyList<Choice> ChoicesFor(Connector connector) =>
        connector == Jira
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

    public string? KnownSiteOf(Connector connector) =>
        LoginSiteOf(connector) ?? (connector == Jira ? Settings.JiraBoards.FirstOrDefault()?.Site : null);

    public void Login(Window owner, Connector connector, string start)
    {
        if (web.Login(owner, connector, start) is { } site)
            Settings = Settings with { LoginSites = new Dictionary<string, string>(Settings.LoginSites) { [connector.Name] = site } };
    }

    public async Task<string?> CheckLoginAsync(Connector connector)
    {
        if (LoginSiteOf(connector) is not { } site)
            return null;
        if (await web.IsLoggedInAsync(site, connector.Login!))
            return site;
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
            for (var start = 0; ; start += 50)
            {
                var (boards, last) = BoardPage(await get($"{site.TrimEnd('/')}/rest/agile/1.0/board?startAt={start}&maxResults=50"), site);
                found.AddRange(boards);
                if (last)
                    break;
            }
        if (found.Count == 0)
            throw new InvalidOperationException("Jira gav ingen boards. Log ind igen, eller tjek dit token.");
        var chosen = Settings.ChosenBoards;
        Dictionary<string, IReadOnlyDictionary<string, string>> jiraFields = new(Settings.JiraFields);
        foreach (var site in sites)
            jiraFields[site] = JiraFieldsOf(await get($"{site.TrimEnd('/')}/rest/api/3/field"));
        JiraBoard[] merged = [.. Merged(found, Settings.JiraBoards)];
        await Parallel.ForEachAsync(Enumerable.Range(0, merged.Length).Where(i => chosen.Contains(merged[i].Key)), async (i, _) =>
            merged[i] = Configured(merged[i], await get(ConfigurationUrl(merged[i]))));
        Settings = Settings with { JiraBoards = merged, JiraFields = jiraFields };
    }

    public static IReadOnlyDictionary<string, string> JiraFieldsOf(string json)
    {
        JsonObject[] fields = [.. (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>()];
        Dictionary<string, string> found = [];
        if (fields.FirstOrDefault(field => (string?)field["schema"]?["custom"] == "com.pyxis.greenhopper.jira:gh-sprint")?["id"]?.ToString() is { } sprint)
            found[SprintName] = sprint;
        foreach (var name in CustomFieldNames)
            if (fields.FirstOrDefault(field => (string?)field["name"] == name)?["id"]?.ToString() is { } id)
                found[name] = id;
        return found;
    }

    IReadOnlyDictionary<string, string> FieldsFor(string site) => Settings.JiraFields.GetValueOrDefault(site) ?? DefaultFields;

    public static IEnumerable<JiraBoard> Merged(IEnumerable<JiraBoard> found, IReadOnlyList<JiraBoard> known) =>
        found.Select(board => known.FirstOrDefault(other => other.Key == board.Key) is { } stored ? stored with { Name = board.Name } : board)
            .OrderBy(board => board.Name, StringComparer.CurrentCultureIgnoreCase);

    static string ConfigurationUrl(JiraBoard board) => $"{board.Site.TrimEnd('/')}/rest/agile/1.0/board/{board.Id}/configuration";

    public async Task<IReadOnlyList<FeedItem>> FetchJiraAsync()
    {
        var settings = Settings;
        var source = connectors.SourceOf(Jira);
        var searches = settings.Chosen.Select(board => (Board: board, Jql: BoardJql(board, settings.JiraColumns))).Where(search => search.Jql is not null).ToArray();
        if (searches.Length == 0 || source == ConnectorSource.Off)
            return [];
        var columns = ColumnsByStatus(settings.Chosen);
        if (source == ConnectorSource.Login && await CheckLoginAsync(Jira) is null)
            throw new InvalidOperationException(NotLoggedIn);
        var results = await SearchJiraAsync(source, [.. searches.Select(search => (search.Board.Site, search.Jql!)).Distinct()]);
        FeedItem[] items = [.. results.SelectMany(result => JiraItems(result.Text, result.Site, columns, FieldsFor(result.Site))).DistinctBy(item => item.Url)];
        await LookUpEpicsAsync(source, [.. TasksWithUnknownEpic(items).Where(task => !epicsOfTasks.ContainsKey(task.Url))]);
        return WithEpics(items, epicsOfTasks);
    }

    async Task LookUpEpicsAsync(ConnectorSource source, IssueRef[] tasks)
    {
        if (tasks.Length == 0)
            return;
        try
        {
            var found = (await SearchJiraAsync(source, [.. LookupSearches(tasks)])).SelectMany(result => JiraItems(result.Text, result.Site, NoColumns)).ToArray();
            foreach (var task in tasks)
                epicsOfTasks[task.Url] = found.FirstOrDefault(item => item.Url == task.Url)?.Parent is { Kind: IssueKind.Epic } epic ? epic : null;
        }
        catch (Exception exception) when (IsFetchError(exception))
        {
        }
    }

    public static IEnumerable<(string Site, string Jql)> LookupSearches(IEnumerable<IssueRef> tasks) =>
        tasks.GroupBy(task => new Uri(task.Url).GetLeftPart(UriPartial.Authority))
            .SelectMany(site => site.Chunk(PageSize).Select(chunk => (site.Key, $"key in ({string.Join(", ", chunk.Select(task => task.Key))})")));

    async Task<IReadOnlyList<(string Site, string Text)>> SearchJiraAsync(ConnectorSource source, IReadOnlyList<(string Site, string Jql)> searches)
    {
        if (source != ConnectorSource.Login)
            return [.. (await CallAllPagesAsync(Jira, [.. searches.Select(search => JiraSearch(search.Site, search.Jql, FieldsFor(search.Site).Values))], NextJiraPage))
                .Select(result => ((string)result.Call.Arguments["cloudId"]!, result.Text))];
        Dictionary<string, Func<string, Task<string>>> readers = [];
        foreach (var site in searches.Select(search => search.Site).Distinct())
            readers[site] = await web.ReaderAsync(site);
        var pages = new IReadOnlyList<string>[searches.Count];
        var customFields = searches.Select(search => FieldsFor(search.Site).Values).ToArray();
        await Parallel.ForEachAsync(Enumerable.Range(0, searches.Count), async (i, _) => pages[i] = await PagesAsync(readers[searches[i].Site], searches[i].Site, searches[i].Jql, customFields[i]));
        return [.. searches.Zip(pages).SelectMany(found => found.Second.Select(page => (found.First.Site, page)))];
    }

    public static async Task<IReadOnlyList<string>> PagesAsync(Func<string, Task<string>> get, string site, string jql, IEnumerable<string> customFields)
    {
        List<string> pages = [];
        string? token = null;
        do
        {
            var page = await get(SearchUrl(site, jql, customFields, token));
            pages.Add(page);
            token = NextPageToken(page);
        }
        while (token is not null);
        return pages;
    }

    public static string? NextPageToken(string json) =>
        JsonNode.Parse(json) is JsonObject page
            ? (string?)page["nextPageToken"] is { Length: > 0 } token
                ? token
                : page["issues"] is JsonObject issues && issues["pageInfo"] is JsonObject info && (bool?)info["hasNextPage"] == true ? (string?)info["endCursor"] : null
            : null;

    public static ToolCall? NextJiraPage(ToolCall call, string text) =>
        NextPageToken(text) is { } token ? call with { Arguments = With(call.Arguments, "nextPageToken", token) } : null;

    public static ToolCall? NextGitHubPage(ToolCall call, string text)
    {
        var page = JsonNode.Parse(text);
        var number = (int?)call.Arguments["page"] ?? 1;
        return (page?["items"] as JsonArray)?.Count == PageSize && number * PageSize < Math.Min((int?)page?["total_count"] ?? 0, GitHubSearchLimit)
            ? call with { Arguments = With(call.Arguments, "page", number + 1) }
            : null;
    }

    static JsonObject With(JsonObject arguments, string name, JsonNode value)
    {
        var copy = arguments.DeepClone().AsObject();
        copy[name] = value;
        return copy;
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

    public static IEnumerable<IssueRef> TasksWithUnknownEpic(IReadOnlyList<FeedItem> items) =>
        items.Where(item => item.Kind == IssueKind.SubTask).Select(item => item.Parent).OfType<IssueRef>()
            .Where(task => !items.Any(item => item.Url == task.Url)).DistinctBy(task => task.Url);

    public static IReadOnlyList<FeedItem> WithEpics(IReadOnlyList<FeedItem> items, IReadOnlyDictionary<string, IssueRef?> epicsOfTasks) =>
        [.. items.Select(item => item with { Epic = EpicOf(item, items, epicsOfTasks) })];

    static IssueRef? EpicOf(FeedItem item, IReadOnlyList<FeedItem> items, IReadOnlyDictionary<string, IssueRef?> epicsOfTasks) =>
        item.Kind == IssueKind.SubTask && item.Parent is { } task
            ? (items.FirstOrDefault(other => other.Url == task.Url) is { } listed ? listed.Parent : epicsOfTasks.GetValueOrDefault(task.Url)) is { Kind: IssueKind.Epic } epic ? epic : null
            : item.Parent is { Kind: IssueKind.Epic } parent ? parent : null;

    public async Task<IReadOnlyList<FeedItem>> FetchGitHubAsync()
    {
        GitHubFeed[] feeds = [.. GitHubFeeds.Where(feed => Settings.GitHub.Contains(feed.Key))];
        return feeds.Length > 0 && connectors.SourceOf(GitHub) != ConnectorSource.Off
            ? [.. (await CallAllPagesAsync(GitHub, [.. feeds.SelectMany(feed => feed.Tools.Select(tool => GitHubSearch(tool, feed.Query)))], NextGitHubPage))
                .SelectMany(found => GitHubItems(found.Text, feeds.First(feed => feed.Query == (string?)found.Call.Arguments["query"]).Title)).DistinctBy(item => item.Url)]
            : [];
    }

    public static string SearchUrl(string site, string jql, IEnumerable<string> customFields, string? pageToken = null) =>
        $"{site.TrimEnd('/')}/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields={string.Join(',', IssueFields.Concat(customFields))}&maxResults={PageSize}"
        + (pageToken is null ? "" : $"&nextPageToken={Uri.EscapeDataString(pageToken)}");

    public static string? BoardJql(JiraBoard board, IReadOnlyList<string> columns) =>
        board.FilterId is { } filter && board.Columns.Where(column => columns.Contains(column.Name)).SelectMany(column => column.StatusIds).Distinct().ToArray() is { Length: > 0 } statuses
            ? $"filter = {filter} AND assignee = currentUser() AND status in ({string.Join(", ", statuses)}) ORDER BY updated DESC"
            : null;

    public static (IReadOnlyList<JiraBoard> Boards, bool Last) BoardPage(string json, string site)
    {
        var page = JsonNode.Parse(json);
        IReadOnlyList<JiraBoard> boards = [.. (page?["values"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(board => (Id: (int?)board["id"], Name: (string?)board["name"], Project: (string?)board["location"]?["projectKey"]))
            .Where(board => board.Id is not null && board.Name is not null)
            .Select(board => new JiraBoard(board.Id!.Value, board.Project is null ? board.Name! : $"{board.Name} ({board.Project})", site))];
        return (boards, (bool?)page?["isLast"] != false || boards.Count == 0);
    }

    public static JiraBoard Configured(JiraBoard board, string json)
    {
        var configuration = JsonNode.Parse(json);
        return board with
        {
            FilterId = configuration?["filter"]?["id"]?.ToString(),
            Columns = [.. (configuration?["columnConfig"]?["columns"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(column => new JiraColumn((string?)column["name"] ?? "", [.. (column["statuses"] as JsonArray ?? []).Select(status => status?["id"]?.ToString()).OfType<string>()]))
                .Where(column => column.Name.Length > 0)],
        };
    }

    public static bool IsJiraSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var site) && site.Scheme == Uri.UriSchemeHttps && site.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> JiraSites(string json) =>
        [.. (JsonNode.Parse(json) switch
            {
                JsonArray sites => sites,
                JsonObject result => result["data"]?["resources"] as JsonArray,
                _ => null,
            } ?? []).OfType<JsonObject>()
            .Select(site => (string?)site["url"]).OfType<string>()];

    public static IEnumerable<FeedItem> JiraItems(string json, string site, IReadOnlyDictionary<string, string> columns, IReadOnlyDictionary<string, string>? customFields = null) =>
        JiraIssues(json)
            .Select(issue => (Key: (string?)issue["key"], Fields: issue["fields"], Site: site))
            .Where(issue => issue.Key is not null)
            .Select(issue => new FeedItem(
                JiraSource,
                columns.GetValueOrDefault(issue.Fields?["status"]?["id"]?.ToString() ?? "") ?? (string?)issue.Fields?["status"]?["name"] ?? "",
                issue.Key!,
                (string?)issue.Fields?["summary"] ?? "",
                $"{issue.Site.TrimEnd('/')}/browse/{issue.Key}",
                Updated: TimeOf(issue.Fields?["updated"]),
                Kind: KindOf(issue.Fields?["issuetype"]),
                Parent: ParentOf(issue.Fields?["parent"], issue.Site),
                Details: DetailsOf(issue.Fields, customFields ?? DefaultFields)));

    static IssueDetails? DetailsOf(JsonNode? fields, IReadOnlyDictionary<string, string> customFields)
    {
        IssueTag?[] tags =
        [
            Tag("Status", TextOf(fields?["status"])),
            Tag("Prioritet", TextOf(fields?["priority"])),
            .. CustomFieldNames.Select(name => Tag(name, customFields.GetValueOrDefault(name) is { } id ? TextOf(fields?[id]) : null)),
            Tag("Time tracking", TimeTrackingOf(fields?["timetracking"])),
            Tag("Due date", DateOf(fields?["duedate"])),
            Tag("Labels", TextOf(fields?["labels"])),
            Tag("Komponent", TextOf(fields?["components"])),
            Tag("Oprettet", DateOf(fields?["created"])),
        ];
        var details = new IssueDetails(SprintOf(fields), [.. tags.OfType<IssueTag>()], DescriptionOf(fields?["description"]));
        return details is { Sprint: null, Tags.Count: 0, Description: "" } ? null : details;
    }

    static IssueTag? Tag(string key, string? value) => string.IsNullOrWhiteSpace(value) ? null : new(key, value);

    static string? TextOf(JsonNode? value) => value switch
    {
        JsonArray values => values.Select(TextOf).OfType<string>().ToArray() is { Length: > 0 } texts ? string.Join(", ", texts) : null,
        JsonObject item => (string?)item["displayName"] ?? (string?)item["value"] ?? (string?)item["name"],
        JsonValue number when number.TryGetValue(out double amount) => amount.ToString(CultureInfo.CurrentCulture),
        JsonValue text when text.TryGetValue(out string? words) => words,
        _ => null,
    };

    static string? DateOf(JsonNode? date) => TimeOf(date)?.ToLocalTime().ToString("d. MMM", CultureInfo.CurrentCulture);

    static string? TimeTrackingOf(JsonNode? tracking) =>
        string.Join(" · ", new[] { (Field: "originalEstimate", Label: "estimeret"), (Field: "remainingEstimate", Label: "tilbage"), (Field: "timeSpent", Label: "brugt") }
            .Select(part => (string?)tracking?[part.Field] is { } time ? $"{time} {part.Label}" : null).OfType<string>()) is { Length: > 0 } text ? text : null;

    static string? SprintOf(JsonNode? fields)
    {
        var sprints = (fields as JsonObject)?.Select(field => field.Value).OfType<JsonArray>()
            .FirstOrDefault(values => values.OfType<JsonObject>().Any(value => value["boardId"] is not null && value["state"] is not null))?.OfType<JsonObject>().ToArray() ?? [];
        if ((sprints.FirstOrDefault(sprint => (string?)sprint["state"] == "active") ?? sprints.LastOrDefault()) is not { } sprint)
            return null;
        return (string?)sprint["state"] == "active" && TimeOf(sprint["endDate"]) is { } end
            ? $"{(string?)sprint["name"]} · slutter {end.ToLocalTime().ToString("d. MMM", CultureInfo.CurrentCulture)}"
            : (string?)sprint["name"];
    }

    static string DescriptionOf(JsonNode? description)
    {
        var markdown = WithoutImages(description switch
        {
            JsonValue value when value.TryGetValue(out string? text) => text,
            JsonObject document => MarkdownOf(document),
            _ => "",
        }).Trim();
        if (markdown.Length <= DescriptionLength)
            return markdown;
        var cut = markdown[..DescriptionLength];
        return $"{(cut.LastIndexOfAny([' ', '\n', '\r', '\t']) is > 0 and var space ? cut[..space] : cut).TrimEnd()} …";
    }

    static string WithoutImages(string markdown) =>
        Markdown.Parse(markdown, SourcePositions).Descendants<LinkInline>().Where(link => link.IsImage && !InsideImage(link)).Reverse()
            .Aggregate(markdown, (text, image) => text.Remove(image.Span.Start, image.Span.Length));

    static bool InsideImage(Inline inline) => inline.Parent is { } parent && (parent is LinkInline { IsImage: true } || InsideImage(parent));

    static string MarkdownOf(JsonNode? node) => node is not JsonObject item ? "" : (string?)item["type"] switch
    {
        "text" => MarkedOf(item),
        "hardBreak" => "  \n",
        "paragraph" => $"{ChildrenOf(item)}\n\n",
        "heading" => $"{new string('#', Math.Clamp((int?)item["attrs"]?["level"] ?? 1, 1, 6))} {ChildrenOf(item)}\n\n",
        "bulletList" or "taskList" or "decisionList" => $"{string.Concat(ItemsOf(item).Select(text => $"{Nested("- ", text)}\n"))}\n",
        "orderedList" => $"{string.Concat(ItemsOf(item).Select((text, index) => $"{Nested($"{StartOf(item) + index}. ", text)}\n"))}\n",
        "codeBlock" => $"```\n{ChildrenOf(item)}\n```\n\n",
        "blockquote" => $"> {ChildrenOf(item).Trim().Replace("\n", "\n> ")}\n\n",
        "rule" => "---\n\n",
        "inlineCard" => (string?)item["attrs"]?["url"] ?? "",
        "blockCard" or "embedCard" => $"{(string?)item["attrs"]?["url"]}\n\n",
        "date" => DayOf(item["attrs"]?["timestamp"]),
        "mention" or "emoji" or "status" => (string?)item["attrs"]?["text"] ?? (string?)item["attrs"]?["shortName"] ?? "",
        _ => ChildrenOf(item),
    };

    static int StartOf(JsonObject list) => int.TryParse(list["attrs"]?["order"]?.ToString(), out var order) ? order : 1;

    static string DayOf(JsonNode? timestamp) =>
        long.TryParse(timestamp?.ToString(), out var stamp) && stamp >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && stamp <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(stamp).ToString("d. MMM", CultureInfo.CurrentCulture)
            : "";

    static string Nested(string marker, string text) => $"{marker}{text.Replace("\n", $"\n{new string(' ', marker.Length)}")}";

    static string MarkedOf(JsonObject item)
    {
        var text = (string?)item["text"] ?? "";
        var (start, end) = (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
        if (start >= end)
            return text;
        var marked = (item["marks"] as JsonArray ?? []).OfType<JsonObject>().OrderBy(mark => (string?)mark["type"] == "link").Aggregate(text[start..end], (inner, mark) => (string?)mark["type"] switch
        {
            "strong" => $"**{inner}**",
            "em" => $"*{inner}*",
            "code" => $"`{inner}`",
            "link" => $"[{inner}]({(string?)mark["attrs"]?["href"]})",
            _ => inner,
        });
        return $"{text[..start]}{marked}{text[end..]}";
    }

    static string ChildrenOf(JsonObject item) => string.Concat((item["content"] as JsonArray ?? []).Select(MarkdownOf));

    static IEnumerable<string> ItemsOf(JsonObject list)
    {
        var items = new List<string>();
        foreach (var entry in (list["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var nested = (string?)entry["type"] == "taskList";
            if (nested && items.Count > 0)
                items[^1] += $"\n{MarkdownOf(entry).Trim()}";
            else
                items.Add((nested ? MarkdownOf(entry) : ChildrenOf(entry)).Trim());
        }
        return items;
    }

    static IssueKind KindOf(JsonNode? type) =>
        (int?)type?["hierarchyLevel"] == 1 ? IssueKind.Epic
        : (bool?)type?["subtask"] == true ? IssueKind.SubTask
        : (string?)type?["name"] == "Task" ? IssueKind.Task
        : IssueKind.Other;

    static IssueRef? ParentOf(JsonNode? parent, string site) =>
        (string?)parent?["key"] is { } key
            ? new IssueRef(key, (string?)parent["fields"]?["summary"] ?? "", KindOf(parent["fields"]?["issuetype"]), $"{site.TrimEnd('/')}/browse/{key}")
            : null;

    public static IEnumerable<FeedItem> GitHubItems(string json, string group) =>
        (JsonNode.Parse(json)?["items"]?.AsArray().OfType<JsonObject>() ?? [])
            .Select(item => (Url: (string?)item["html_url"], Title: (string?)item["title"], Number: (int?)item["number"], Repository: ((string?)item["repository_url"])?.Split('/')[^1], Updated: TimeOf(item["updated_at"]), Draft: (bool?)item["draft"] == true))
            .Where(item => item.Url is not null)
            .Select(item => new FeedItem(GitHubSource, group, $"#{item.Number}", item.Title ?? "", item.Url!, item.Repository ?? "", item.Updated, item.Draft));

    static DateTimeOffset? TimeOf(JsonNode? time) =>
        DateTimeOffset.TryParse((string?)time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    static IReadOnlyDictionary<string, string> ColumnsByStatus(IEnumerable<JiraBoard> boards) =>
        boards.SelectMany(board => board.Columns).SelectMany(column => column.StatusIds.Select(status => (Status: status, Column: column.Name)))
            .DistinctBy(found => found.Status).ToDictionary(found => found.Status, found => found.Column);

    static IEnumerable<JsonObject> JiraIssues(string json) =>
        (JsonNode.Parse(json)?["issues"] switch
        {
            JsonArray issues => issues,
            JsonObject page => page["nodes"] as JsonArray,
            _ => null,
        } ?? []).OfType<JsonObject>();

    static readonly string[] IssueFields = ["summary", "status", "updated", "issuetype", "parent", "priority", "labels", "components", "created", "description", "duedate", "timetracking"];

    public static ToolCall JiraSearch(string site, string jql, IEnumerable<string> customFields) => new("searchJiraIssuesUsingJql", new JsonObject
    {
        ["cloudId"] = site,
        ["jql"] = jql,
        ["maxResults"] = PageSize,
        ["fields"] = new JsonArray([.. IssueFields.Concat(customFields).Select(field => (JsonNode)field)]),
        ["responseContentFormat"] = "markdown",
    });

    static ToolCall GitHubSearch(string tool, string query) => new(tool, new JsonObject
    {
        ["query"] = query,
        ["page"] = 1,
        ["perPage"] = PageSize,
        ["fields"] = new JsonArray("number", "title", "html_url", "repository_url", "updated_at", "draft"),
    });

    static async Task<McpEndpoint?> OwnEndpointAsync(Connector connector) =>
        File.Exists(McpEndpoint.ClaudeConfigFile) ? McpEndpoint.FromClaudeConfig(await File.ReadAllTextAsync(McpEndpoint.ClaudeConfigFile), connector.Name) : null;

    async Task<JiraBoard> ConfiguredAsync(JiraBoard board, bool chosen) =>
        chosen && board.FilterId is null
            ? Configured(board, await (await JiraGetAsync())(ConfigurationUrl(board)))
            : board;

    async Task<Func<string, Task<string>>> JiraGetAsync()
    {
        if (connectors.SourceOf(Jira) == ConnectorSource.Login)
            return await web.ReaderAsync(LoginSiteOf(Jira) ?? throw new InvalidOperationException(NotLoggedIn));
        var authorization = (await OwnEndpointAsync(Jira))?.Headers.GetValueOrDefault("Authorization")
            ?? throw new InvalidOperationException("Kræver dit token (Hamster) eller Login.");
        return url => GetWithTokenAsync(url, authorization);
    }

    async Task<(IReadOnlyList<string> Sites, Func<string, Task<string>> Get)> JiraRestAsync()
    {
        if (connectors.SourceOf(Jira) == ConnectorSource.Login)
            return await CheckLoginAsync(Jira) is { } site ? ([site], await web.ReaderAsync(site)) : throw new InvalidOperationException(NotLoggedIn);
        var get = await JiraGetAsync();
        string[] sites = [.. JiraSites((await CallAsync(Jira, [new("getAccessibleAtlassianResources", new JsonObject())])).Single().Text).Where(IsJiraSite)];
        return sites.Length > 0 ? (sites, get) : throw new InvalidOperationException("Ingen Jira-sites fundet.");
    }

    static async Task<string> GetWithTokenAsync(string url, string authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Http.SendAsync(request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync()
            : throw new InvalidOperationException($"Jira afviste token'et ({(int)response.StatusCode} {response.ReasonPhrase}).");
    }

    async Task<IReadOnlyList<(ToolCall Call, string Text)>> CallAsync(Connector connector, IReadOnlyList<ToolCall> calls) => connectors.SourceOf(connector) switch
    {
        ConnectorSource.ClaudeAi => await fetcher.CallAsync(connector, connector.ClaudeAiName ?? connector.FindIn(await connectors.ServersAsync(), claudeAi: true)?.Name
            ?? throw new InvalidOperationException($"{connector.Title}: {Connector.MissingOnClaudeAi}"), calls),
        ConnectorSource.Hamster => await OwnEndpointAsync(connector) is { } endpoint
            ? [.. calls.Zip(await McpClient.CallAsync(endpoint, calls))]
            : throw new InvalidOperationException($"{connector.Title} er ikke installeret."),
        _ => throw new InvalidOperationException($"{connector.Title} er slået fra."),
    };
}

public sealed class Feed(IReadOnlyList<(string Source, Func<Task<IReadOnlyList<FeedItem>>> Fetch)> sources)
{
    readonly Dictionary<string, IReadOnlyList<FeedItem>> fetched = [];
    readonly Dictionary<string, HashSet<string>> seen = [];
    Task refreshing = Task.CompletedTask;
    bool again, forget;

    public event Action<bool>? Changed;

    public IReadOnlyList<FeedItem> Items { get; private set; } = [];

    public IReadOnlyList<(string Source, string Error)> Errors { get; private set; } = [];

    public DateTime? UpdatedAt { get; private set; }

    public IEnumerable<FeedSource> SourcesAt(DateTimeOffset now, IEnumerable<(string Source, string Group)> subscribed)
    {
        var slots = subscribed.Concat(Items.Select(item => (item.Source, item.Group))).Distinct().ToArray();
        return slots.Select(slot => slot.Source).Concat(Errors.Select(error => error.Source)).Distinct()
            .Select(source => new FeedSource(source, Errors.FirstOrDefault(error => error.Source == source).Error, [.. slots.Where(slot => slot.Source == source).Select(slot => GroupOf(slot, now))]));
    }

    FeedGroup GroupOf((string Source, string Group) slot, DateTimeOffset now)
    {
        FeedItem[] items = [.. Items.Where(item => (item.Source, item.Group) == slot)];
        return new FeedGroup($"{slot.Group} · {items.Length}", [.. Boxes(items, now)]);
    }

    static IEnumerable<FeedBox> Boxes(FeedItem[] items, DateTimeOffset now)
    {
        foreach (var epic in items.Select(item => item.Kind == IssueKind.Epic ? item.Ref : item.Epic).OfType<IssueRef>().DistinctBy(epic => epic.Url).OrderBy(epic => epic.Key, FeedItem.KeyOrder))
            yield return new FeedBox(
                items.FirstOrDefault(item => item.Url == epic.Url) is { } listed ? RowOf(listed, now) : RowOf(epic),
                [.. Rows([.. items.Where(item => item.Kind != IssueKind.Epic && item.Epic?.Url == epic.Url)], now)]);
        FeedItem[] loose = [.. items.Where(item => item.Kind != IssueKind.Epic && item.Epic is null)];
        if (loose.Length > 0)
            yield return new FeedBox(null, [.. Rows(loose, now)]);
    }

    static IEnumerable<FeedRow> Rows(FeedItem[] items, DateTimeOffset now)
    {
        static bool Nested(FeedItem item) => item.Kind == IssueKind.SubTask && item.Parent is not null;
        var tops = items.Where(item => !Nested(item)).Select(item => (Key: item.Id, item.Url, Row: RowOf(item, now)))
            .Concat(items.Where(Nested).Select(item => item.Parent!).Where(task => !items.Any(item => item.Url == task.Url)).DistinctBy(task => task.Url)
                .Select(task => (task.Key, task.Url, Row: RowOf(task) with { Muted = true })))
            .OrderBy(top => top.Key, FeedItem.KeyOrder);
        foreach (var top in tops)
        {
            yield return top.Row;
            foreach (var subTask in items.Where(item => Nested(item) && item.Parent!.Url == top.Url).OrderBy(item => item.Id, FeedItem.KeyOrder))
                yield return RowOf(subTask, now) with { Depth = 1 };
        }
    }

    static FeedRow RowOf(FeedItem item, DateTimeOffset now) => new(item.Id, item.Title, item.Url, item.DetailAt(now), item.Kind, Details: item.Details);

    static FeedRow RowOf(IssueRef issue) => new(issue.Key, issue.Title, issue.Url, "", issue.Kind);

    public Task RefreshAsync(bool subscriptionsChanged = false)
    {
        forget |= subscriptionsChanged;
        if (!refreshing.IsCompleted)
        {
            again = true;
            return refreshing;
        }
        return refreshing = RefreshUntilCurrentAsync();
    }

    async Task RefreshUntilCurrentAsync()
    {
        do
        {
            again = false;
            if (forget)
            {
                seen.Clear();
                forget = false;
            }
            var news = false;
            List<(string Source, string Error)> errors = [];
            foreach (var (source, fetch) in sources)
                try
                {
                    news |= Take(source, await fetch());
                }
                catch (Exception exception) when (Subscriptions.IsFetchError(exception))
                {
                    errors.Add((source, exception.Message));
                }
            (Items, Errors) = ([.. sources.SelectMany(source => fetched.GetValueOrDefault(source.Source) ?? [])], errors);
            if (errors.Count == 0)
                UpdatedAt = DateTime.Now;
            Changed?.Invoke(news);
        }
        while (again);
    }

    bool Take(string source, IReadOnlyList<FeedItem> items)
    {
        fetched[source] = items;
        var current = items.Select(item => item.Url).ToHashSet();
        var fresh = seen.TryGetValue(source, out var before) && !current.IsSubsetOf(before);
        seen[source] = current;
        return fresh;
    }
}
