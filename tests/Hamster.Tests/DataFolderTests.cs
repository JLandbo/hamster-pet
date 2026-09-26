namespace Hamster.Tests;

public class DataFolderTests
{
    [Fact]
    public void Of_WhenOverridden_ThenUsesTheOverride()
    {
        // Act
        var folder = DataFolder.Of(@"C:\test\hamster");

        // Assert
        Assert.Equal(@"C:\test\hamster", folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Of_WhenNotOverridden_ThenUsesLocalAppData(string? overridden)
    {
        // Act
        var folder = DataFolder.Of(overridden);

        // Assert
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hamster"), folder);
    }
}
