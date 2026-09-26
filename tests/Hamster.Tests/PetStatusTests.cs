namespace Hamster.Tests;

public class PetStatusTests
{
    public static TheoryData<PetStatus, Mood> Cases()
    {
        var awake = new PetStatus(Awake: true);
        var curious = awake with { Hovered = true };
        var tired = curious with { Tired = true };
        var music = tired with { Music = true };
        var dance = music with { BackgroundWork = true };
        var news = dance with { HasNews = true };
        var listen = news with { Typing = true };
        var sad = listen with { Failed = true };
        var happy = sad with { Celebrating = true };
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
            { new PetStatus(Tired: true), Mood.Sleep },
            { awake with { Tired = true }, Mood.Tired },
            { tired, Mood.Tired },
            { new PetStatus(Music: true), Mood.Dance },
            { music, Mood.Dance },
            { dance, Mood.Dance },
            { news, Mood.News },
            { listen, Mood.Listen },
            { sad, Mood.Sad },
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
