namespace Hamster.Tests;

public sealed class SystemVolumeTests
{
    readonly SystemVolume _volume = new();

    [Fact]
    public void Read_WhenThereIsASpeaker_ThenGivesALevelBetweenZeroAndOne()
    {
        // Act
        var (level, _) = Current();

        // Assert
        Assert.InRange(level, 0, 1);
    }

    [Fact]
    public void SetLevel_WhenSetToTheCurrentLevel_ThenKeepsIt()
    {
        // Arrange
        var (level, _) = Current();

        // Act
        _volume.SetLevel(level);

        // Assert
        Assert.Equal(level, Current().Level, 3);
    }

    [Fact]
    public void SetMuted_WhenSetToTheCurrentState_ThenKeepsIt()
    {
        // Arrange
        var (_, muted) = Current();

        // Act
        _volume.SetMuted(muted);

        // Assert
        Assert.Equal(muted, Current().Muted);
    }

    (double Level, bool Muted) Current()
    {
        var current = _volume.Read();
        Assert.SkipWhen(current is null, "Maskinen har ingen lydenhed.");
        return current.GetValueOrDefault();
    }
}
