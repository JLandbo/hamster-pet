using System.Windows;
using System.Windows.Documents;

namespace Hamster.Tests;

public class MarkdownConverterTests
{
    [Fact]
    public void Render_WhenPlainText_ThenOneParagraphWithTheText()
    {
        // Act
        var document = MarkdownConverter.Render("Hej med dig");

        // Assert
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
        Assert.Equal("Hej med dig", Text(paragraph));
    }

    [Fact]
    public void Render_WhenFencedCode_ThenMonospaceParagraphWithTheCode()
    {
        // Act
        var document = MarkdownConverter.Render("```cs\nvar x = 1;\nx++;\n```");

        // Assert
        var code = Assert.IsType<Paragraph>(Assert.IsType<Section>(Assert.Single(document.Blocks)).Blocks.LastBlock);
        Assert.Equal("Cascadia Mono, Consolas", code.FontFamily.Source);
        Assert.Equal("var x = 1;\nx++;", Text(code).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Render_WhenFencedCode_ThenOffersToCopyIt()
    {
        // Act
        var document = MarkdownConverter.Render("```cs" + (char)10 + "var x = 1;" + (char)10 + "```");

        // Assert
        var header = Assert.IsType<Paragraph>(Assert.IsType<Section>(Assert.Single(document.Blocks)).Blocks.FirstBlock);
        Assert.IsType<Hyperlink>(Assert.Single(header.Inlines));
    }

    [Fact]
    public void Render_WhenPipeTable_ThenTableWithBoldHeaderAndCells()
    {
        // Act
        var document = MarkdownConverter.Render("| Navn | Alder |\n|---|---|\n| Hammy | 2 |");

        // Assert
        var rows = Assert.IsType<Table>(Assert.Single(document.Blocks)).RowGroups.Single().Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(FontWeights.SemiBold, rows[0].FontWeight);
        Assert.Equal(["Hammy", "2"], rows[1].Cells.Select(cell => Text(cell.Blocks.Single())));
    }

    [Fact]
    public void Render_WhenPipeTable_ThenColumnsShareTheWidthByLongestWord()
    {
        // Act
        var document = MarkdownConverter.Render("| Tid | Regnrisiko |\n|---|---|\n| 09 | 34 % |");

        // Assert
        var columns = Assert.IsType<Table>(Assert.Single(document.Blocks)).Columns;
        Assert.Equal([new GridLength(3, GridUnitType.Star), new GridLength(10, GridUnitType.Star)], columns.Select(column => column.Width));
    }

    [Fact]
    public void Render_WhenBoldAndInlineCode_ThenBoldAndMonospaceInlines()
    {
        // Act
        var document = MarkdownConverter.Render("**fed** og `kode`");

        // Assert
        var inlines = Assert.IsType<Span>(((Paragraph)document.Blocks.Single()).Inlines.Single()).Inlines.ToArray();
        Assert.Equal("fed", Text(Assert.IsType<Bold>(inlines[0])));
        Assert.Equal("Cascadia Mono, Consolas", Assert.IsType<Run>(inlines[^1]).FontFamily.Source);
    }

    [Fact]
    public void Render_WhenOrderedListStartsAtThree_ThenListStartsAtThree()
    {
        // Act
        var document = MarkdownConverter.Render("3. tre\n4. fire");

        // Assert
        var list = Assert.IsType<List>(Assert.Single(document.Blocks));
        Assert.Equal(3, list.StartIndex);
        Assert.Equal(2, list.ListItems.Count);
    }

    [Fact]
    public void Render_WhenOrderedListStartsAtZero_ThenStartsAtOne()
    {
        // Act
        var document = MarkdownConverter.Render("0. nul\n1. en");

        // Assert
        Assert.Equal(1, Assert.IsType<List>(Assert.Single(document.Blocks)).StartIndex);
    }

    [Fact]
    public void Render_WhenWebLink_ThenHyperlinkToTheUrl()
    {
        // Act
        var document = MarkdownConverter.Render("[Markdig](https://github.com/xoofx/markdig)");

        // Assert
        var link = Assert.IsType<Hyperlink>(Assert.IsType<Span>(((Paragraph)document.Blocks.Single()).Inlines.Single()).Inlines.Single());
        Assert.Equal((new Uri("https://github.com/xoofx/markdig"), "Markdig"), (link.NavigateUri, Text(link)));
    }

    [Theory]
    [InlineData("Se [<https://example.com>](https://example.com)")]
    [InlineData("[**<https://a.example.com>**](https://example.com)")]
    [InlineData("[![[a](https://a.example.com)](https://example.com/i.png)](https://example.com)")]
    public void Render_WhenALinkIsInsideALink_ThenOnlyTheOuterIsAHyperlink(string markdown)
    {
        // Act
        var document = MarkdownConverter.Render(markdown);

        // Assert
        Assert.Equal(new Uri("https://example.com"), Assert.Single(Hyperlinks(document)).NavigateUri);
    }

    [Theory]
    [InlineData("<https://example.com>")]
    [InlineData("Se https://example.com her")]
    public void Render_WhenUrlInText_ThenHyperlinkToIt(string markdown)
    {
        // Act
        var document = MarkdownConverter.Render(markdown);

        // Assert
        var inlines = Assert.IsType<Span>(((Paragraph)document.Blocks.Single()).Inlines.Single()).Inlines;
        Assert.Equal(new Uri("https://example.com"), Assert.Single(inlines.OfType<Hyperlink>()).NavigateUri);
    }

    [Theory]
    [InlineData("[noter](noter.md)")]
    [InlineData("[noter](file:///C:/noter.md)")]
    public void Render_WhenLinkIsNotAWebAddress_ThenOnlyItsText(string markdown)
    {
        // Act
        var document = MarkdownConverter.Render(markdown);

        // Assert
        var inline = Assert.IsType<Span>(((Paragraph)document.Blocks.Single()).Inlines.Single()).Inlines.Single();
        Assert.Equal((false, "noter"), (inline is Hyperlink, Text(inline)));
    }

    static string Text(TextElement element) => new TextRange(element.ContentStart, element.ContentEnd).Text;

    static IEnumerable<Hyperlink> Hyperlinks(DependencyObject element) =>
        LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(child => child is Hyperlink link ? Hyperlinks(child).Prepend(link) : Hyperlinks(child));
}
