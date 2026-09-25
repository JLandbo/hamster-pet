using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hamster;

public static class SpriteRenderer
{
    public static BitmapSource Render(string[] rows, IReadOnlyDictionary<char, uint> palette)
    {
        var (width, height) = (rows[0].Length, rows.Length);
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = palette.GetValueOrDefault(rows[y][x]);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
