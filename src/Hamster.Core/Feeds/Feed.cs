namespace Hamster.Core.Feeds;

public sealed record FeedRow(string Id, string Title, string Url, string Detail, IssueKind Kind = IssueKind.Other, int Depth = 0, bool Muted = false, IssueDetails? Details = null);

public sealed record FeedBox(FeedRow? Epic, IReadOnlyList<FeedRow> Rows);

public sealed record FeedGroup(string Title, IReadOnlyList<FeedBox> Boxes);

public sealed record FeedSource(string Title, string? Error, IReadOnlyList<FeedGroup> Groups);

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
        var title = slot.Source == Subscriptions.GitHubSource ? Subscriptions.GitHubFeeds.FirstOrDefault(feed => feed.Key == slot.Group)?.Title ?? slot.Group : slot.Group;
        return new FeedGroup($"{title} · {items.Length}", [.. Boxes(items, now)]);
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
            Changed?.Invoke(news && !forget);
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
