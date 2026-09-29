using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Hamster.Desktop;

public static partial class ScreenSnip
{
    static readonly TimeSpan _pollTime = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan _giveUpAfter = TimeSpan.FromSeconds(30);

    public static async Task<BitmapSource?> CaptureAsync()
    {
        var before = GetClipboardSequenceNumber();
        Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        for (var waited = TimeSpan.Zero; waited < _giveUpAfter; waited += _pollTime)
        {
            await Task.Delay(_pollTime);
            if (GetClipboardSequenceNumber() != before && ClipboardImage() is { } image)
            {
                return image;
            }
        }
        return null;
    }

    static BitmapSource? ClipboardImage()
    {
        try
        {
            return Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
