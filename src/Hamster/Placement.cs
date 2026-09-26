using System.Windows;

namespace Hamster;

public sealed record WindowSize(double Width, double Height)
{
    public WindowSize FitIn(Rect screen) => new(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
}

public sealed record Placement(double Right, double Bottom, double Width, double ChatHeight)
{
    public Placement ClampedTo(Rect corners) =>
        this with { Right = Math.Clamp(Right, corners.Left, corners.Right), Bottom = Math.Clamp(Bottom, corners.Top, corners.Bottom) };
}
