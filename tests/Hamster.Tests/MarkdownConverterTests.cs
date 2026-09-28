using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hamster.Tests;

public class MarkdownConverterTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        MarkdownConverter.ShowWebImages = false;
        Directory.Delete(folder, true);
    }

    [Theory]
    [InlineData("graf.png")]
    [InlineData("GRAF.PNG")]
    [InlineData("graf.jpg")]
    [InlineData("graf.jpeg")]
    [InlineData("graf.gif")]
    [InlineData("graf.bmp")]
    [InlineData("graf.tif")]
    [InlineData("graf.tiff")]
    [InlineData("graf.ico")]
    public void Render_WhenTheAnswerShowsALocalImage_ThenShowsIt(string name) => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile(name, 40);

        // Act
        var document = Rendered($"![graf](<{file.Replace('\\', '/')}>)");

        // Assert
        Assert.Equal(40, Width(document));
    });

    [Theory]
    [InlineData(40, 20, 100, 50)]
    [InlineData(20, 40, 50, 100)]
    [InlineData(1200, 600, 349, 174.5)]
    [InlineData(100, 400, 80, 320)]
    public void Render_WhenTheImageIsShown_ThenItKeepsItsShapeWithinTheMinimumAndMaximum(int width, int height, double shownWidth, double shownHeight) => UiThread.Run(() =>
    {
        // Arrange
        var picture = Picture(Rendered($"![graf](<{ImageFile("graf.png", width, height)}>)"))!;

        // Act
        picture.Measure(new Size(349, double.PositiveInfinity));

        // Assert
        Assert.Equal(new Size(shownWidth, shownHeight), picture.DesiredSize);
    });

    [Fact]
    public void Render_WhenThereIsLittleRoom_ThenTheLongestSideStaysAtLeastAHundred() => UiThread.Run(() =>
    {
        // Arrange
        var picture = Picture(Rendered($"![graf](<{ImageFile("graf.png", 640, 480)}>)"))!;

        // Act
        var smallest = (picture.MinWidth, picture.MinHeight);

        // Assert
        Assert.Equal((100, 75), smallest);
    });

    [Fact]
    public void Render_WhenTheImageIsWebP_ThenShowsIt() => UiThread.Run(() =>
    {
        // Arrange
        var file = Path.Combine(folder, "graf.webp");
        File.WriteAllBytes(file, Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));

        // Act
        var document = Rendered($"![graf](<{file}>)");

        // Assert
        Assert.Equal(1, Width(document));
    });

    [Theory]
    [InlineData("skråstreger")]
    [InlineData("omvendte skråstreger")]
    [InlineData("filadresse")]
    public void Render_WhenThePathIsWrittenInAnotherWay_ThenShowsTheImage(string way) => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("Skærm billeder/graf.png", 40);
        var markdown = way switch
        {
            "skråstreger" => $"![graf](<{file.Replace('\\', '/')}>)",
            "omvendte skråstreger" => $"![graf](<{file}>)",
            _ => $"![graf]({new Uri(file).AbsoluteUri})",
        };

        // Act
        var document = Rendered(markdown);

        // Assert
        Assert.Equal(40, Width(document));
    });

    [Fact]
    public void Render_WhenImagesShareANameOrShowUpAmongOtherContent_ThenEachShowsItsOwnFile() => UiThread.Run(() =>
    {
        // Arrange
        var first = ImageFile("a/image.png", 10);
        var second = ImageFile("b/image.png", 20);
        var markdown = $"""
            # Overskrift med ![a](<{first}>)

            - punkt med ![b](<{second}>)

            | billede |
            |---|
            | ![a](<{first}>) |

            **fed ![b](<{second}>)**

            ```
            ![kode](<{first}>)
            ```
            """;

        // Act
        var document = Rendered(markdown);

        // Assert
        Assert.Equal([10, 20, 10, 20], Pictures(document).Select(picture => ((BitmapImage)picture.Source).PixelWidth));
    });

    [Fact]
    public void Render_WhenTheImageIsOnDisk_ThenItIsReadOffTheUiThread() => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("graf.png", 40);
        var ui = Environment.CurrentManagedThreadId;
        using var handedOver = new ManualResetEventSlim();
        Dispatcher.CurrentDispatcher.Hooks.OperationPosted += (_, _) =>
        {
            if (Environment.CurrentManagedThreadId != ui)
            {
                handedOver.Set();
            }
        };

        // Act
        var document = MarkdownConverter.Render($"![graf](<{file}>)");
        var shownAtOnce = Picture(document)!.Source is not null;
        var readWhileTheUiThreadWaited = handedOver.Wait(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal((false, true), (shownAtOnce, readWhileTheUiThreadWaited));
    });

    [Fact]
    public void Render_WhenTheFileIsMissing_ThenThePlaceholderBecomesItsText() => UiThread.Run(() =>
    {
        // Act
        var document = MarkdownConverter.Render($"![graf](<{Path.Combine(folder, "missing.png")}>)");
        var placeholderAtOnce = Picture(document) is not null;
        UiThread.Until(() => Picture(document) is null);

        // Assert
        Assert.Equal((true, "graf"), (placeholderAtOnce, DocumentText(document)));
    });

    [Fact]
    public void Render_WhenTheImageIsClicked_ThenItsLinkOpensTheFile() => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("graf.png", 40);
        var document = Rendered($"![graf](<{file}>)");
        var link = Assert.Single(Hyperlinks(document));
        var clicked = false;
        link.AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, _) => clicked = true), true);

        // Act
        Picture(document)!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });

        // Assert
        Assert.Equal((file, true), (link.NavigateUri.LocalPath, clicked));
    });

    [Fact]
    public void Render_WhenTheImageIsRightClicked_ThenCopyLinkGivesTheFile() => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("graf.png", 40);
        var picture = Picture(Rendered($"![graf](<{file}>)"))!;

        // Act
        var link = App.LinkAt(picture);

        // Assert
        Assert.Equal(file, link?.LocalPath);
    });

    [Fact]
    public void Render_WhenTheImageFileChanges_ThenOnlyNewAnswersShowTheNewVersion() => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("graf.png", 10);
        var markdown = $"![graf](<{file}>)";
        var before = Rendered(markdown);
        ImageFile("graf.png", 20);

        // Act
        var after = Rendered(markdown);

        // Assert
        Assert.Equal((10, 20), (Width(before), Width(after)));
    });

    [Theory]
    [InlineData("missing.png")]
    [InlineData("notes.txt")]
    [InlineData("broken.png")]
    [InlineData("noframes.gif")]
    public void Render_WhenTheFileIsNoImage_ThenShowsItsText(string name) => UiThread.Run(() =>
    {
        // Arrange
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "noter");
        File.WriteAllText(Path.Combine(folder, "broken.png"), "ikke et billede");
        File.WriteAllBytes(Path.Combine(folder, "noframes.gif"), [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0, 0x3B]);

        // Act
        var document = Rendered($"![graf](<{Path.Combine(folder, name)}>)");

        // Assert
        Assert.Equal((null, "graf"), (Picture(document), DocumentText(document)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_WhenTheImageIsOnANetworkShare_ThenShowsItsText(bool webImages)
    {
        // Arrange
        MarkdownConverter.ShowWebImages = webImages;

        // Act
        var document = MarkdownConverter.Render("![graf](file://server/share/graf.png)");

        // Assert
        Assert.Equal((null, "graf"), (Picture(document), DocumentText(document)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Render_WhenTheImageIsOnTheWeb_ThenShowsItOnlyWhenExternalImagesAreOn(bool webImages, bool shown) => UiThread.Run(() =>
    {
        // Arrange
        MarkdownConverter.ShowWebImages = webImages;

        // Act
        var document = MarkdownConverter.Render("![logo](http://localhost:1/logo.png)");

        // Assert
        Assert.Equal(shown, Picture(document) is not null);
    });

    [Fact]
    public void Render_WhenAWebImageLoads_ThenShowsIt() => UiThread.Run(() =>
    {
        // Arrange
        MarkdownConverter.ShowWebImages = true;
        var address = Served(File.ReadAllBytes(ImageFile("graf.png", 40)));

        // Act
        var document = Rendered($"![graf]({address})");

        // Assert
        Assert.Equal(40, Width(document));
    });

    [Fact]
    public void Render_WhenAWebImageCannotBeFetched_ThenShowsItsText() => UiThread.Run(() =>
    {
        // Arrange
        MarkdownConverter.ShowWebImages = true;

        // Act
        var document = Rendered("![logo](http://localhost:1/logo.png)");

        // Assert
        Assert.Equal((null, "logo"), (Picture(document), DocumentText(document)));
    });

    [Fact]
    public void Render_WhenTheImageIsInsideALink_ThenTheLinkOpensTheLinkedPage() => UiThread.Run(() =>
    {
        // Arrange
        var file = ImageFile("graf.png", 40);

        // Act
        var document = Rendered($"[![graf](<{file}>)](https://example.com)");

        // Assert
        Assert.Equal((new Uri("https://example.com"), true), (Assert.Single(Hyperlinks(document)).NavigateUri, Picture(document)?.Source is not null));
    });
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

    static string DocumentText(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text.Trim();

    static Image? Picture(DependencyObject element) => Pictures(element).FirstOrDefault();

    static IEnumerable<Image> Pictures(DependencyObject element) =>
        LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(child => child is Image image ? [image] : Pictures(child));

    static FlowDocument Rendered(string markdown)
    {
        var document = MarkdownConverter.Render(markdown);
        UiThread.Until(() => Pictures(document).All(picture => picture.Source is not null));
        return document;
    }

    static int Width(FlowDocument document) => Assert.IsType<BitmapImage>(Picture(document)!.Source).PixelWidth;

    static Uri Served(byte[] image)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            _ = await stream.ReadAsync(new byte[4096]);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {image.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(image);
            listener.Stop();
        });
        return new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/graf.png");
    }

    string ImageFile(string name, int width, int height = 10)
    {
        var file = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        BitmapEncoder encoder = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4)));
        using var image = new MemoryStream();
        encoder.Save(image);
        File.WriteAllBytes(file, Path.GetExtension(name).Equals(".ico", StringComparison.OrdinalIgnoreCase) ? Icon(image.ToArray(), width) : image.ToArray());
        return file;
    }

    static byte[] Icon(byte[] png, int width)
    {
        using var icon = new MemoryStream();
        using var writer = new BinaryWriter(icon);
        writer.Write(new byte[] { 0, 0, 1, 0, 1, 0, (byte)width, 10, 0, 0, 1, 0, 32, 0 });
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
        return icon.ToArray();
    }

    static IEnumerable<Hyperlink> Hyperlinks(DependencyObject element) =>
        LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(child => child is Hyperlink link ? Hyperlinks(child).Prepend(link) : Hyperlinks(child));
}
