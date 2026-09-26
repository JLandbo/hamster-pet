using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;

namespace Hamster;

public sealed class MarkdownConverter : IValueConverter
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseAutoLinks().Build();
    public static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");
    static readonly Thickness Spacing = new(0, 8, 0, 0);
    const string Shade = "Edge";
    const string Emphasis = "Text";
    const string Subtle = "Muted";
    const string Icons = "IconFont";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Render((string)value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument();
        AddBlocks(document.Blocks, Markdown.Parse(markdown, Pipeline));
        return document;
    }

    static Block ToBlock(Markdig.Syntax.Block block) => block switch
    {
        HeadingBlock heading => Themed(new Paragraph(Span(heading.Inline, new Bold())), TextElement.ForegroundProperty, Emphasis),
        ParagraphBlock paragraph => new Paragraph(Span(paragraph.Inline, new Span())),
        CodeBlock code => Code(code.Lines.ToString()),
        ListBlock list => List(list),
        Markdig.Extensions.Tables.Table table => Table(table),
        QuoteBlock quote => Themed(Section(quote, new Section { BorderThickness = new(3, 0, 0, 0), Padding = new(8, 0, 0, 0) }),
            Block.BorderBrushProperty, Shade),
        ThematicBreakBlock => Themed(new Paragraph { BorderThickness = new(0, 0, 0, 1) }, Block.BorderBrushProperty, Shade),
        LeafBlock leaf => new Paragraph(new Run(leaf.Lines.ToString())),
        ContainerBlock container => Section(container, new Section()),
        _ => new Paragraph(),
    };

    static void AddBlocks(BlockCollection target, ContainerBlock source)
    {
        foreach (var block in source.Select(ToBlock))
        {
            block.Margin = target.Count == 0 ? new(0) : Spacing;
            target.Add(block);
        }
    }

    static Section Code(string text)
    {
        var icon = new Run(ClipboardText.CopyIcon);
        var copy = Themed(Themed(new Hyperlink(icon) { TextDecorations = null, ToolTip = "Kopiér" }, TextElement.ForegroundProperty, Subtle),
            TextElement.FontFamilyProperty, Icons);
        copy.Click += (_, _) => ClipboardText.Copy(text, glyph => icon.Text = glyph);
        return Themed(new Section
        {
            Padding = new(8, 4, 8, 6),
            Blocks =
            {
                new Paragraph(copy) { TextAlignment = TextAlignment.Right, FontSize = 11, Margin = new(0) },
                new Paragraph(new Run(text)) { FontFamily = CodeFont, FontSize = 12, Margin = new(0) },
            },
        }, TextElement.BackgroundProperty, Shade);
    }

    static Section Section(ContainerBlock blocks, Section section)
    {
        AddBlocks(section.Blocks, blocks);
        return section;
    }

    static List List(ListBlock source)
    {
        var list = new List
        {
            MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            StartIndex = int.TryParse(source.OrderedStart, out var start) && start > 0 ? start : 1,
            Padding = new(22, 0, 0, 0),
        };
        foreach (var item in source.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            AddBlocks(listItem.Blocks, item);
            list.ListItems.Add(listItem);
        }
        return list;
    }

    static Table Table(Markdig.Extensions.Tables.Table source)
    {
        var rows = new TableRowGroup();
        foreach (var row in source.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            var tableRow = row.IsHeader ? Themed(new TableRow { FontWeight = FontWeights.SemiBold }, TextElement.ForegroundProperty, Emphasis) : new TableRow();
            foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>())
            {
                var tableCell = Themed(new TableCell { Padding = new(4, 3, 4, 3), BorderThickness = new(0, 0, 0, 1) }, TableCell.BorderBrushProperty, Shade);
                AddBlocks(tableCell.Blocks, cell);
                tableRow.Cells.Add(tableCell);
            }
            rows.Rows.Add(tableRow);
        }
        var table = new Table { CellSpacing = 0, RowGroups = { rows } };
        var columns = rows.Rows.SelectMany(row => row.Cells.Index()).GroupBy(cell => cell.Index, cell => LongestWord(cell.Item));
        foreach (var column in columns)
            table.Columns.Add(new TableColumn { Width = new(Math.Max(1, column.Max()), GridUnitType.Star) });
        return table;
    }

    static int LongestWord(TableCell cell) => new TextRange(cell.ContentStart, cell.ContentEnd).Text.Split().Max(word => word.Length);

    static Span Span(ContainerInline? source, Span span)
    {
        span.Inlines.AddRange(source?.Select(ToInline) ?? []);
        return span;
    }

    static Inline ToInline(Markdig.Syntax.Inlines.Inline inline) => inline switch
    {
        LiteralInline literal => new Run(literal.Content.ToString()),
        CodeInline code => Themed(new Run(code.Content) { FontFamily = CodeFont }, TextElement.BackgroundProperty, Shade),
        EmphasisInline { DelimiterCount: >= 2 } strong => Span(strong, new Bold()),
        EmphasisInline emphasis => Span(emphasis, new Italic()),
        LineBreakInline { IsHard: true } => new LineBreak(),
        LineBreakInline => new Run(" "),
        AutolinkInline link when WebUri(link.Url) is { } uri && !InsideLink(link) => new Hyperlink(new Run(link.Url)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri },
        AutolinkInline link => new Run(link.Url),
        HtmlEntityInline entity => new Run(entity.Transcoded.ToString()),
        HtmlInline html => new Run(html.Tag),
        LinkInline { IsImage: false } link when WebUri(link.Url) is { } uri && !InsideLink(link) => Span(link, new Hyperlink { NavigateUri = uri, ToolTip = uri.AbsoluteUri }),
        ContainerInline container => Span(container, new Span()),
        _ => new Run(inline.ToString()),
    };

    static bool InsideLink(Markdig.Syntax.Inlines.Inline inline) => inline.Parent?.ContainsParentOfType<LinkInline>() == true;

    static Uri? WebUri(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    static T Themed<T>(T element, DependencyProperty property, string resourceKey) where T : FrameworkContentElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
