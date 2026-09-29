using System.Globalization;
using System.Text.Json.Nodes;

namespace Hamster.Tests;

public sealed class SubscriptionsTests : IDisposable
{
    const string _site = "https://firma.atlassian.net";
    readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    Subscriptions NewSubscriptions(ClaudeSettings? settings = null) => new(
        new Connectors(new ClaudeClient(_folder, Path.Combine(_folder, "i.txt"), new JsonFile<ClaudeSettings>(Path.Combine(_folder, "settings.json"), settings ?? ClaudeSettings.Default)), () => { }, () => false),
        new ClaudeFetcher(_folder),
        new WebSession(_folder),
        new JsonFile<SubscriptionSettings>(Path.Combine(_folder, "subscriptions.json"), SubscriptionSettings.Empty));

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
        var jql = FeedApi.BoardJql(board, ["Test", "Review", "Findes ikke her"]);

        // Assert
        Assert.Equal("filter = 10200 AND assignee = currentUser() AND status in (10012, 10014, 10019) ORDER BY updated DESC", jql);
    }

    [Theory]
    [InlineData("10200", "Done")]
    [InlineData(null, "Test")]
    public void BoardJql_WhenNothingOnTheBoardIsChosen_ThenSkipsTheBoard(string? filter, string column)
    {
        // Act
        var jql = FeedApi.BoardJql(Board(12, filter, ("Test", ["10012"])), [column]);

        // Assert
        Assert.Null(jql);
    }

    [Fact]
    public void BoardPage_WhenJiraListsBoards_ThenNamesThemWithTheirProject()
    {
        // Arrange
        const string json = """{"isLast":false,"values":[{"id":12,"name":"ACS board","location":{"projectKey":"ACS"}},{"id":13,"name":"Team"}]}""";

        // Act
        var (boards, last) = FeedApi.BoardPage(json, _site);

        // Assert
        Assert.Equal([new JiraBoard(12, "ACS board (ACS)", _site), new JiraBoard(13, "Team", _site)], boards);
        Assert.False(last);
    }

    [Fact]
    public void Configured_WhenJiraSendsTheBoardSetup_ThenReadsItsFilterAndColumns()
    {
        // Arrange
        const string json = """{"filter":{"id":"10200"},"columnConfig":{"columns":[{"name":"To Do","statuses":[{"id":"1"}]},{"name":"Test","statuses":[{"id":"10012"},{"id":"10020"}]}]}}""";

        // Act
        var board = FeedApi.Configured(new JiraBoard(12, "ACS board", _site), json);

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
        var accepted = FeedApi.IsJiraSite(url);

        // Assert
        Assert.Equal(expected, accepted);
    }

    [Theory]
    [InlineData("""[{"id":"f6909ca2","url":"https://firma.atlassian.net","name":"firma"}]""")]
    [InlineData("""{"data":{"resources":[{"cloudId":"f6909ca2","url":"https://firma.atlassian.net"}]}}""")]
    public void JiraSites_WhenEitherAtlassianServerAnswers_ThenReadsTheSites(string json)
    {
        // Act
        var sites = FeedApi.JiraSites(json);

        // Assert
        Assert.Equal([_site], sites);
    }

    [Theory]
    [InlineData("""{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"10012","name":"Test"}}}]}""")]
    [InlineData("""{"issues":{"nodes":[{"key":"ACS-1","self":"https://api.atlassian.com/ex/jira/f6909ca2/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"10012","name":"Test"}}}]}}""")]
    public void JiraItems_WhenIssuesAreFound_ThenLinksToTheirSiteUnderTheirColumn(string json)
    {
        // Act
        var items = FeedApi.JiraItems(json, _site, new Dictionary<string, string> { ["10012"] = "Under Test in DEV" });

        // Assert
        Assert.Equal([("Jira", "Under Test in DEV", "ACS-1", "Test af login", "https://firma.atlassian.net/browse/ACS-1")], items.Select(item => (item.Source, item.Group, item.Id, item.Title, item.Url)));
    }

    [Fact]
    public void JiraItems_WhenTheIssueHasAnUpdateTime_ThenKeepsIt()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","updated":"2026-09-26T10:42:00.000+0200"}}]}""";

        // Act
        var item = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>()));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 42, 0, TimeSpan.FromHours(2)), item.Updated);
    }

    [Fact]
    public void JiraItems_WhenTheStatusIsInNoKnownColumn_ThenUsesTheStatusName()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test af login","status":{"id":"3","name":"In Progress"}}}]}""";

        // Act
        var item = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>()));

        // Assert
        Assert.Equal("In Progress", item.Group);
    }

    [Fact]
    public void SearchUrl_WhenGivenJql_ThenAsksJiraForMyIssuesWithTheirDetails()
    {
        // Act
        var url = FeedApi.SearchUrl(_site, "status in (\"Test\")", ["customfield_10020"]);

        // Assert
        Assert.Equal("https://firma.atlassian.net/rest/api/3/search/jql?jql=status%20in%20%28%22Test%22%29&fields=summary,status,updated,issuetype,parent,priority,labels,components,created,description,duedate,timetracking,customfield_10020&maxResults=100", url);
    }

    [Fact]
    public void GitHubItems_WhenPullRequestsAreFound_ThenKeepsTheirRepositoryAndUpdateTime()
    {
        // Arrange
        const string json = """{"items":[{"html_url":"https://github.com/AciesDK/app/pull/11","number":11,"repository_url":"https://api.github.com/repos/AciesDK/app","title":"Ny knap","updated_at":"2026-09-26T08:42:00Z"}],"total_count":1}""";

        // Act
        var items = FeedApi.GitHubItems(json, "Mine åbne PR'er");

        // Assert
        Assert.Equal([new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", new DateTimeOffset(2026, 9, 26, 8, 42, 0, TimeSpan.Zero))], items);
    }

    [Fact]
    public void GitHubItems_WhenThePullRequestIsADraft_ThenMarksIt()
    {
        // Arrange
        const string json = """{"items":[{"html_url":"https://github.com/AciesDK/app/pull/11","number":11,"title":"Ny knap","draft":true}]}""";

        // Act
        var item = Assert.Single(FeedApi.GitHubItems(json, "Mine åbne PR'er"));

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
        var settings = SubscriptionSettings.Empty with { JiraBoards = [new JiraBoard(12, "ACS board", _site)] };
        var configured = Board(12, "10200", ("Test", ["10012"]));

        // Act
        var changed = settings.WithBoard(configured, true);

        // Assert
        Assert.Equal(("10200", configured.Key), (Assert.Single(changed.JiraBoards).FilterId, Assert.Single(changed.ChosenBoards)));
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
        var items = FeedApi.JiraItems(json, _site, new Dictionary<string, string>()).Select(item => (item.Id, item.Kind, item.Parent));

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
        var epics = FeedApi.WithEpics(items, new Dictionary<string, IssueRef?>()).Select(item => item.Epic);

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
        var item = Assert.Single(FeedApi.WithEpics(items, new Dictionary<string, IssueRef?> { [Url("ACS-2")] = epic }));

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
        var tasks = FeedApi.TasksWithUnknownEpic(items);

        // Assert
        Assert.Equal([task], tasks);
    }

    [Fact]
    public void SearchUrl_WhenAskedForTheNextPage_ThenPassesTheToken()
    {
        // Act
        var url = FeedApi.SearchUrl(_site, "assignee = currentUser()", ["customfield_10020"], "abc=");

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
        var token = FeedApi.NextPageToken(json);

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
        var pages = await FeedApi.PagesAsync(Get, _site, "assignee = currentUser()", ["customfield_10020"]);

        // Assert
        Assert.Equal((2, 2), (pages.Count, asked.Count));
    }

    [Fact]
    public void NextJiraPage_WhenThereIsAnotherPage_ThenAsksForItWithTheSameSearch()
    {
        // Arrange
        var call = new ToolCall("searchJiraIssuesUsingJql", new JsonObject { ["jql"] = "x" });

        // Act
        var next = FeedApi.NextJiraPage(call, """{"issues":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"c2"}}}""");

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
        var next = FeedApi.NextGitHubPage(call, json);

        // Assert
        Assert.Equal(expected, (int?)next?.Arguments["page"]);
    }

    [Fact]
    public void LookupSearches_WhenThereAreManyTasks_ThenAsksForAHundredAtATime()
    {
        // Arrange
        var tasks = Enumerable.Range(1, 150).Select(number => Ref($"ACS-{number}", IssueKind.Task));

        // Act
        var searches = FeedApi.LookupSearches(tasks).ToArray();

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
        var board = Assert.Single(FeedApi.Merged([new JiraBoard(12, "Nyt navn", _site)], [known]));

        // Assert
        Assert.Equal(("Nyt navn", "10200"), (board.Name, board.FilterId));
    }

    [Fact]
    public void JiraItems_WhenTheIssueHasDetails_ThenShowsTheActiveSprintItsTagsAndDescription()
    {
        // Arrange
        const string json = """
            {"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{
              "summary":"Test","status":{"name":"In Progress"},"priority":{"name":"High"},"assignee":{"displayName":"Jacob Skov"},"created":"2026-08-27T13:55:24.050+0200",
              "labels":["azure","bi"],"components":[{"name":"Cloud"}],"description":"Vi skal **teste**\n\n* det hele",
              "customfield_10026":3.0,"customfield_10066":{"displayName":"Anna Reviewer"},"customfield_10061":{"displayName":"Bo Tester"},"customfield_10068":{"value":"Idealcombi"},
              "timetracking":{"originalEstimate":"2d","remainingEstimate":"1d 4h","timeSpent":"4h"},
              "customfield_10020":[{"name":"Sprint 2","state":"active","boardId":8,"endDate":"2026-10-01T10:00:00.000Z"},{"name":"Sprint 1","state":"closed","boardId":8}]}}]}
            """;
        var fields = new Dictionary<string, string> { ["Sprint"] = "customfield_10020", ["Story Points"] = "customfield_10026", ["Reviewer"] = "customfield_10066", ["Tester"] = "customfield_10061", ["Kundenavn"] = "customfield_10068" };

        // Act
        var details = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>(), fields)).Details!;

        // Assert
        Assert.Equal($"Sprint 2 · slutter {new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("d. MMM", CultureInfo.CurrentCulture)}", details.Sprint);
        Assert.Equal(["Status In Progress", "Prioritet High", "Story Points 3", "Reviewer Anna Reviewer", "Tester Bo Tester", "Kundenavn Idealcombi",
            "Tidsregistrering 2d estimeret · 1d 4h tilbage · 4h brugt", "Etiketter azure, bi", "Komponent Cloud",
            $"Oprettet {new DateTimeOffset(2026, 8, 27, 13, 55, 24, TimeSpan.FromHours(2)).ToLocalTime().ToString("d. MMM", CultureInfo.CurrentCulture)}"],
            details.Tags.Select(tag => $"{tag.Key} {tag.Value}"));
        Assert.Equal("Vi skal **teste**\n\n* det hele", details.Description);
    }

    [Fact]
    public void JiraItems_WhenTheDescriptionIsLong_ThenKeepsItWhole()
    {
        // Arrange
        var words = string.Join(' ', Enumerable.Repeat("ord", 5000));
        var json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":"WORDS"}}]}""".Replace("WORDS", words);

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal(words, description);
    }

    [Fact]
    public void JiraItems_WhenTheDescriptionIsAJiraDocument_ThenTurnsItIntoMarkdown()
    {
        // Arrange
        const string json = """
            {"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":{"type":"doc","content":[
              {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Baggrund"}]},
              {"type":"paragraph","content":[{"type":"text","text":"Ring til "},{"type":"mention","attrs":{"text":"@Jacob"}},{"type":"text","text":" om "},
                {"type":"text","text":"API","marks":[{"type":"strong"}]},{"type":"text","text":"-kaldet, se "},{"type":"inlineCard","attrs":{"url":"https://x.atlassian.net/browse/ACS-2"}},
                {"type":"text","text":" og "},{"type":"text","text":"Foo()","marks":[{"type":"code"}]},{"type":"text","text":"."}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"punkt 1"}]}]},
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"punkt 2"}]}]}]}]}}}]}
            """;

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("## Baggrund\n\nRing til @Jacob om **API**\\-kaldet\\, se https://x.atlassian.net/browse/ACS-2 og `Foo()`\\.\n\n- punkt 1\n- punkt 2", description);
    }

    [Fact]
    public void JiraItems_WhenTextHasMarkdownCharacters_ThenShowsThemAsWritten()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"__init__ og 2*3*4"}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal(@"\_\_init\_\_ og 2\*3\*4", description);
    }

    [Fact]
    public void JiraItems_WhenInlineCodeHasBackticks_ThenWrapsItInMoreBackticks()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"`x`","marks":[{"type":"code"}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("`` `x` ``", description);
    }

    [Fact]
    public void JiraItems_WhenACodeBlockHasAFence_ThenUsesALongerFence()
    {
        // Arrange
        const string content = """{"type":"codeBlock","content":[{"type":"text","text":"```\nx\n```"}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("````\n```\nx\n```\n````", description);
    }

    [Fact]
    public void JiraItems_WhenACodeBlockHasMarkdownCharacters_ThenKeepsThemAsWritten()
    {
        // Arrange
        const string content = """{"type":"codeBlock","content":[{"type":"text","text":"a*b_c"}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("```\na*b_c\n```", description);
    }

    [Fact]
    public void JiraItems_WhenTheJiraDocumentHasALineBreak_ThenKeepsItAsAHardBreak()
    {
        // Arrange
        const string json = """
            {"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":{"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","text":"linje 1"},{"type":"hardBreak"},{"type":"text","text":"linje 2"}]}]}}}]}
            """;

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("linje 1  \nlinje 2", description);
    }

    [Fact]
    public void JiraItems_WhenMarkedTextHasSpacesAtItsEdges_ThenKeepsThemOutsideTheMarks()
    {
        // Arrange
        const string json = """
            {"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":{"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","text":"Ring om"},{"type":"text","text":" API ","marks":[{"type":"strong"}]},{"type":"text","text":"kaldet"}]}]}}}]}
            """;

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("Ring om **API** kaldet", description);
    }

    [Fact]
    public void JiraItems_WhenTheDescriptionHasImages_ThenMarksThemWithoutCountingTheirAddresses()
    {
        // Arrange
        var json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":"Følgende:\n\n![](blob:https://media/?id=LONG)Problem i dag ![](blob:https://media/?id=LONG)"}}]}"""
            .Replace("LONG", new string('x', 1000));

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("Følgende:\n\n[Billede]Problem i dag [Billede]", description);
    }

    [Fact]
    public void JiraItems_WhenAListItemHoldsAList_ThenNestsIt()
    {
        // Arrange
        const string content = """{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Krav"}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]}]}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("- Krav\n  \n  - a", description);
    }

    [Fact]
    public void JiraItems_WhenAListItemHoldsCode_ThenKeepsTheWholeCodeInTheItem()
    {
        // Arrange
        const string content = """{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"codeBlock","content":[{"type":"text","text":"var x = 1;\n\nvar y = 2;"}]}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("- ```\n  var x = 1;\n  \n  var y = 2;\n  ```", description);
    }

    [Fact]
    public void JiraItems_WhenAQuoteHasTwoParagraphs_ThenQuotesBoth()
    {
        // Arrange
        const string content = """{"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]},{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("> a\n> \n> b", description);
    }

    [Fact]
    public void JiraItems_WhenCodeIsLinked_ThenTheLinkWrapsTheCode()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"Foo()","marks":[{"type":"link","attrs":{"href":"https://x.dk"}},{"type":"code"}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("[`Foo()`](https://x.dk)", description);
    }

    [Fact]
    public void JiraItems_WhenACardIsFollowedByText_ThenKeepsThemApart()
    {
        // Arrange
        const string content = """{"type":"blockCard","attrs":{"url":"https://x.atlassian.net/browse/ACS-2"}},{"type":"paragraph","content":[{"type":"text","text":"Se den"}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("https://x.atlassian.net/browse/ACS-2\n\nSe den", description);
    }

    [Fact]
    public void JiraItems_WhenTheDocumentHasATaskList_ThenListsTheTasks()
    {
        // Arrange
        const string content = """{"type":"taskList","content":[{"type":"taskItem","content":[{"type":"text","text":"a"}]},{"type":"taskItem","content":[{"type":"text","text":"b"}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("- a\n- b", description);
    }

    [Fact]
    public void JiraItems_WhenTheDocumentHasADate_ThenShowsTheDayJiraShows()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"Senest "},{"type":"date","attrs":{"timestamp":"1790208000000"}}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal($"Senest {new DateTime(2026, 9, 24).ToString("d. MMM", CultureInfo.CurrentCulture)}", description);
    }

    [Fact]
    public void JiraItems_WhenAnOrderedItemHoldsAList_ThenIndentsItByTheMarkerWidth()
    {
        // Arrange
        const string content = """{"type":"orderedList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Krav"}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]}]}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("1. Krav\n   \n   - a", description);
    }

    [Fact]
    public void JiraItems_WhenAnOrderedListContinues_ThenStartsAtItsNumber()
    {
        // Arrange
        const string content = """{"type":"orderedList","attrs":{"order":3},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"c"}]}]},{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"d"}]}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("3. c\n4. d", description);
    }

    [Fact]
    public void JiraItems_WhenATaskListHoldsATaskList_ThenKeepsTheTasksApart()
    {
        // Arrange
        const string content = """{"type":"taskList","content":[{"type":"taskItem","content":[{"type":"text","text":"a"}]},{"type":"taskList","content":[{"type":"taskItem","content":[{"type":"text","text":"b"}]},{"type":"taskItem","content":[{"type":"text","text":"c"}]}]}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("- a\n  - b\n  - c", description);
    }

    [Fact]
    public void JiraItems_WhenTheDateIsOutOfRange_ThenLeavesItOut()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"Senest"},{"type":"date","attrs":{"timestamp":"99999999999999999"}}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("Senest", description);
    }

    [Fact]
    public void JiraItems_WhenTheDescriptionHasAnAttachedImage_ThenMarksIt()
    {
        // Arrange
        const string content = """{"type":"paragraph","content":[{"type":"text","text":"Se her"}]},{"type":"mediaSingle","content":[{"type":"media","attrs":{"type":"file","id":"abc","collection":"x"}}]}""";

        // Act
        var description = Described(content);

        // Assert
        Assert.Equal("Se her\n\n[Billede]", description);
    }

    [Fact]
    public void JiraItems_WhenAnImageIsInsideALink_ThenMarksTheImage()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":"Før [![x](a)](b) efter"}}]}""";

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("Før [[Billede]](b) efter", description);
    }

    [Fact]
    public void JiraItems_WhenAnImageHoldsAnImage_ThenMarksOnlyTheOuterImage()
    {
        // Arrange
        const string json = """{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":"Før ![a ![b](x)](y) efter"}}]}""";

        // Act
        var description = Assert.Single(FeedApi.JiraItems(json, _site, new Dictionary<string, string>())).Details!.Description;

        // Assert
        Assert.Equal("Før [Billede] efter", description);
    }

    [Fact]
    public void JiraSearch_WhenBuilt_ThenAsksForAllDetailsAsMarkdownAHundredAtATime()
    {
        // Act
        var call = FeedApi.JiraSearch(_site, "assignee = currentUser()", ["customfield_10020", "customfield_10026"]);

        // Assert
        Assert.Equal(("markdown", 100, "customfield_10026"), ((string?)call.Arguments["responseContentFormat"], (int?)call.Arguments["maxResults"], (string?)call.Arguments["fields"]!.AsArray()[^1]));
        Assert.Contains("description", call.Arguments["fields"]!.AsArray().Select(field => (string?)field));
    }

    [Fact]
    public void JiraFieldsOf_WhenJiraListsItsFields_ThenFindsSprintAndTheChosenFields()
    {
        // Arrange
        const string json = """
            [{"id":"summary","name":"Summary","schema":{"system":"summary"}},
             {"id":"customfield_10020","name":"Sprint","schema":{"custom":"com.pyxis.greenhopper.jira:gh-sprint"}},
             {"id":"customfield_10026","name":"Story Points"},{"id":"customfield_10066","name":"Reviewer"},{"id":"customfield_10061","name":"Tester"},{"id":"customfield_10068","name":"Kundenavn"}]
            """;

        // Act
        var fields = FeedApi.JiraFieldsOf(json);

        // Assert
        Assert.Equal(["Sprint=customfield_10020", "Story Points=customfield_10026", "Reviewer=customfield_10066", "Tester=customfield_10061", "Kundenavn=customfield_10068"],
            fields.Select(field => $"{field.Key}={field.Value}"));
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
        Assert.Equal([("Jira", "Under Review"), ("Jira", "Test"), ("GitHub", "review"), ("GitHub", "authored")], slots);
    }

    static string Described(string content)
    {
        return Assert.Single(FeedApi.JiraItems("""{"issues":[{"key":"ACS-1","self":"https://firma.atlassian.net/rest/api/3/issue/1","fields":{"summary":"Test","description":{"type":"doc","content":[CONTENT]}}}]}""".Replace("CONTENT", content),
            _site, new Dictionary<string, string>())).Details!.Description;
    }

    static string Url(string key) => $"{_site}/browse/{key}";

    static IssueRef Ref(string key, IssueKind kind) => new(key, "", kind, Url(key));

    static FeedItem Issue(string key, IssueKind kind, IssueRef? parent = null, IssueRef? epic = null) => new("Jira", "Test", key, "", Url(key), Kind: kind, Parent: parent, Epic: epic);

    static JiraBoard Board(int id, string? filter, params (string Name, string[] Statuses)[] columns)
    {
        return new(id, $"Board {id}", _site) { FilterId = filter, Columns = [.. columns.Select(column => new JiraColumn(column.Name, column.Statuses))] };
    }
}
