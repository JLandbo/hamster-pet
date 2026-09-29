using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hamster.Tests;

public sealed class SpriteRendererTests
{
    [Fact]
    public void Render_WhenFramesComeFromAPng_ThenEachFrameIsItsOwnPartOfTheImage()
    {
        // Arrange
        var png = Png(0xFFFF0000, 0xFF0000FF);

        // Act
        var images = SpriteRenderer.Render([new Frame(png, 0, 1, 1, 100), new Frame(png, 1, 1, 1, 100)]);

        // Assert
        Assert.Equal([0xFFFF0000, 0xFF0000FF], images.Select(image => PixelsOf(image)[0]));
    }

    [Fact]
    public void Render_WhenThePngIsBroken_ThenSaysSo()
    {
        // Arrange
        var broken = Png(0xFFFF0000, 0xFF0000FF)[..40];

        // Act
        var rendering = () => SpriteRenderer.Render([new Frame(broken, 0, 1, 1, 100)]);

        // Assert
        Assert.Throws<InvalidDataException>(rendering);
    }

    [Fact]
    public void Render_WhenTheHamsterSpins_ThenItStartsOnTheNormalBody()
    {
        // Act
        var images = SpriteRenderer.Render([Character.Hamster.Animations[Mood.Spin][0], Character.Hamster.Animations[Mood.Awake][0]]);

        // Assert
        Assert.Equal(PixelsOf(images[1]), PixelsOf(images[0]));
    }

    static byte[] Png(params uint[] pixels)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(pixels.Length, 1, 96, 96, PixelFormats.Bgra32, null, pixels, pixels.Length * 4)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static uint[] PixelsOf(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
