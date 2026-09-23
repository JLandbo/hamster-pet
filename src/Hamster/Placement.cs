using System.Windows;

namespace Hamster;

/// <summary>Where the pet's window stands: its horizontal centre and bottom edge, in screen DIPs.</summary>
public sealed record Placement(double CenterX, double Bottom)
{
    // A saved spot can be on a monitor that is no longer connected.
    public Placement ClampedTo(Rect screen) =>
        new(Math.Clamp(CenterX, screen.Left, screen.Right), Math.Clamp(Bottom, screen.Top, screen.Bottom));
}
