namespace Hamster.Tests;

public class AppTests
{
    [Fact]
    public void LinkAt_WhenTextInALinkIsRightClicked_ThenGivesTheLinkAddress()
    {
        // Arrange
        var text = new Run("Markdig");
        _ = new Hyperlink(text) { NavigateUri = new Uri("https://github.com/xoofx/markdig") };

        // Act
        var link = App.LinkAt(text);

        // Assert
        Assert.Equal(new Uri("https://github.com/xoofx/markdig"), link);
    }

    [Fact]
    public void LinkAt_WhenPlainTextIsRightClicked_ThenGivesNothing()
    {
        // Arrange
        var text = new Run("Hej");
        _ = new Paragraph(text);

        // Act
        var link = App.LinkAt(text);

        // Assert
        Assert.Null(link);
    }
}
