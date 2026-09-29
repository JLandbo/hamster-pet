namespace Hamster.Tests;

public sealed class CharacterLibraryTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly CharacterLibrary _library;

    public CharacterLibraryTests() => _library = new CharacterLibrary(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Names_WhenNoFolderExists_ThenOnlyTheHamster()
    {
        // Act
        var names = _library.Names;

        // Assert
        Assert.Equal(["Hamster"], names);
    }

    [Fact]
    public void Names_WhenCharactersWereAdded_ThenHamsterFirstAndTheRestSorted()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_folder, "Zebra"));
        Directory.CreateDirectory(Path.Combine(_folder, "Kat"));

        // Act
        var names = _library.Names;

        // Assert
        Assert.Equal(["Hamster", "Kat", "Zebra"], names);
    }

    [Fact]
    public void AddCopy_WhenCalled_ThenTheCopyLoadsLikeTheHamster()
    {
        // Act
        var copy = _library.Load(Path.GetFileName(_library.AddCopy()));

        // Assert
        Assert.Equal(Character.Hamster.Palette, copy.Palette);
        Assert.Equal(Character.Hamster.Animations[Mood.Sad].Select(frame => frame.Rows), copy.Animations[Mood.Sad].Select(frame => frame.Rows));
    }

    [Fact]
    public void AddCopy_WhenCalledTwice_ThenNamesThemApart()
    {
        // Act
        var names = new[] { _library.AddCopy(), _library.AddCopy() }.Select(Path.GetFileName);

        // Assert
        Assert.Equal(["Karakter 1", "Karakter 2"], names);
    }
}
