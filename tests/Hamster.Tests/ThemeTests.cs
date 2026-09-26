using System.Windows;
using System.Windows.Media;

namespace Hamster.Tests;

public sealed class ThemeTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string Write(string name, string json)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name);
        File.WriteAllText(file, json);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Read_WhenTheFileHasColors_ThenNamesTheThemeAfterTheFile()
    {
        // Arrange
        var file = Write("Mørk.json", """{"colors": {"Surface": "#F5FAFAF8", "Text": "#1F1F1F"}}""");

        // Act
        var theme = Theme.Read(file)!;

        // Assert
        Assert.Equal(("Mørk", Color.FromArgb(0xF5, 0xFA, 0xFA, 0xF8), Color.FromRgb(0x1F, 0x1F, 0x1F)), (theme.Name, theme.Colors["Surface"], theme.Colors["Text"]));
    }

    [Fact]
    public void Read_WhenColorsAreInvalid_ThenLeavesThemOutWithoutFailing()
    {
        // Arrange
        var file = Write("Mørk.json", """{"colors": {"Surface": "ikke en farve", "Text": null, "Edge": "sc#1", "Card": "ContextColor x 1", "Hover": "ContextColor foo:bar 1 0", "Box": "ContextColor file:///C:/nope.icc 1 0", "Muted": "#5F6368"}}""");

        // Act
        var theme = Theme.Read(file)!;

        // Assert
        Assert.Equal("Muted", Assert.Single(theme.Colors).Key);
    }

    [Fact]
    public void Read_WhenTheFileHasCommentsAndTrailingCommas_ThenReadsIt()
    {
        // Arrange
        var file = Write("Mørk.json", """
            {
              // Mørk baggrund
              "colors": {"Surface": "#101010",},
            }
            """);

        // Act
        var theme = Theme.Read(file);

        // Assert
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), theme!.Colors["Surface"]);
    }

    [Fact]
    public void Read_WhenTheFileIsNotJson_ThenReturnsNull()
    {
        // Arrange
        var file = Write("Mørk.json", "{");

        // Act
        var theme = Theme.Read(file);

        // Assert
        Assert.Null(theme);
    }

    [Fact]
    public void ToResources_WhenTheThemeHasColors_ThenGivesABrushPerColor()
    {
        // Arrange
        var theme = new Theme("Mørk", new Dictionary<string, Color> { ["Text"] = Colors.Black });

        // Act
        var resources = theme.ToResources();

        // Assert
        Assert.Equal(Colors.Black, ((SolidColorBrush)resources["Text"]).Color);
    }

    [Fact]
    public void BrushOf_WhenTheThemeLacksTheColor_ThenGivesTheDefault()
    {
        // Arrange
        var defaults = new ResourceDictionary { ["Text"] = Brushes.Red };

        // Act
        var brush = new Theme("Mørk", new Dictionary<string, Color>()).BrushOf("Text", defaults);

        // Assert
        Assert.Same(Brushes.Red, brush);
    }

    [Fact]
    public void Themes_WhenBuiltIn_ThenAllDefineTheSameColors()
    {
        // Act
        var names = new ThemeLibrary(directory).Themes.Skip(1).Select(theme => string.Join(",", theme.Colors.Keys.Order())).Distinct();

        // Assert
        Assert.Single(names);
    }

    [Fact]
    public void Themes_WhenYouHaveNoThemesOfYourOwn_ThenListsTheDefaultFirstAndThenTheBuiltInThemes()
    {
        // Act
        var themes = new ThemeLibrary(directory).Themes;

        // Assert
        Assert.Equal(["Sort og gul", "Blå", "Lys"], themes.Select(theme => theme.Name));
    }

    [Fact]
    public void Themes_WhenYouHaveThemesOfYourOwn_ThenListsThemByNameAmongTheBuiltInThemes()
    {
        // Arrange
        Write("Mørk.json", """{"colors": {}}""");
        Write("Grå.json", """{"colors": {}}""");
        Write("Ødelagt.json", "{");

        // Act
        var themes = new ThemeLibrary(directory).Themes;

        // Assert
        Assert.Equal(["Sort og gul", "Blå", "Grå", "Lys", "Mørk"], themes.Select(theme => theme.Name));
    }

    [Fact]
    public void Themes_WhenYourThemeHasTheNameOfABuiltInTheme_ThenKeepsTheBuiltInTheme()
    {
        // Arrange
        Write("lys.json", """{"colors": {"Surface": "#000000"}}""");
        Write("Sort og gul.json", """{"colors": {"Surface": "#000000"}}""");

        // Act
        var themes = new ThemeLibrary(directory).Themes.ToArray();

        // Assert
        Assert.Equal(["Sort og gul", "Blå", "Lys"], themes.Select(theme => theme.Name));
        Assert.All(themes, theme => Assert.NotEqual(Colors.Black, theme.Colors.GetValueOrDefault("Surface")));
    }

    [Fact]
    public void Find_WhenTheThemeExists_ThenGivesIt()
    {
        // Arrange
        Write("Mørk.json", """{"colors": {"Surface": "#101010"}}""");

        // Act
        var theme = new ThemeLibrary(directory).Find("Mørk");

        // Assert
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), theme.Colors["Surface"]);
    }

    [Fact]
    public void Find_WhenTheThemeIsUnknown_ThenGivesTheDefault()
    {
        // Act
        var theme = new ThemeLibrary(directory).Find("Mørk");

        // Assert
        Assert.Same(ThemeLibrary.Default, theme);
    }

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeThemes_ThenHasNoTheme()
    {
        // Arrange
        var file = Write("pet.json", """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default with { CharacterName = "Tom" }).Load();

        // Assert
        Assert.Equal(("Hamster", null), (pet.CharacterName, pet.ThemeName));
    }
}
