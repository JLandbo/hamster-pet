using System.Text.Json.Serialization;
using Hamster.Core.Languages;

namespace Hamster.Core.Feeds;

public sealed record JiraColumn(string Name, IReadOnlyList<string> StatusIds);

public sealed record JiraBoard(int Id, string Name, string Site)
{
    public string? FilterId { get; init; }
    public IReadOnlyList<JiraColumn> Columns { get; init; } = [];

    [JsonIgnore]
    public string Key => $"{Site}#{Id}";
}

public sealed record GitHubFeed(string Key, string Query, IReadOnlyList<string> Tools)
{
    public string Title => Key switch
    {
        "review" => Strings.Of("Tasks.GitHubReview"),
        "authored" => Strings.Of("Tasks.GitHubAuthored"),
        "assigned" => Strings.Of("Tasks.GitHubAssigned"),
        _ => Key,
    };
}

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
