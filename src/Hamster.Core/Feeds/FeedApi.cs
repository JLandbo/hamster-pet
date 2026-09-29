using System.Globalization;
using System.Text.Json.Nodes;
using Hamster.Core.Connections;
using Hamster.Core.Languages;

namespace Hamster.Core.Feeds;

static class FeedApi
{
    const int _pageSize = 100;
    const string _sprintName = "Sprint";
    static readonly string[] _customFieldNames = ["Story Points", "Reviewer", "Tester", "Kundenavn"];
    static readonly string[] _issueFields = ["summary", "status", "updated", "issuetype", "parent", "priority", "labels", "components", "created", "description", "duedate", "timetracking"];
    internal static readonly IReadOnlyDictionary<string, string> DefaultFields = new Dictionary<string, string> { [_sprintName] = "customfield_10020" };
    const int _gitHubSearchLimit = 1000;

    public static IReadOnlyDictionary<string, string> JiraFieldsOf(string json)
    {
        JsonObject[] fields = [.. (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>()];
        Dictionary<string, string> found = [];
        if (fields.FirstOrDefault(field => (string?)field["schema"]?["custom"] == "com.pyxis.greenhopper.jira:gh-sprint")?["id"]?.ToString() is { } sprint)
        {
            found[_sprintName] = sprint;
        }
        foreach (var name in _customFieldNames)
        {
            if (fields.FirstOrDefault(field => (string?)field["name"] == name)?["id"]?.ToString() is { } id)
            {
                found[name] = id;
            }
        }
        return found;
    }

    public static IEnumerable<JiraBoard> Merged(IEnumerable<JiraBoard> found, IReadOnlyList<JiraBoard> known)
    {
        return found.Select(board => known.FirstOrDefault(other => other.Key == board.Key) is { } stored ? stored with { Name = board.Name } : board)
            .OrderBy(board => board.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    internal static string ConfigurationUrl(JiraBoard board) => $"{board.Site.TrimEnd('/')}/rest/agile/1.0/board/{board.Id}/configuration";

    public static IEnumerable<(string Site, string Jql)> LookupSearches(IEnumerable<IssueRef> tasks) => tasks.GroupBy(task => new Uri(task.Url).GetLeftPart(UriPartial.Authority))
            .SelectMany(site => site.Chunk(_pageSize).Select(chunk => (site.Key, $"key in ({string.Join(", ", chunk.Select(task => task.Key))})")));

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

    public static string? NextPageToken(string json) => JsonNode.Parse(json) is JsonObject page
            ? (string?)page["nextPageToken"] is { Length: > 0 } token
                ? token
                : page["issues"] is JsonObject issues && issues["pageInfo"] is JsonObject info && (bool?)info["hasNextPage"] == true ? (string?)info["endCursor"] : null
            : null;

    public static ToolCall? NextJiraPage(ToolCall call, string text) => NextPageToken(text) is { } token ? call with { Arguments = With(call.Arguments, "nextPageToken", token) } : null;

    public static ToolCall? NextGitHubPage(ToolCall call, string text)
    {
        var page = JsonNode.Parse(text);
        var number = (int?)call.Arguments["page"] ?? 1;
        return (page?["items"] as JsonArray)?.Count == _pageSize && number * _pageSize < Math.Min((int?)page?["total_count"] ?? 0, _gitHubSearchLimit)
            ? call with { Arguments = With(call.Arguments, "page", number + 1) }
            : null;
    }

    static JsonObject With(JsonObject arguments, string name, JsonNode value)
    {
        var copy = arguments.DeepClone().AsObject();
        copy[name] = value;
        return copy;
    }

    public static IEnumerable<IssueRef> TasksWithUnknownEpic(IReadOnlyList<FeedItem> items) => items.Where(item => item.Kind == IssueKind.SubTask).Select(item => item.Parent).OfType<IssueRef>()
            .Where(task => !items.Any(item => item.Url == task.Url)).DistinctBy(task => task.Url);

    public static IReadOnlyList<FeedItem> WithEpics(IReadOnlyList<FeedItem> items, IReadOnlyDictionary<string, IssueRef?> epicsOfTasks)
    {
        return [.. items.Select(item => item with { Epic = EpicOf(item, items, epicsOfTasks) })];
    }

    static IssueRef? EpicOf(FeedItem item, IReadOnlyList<FeedItem> items, IReadOnlyDictionary<string, IssueRef?> epicsOfTasks) => item.Kind == IssueKind.SubTask && item.Parent is { } task
            ? (items.FirstOrDefault(other => other.Url == task.Url) is { } listed ? listed.Parent : epicsOfTasks.GetValueOrDefault(task.Url)) is { Kind: IssueKind.Epic } epic ? epic : null
            : item.Parent is { Kind: IssueKind.Epic } parent ? parent : null;

    public static string SearchUrl(string site, string jql, IEnumerable<string> customFields, string? pageToken = null)
    {
        return $"{site.TrimEnd('/')}/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields={string.Join(',', _issueFields.Concat(customFields))}&maxResults={_pageSize}"
        + (pageToken is null ? "" : $"&nextPageToken={Uri.EscapeDataString(pageToken)}");
    }

    public static string? BoardJql(JiraBoard board, IReadOnlyList<string> columns)
    {
        return board.FilterId is { } filter && board.Columns.Where(column => columns.Contains(column.Name)).SelectMany(column => column.StatusIds).Distinct().ToArray() is { Length: > 0 } statuses
            ? $"filter = {filter} AND assignee = currentUser() AND status in ({string.Join(", ", statuses)}) ORDER BY updated DESC"
            : null;
    }

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

    public static bool IsJiraSite(string url) => Uri.TryCreate(url, UriKind.Absolute, out var site) && Subscriptions.Jira.Login!.SiteOf(site) is not null;

    public static IReadOnlyList<string> JiraSites(string json) => [.. (JsonNode.Parse(json) switch
            {
                JsonArray sites => sites,
                JsonObject result => result["data"]?["resources"] as JsonArray,
                _ => null,
            } ?? []).OfType<JsonObject>()
            .Select(site => (string?)site["url"]).OfType<string>()];

    public static IEnumerable<FeedItem> JiraItems(string json, string site, IReadOnlyDictionary<string, string> columns, IReadOnlyDictionary<string, string>? customFields = null) => JiraIssues(json)
            .Select(issue => (Key: (string?)issue["key"], Fields: issue["fields"], Site: site))
            .Where(issue => issue.Key is not null)
            .Select(issue => new FeedItem(
                Subscriptions.JiraSource,
                columns.GetValueOrDefault(issue.Fields?["status"]?["id"]?.ToString() ?? "") ?? (string?)issue.Fields?["status"]?["name"] ?? "",
                issue.Key!,
                (string?)issue.Fields?["summary"] ?? "",
                BrowseUrl(issue.Site, issue.Key!),
                Updated: TimeOf(issue.Fields?["updated"]),
                Kind: KindOf(issue.Fields?["issuetype"]),
                Parent: ParentOf(issue.Fields?["parent"], issue.Site),
                Details: DetailsOf(issue.Fields, customFields ?? DefaultFields)));

    static IssueDetails? DetailsOf(JsonNode? fields, IReadOnlyDictionary<string, string> customFields)
    {
        IssueTag?[] tags =
        [
            Tag(Strings.Of("Tasks.Status"), TextOf(fields?["status"])),
            Tag(Strings.Of("Tasks.Priority"), TextOf(fields?["priority"])),
            .. _customFieldNames.Select(name => Tag(name, customFields.GetValueOrDefault(name) is { } id ? TextOf(fields?[id]) : null)),
            Tag(Strings.Of("Tasks.TimeTracking"), TimeTrackingOf(fields?["timetracking"])),
            Tag(Strings.Of("Tasks.DueDate"), DateOf(fields?["duedate"])),
            Tag(Strings.Of("Tasks.Labels"), TextOf(fields?["labels"])),
            Tag(Strings.Of("Tasks.Component"), TextOf(fields?["components"])),
            Tag(Strings.Of("Tasks.Created"), DateOf(fields?["created"])),
        ];
        var details = new IssueDetails(SprintOf(fields), [.. tags.OfType<IssueTag>()], JiraDescription.Of(fields?["description"]));
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

    static string? TimeTrackingOf(JsonNode? tracking)
    {
        (string Field, string Label)[] parts = [("originalEstimate", Strings.Of("Tasks.Estimated")), ("remainingEstimate", Strings.Of("Tasks.Remaining")), ("timeSpent", Strings.Of("Tasks.Spent"))];
        return string.Join(" · ", parts.Select(part => (string?)tracking?[part.Field] is { } time ? $"{time} {part.Label}" : null).OfType<string>()) is { Length: > 0 } text ? text : null;
    }

    static string? SprintOf(JsonNode? fields)
    {
        var sprints = (fields as JsonObject)?.Select(field => field.Value).OfType<JsonArray>()
            .FirstOrDefault(values => values.OfType<JsonObject>().Any(value => value["boardId"] is not null && value["state"] is not null))?.OfType<JsonObject>().ToArray() ?? [];
        if ((sprints.FirstOrDefault(sprint => (string?)sprint["state"] == "active") ?? sprints.LastOrDefault()) is not { } sprint)
        {
            return null;
        }
        return (string?)sprint["state"] == "active" && DateOf(sprint["endDate"]) is { } end
            ? Strings.Format("Tasks.SprintEnds", (string?)sprint["name"], end)
            : (string?)sprint["name"];
    }

    static IssueKind KindOf(JsonNode? type) => (int?)type?["hierarchyLevel"] == 1 ? IssueKind.Epic
        : (bool?)type?["subtask"] == true ? IssueKind.SubTask
        : (string?)type?["name"] == "Task" ? IssueKind.Task
        : IssueKind.Other;

    static IssueRef? ParentOf(JsonNode? parent, string site) => (string?)parent?["key"] is { } key
            ? new IssueRef(key, (string?)parent["fields"]?["summary"] ?? "", KindOf(parent["fields"]?["issuetype"]), BrowseUrl(site, key))
            : null;

    static string BrowseUrl(string site, string key) => $"{site.TrimEnd('/')}/browse/{key}";

    public static IEnumerable<FeedItem> GitHubItems(string json, string group) => (JsonNode.Parse(json)?["items"]?.AsArray().OfType<JsonObject>() ?? [])
            .Select(item => (Url: (string?)item["html_url"], Title: (string?)item["title"], Number: (int?)item["number"], Repository: ((string?)item["repository_url"])?.Split('/')[^1], Updated: TimeOf(item["updated_at"]), Draft: (bool?)item["draft"] == true))
            .Where(item => item.Url is not null)
            .Select(item => new FeedItem(Subscriptions.GitHubSource, group, $"#{item.Number}", item.Title ?? "", item.Url!, item.Repository ?? "", item.Updated, item.Draft));

    static DateTimeOffset? TimeOf(JsonNode? time) => DateTimeOffset.TryParse((string?)time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    internal static IReadOnlyDictionary<string, string> ColumnsByStatus(IEnumerable<JiraBoard> boards)
    {
        return boards.SelectMany(board => board.Columns).SelectMany(column => column.StatusIds.Select(status => (Status: status, Column: column.Name)))
            .DistinctBy(found => found.Status).ToDictionary(found => found.Status, found => found.Column);
    }

    static IEnumerable<JsonObject> JiraIssues(string json) => (JsonNode.Parse(json)?["issues"] switch
        {
            JsonArray issues => issues,
            JsonObject page => page["nodes"] as JsonArray,
            _ => null,
        } ?? []).OfType<JsonObject>();

    public static ToolCall JiraSearch(string site, string jql, IEnumerable<string> customFields) => new("searchJiraIssuesUsingJql", new JsonObject
    {
        ["cloudId"] = site,
        ["jql"] = jql,
        ["maxResults"] = _pageSize,
        ["fields"] = new JsonArray([.. _issueFields.Concat(customFields).Select(field => (JsonNode)field)]),
        ["responseContentFormat"] = "markdown",
    });

    internal static ToolCall GitHubSearch(string tool, string query) => new(tool, new JsonObject
    {
        ["query"] = query,
        ["page"] = 1,
        ["perPage"] = _pageSize,
        ["fields"] = new JsonArray("number", "title", "html_url", "repository_url", "updated_at", "draft"),
    });
}
