namespace Hamster.Tests;

public class SpritesTests
{
    [Fact]
    public void Animations_WhenBuilt_ThenEveryMoodHasFrames()
    {
        // Act
        var moods = Sprites.Animations.Where(animation => animation.Value.Length > 0).Select(animation => animation.Key);

        // Assert
        Assert.Equal(Enum.GetValues<Mood>().Order(), moods.Order());
    }

    [Fact]
    public void Animations_WhenBuilt_ThenSpinStartsOnTheNormalBody()
    {
        // Act
        var firstSpinFrame = Sprites.Animations[Mood.Spin][0].Rows;

        // Assert
        Assert.Equal(Sprites.Animations[Mood.Awake][0].Rows, firstSpinFrame);
    }

    [Fact]
    public void Animations_WhenBuilt_ThenFramesHaveFixedSizeAndKnownColors()
    {
        // Arrange
        var rows = Sprites.Animations.Values.SelectMany(frames => frames).Select(frame => frame.Rows).ToArray();

        // Act
        var sizes = rows.Select(frame => (frame.Length, frame.Select(row => row.Length).Distinct().Single())).Distinct();
        var colors = rows.SelectMany(frame => frame).SelectMany(row => row).Distinct();

        // Assert
        Assert.Equal([(Sprites.Height, Sprites.Width)], sizes);
        Assert.All(colors, color => Assert.True(color == '.' || Sprites.Palette.ContainsKey(color), $"Unknown color '{color}'"));
    }
}
