namespace Hamster.Tests;

public class MoodTransitionTests
{
    static int SpinLength => Character.Hamster.Animations[Mood.Spin].Length;

    [Fact]
    public void Next_WhenSpinIsMidCycle_ThenKeepsSpinning()
    {
        // Act
        var mood = MoodTransition.Next(Mood.Spin, framesShown: 5, wanted: Mood.Happy, SpinLength);

        // Assert
        Assert.Equal(Mood.Spin, mood);
    }

    [Fact]
    public void Next_WhenSpinFinishedItsCycle_ThenSwitches()
    {
        // Act
        var mood = MoodTransition.Next(Mood.Spin, framesShown: SpinLength, wanted: Mood.Happy, SpinLength);

        // Assert
        Assert.Equal(Mood.Happy, mood);
    }

    [Theory]
    [InlineData(Mood.Dangle)]
    [InlineData(Mood.Run)]
    public void Next_WhenGrabbedMidSpin_ThenReactsRightAway(Mood wanted)
    {
        // Act
        var mood = MoodTransition.Next(Mood.Spin, framesShown: 5, wanted, SpinLength);

        // Assert
        Assert.Equal(wanted, mood);
    }

    [Fact]
    public void Next_WhenNotSpinning_ThenSwitchesRightAway()
    {
        // Act
        var mood = MoodTransition.Next(Mood.Research, framesShown: 1, wanted: Mood.Happy, SpinLength);

        // Assert
        Assert.Equal(Mood.Happy, mood);
    }
}
