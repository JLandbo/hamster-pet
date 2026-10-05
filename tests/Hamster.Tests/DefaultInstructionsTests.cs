namespace Hamster.Tests;

public sealed class DefaultInstructionsTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");

    string File => Path.Combine(_folder, "instructions.txt");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Text_WhenRead_ThenHoldsTheRules()
    {
        // Act
        var text = DefaultInstructions.Text;

        // Assert
        Assert.Contains("# Rules", text);
    }

    [Fact]
    public void WriteIfMissing_WhenThereIsNoFile_ThenWritesTheDefault()
    {
        // Act
        DefaultInstructions.WriteIfMissing(File);

        // Assert
        Assert.Equal(DefaultInstructions.Text, System.IO.File.ReadAllText(File));
    }

    [Fact]
    public void WriteIfMissing_WhenTheFolderCannotBeMade_ThenGoesOn()
    {
        // Arrange
        Directory.CreateDirectory(_folder);
        var blocking = Path.Combine(_folder, "blocking");
        System.IO.File.WriteAllText(blocking, "");

        // Act
        var failure = Record.Exception(() => DefaultInstructions.WriteIfMissing(Path.Combine(blocking, "instructions.txt")));

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public void WriteIfMissing_WhenTheFileExists_ThenLeavesIt()
    {
        // Arrange
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "My own rules.");

        // Act
        DefaultInstructions.WriteIfMissing(File);

        // Assert
        Assert.Equal("My own rules.", System.IO.File.ReadAllText(File));
    }
}
