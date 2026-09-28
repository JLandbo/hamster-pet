namespace Hamster.Tests;

public sealed class PictureMentionsTests : IDisposable
{
    static readonly string[] Files =
    [
        "graf.png", "før.png", "efter.png", @"Skærm billeder\graf v2.png", "noter.txt", "graf.png.txt",
        "b.png", "b.jpg", "b.jpeg", "b.gif", "b.bmp", "b.tif", "b.tiff", "b.ico", "b.webp",
    ];

    readonly string folder = Directory.CreateTempSubdirectory("billeder æøå ").FullName;

    public PictureMentionsTests()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16)));
        using var png = new MemoryStream();
        encoder.Save(png);
        foreach (var file in Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllBytes(Path.Combine(folder, file), png.ToArray());
        }
        Directory.CreateDirectory(Path.Combine(folder, "mappe.png"));
    }

    public void Dispose() => Directory.Delete(folder, true);

    [Theory]
    [InlineData(@"Gemt i {dir}\graf.png", "graf.png")]
    [InlineData("Gemt i {dir/}/graf.png", "graf.png")]
    [InlineData(@"Gemt i {dir/}/Skærm billeder\graf v2.png", @"Skærm billeder\graf v2.png")]
    [InlineData(@"Gemt i {dir2}\\graf.png", "graf.png")]
    [InlineData("Åbn {uri}/Sk%C3%A6rm%20billeder/graf%20v2.png i browseren", @"Skærm billeder\graf v2.png")]
    [InlineData(@"Skærmbilledet ligger i {dir}\Skærm billeder\graf v2.png, se selv", @"Skærm billeder\graf v2.png")]
    [InlineData(@"Se `{dir}\Skærm billeder\graf v2.png` her", @"Skærm billeder\graf v2.png")]
    [InlineData("Se [grafen]({dir/}/graf.png) her", "graf.png")]
    [InlineData(@"Se <{dir}\graf.png> her", "graf.png")]
    [InlineData(@"Se „{dir}\graf.png“ her", "graf.png")]
    [InlineData(@"Se ""{dir}\graf.png"" her", "graf.png")]
    [InlineData(@"Grafen ({dir}\graf.png) viser salget", "graf.png")]
    [InlineData(@"Se **{dir}\graf.png** her", "graf.png")]
    [InlineData(@"Se _{dir}\graf.png_ her", "graf.png")]
    [InlineData(@"Se {dir}\graf.png.", "graf.png")]
    [InlineData(@"Se {dir}\graf.png, og så videre", "graf.png")]
    [InlineData(@"Se {dir}\graf.png; og så videre", "graf.png")]
    [InlineData(@"Se {dir}\graf.png: salget stiger", "graf.png")]
    [InlineData(@"Se {dir}\graf.png!", "graf.png")]
    [InlineData(@"Er det {dir}\graf.png?", "graf.png")]
    [InlineData(@"Åbn {dir}\graf.png-filen og se", "graf.png")]
    [InlineData(@"Se {dir}\GRAF.PNG her", "graf.png")]
    [InlineData(@"Se {dir}\findes-ikke.png her", "findes-ikke.png")]
    public void In_WhenAFullPathIsWritten_ThenFindsTheFile(string text, string file)
    {
        // Act
        var found = PictureMentions.In(Filled(text));

        // Assert
        Assert.Equal(Path.Combine(folder, file), Assert.Single(found).LocalPath, ignoreCase: true);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tif")]
    [InlineData("tiff")]
    [InlineData("ico")]
    [InlineData("webp")]
    [InlineData("PNG")]
    public void In_WhenTheImageHasAKnownType_ThenFindsIt(string type)
    {
        // Act
        var found = PictureMentions.In(Filled($@"Se {{dir}}\b.{type} her"));

        // Assert
        Assert.Single(found);
    }

    [Theory]
    [InlineData(@"Se {dir}\noter.txt")]
    [InlineData(@"Se {dir}\graf.png.txt")]
    [InlineData(@"Se {dir}\graf.png_original")]
    [InlineData("Se graf.png")]
    [InlineData("Se ./graf.png")]
    [InlineData(@"Delt på \\server\share\graf.png")]
    [InlineData("Delt på file://server/share/graf.png")]
    [InlineData("Se https://example.com/graf.png")]
    [InlineData("Det er et png-billede")]
    [InlineData("Se file:///C:/a%00b.png")]
    [InlineData("Se C:\\a\0b.png")]
    public void In_WhenNothingShouldBeShown_ThenFindsNothing(string text)
    {
        // Act
        var found = PictureMentions.In(Filled(text));

        // Assert
        Assert.Empty(found);
    }

    [Fact]
    public void Render_WhenTheDriveDoesNotExist_ThenNoPictureIsShown() => UiThread.Run(() =>
    {
        // Arrange
        var free = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => new DriveInfo(letter.ToString()).DriveType == DriveType.NoRootDirectory);

        // Act
        var document = MarkdownConverter.Render($@"Se {free}:\graf.png", PictureMentions.In);
        UiThread.Until(() => !Documents.Images(document).Any());

        // Assert
        Assert.Single(document.Blocks);
    });

    [Fact]
    public void In_WhenTheSameFileIsWrittenInSeveralWays_ThenFindsItOnce()
    {
        // Act
        var found = PictureMentions.In(Filled(@"Se {dir}\graf.png og {dir/}/graf.png og {uri}/graf.png"));

        // Assert
        Assert.Single(found);
    }

    [Fact]
    public void In_WhenSeveralFilesAreMentioned_ThenFindsThemInOrder()
    {
        // Act
        var found = PictureMentions.In(Filled(@"Se {dir}\b.jpg, {dir}\noter.txt og {dir}\b.gif"));

        // Assert
        Assert.Equal([Path.Combine(folder, "b.jpg"), Path.Combine(folder, "b.gif")], found.Select(uri => uri.LocalPath));
    }

    [Theory]
    [InlineData(@"Se {dir}\graf.png her.")]
    [InlineData(@"{dir}\graf.png viser salget.")]
    [InlineData(@"Salget ses i {dir}\graf.png")]
    [InlineData(@"Se **{dir}\graf.png** her.")]
    [InlineData(@"Se *{dir}\graf.png* her.")]
    [InlineData(@"Se ***{dir}\graf.png*** her.")]
    [InlineData(@"Se `{dir}\graf.png` her.")]
    [InlineData("Se [grafen]({dir/}/graf.png) her.")]
    [InlineData(@"# Salg i {dir}\graf.png")]
    [InlineData(@"Salg i {dir}\graf.png" + "\n===")]
    [InlineData(@"- Se {dir}\graf.png" + "\n- Næste")]
    [InlineData(@"1. Se {dir}\graf.png" + "\n2. Næste")]
    [InlineData("- Punkt\n  - Se {dir}\\graf.png\n- Næste")]
    [InlineData("| a | b |\n|---|---|\n| Se {dir}\\graf.png | x |")]
    [InlineData("> Se {dir}\\graf.png\n\nTekst")]
    public void Render_WhenAMarkdownConstructMentionsAnImage_ThenThePictureFollowsItsBlock(string answer) => UiThread.Run(() =>
    {
        // Act
        var document = Rendered(answer);

        // Assert
        Assert.Equal(Path.Combine(folder, "graf.png"), PictureAfter(document, "graf.png"));
    });

    [Fact]
    public void Render_WhenOneParagraphMentionsTwoImages_ThenBothFollowInTheOrderTheyWereMentioned() => UiThread.Run(() =>
    {
        // Act
        var document = Rendered(@"Sammenlign {dir}\før.png med {dir}\efter.png.");

        // Assert
        Assert.Equal([Path.Combine(folder, "før.png"), Path.Combine(folder, "efter.png")], PicturesOf(document.Blocks.Skip(1)));
    });

    [Fact]
    public void Render_WhenTheImageIsAlsoWrittenAsAPicture_ThenItIsShownOnce() => UiThread.Run(() =>
    {
        // Act
        var document = Rendered("![graf](<{dir/}/graf.png>)\n\nGemt i {dir}\\graf.png");

        // Assert
        Assert.Single(Documents.Images(document));
    });

    [Theory]
    [InlineData("ødelagt.png")]
    [InlineData("findes-ikke.png")]
    [InlineData("mappe.png")]
    public void Render_WhenTheMentionedFileCannotBeShown_ThenNoPictureIsShown(string file) => UiThread.Run(() =>
    {
        // Arrange
        File.WriteAllText(Path.Combine(folder, "ødelagt.png"), "ikke et billede");

        // Act
        var document = MarkdownConverter.Render(Filled($@"Se {{dir}}\{file}"), PictureMentions.In);
        UiThread.Until(() => !Documents.Images(document).Any());

        // Assert
        Assert.Single(document.Blocks);
    });

    [Fact]
    public void Render_WhenThePathIsInACodeBlock_ThenNoPictureIsShown() => UiThread.Run(() =>
    {
        // Act
        var document = MarkdownConverter.Render(Filled("```\n{dir}\\graf.png\n```"), PictureMentions.In);

        // Assert
        Assert.Empty(Documents.Images(document));
    });

    [Fact]
    public void Render_WhenMentionsAreNotAskedFor_ThenNoPictureIsShown() => UiThread.Run(() =>
    {
        // Act
        var document = MarkdownConverter.Render(Filled(@"Se {dir}\graf.png"));

        // Assert
        Assert.Empty(Documents.Images(document));
    });

    [Fact]
    public void Convert_WhenAnAnswerMentionsAnImage_ThenTheBubbleShowsIt() => UiThread.Run(() =>
    {
        // Act
        var document = Documents.Loaded((FlowDocument)new ChatMarkdownConverter().Convert(Filled(@"Grafen er gemt i `{dir}\graf.png`"), typeof(FlowDocument), null!, null!));

        // Assert
        Assert.IsType<BitmapImage>(Assert.Single(Documents.Images(document)).Source);
    });

    FlowDocument Rendered(string answer) => Documents.Loaded(MarkdownConverter.Render(Filled(answer), PictureMentions.In));

    string Filled(string text)
    {
        var forward = folder.Replace('\\', '/');
        return text
            .Replace("{dir/}", forward)
            .Replace("{dir2}", folder.Replace(@"\", @"\\"))
            .Replace("{dir}", folder)
            .Replace("{uri}", new Uri(folder).AbsoluteUri);
    }

    static string? PictureAfter(FlowDocument document, string text)
    {
        foreach (var blocks in Collections(document.Blocks))
        {
            var list = blocks.ToList();
            var index = list.FindIndex(block => block is Paragraph paragraph && new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Contains(text));
            if (index >= 0)
            {
                return index + 1 < list.Count ? PicturesOf([list[index + 1]]).SingleOrDefault() : null;
            }
        }
        return null;
    }

    static IEnumerable<BlockCollection> Collections(BlockCollection blocks) =>
        blocks.SelectMany(block => block switch
        {
            Section section => Collections(section.Blocks),
            List list => list.ListItems.SelectMany(item => Collections(item.Blocks)),
            Table table => table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells).SelectMany(cell => Collections(cell.Blocks)),
            _ => [],
        }).Prepend(blocks);

    static IEnumerable<string> PicturesOf(IEnumerable<Block> blocks) => blocks.SelectMany(Documents.Links).Where(link => Documents.Images(link).Any()).Select(link => link.NavigateUri.LocalPath);
}
