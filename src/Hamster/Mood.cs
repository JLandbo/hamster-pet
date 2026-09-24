namespace Hamster;

public enum Mood { Sleep, Awake, Curious, Spin, Research, Happy, Alert, Giggle, Run, Dangle, Dance, Sad, Listen, News, Tired }

public static class MoodTransition
{
    public static Mood Next(Mood current, int framesShown, Mood wanted) =>
        current == Mood.Spin && wanted != current && wanted is not (Mood.Dangle or Mood.Run)
            && framesShown % Sprites.Animations[Mood.Spin].Length != 0
            ? current
            : wanted;
}

public readonly record struct PetStatus(
    bool Pressed = false,
    bool Moving = false,
    bool Giggling = false,
    bool WaitingForUser = false,
    bool BrowsingWeb = false,
    bool Busy = false,
    bool Celebrating = false,
    bool Failed = false,
    bool Typing = false,
    bool HasNews = false,
    bool BackgroundWork = false,
    bool Tired = false,
    bool Hovered = false,
    bool Awake = false)
{
    public Mood Mood => this switch
    {
        { Pressed: true, Moving: true } => Mood.Run,
        { Pressed: true } => Mood.Dangle,
        { Giggling: true } => Mood.Giggle,
        { WaitingForUser: true } => Mood.Alert,
        { BrowsingWeb: true } => Mood.Research,
        { Busy: true } => Mood.Spin,
        { Celebrating: true } => Mood.Happy,
        { Failed: true } => Mood.Sad,
        { Typing: true } => Mood.Listen,
        { HasNews: true } => Mood.News,
        { BackgroundWork: true } => Mood.Dance,
        { Tired: true, Hovered: true } or { Tired: true, Awake: true } => Mood.Tired,
        { Hovered: true } => Mood.Curious,
        { Awake: true } => Mood.Awake,
        _ => Mood.Sleep,
    };
}
