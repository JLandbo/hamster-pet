using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Hamster.Desktop;

public static class ClipboardAttachments
{
    public static IEnumerable<string>? ClipboardFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    public static BitmapSource? ClipboardImage()
    {
        try
        {
            return Clipboard.ContainsImage() && !Clipboard.ContainsText() ? Clipboard.GetImage() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
