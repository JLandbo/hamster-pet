using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hamster;

public static class SpriteRenderer
{
    public static BitmapSource Render(string[] rows, IReadOnlyDictionary<char, uint> palette)
    {
        var pixels = new uint[Character.Width * Character.Height];
        for (var y = 0; y < Character.Height; y++)
            for (var x = 0; x < Character.Width; x++)
                pixels[y * Character.Width + x] = palette.GetValueOrDefault(rows[y][x]);

        var bitmap = BitmapSource.Create(Character.Width, Character.Height, 96, 96, PixelFormats.Bgra32, null, pixels, Character.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
