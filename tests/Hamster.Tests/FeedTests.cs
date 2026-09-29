namespace Hamster.Tests;

public sealed class FeedTests
{
    const string _site = "https://firma.atlassian.net";

    [Fact]
    public async Task RefreshAsync_WhenTheFirstItemsArrive_ThenIsNotNews()
    {
        // Arrange
        List<bool> news = [];
        var feed = new Feed([("Jira", Returning(Item("a")))]);
        feed.Changed += news.Add;

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.Equal([false], news);
    }

    [Fact]
    public async Task RefreshAsync_WhenANewItemArrives_ThenIsNews()
    {
        // Arrange
        FeedItem[] items = [Item("a")];
        List<bool> news = [];
        var feed = new Feed([("Jira", Current(() => items))]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        await feed.RefreshAsync();
        items = [Item("a"), Item("b")];

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.Equal([false, false, true], news);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheFirstFetchFailed_ThenTheFirstSuccessfulOneIsNotNews()
    {
        // Arrange
        var failing = true;
        List<bool> news = [];
        var feed = new Feed([("Jira", () => failing ? Failing("fejl")() : Returning(Item("a"))())]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        failing = false;

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.Equal([false, false], news);
    }

    [Fact]
    public async Task RefreshAsync_WhenAnItemLeavesAndComesBack_ThenIsNews()
    {
        // Arrange
        FeedItem[] items = [Item("a")];
        List<bool> news = [];
        var feed = new Feed([("Jira", Current(() => items))]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        items = [];
        await feed.RefreshAsync();
        items = [Item("a")];

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.True(news[^1]);
    }

    [Fact]
    public async Task RefreshAsync_WhenOneSourceKeepsFailing_ThenNewItemsFromTheOtherAreNews()
    {
        // Arrange
        FeedItem[] items = [Item("a")];
        List<bool> news = [];
        var feed = new Feed([("Jira", Failing("fejl")), ("GitHub", Current(() => items))]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        items = [Item("a"), Item("b")];

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.True(news[^1]);
    }

    [Fact]
    public async Task RefreshAsync_WhenASourceFails_ThenKeepsItsItemsShowsTheErrorAndIsNotMarkedUpdated()
    {
        // Arrange
        var failing = false;
        var feed = new Feed([("Jira", () => failing ? Failing("fejl")() : Returning(Item("a"))())]);
        await feed.RefreshAsync();
        var updated = feed.UpdatedAt;
        failing = true;

        // Act
        await feed.RefreshAsync();

        // Assert
        Assert.Equal(("a", ("Jira", "fejl"), updated), (Assert.Single(feed.Items).Id, Assert.Single(feed.Errors), feed.UpdatedAt));
    }

    [Fact]
    public async Task RefreshAsync_WhenAskedWhileRefreshing_ThenWaitsForAnotherRefresh()
    {
        // Arrange
        var gate = new TaskCompletionSource<IReadOnlyList<FeedItem>>();
        var fetches = 0;
        var feed = new Feed([("Jira", () => ++fetches == 1 ? gate.Task : Returning()())]);
        _ = feed.RefreshAsync();

        // Act
        var asked = feed.RefreshAsync();
        var waiting = (asked.IsCompleted, fetches);
        gate.SetResult([]);
        await asked;

        // Assert
        Assert.Equal(((false, 1), 2), (waiting, fetches));
    }

    [Fact]
    public async Task RefreshAsync_WhenTheSubscriptionsChangeWhileFetching_ThenTheNewItemsAreNotNews()
    {
        // Arrange
        FeedItem[] items = [Item("a")];
        var gate = Task.CompletedTask;
        List<bool> news = [];
        var feed = new Feed([("Jira", async () => { await gate; return items; })]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        var fetching = new TaskCompletionSource();
        gate = fetching.Task;
        var refreshing = feed.RefreshAsync();
        items = [Item("a"), Item("b")];

        // Act
        var changed = feed.RefreshAsync(subscriptionsChanged: true);
        fetching.SetResult();
        await refreshing;
        await changed;

        // Assert
        Assert.Equal([false, false, false], news);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheSubscriptionsChanged_ThenExistingItemsAreNotNews()
    {
        // Arrange
        FeedItem[] items = [Item("a")];
        List<bool> news = [];
        var feed = new Feed([("Jira", Current(() => items))]);
        feed.Changed += news.Add;
        await feed.RefreshAsync();
        items = [Item("a"), Item("b")];

        // Act
        await feed.RefreshAsync(subscriptionsChanged: true);

        // Assert
        Assert.False(news[^1]);
    }

    [Fact]
    public async Task SourcesAt_WhenItemsAreFound_ThenGroupsThemPerSourceAndGroupWithCounts()
    {
        // Arrange
        var feed = await Refreshed(Item("a", "Jira", "Test"), Item("b", "Jira", "Under Review"), Item("c", "Jira", "Test"), Item("d", "GitHub", "Mine åbne PR'er"));

        // Act
        var sources = feed.SourcesAt(DateTimeOffset.Now, []).Select(source => $"{source.Title}: {string.Join(" | ", source.Groups.Select(group => $"{group.Title} {string.Join(",", group.Boxes.SelectMany(box => box.Rows).Select(row => row.Id))}"))}");

        // Assert
        Assert.Equal(["Jira: Test · 2 a,c | Under Review · 1 b", "GitHub: Mine åbne PR'er · 1 d"], sources);
    }

    [Fact]
    public async Task SourcesAt_WhenTheGitHubGroupIsAFeedKey_ThenShowsTheFeedTitle()
    {
        // Arrange
        var feed = await Refreshed(Item("a", "GitHub", "authored"));

        // Act
        var group = feed.SourcesAt(DateTimeOffset.Now, []).Single().Groups.Single();

        // Assert
        Assert.Equal("Mine åbne PR'er · 1", group.Title);
    }

    [Fact]
    public async Task SourcesAt_WhenASubscribedGroupIsEmpty_ThenShowsItWithZero()
    {
        // Arrange
        var feed = await Refreshed(Item("a", "Jira", "Under Review"));

        // Act
        var groups = feed.SourcesAt(DateTimeOffset.Now, [("Jira", "Test"), ("Jira", "Under Review")]).SelectMany(source => source.Groups).Select(group => group.Title);

        // Assert
        Assert.Equal(["Test · 0", "Under Review · 1"], groups);
    }

    [Fact]
    public async Task SourcesAt_WhenASourceFailed_ThenShowsItsErrorEvenWithoutSubscriptions()
    {
        // Arrange
        var feed = new Feed([("GitHub", Failing("claude svarede ikke."))]);
        await feed.RefreshAsync();

        // Act
        var sources = feed.SourcesAt(DateTimeOffset.Now, [("Jira", "Test")]).Select(source => (source.Title, source.Error));

        // Assert
        Assert.Equal([("Jira", null), ("GitHub", "claude svarede ikke.")], sources);
    }

    [Fact]
    public async Task SourcesAt_WhenTasksHaveEpics_ThenBoxesThemUnderTheirEpicSortedByKey()
    {
        // Arrange
        var epic = Ref("ACS-1", IssueKind.Epic);
        var feed = await Refreshed(
            Issue("ACS-20", IssueKind.Task, epic, epic),
            Issue("ACS-7", IssueKind.Task),
            Issue("ACS-11", IssueKind.SubTask, Ref("ACS-50", IssueKind.Task), epic),
            Issue("ACS-9", IssueKind.SubTask, Ref("ACS-3", IssueKind.Task), epic),
            Issue("ACS-3", IssueKind.Task, epic, epic));

        // Act
        var boxes = feed.SourcesAt(DateTimeOffset.Now, []).Single().Groups.Single().Boxes
            .Select(box => $"{box.Epic?.Id ?? "-"}: {string.Join(", ", box.Rows.Select(row => $"{(row.Depth > 0 ? ">" : "")}{(row.Muted ? "~" : "")}{row.Id}"))}");

        // Assert
        Assert.Equal(["ACS-1: ACS-3, >ACS-9, ACS-20, ~ACS-50, >ACS-11", "-: ACS-7"], boxes);
    }

    [Fact]
    public async Task SourcesAt_WhenTheEpicItselfIsListed_ThenItHeadsItsBox()
    {
        // Arrange
        var epic = Ref("ACS-1", IssueKind.Epic);
        var feed = await Refreshed(Issue("ACS-2", IssueKind.Task, epic, epic), Issue("ACS-1", IssueKind.Epic) with { Updated = DateTimeOffset.Now.AddHours(-2) });

        // Act
        var box = Assert.Single(feed.SourcesAt(DateTimeOffset.Now, []).Single().Groups.Single().Boxes);

        // Assert
        Assert.Equal(("ACS-1", "2 t", "ACS-2"), (box.Epic?.Id, box.Epic?.Detail, Assert.Single(box.Rows).Id));
    }

    static Func<Task<IReadOnlyList<FeedItem>>> Returning(params FeedItem[] items) => () => Task.FromResult<IReadOnlyList<FeedItem>>(items);

    static Func<Task<IReadOnlyList<FeedItem>>> Current(Func<FeedItem[]> items) => () => Task.FromResult<IReadOnlyList<FeedItem>>(items());

    static Func<Task<IReadOnlyList<FeedItem>>> Failing(string message) => () => Task.FromException<IReadOnlyList<FeedItem>>(new InvalidOperationException(message));

    static async Task<Feed> Refreshed(params FeedItem[] items)
    {
        var feed = new Feed([("Jira", Returning(items))]);
        await feed.RefreshAsync();
        return feed;
    }

    static string Url(string key) => $"{_site}/browse/{key}";

    static IssueRef Ref(string key, IssueKind kind) => new(key, "", kind, Url(key));

    static FeedItem Issue(string key, IssueKind kind, IssueRef? parent = null, IssueRef? epic = null) => new("Jira", "Test", key, "", Url(key), Kind: kind, Parent: parent, Epic: epic);

    static FeedItem Item(string id, string source = "Jira", string group = "Test") => new(source, group, id, "", $"https://example.com/{id}");
}
