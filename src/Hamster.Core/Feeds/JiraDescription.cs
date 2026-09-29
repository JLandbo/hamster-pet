using System.Globalization;
using System.Text.Json.Nodes;
using Markdig;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Hamster.Core.Languages;

namespace Hamster.Core.Feeds;

static class JiraDescription
{
    static readonly MarkdownPipeline _sourcePositions = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    public static string Of(JsonNode? description) => WithImagesMarked(description switch
    {
        JsonValue value when value.TryGetValue(out string? text) => text,
        JsonObject document => MarkdownOf(document),
        _ => "",
    }).Trim();

    static string WithImagesMarked(string markdown) => Markdown.Parse(markdown, _sourcePositions).Descendants<LinkInline>().Where(link => link.IsImage && !InsideImage(link)).Reverse()
            .Aggregate(markdown, (text, image) => text.Remove(image.Span.Start, image.Span.Length).Insert(image.Span.Start, ImageMark));

    static string ImageMark => Strings.Of("Tasks.Image");

    static bool InsideImage(Inline inline) => inline.Parent is { } parent && (parent is LinkInline { IsImage: true } || InsideImage(parent));

    static string MarkdownOf(JsonNode? node) => node is not JsonObject item ? "" : (string?)item["type"] switch
    {
        "text" => MarkedOf(item),
        "hardBreak" => "  \n",
        "paragraph" => $"{ChildrenOf(item)}\n\n",
        "heading" => $"{new string('#', Math.Clamp((int?)item["attrs"]?["level"] ?? 1, 1, 6))} {ChildrenOf(item)}\n\n",
        "bulletList" or "taskList" or "decisionList" => $"{string.Concat(ItemsOf(item).Select(text => $"{Nested("- ", text)}\n"))}\n",
        "orderedList" => $"{string.Concat(ItemsOf(item).Select((text, index) => $"{Nested($"{StartOf(item) + index}. ", text)}\n"))}\n",
        "codeBlock" => CodeBlockOf(item),
        "blockquote" => $"> {ChildrenOf(item).Trim().Replace("\n", "\n> ")}\n\n",
        "rule" => "---\n\n",
        "mediaSingle" => $"{ImageMark}\n\n",
        "inlineCard" => (string?)item["attrs"]?["url"] ?? "",
        "blockCard" or "embedCard" => $"{(string?)item["attrs"]?["url"]}\n\n",
        "date" => DayOf(item["attrs"]?["timestamp"]),
        "mention" or "emoji" or "status" => (string?)item["attrs"]?["text"] ?? (string?)item["attrs"]?["shortName"] ?? "",
        _ => ChildrenOf(item),
    };

    static int StartOf(JsonObject list) => int.TryParse(list["attrs"]?["order"]?.ToString(), out var order) ? order : 1;

    static string DayOf(JsonNode? timestamp)
    {
        return long.TryParse(timestamp?.ToString(), out var stamp) && stamp >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && stamp <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(stamp).ToString("d. MMM", CultureInfo.CurrentCulture)
            : "";
    }

    static string Nested(string marker, string text) => $"{marker}{text.Replace("\n", $"\n{new string(' ', marker.Length)}")}";

    static string MarkedOf(JsonObject item)
    {
        var text = (string?)item["text"] ?? "";
        var (start, end) = (text.Length - text.TrimStart().Length, text.TrimEnd().Length);
        if (start >= end)
        {
            return text;
        }
        var marks = (item["marks"] as JsonArray ?? []).OfType<JsonObject>().OrderBy(mark => (string?)mark["type"] == "link").ToArray();
        var core = marks.Any(mark => (string?)mark["type"] == "code") ? text[start..end] : Escaped(text[start..end]);
        var marked = marks.Aggregate(core, (inner, mark) => (string?)mark["type"] switch
        {
            "strong" => $"**{inner}**",
            "em" => $"*{inner}*",
            "code" => CodeSpan(inner),
            "link" => $"[{inner}]({(string?)mark["attrs"]?["href"]})",
            _ => inner,
        });
        return $"{text[..start]}{marked}{text[end..]}";
    }

    static string Escaped(string text) => string.Concat(text.Select(character => CharHelper.IsAsciiPunctuation(character) ? $"\\{character}" : $"{character}"));

    static string CodeSpan(string code)
    {
        var ticks = Backticks(code, 1);
        return code.StartsWith('`') || code.EndsWith('`') ? $"{ticks} {code} {ticks}" : $"{ticks}{code}{ticks}";
    }

    static string CodeBlockOf(JsonObject item)
    {
        var code = string.Concat((item["content"] as JsonArray ?? []).Select(node => (string?)node?["text"]));
        var fence = Backticks(code, 3);
        return $"{fence}\n{code}\n{fence}\n\n";
    }

    static string Backticks(string code, int least)
    {
        var longest = code.Aggregate((Longest: 0, Run: 0), (runs, character) => character == '`' ? (Math.Max(runs.Longest, runs.Run + 1), runs.Run + 1) : (runs.Longest, 0)).Longest;
        return new('`', Math.Max(least, longest + 1));
    }

    static string ChildrenOf(JsonObject item) => string.Concat((item["content"] as JsonArray ?? []).Select(MarkdownOf));

    static IEnumerable<string> ItemsOf(JsonObject list)
    {
        var items = new List<string>();
        foreach (var entry in (list["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var nested = (string?)entry["type"] == "taskList";
            if (nested && items.Count > 0)
            {
                items[^1] += $"\n{MarkdownOf(entry).Trim()}";
            }
            else
            {
                items.Add((nested ? MarkdownOf(entry) : ChildrenOf(entry)).Trim());
            }
        }
        return items;
    }
}
