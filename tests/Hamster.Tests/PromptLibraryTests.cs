namespace Hamster.Tests;

public sealed class PromptLibraryTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly PromptLibrary _library;

    public PromptLibraryTests() => _library = new PromptLibrary(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Prompts_WhenNoFolderExists_ThenNone()
    {
        // Act
        var prompts = _library.Prompts;

        // Assert
        Assert.Empty(prompts);
    }

    [Fact]
    public void EnsureFolder_WhenCalledTheFirstTime_ThenAddsAnExample()
    {
        // Act
        _library.EnsureFolder();

        // Assert
        Assert.Equal(["Dagens overblik"], _library.Prompts.Select(prompt => prompt.Name));
    }

    [Fact]
    public void EnsureFolder_WhenTheFolderExists_ThenLeavesItAlone()
    {
        // Arrange
        Directory.CreateDirectory(_folder);

        // Act
        _library.EnsureFolder();

        // Assert
        Assert.Empty(_library.Prompts);
    }

    [Fact]
    public void Prompts_WhenFilesWereAdded_ThenListsTextFilesSortedByName()
    {
        // Arrange
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Oversæt.md"), "Oversæt til engelsk");
        File.WriteAllText(Path.Combine(_folder, "Daglig status.TXT"), "Status");
        File.WriteAllText(Path.Combine(_folder, "billede.png"), "");

        // Act
        var names = _library.Prompts.Select(prompt => prompt.Name);

        // Assert
        Assert.Equal(["Daglig status", "Oversæt"], names);
    }
}
