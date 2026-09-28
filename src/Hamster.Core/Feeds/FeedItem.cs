using System.Globalization;
using Hamster.Core.Languages;

namespace Hamster.Core.Feeds;

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
        string.Join(" · ", new[] { Repository, Draft ? Strings.Of("Tasks.Draft") : "", Updated is { } updated ? Ago(now - updated) : "" }.Where(part => part.Length > 0));

    public static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => Strings.Of("Tasks.Now"),
        { TotalHours: < 1 } => Strings.Format("Tasks.MinutesAgo", (int)elapsed.TotalMinutes),
        { TotalDays: < 1 } => Strings.Format("Tasks.HoursAgo", (int)elapsed.TotalHours),
        { TotalDays: < 2 } => Strings.Of("Tasks.Yesterday"),
        _ => Strings.Format("Tasks.DaysAgo", (int)elapsed.TotalDays),
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
