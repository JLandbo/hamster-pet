using System.Windows;

namespace Hamster;

public sealed record Placement(double CenterX, double Bottom)
{
    public Placement ClampedTo(Rect screen) =>
        new(Math.Clamp(CenterX, screen.Left, screen.Right), Math.Clamp(Bottom, screen.Top, screen.Bottom));
}
