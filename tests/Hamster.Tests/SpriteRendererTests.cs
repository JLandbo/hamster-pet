namespace Hamster.Tests;

public sealed class SpriteRendererTests
{
    [Fact]
    public void Render_WhenTheFrameIsBig_ThenTheImageHasTheFramesOwnSize()
    {
        // Arrange
        string[] rows = [.. Enumerable.Repeat(new string('a', 64), 48)];

        // Act
        var image = SpriteRenderer.Render(rows, new Dictionary<char, uint> { ['a'] = 0xFF112233 });

        // Assert
        Assert.Equal((64, 48), (image.PixelWidth, image.PixelHeight));
    }
}
