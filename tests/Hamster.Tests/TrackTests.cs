namespace Hamster.Tests;

public class TrackTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void From_WhenThereIsNoTitle_ThenNoTrack(string? title)
    {
        // Act
        var track = Track.From(title, "Kunstner", playing: true);

        // Assert
        Assert.Null(track);
    }

    [Theory]
    [InlineData("Sang", "Kunstner", "Sang – Kunstner")]
    [InlineData("Sang", null, "Sang")]
    [InlineData(" Sang ", " ", "Sang")]
    public void Text_WhenTrackIsKnown_ThenShowsTitleAndArtist(string title, string? artist, string expected)
    {
        // Act
        var text = Track.From(title, artist, playing: true)!.Text;

        // Assert
        Assert.Equal(expected, text);
    }
}
