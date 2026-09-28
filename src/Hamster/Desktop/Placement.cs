using System.Windows;

namespace Hamster.Desktop;

public sealed record WindowSize(double Width, double Height)
{
    public WindowSize FitIn(Rect screen) => new(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
}

public sealed record Placement(double Right, double Bottom, double Width, double ChatHeight)
{
    public Placement ClampedTo(Rect corners) =>
        this with { Right = Math.Clamp(Right, corners.Left, corners.Right), Bottom = Math.Clamp(Bottom, corners.Top, corners.Bottom) };

    public Placement DraggedTo(double width, double narrowest) => this with { Width = width > narrowest ? width : Math.Min(width, Width) };

    public Placement CenteredIn(Rect screen, Size pet) =>
        this with { Right = screen.Left + (screen.Width + pet.Width) / 2, Bottom = screen.Top + (screen.Height + pet.Height) / 2 };
}
