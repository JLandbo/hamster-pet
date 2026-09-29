namespace Hamster.Tests;

public sealed class SystemVolumeTests
{
    readonly SystemVolume _volume = new();

    [Fact]
    public void Read_WhenThereIsASpeaker_ThenGivesALevelBetweenZeroAndOne()
    {
        // Act
        var (level, _) = _volume.Read()!.Value;

        // Assert
        Assert.InRange(level, 0, 1);
    }

    [Fact]
    public void SetLevel_WhenSetToTheCurrentLevel_ThenKeepsIt()
    {
        // Arrange
        var (level, _) = _volume.Read()!.Value;

        // Act
        _volume.SetLevel(level);

        // Assert
        Assert.Equal(level, _volume.Read()!.Value.Level, 3);
    }

    [Fact]
    public void SetMuted_WhenSetToTheCurrentState_ThenKeepsIt()
    {
        // Arrange
        var (_, muted) = _volume.Read()!.Value;

        // Act
        _volume.SetMuted(muted);

        // Assert
        Assert.Equal(muted, _volume.Read()!.Value.Muted);
    }
}
