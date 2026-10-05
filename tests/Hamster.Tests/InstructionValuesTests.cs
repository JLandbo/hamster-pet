namespace Hamster.Tests;

public sealed class InstructionValuesTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Fill_WhenANameIsKnown_ThenPutsInItsValue()
    {
        // Act
        var text = InstructionValues.Fill("Work in {{workspace}}.", new Dictionary<string, string> { ["workspace"] = @"C:\Data\workspace" });

        // Assert
        Assert.Equal(@"Work in C:\Data\workspace.", text);
    }

    [Fact]
    public void Fill_WhenANameIsUnknown_ThenLeavesItAsWritten()
    {
        // Act
        var text = InstructionValues.Fill("Use {{api url}} here.", new Dictionary<string, string> { ["workspace"] = @"C:\Data\workspace" });

        // Assert
        Assert.Equal("Use {{api url}} here.", text);
    }

    [Fact]
    public void Of_WhenGivenTheDataFiles_ThenNamesTheirFolders()
    {
        // Arrange
        Directory.CreateDirectory(_folder);
        using var files = new DataFiles(_folder);

        // Act
        var values = InstructionValues.Of(files);

        // Assert
        Assert.Equal((_folder, files.Workspace), (values["dataFolder"], values["workspace"]));
    }
}
