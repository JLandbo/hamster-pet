namespace Hamster.Tests;

public sealed class PromptLibraryTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    readonly PromptLibrary library;

    public PromptLibraryTests() => library = new PromptLibrary(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Prompts_WhenNoFolderExists_ThenNone()
    {
        // Act
        var prompts = library.Prompts;

        // Assert
        Assert.Empty(prompts);
    }

    [Fact]
    public void EnsureFolder_WhenCalledTheFirstTime_ThenAddsAnExample()
    {
        // Act
        library.EnsureFolder();

        // Assert
        Assert.Equal(["Dagens overblik"], library.Prompts.Select(prompt => prompt.Name));
    }

    [Fact]
    public void EnsureFolder_WhenTheFolderExists_ThenLeavesItAlone()
    {
        // Arrange
        Directory.CreateDirectory(folder);

        // Act
        library.EnsureFolder();

        // Assert
        Assert.Empty(library.Prompts);
    }

    [Fact]
    public void Prompts_WhenFilesWereAdded_ThenListsTextFilesSortedByName()
    {
        // Arrange
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Oversæt.md"), "Oversæt til engelsk");
        File.WriteAllText(Path.Combine(folder, "Daglig status.TXT"), "Status");
        File.WriteAllText(Path.Combine(folder, "billede.png"), "");

        // Act
        var names = library.Prompts.Select(prompt => prompt.Name);

        // Assert
        Assert.Equal(["Daglig status", "Oversæt"], names);
    }
}
