namespace Hamster.Tests;

public sealed class CharacterLibraryTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly CharacterLibrary library;

    public CharacterLibraryTests() => library = new CharacterLibrary(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Names_WhenNoFolderExists_ThenOnlyTheHamster()
    {
        // Act
        var names = library.Names;

        // Assert
        Assert.Equal(["Hamster"], names);
    }

    [Fact]
    public void Names_WhenCharactersWereAdded_ThenHamsterFirstAndTheRestSorted()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(folder, "Zebra"));
        Directory.CreateDirectory(Path.Combine(folder, "Kat"));

        // Act
        var names = library.Names;

        // Assert
        Assert.Equal(["Hamster", "Kat", "Zebra"], names);
    }

    [Fact]
    public void AddCopy_WhenCalled_ThenTheCopyLoadsLikeTheHamster()
    {
        // Act
        var copy = library.Load(Path.GetFileName(library.AddCopy()));

        // Assert
        Assert.Equal(Character.Hamster.Palette, copy.Palette);
        Assert.Equal(Character.Hamster.Animations[Mood.Sad].Select(frame => frame.Rows), copy.Animations[Mood.Sad].Select(frame => frame.Rows));
    }

    [Fact]
    public void AddCopy_WhenCalledTwice_ThenNamesThemApart()
    {
        // Act
        var names = new[] { library.AddCopy(), library.AddCopy() }.Select(Path.GetFileName);

        // Assert
        Assert.Equal(["Karakter 1", "Karakter 2"], names);
    }
}
