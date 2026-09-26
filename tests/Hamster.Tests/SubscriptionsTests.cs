using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class SubscriptionsTests : IDisposable
{
    const string Site = "https://firma.atlassian.net";
    readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    Subscriptions NewSubscriptions(ClaudeSettings? settings = null) => new(
        new Connectors(new ClaudeClient(folder, Path.Combine(folder, "i.txt"), new JsonFile<ClaudeSettings>(Path.Combine(folder, "settings.json"), settings ?? ClaudeSettings.Default)), () => { }, () => false),
        new ClaudeFetcher(folder),
        new WebSession(folder),
        new JsonFile<SubscriptionSettings>(Path.Combine(folder, "subscriptions.json"), SubscriptionSettings.Empty));

    [Fact]
    public void ChoicesFor_WhenBoardsAreChosen_ThenListsTheirColumnsInBoardOrder()
    {
        // Arrange
        var subscriptions = NewSubscriptions();
        JiraBoard[] boards = [Board(12, "10200", ("To Do", ["1"]), ("Test", ["10012"])), Board(13, "10300", ("Test", ["10012"]), ("Review", ["10014"]))];
        subscriptions.Settings = (SubscriptionSettings.Empty with { JiraBoards = boards }).WithColumn("Test", true).WithBoard(boards[0], true).WithBoard(boards[1], true);

        // Act
        var choices = subscriptions.ChoicesFor(Subscriptions.Jira);

        // Assert
        Assert.Equal([new Choice("To Do", "To Do", false, ChoiceKind.Column), new Choice("Test", "Test", true, ChoiceKind.Column), new Choice("Review", "Review", false, ChoiceKind.Column)], choices);
    }

    [Fact]
    public async Task ChooseAsync_WhenAGitHubKindIsTicked_ThenOnlyThatKindIsChosen()
    {
        // Arrange
        var subscriptions = NewSubscriptions();

        // Act
        await subscriptions.ChooseAsync(new Choice("review", "Review anmodet af mig", false, ChoiceKind.GitHub), true);

        // Assert
        Assert.Equal([true, false, false], subscriptions.ChoicesFor(Subscriptions.GitHub).Select(choice => choice.Chosen));
    }

    [Fact]
    public async Task ChooseAsync_WhenAKnownBoardIsTicked_ThenItsColumnsCanBeChosen()
    {
        // Arrange
        var subscriptions = NewSubscriptions();
        var board = Board(12, "10200", ("Test", ["10012"]));
        subscriptions.Settings = SubscriptionSettings.Empty with { JiraBoards = [board] };

        // Act
        await subscriptions.ChooseAsync(new Choice(board.Key, board.Name, false, ChoiceKind.Board), true);

        // Assert
        Assert.Equal(["Test"], subscriptions.ChoicesFor(Subscriptions.Jira).Select(choice => choice.Key));
    }

    [Fact]
    public void BoardJql_WhenColumnsAreChosen_ThenFindsMyIssuesInThemOnTheBoard()
    {
        // Arrange
        var board = Board(12, "10200", ("To Do", ["1"]), ("Test", ["10012"]), ("Review", ["10014", "10019"]));

        // Act
        var jql = Subscriptions.BoardJql(board, ["Test", "Review", "Findes ikke her"]);

        // Assert
        Assert.Equal("filter = 10200 AND assignee = currentUser() AND status in (10012, 10014, 10019) ORDER BY updated DESC", jql);
    }

    [Theory]
    [InlineData("10200", "Done")]
    [InlineData(null, "Test")]
    public void BoardJql_WhenNothingOnTheBoardIsChosen_ThenSkipsTheBoard(string? filter, string column)
    {
        // Act
        var jql = Subscriptions.BoardJql(Board(12, filter, ("Test", ["10012"])), [column]);

        // Assert
        Assert.Null(jql);
    }

    [Fact]
    public void BoardPage_WhenJiraListsBoards_ThenNamesThemWithTheirProject()
    {
        // Arrange
        const string json = """{"isLast":false,"values":[{"id":12,"name":"ACS board","location":{"projectKey":"ACS"}},{"id":13,"name":"Team"}]}""";

        // Act
        var (boards, last) = Subscriptions.BoardPage(json, Site);

        // Assert
        Assert.Equal([new JiraBoard(12, "ACS board (ACS)", Site), new JiraBoard(13, "Team", Site)], boards);
        Assert.False(last);
    }

    [Fact]
    public void Configured_WhenJiraSendsTheBoardSetup_ThenReadsItsFilterAndColumns()
    {
        // Arrange
        const string json = """{"filter":{"id":"10200"},"columnConfig":{"columns":[{"name":"To Do","statuses":[{"id":"1"}]},{"name":"Test","statuses":[{"id":"10012"},{"id":"10020"}]}]}}""";

        // Act
        var board = Subscriptions.Configured(new JiraBoard(12, "ACS board", Site), json);

        // Assert
        Assert.Equal(("10200", "To Do: 1 | Test: 10012,10020"), (board.FilterId, string.Join(" | ", board.Columns.Select(column => $"{column.Name}: {string.Join(',', column.StatusIds)}"))));
    }

    [Theory]
    [InlineData("https://aciesdk.atlassian.net", true)]
    [InlineData("http://aciesdk.atlassian.net", false)]
    [InlineData("https://atlassian.net.example.com", false)]
    [InlineData("https://example.com", false)]
    public void IsJiraSite_WhenGivenAUrl_ThenOnlyAcceptsAtlassianSitesOverHttps(string url, bool expected)
    {
        // Act
        var accepted = Subscriptions.IsJiraSite(url);

        // Assert
        Assert.Equal(expected, accepted);
    }

    [Theory]
    [InlineData("""[{"id":"f6909ca2","url":"https://firma.atlassian.net","name":"firma"}]""")]
    [InlineData("""{"data":{"resources":[{"cloudId":"f6909ca2","url":"https://firma.atlassian.net"}]}}""")]
    public void JiraSites_WhenEitherAtlassianServerAnswers_ThenReadsTheSites(string json)
    {
        // Act
        var sites = Subscriptions.JiraSites(json);

        // Assert
        Assert.Equal([Site], sites);
    }

    [Theory]
    [InlineData("""{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"10012","name":"Test"}}}]}""")]
    [InlineData("""{"issues":{"nodes":[{"key":"ACS-1","self":"https://api.atlassian.com/ex/jira/f6909ca2/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"10012","name":"Test"}}}]}}""")]
    public void JiraItems_WhenIssuesAreFound_ThenLinksToTheirSiteUnderTheirColumn(string json)
    {
        // Act
        var items = Subscriptions.JiraItems(json, Site, new Dictionary<string, string> { ["10012"] = "Under Test in DEV" });

        // Assert
        Assert.Equal([new FeedItem("Jira", "Under Test in DEV", "ACS-1", "Test af login", "https://firma.atlassian.net/browse/ACS-1")], items);
    }

    [Fact]
    public void JiraItems_WhenTheIssueHasAnUpdateTime_ThenKeepsIt()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","updated":"2026-09-26T10:42:00.000+0200"}}]}""";

        // Act
        var item = Assert.Single(Subscriptions.JiraItems(json, Site, new Dictionary<string, string>()));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 42, 0, TimeSpan.FromHours(2)), item.Updated);
    }

    [Fact]
    public void JiraItems_WhenTheStatusIsInNoKnownColumn_ThenUsesTheStatusName()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"3","name":"In Progress"}}}]}""";

        // Act
        var item = Assert.Single(Subscriptions.JiraItems(json, Site, new Dictionary<string, string>()));

        // Assert
        Assert.Equal("In Progress", item.Group);
    }

    [Fact]
    public void SearchUrl_WhenGivenJql_ThenAsksJiraForMyIssuesWithTheirSummary()
    {
        // Act
        var url = Subscriptions.SearchUrl(Site, "status in (\"Test\")");

        // Assert
        Assert.Equal("https://firma.atlassian.net/rest/api/3/search/jql?jql=status%20in%20%28%22Test%22%29&fields=summary,status,updated,issuetype,parent&maxResults=100", url);
    }

    [Fact]
    public void GitHubItems_WhenPullRequestsAreFound_ThenKeepsTheirRepositoryAndUpdateTime()
    {
        // Arrange
        const string json = """{"items":[{"html_url":"https://github.com/AciesDK/app/pull/11","number":11,"repository_url":"https://api.github.com/repos/AciesDK/app","title":"Ny knap","updated_at":"2026-09-26T08:42:00Z"}],"total_count":1}""";

        // Act
        var items = Subscriptions.GitHubItems(json, "Mine åbne PR'er");

        // Assert
        Assert.Equal([new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", new DateTimeOffset(2026, 9, 26, 8, 42, 0, TimeSpan.Zero))], items);
    }

    [Theory]
    [InlineData(0.5, "nu")]
    [InlineData(12, "12 min")]
    [InlineData(150, "2 t")]
    [InlineData(1500, "i går")]
    [InlineData(4400, "3 dage")]
    public void Ago_WhenTimeHasPassed_ThenSaysHowLongAgo(double minutes, string expected)
    {
        // Act
        var ago = FeedItem.Ago(TimeSpan.FromMinutes(minutes));

        // Assert
        Assert.Equal(expected, ago);
    }

    [Fact]
    public void DetailAt_WhenTheItemHasARepositoryAndUpdateTime_ThenShowsBoth()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var item = new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", now.AddHours(-2));

        // Act
        var detail = item.DetailAt(now);

        // Assert
        Assert.Equal("app · 2 t", detail);
    }

    [Fact]
    public void DetailAt_WhenThePullRequestIsADraft_ThenSaysSo()
    {
        // Arrange
        var item = new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", Draft: true);

        // Act
        var detail = item.DetailAt(DateTimeOffset.Now);

        // Assert
        Assert.Equal("app · kladde", detail);
    }

    [Fact]
    public void GitHubItems_WhenThePullRequestIsADraft_ThenMarksIt()
    {
        // Arrange
        const string json = """{"items":[{"html_url":"https://github.com/AciesDK/app/pull/11","number":11,"title":"Ny knap","draft":true}]}""";

        // Act
        var item = Assert.Single(Subscriptions.GitHubItems(json, "Mine åbne PR'er"));

        // Assert
        Assert.True(item.Draft);
    }

    [Fact]
    public void WithColumn_WhenChosenAndUnchosen_ThenKeepsTheOthers()
    {
        // Arrange
        var settings = SubscriptionSettings.Empty.WithColumn("Test", true).WithColumn("Review", true);

        // Act
        var changed = settings.WithColumn("Test", false).WithGitHub("review", true);

        // Assert
        Assert.Equal(("Review", "review"), (Assert.Single(changed.JiraColumns), Assert.Single(changed.GitHub)));
    }

    [Fact]
    public void WithBoard_WhenAConfiguredBoardIsChosen_ThenReplacesTheKnownOne()
    {
        // Arrange
        var settings = SubscriptionSettings.Empty with { JiraBoards = [new JiraBoard(12, "ACS board", Site)] };
        var configured = Board(12, "10200", ("Test", ["10012"]));

        // Act
        var changed = settings.WithBoard(configured, true);

        // Assert
        Assert.Equal(("10200", configured.Key), (Assert.Single(changed.JiraBoards).FilterId, Assert.Single(changed.ChosenBoards)));
    }

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
    public async Task FetchJiraAsync_WhenLoginIsUsedButNotLoggedIn_ThenSaysSo()
    {
        // Arrange
        var subscriptions = NewSubscriptions(ClaudeSettings.Default with { LoginConnectors = [Subscriptions.Jira.Name] });
        var board = Board(12, "10200", ("Test", ["7"]));
        subscriptions.Settings = SubscriptionSettings.Empty with { JiraBoards = [board], ChosenBoards = [board.Key], JiraColumns = ["Test"] };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(subscriptions.FetchJiraAsync);

        // Assert
        Assert.Equal("Ikke logget ind. Log ind under Indstillinger.", exception.Message);
    }

    [Fact]
    public void WithoutLogin_WhenLoggedIntoSeveral_ThenForgetsOnlyThatOne()
    {
        // Arrange
        var settings = SubscriptionSettings.Empty with { LoginSites = new Dictionary<string, string> { ["a"] = "https://a.atlassian.net", ["b"] = "https://b.atlassian.net" } };

        // Act
        var changed = settings.WithoutLogin("a");

        // Assert
        Assert.Equal("b", Assert.Single(changed.LoginSites).Key);
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

    [Fact]
    public void KeyOrder_WhenKeysHaveNumbers_ThenSortsByTheirNumber()
    {
        // Arrange
        string[] keys = ["ACS-17578", "#498", "ACS-5588", "#11"];

        // Act
        var sorted = keys.Order(FeedItem.KeyOrder);

        // Assert
        Assert.Equal(["#11", "#498", "ACS-5588", "ACS-17578"], sorted);
    }

    [Fact]
    public void JiraItems_WhenIssuesHaveTypesAndParents_ThenKeepsThem()
    {
        // Arrange
        const string json = """
            {"issues":[
              {"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Platform","issuetype":{"name":"Epic","subtask":false,"hierarchyLevel":1}}},
              {"key":"ACS-2","self":"https://firma.atlassian.net/rest/api/3/issue/2","fields":{"summary":"S3","issuetype":{"name":"Task","subtask":false,"hierarchyLevel":0},"parent":{"key":"ACS-1","fields":{"summary":"Platform","issuetype":{"name":"Epic","subtask":false,"hierarchyLevel":1}}}}},
              {"key":"ACS-3","self":"https://firma.atlassian.net/rest/api/3/issue/3","fields":{"summary":"Test","issuetype":{"name":"Sub-task","subtask":true,"hierarchyLevel":-1},"parent":{"key":"ACS-2","fields":{"summary":"S3","issuetype":{"name":"Task","subtask":false,"hierarchyLevel":0}}}}},
              {"key":"ACS-4","self":"https://firma.atlassian.net/rest/api/3/issue/4","fields":{"summary":"Fejl","issuetype":{"name":"Bug","subtask":false,"hierarchyLevel":0}}}
            ]}
            """;

        // Act
        var items = Subscriptions.JiraItems(json, Site, new Dictionary<string, string>()).Select(item => (item.Id, item.Kind, item.Parent));

        // Assert
        Assert.Equal(
        [
            ("ACS-1", IssueKind.Epic, null),
            ("ACS-2", IssueKind.Task, new IssueRef("ACS-1", "Platform", IssueKind.Epic, Url("ACS-1"))),
            ("ACS-3", IssueKind.SubTask, new IssueRef("ACS-2", "S3", IssueKind.Task, Url("ACS-2"))),
            ("ACS-4", IssueKind.Other, (IssueRef?)null),
        ], items);
    }

    [Fact]
    public void WithEpics_WhenTheSubTasksTaskIsListed_ThenUsesTheTasksEpic()
    {
        // Arrange
        var epic = Ref("ACS-1", IssueKind.Epic);
        FeedItem[] items = [Issue("ACS-2", IssueKind.Task, epic), Issue("ACS-3", IssueKind.SubTask, Ref("ACS-2", IssueKind.Task))];

        // Act
        var epics = Subscriptions.WithEpics(items, new Dictionary<string, IssueRef?>()).Select(item => item.Epic);

        // Assert
        Assert.Equal([epic, epic], epics);
    }

    [Fact]
    public void WithEpics_WhenTheSubTasksTaskWasLookedUp_ThenUsesTheLookedUpEpic()
    {
        // Arrange
        var epic = Ref("ACS-1", IssueKind.Epic);
        FeedItem[] items = [Issue("ACS-3", IssueKind.SubTask, Ref("ACS-2", IssueKind.Task))];

        // Act
        var item = Assert.Single(Subscriptions.WithEpics(items, new Dictionary<string, IssueRef?> { [Url("ACS-2")] = epic }));

        // Assert
        Assert.Equal(epic, item.Epic);
    }

    [Fact]
    public void TasksWithUnknownEpic_WhenASubTasksTaskIsNotListed_ThenAsksForItOnce()
    {
        // Arrange
        var task = Ref("ACS-2", IssueKind.Task);
        FeedItem[] items = [Issue("ACS-3", IssueKind.SubTask, task), Issue("ACS-4", IssueKind.SubTask, task), Issue("ACS-6", IssueKind.SubTask, Ref("ACS-5", IssueKind.Task)), Issue("ACS-5", IssueKind.Task)];

        // Act
        var tasks = Subscriptions.TasksWithUnknownEpic(items);

        // Assert
        Assert.Equal([task], tasks);
    }

    [Fact]
    public void SearchUrl_WhenAskedForTheNextPage_ThenPassesTheToken()
    {
        // Act
        var url = Subscriptions.SearchUrl(Site, "assignee = currentUser()", "abc=");

        // Assert
        Assert.EndsWith("&maxResults=100&nextPageToken=abc%3D", url);
    }

    [Theory]
    [InlineData("""{"issues":[],"nextPageToken":"t2","isLast":false}""", "t2")]
    [InlineData("""{"issues":[],"isLast":true}""", null)]
    [InlineData("""{"issues":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"c2"}}}""", "c2")]
    [InlineData("""{"issues":{"nodes":[],"pageInfo":{"hasNextPage":false,"endCursor":null}}}""", null)]
    public void NextPageToken_WhenJiraAnswers_ThenFindsTheNextPage(string json, string? expected)
    {
        // Act
        var token = Subscriptions.NextPageToken(json);

        // Assert
        Assert.Equal(expected, token);
    }

    [Fact]
    public async Task PagesAsync_WhenJiraHasMorePages_ThenFetchesThemAll()
    {
        // Arrange
        List<string> asked = [];
        Task<string> Get(string url)
        {
            asked.Add(url);
            return Task.FromResult(url.Contains("nextPageToken=t2") ? """{"issues":[],"isLast":true}""" : """{"issues":[],"nextPageToken":"t2"}""");
        }

        // Act
        var pages = await Subscriptions.PagesAsync(Get, Site, "assignee = currentUser()");

        // Assert
        Assert.Equal((2, 2), (pages.Count, asked.Count));
    }

    [Fact]
    public void NextJiraPage_WhenThereIsAnotherPage_ThenAsksForItWithTheSameSearch()
    {
        // Arrange
        var call = new ToolCall("searchJiraIssuesUsingJql", new JsonObject { ["jql"] = "x" });

        // Act
        var next = Subscriptions.NextJiraPage(call, """{"issues":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"c2"}}}""");

        // Assert
        Assert.Equal(("x", "c2"), ((string?)next?.Arguments["jql"], (string?)next?.Arguments["nextPageToken"]));
    }

    [Theory]
    [InlineData(100, 250, 1, 2)]
    [InlineData(100, 200, 2, null)]
    [InlineData(40, 40, 1, null)]
    public void NextGitHubPage_WhenGitHubAnswers_ThenAsksForTheNextPageOnlyIfThereIsOne(int items, int total, int page, int? expected)
    {
        // Arrange
        var call = new ToolCall("search_issues", new JsonObject { ["query"] = "is:pr", ["page"] = page });
        var json = new JsonObject { ["total_count"] = total, ["items"] = new JsonArray([.. Enumerable.Range(0, items).Select(_ => (JsonNode)new JsonObject())]) }.ToJsonString();

        // Act
        var next = Subscriptions.NextGitHubPage(call, json);

        // Assert
        Assert.Equal(expected, (int?)next?.Arguments["page"]);
    }

    [Fact]
    public void LookupSearches_WhenThereAreManyTasks_ThenAsksForAHundredAtATime()
    {
        // Arrange
        var tasks = Enumerable.Range(1, 150).Select(number => Ref($"ACS-{number}", IssueKind.Task));

        // Act
        var searches = Subscriptions.LookupSearches(tasks).ToArray();

        // Assert
        Assert.Equal((2, "key in (ACS-101"), (searches.Length, searches[1].Jql[..15]));
    }

    [Fact]
    public void GitHubFeeds_WhenAssignedToMe_ThenSearchesBothIssuesAndPullRequests()
    {
        // Act
        var assigned = Subscriptions.GitHubFeeds.Single(feed => feed.Key == "assigned");

        // Assert
        Assert.Equal(["search_issues", "search_pull_requests"], assigned.Tools);
    }

    [Fact]
    public void Merged_WhenAKnownBoardWasRenamed_ThenKeepsItsConfigurationWithTheNewName()
    {
        // Arrange
        var known = Board(12, "10200", ("Test", ["7"]));

        // Act
        var board = Assert.Single(Subscriptions.Merged([new JiraBoard(12, "Nyt navn", Site)], [known]));

        // Assert
        Assert.Equal(("Nyt navn", "10200"), (board.Name, board.FilterId));
    }

    [Fact]
    public void Slots_WhenColumnsAndGitHubFeedsAreChosen_ThenListsThemInBoardAndFeedOrder()
    {
        // Arrange
        var subscriptions = NewSubscriptions();
        var board = Board(12, "10200", ("Under Review", ["4"]), ("Test", ["7"]), ("Done", ["9"]));
        subscriptions.Settings = SubscriptionSettings.Empty with { JiraBoards = [board], ChosenBoards = [board.Key], JiraColumns = ["Test", "Under Review"], GitHub = ["authored", "review"] };

        // Act
        var slots = subscriptions.Slots;

        // Assert
        Assert.Equal([("Jira", "Under Review"), ("Jira", "Test"), ("GitHub", "Review anmodet af mig"), ("GitHub", "Mine åbne PR'er")], slots);
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

    static string Url(string key) => $"{Site}/browse/{key}";

    static IssueRef Ref(string key, IssueKind kind) => new(key, "", kind, Url(key));

    static FeedItem Issue(string key, IssueKind kind, IssueRef? parent = null, IssueRef? epic = null) => new("Jira", "Test", key, "", Url(key), Kind: kind, Parent: parent, Epic: epic);

    static FeedItem Item(string id, string source = "Jira", string group = "Test") => new(source, group, id, "", $"https://example.com/{id}");

    static JiraBoard Board(int id, string? filter, params (string Name, string[] Statuses)[] columns) =>
        new(id, $"Board {id}", Site) { FilterId = filter, Columns = [.. columns.Select(column => new JiraColumn(column.Name, column.Statuses))] };
}
