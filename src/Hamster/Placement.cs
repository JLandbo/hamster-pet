using System.Windows;

namespace Hamster;

public sealed record Placement(double Right, double Bottom, double Width, double ChatHeight)
{
    public Placement ClampedTo(Rect corners) =>
        this with { Right = Math.Clamp(Right, corners.Left, corners.Right), Bottom = Math.Clamp(Bottom, corners.Top, corners.Bottom) };
}
