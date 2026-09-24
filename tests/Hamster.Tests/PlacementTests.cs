using System.Windows;

namespace Hamster.Tests;

public class PlacementTests
{
    static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(800, 600, 800, 600)]
    [InlineData(3000, 600, 1920, 600)]
    [InlineData(-500, -200, 0, 0)]
    public void ClampedTo_WhenPlacementGiven_ThenStaysOnScreenWithItsSize(double right, double bottom, double expectedRight, double expectedBottom)
    {
        // Act
        var placement = new Placement(right, bottom, 400, 900).ClampedTo(Screen);

        // Assert
        Assert.Equal(new Placement(expectedRight, expectedBottom, 400, 900), placement);
    }
}
