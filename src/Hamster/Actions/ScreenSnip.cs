using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Hamster.Actions;

public static partial class ScreenSnip
{
    static readonly TimeSpan PollTime = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    public static async Task<BitmapSource?> CaptureAsync()
    {
        var before = GetClipboardSequenceNumber();
        Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        for (var waited = TimeSpan.Zero; waited < GiveUpAfter; waited += PollTime)
        {
            await Task.Delay(PollTime);
            if (GetClipboardSequenceNumber() != before && ClipboardImage() is { } image)
                return image;
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
