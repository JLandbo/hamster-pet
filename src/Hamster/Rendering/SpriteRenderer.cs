using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Hamster.Core.Pet;

namespace Hamster.Rendering;

public static class SpriteRenderer
{
    public static BitmapSource[] Render(IEnumerable<Frame> frames)
    {
        Dictionary<byte[], BitmapSource> sheets = [];
        return [.. frames.Select(frame => Crop(sheets.TryGetValue(frame.Sheet, out var sheet) ? sheet : sheets[frame.Sheet] = Decode(frame.Sheet), frame))];
    }

    static BitmapSource Decode(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            return BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (FileFormatException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    static BitmapSource Crop(BitmapSource sheet, Frame frame)
    {
        var part = new CroppedBitmap(sheet, new Int32Rect(frame.Index * frame.Width, 0, frame.Width, frame.Height));
        part.Freeze();
        return part;
    }
}
