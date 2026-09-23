namespace Hamster.Tests;

public class PetStatusTests
{
    // Each case also sets every lower-priority flag, so the whole priority order is proven.
    public static TheoryData<PetStatus, Mood> Cases()
    {
        var awake = new PetStatus(Awake: true);
        var curious = awake with { Hovered = true };
        var happy = curious with { Celebrating = true };
        var spin = happy with { Busy = true };
        var research = spin with { BrowsingWeb = true };
        var alert = research with { WaitingForUser = true };
        var giggle = alert with { Giggling = true };
        var dangle = giggle with { Pressed = true };
        return new()
        {
            { new PetStatus(), Mood.Sleep },
            { new PetStatus(Moving: true), Mood.Sleep },
            { awake, Mood.Awake },
            { curious, Mood.Curious },
            { happy, Mood.Happy },
            { spin, Mood.Spin },
            { research, Mood.Research },
            { alert, Mood.Alert },
            { giggle, Mood.Giggle },
            { dangle, Mood.Dangle },
            { dangle with { Moving = true }, Mood.Run },
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Mood_WhenStatusGiven_ThenPicksHighestPriorityMood(PetStatus status, Mood expected)
    {
        // Act
        var mood = status.Mood;

        // Assert
        Assert.Equal(expected, mood);
    }
}
