using System.Runtime.InteropServices;
using System.Windows;

namespace Hamster;

public static partial class ScreenArea
{
    const uint NearestMonitor = 2;

    public static Rect Of(FrameworkElement element)
    {
        var center = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(new Pixel((int)center.X, (int)center.Y), NearestMonitor), ref info);
        var toUnits = PresentationSource.FromVisual(element)!.CompositionTarget!.TransformFromDevice;
        return new Rect(toUnits.Transform(new Point(info.Work.Left, info.Work.Top)), toUnits.Transform(new Point(info.Work.Right, info.Work.Bottom)));
    }

    record struct Pixel(int X, int Y);

    record struct Box(int Left, int Top, int Right, int Bottom);

    struct MonitorInfo
    {
        public int Size;
        public Box Monitor;
        public Box Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(Pixel point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
