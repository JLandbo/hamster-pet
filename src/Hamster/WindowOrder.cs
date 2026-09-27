using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hamster;

public static partial class WindowOrder
{
    const nint Top = 0;
    const uint KeepPositionSizeAndFocus = 0x0013;

    public static void BringToFront(Window window) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, Top, 0, 0, 0, 0, KeepPositionSizeAndFocus);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
