using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hamster;

public static class SpriteRenderer
{
    public static BitmapSource Render(string[] rows)
    {
        var pixels = new uint[Sprites.Width * Sprites.Height];
        for (var y = 0; y < Sprites.Height; y++)
            for (var x = 0; x < Sprites.Width; x++)
                pixels[y * Sprites.Width + x] = Sprites.Palette.GetValueOrDefault(rows[y][x]);

        var bitmap = BitmapSource.Create(Sprites.Width, Sprites.Height, 96, 96, PixelFormats.Bgra32, null, pixels, Sprites.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
