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
    public void Animations_WhenBuilt_ThenFramesUseKnownColors()
    {
        // Act
        var colors = Sprites.Animations.Values.SelectMany(frames => frames).SelectMany(frame => frame.Rows).SelectMany(row => row).Distinct();

        // Assert
        Assert.All(colors, color => Assert.True(color == '.' || Sprites.Palette.ContainsKey(color), $"Unknown color '{color}'"));
    }
}
