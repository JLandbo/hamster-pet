using System.Windows;

namespace Hamster.Tests;

public sealed class PlacementTests
{
    static readonly Rect _screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(800, 600, 800, 600)]
    [InlineData(3000, 600, 1920, 600)]
    [InlineData(-500, -200, 0, 0)]
    public void ClampedTo_WhenPlacementGiven_ThenStaysOnScreenWithItsSize(double right, double bottom, double expectedRight, double expectedBottom)
    {
        // Act
        var placement = new Placement(right, bottom, 400, 900).ClampedTo(_screen);

        // Assert
        Assert.Equal(new Placement(expectedRight, expectedBottom, 400, 900), placement);
    }

    [Fact]
    public void DraggedTo_WhenDraggedWiderThanTheNarrowest_ThenTakesTheDraggedWidth()
    {
        // Act
        var placement = new Placement(1920, 1080, 450, 900).DraggedTo(700, 631);

        // Assert
        Assert.Equal(700, placement.Width);
    }

    [Theory]
    [InlineData(631, 631, 450)]
    [InlineData(600, 631, 450)]
    [InlineData(250, 300, 250)]
    public void DraggedTo_WhenNotDraggedWiderThanTheNarrowest_ThenTakesTheSmallerOfTheDraggedAndTheChosenWidth(double width, double narrowest, double expected)
    {
        // Act
        var placement = new Placement(1920, 1080, 450, 900).DraggedTo(width, narrowest);

        // Assert
        Assert.Equal(expected, placement.Width);
    }

    [Fact]
    public void CenteredIn_WhenTheScreenIsGiven_ThenPutsThePetInTheMiddleWithItsSize()
    {
        // Act
        var placement = new Placement(1920, 1080, 400, 900).CenteredIn(_screen, new Size(160, 144));

        // Assert
        Assert.Equal(new Placement(1040, 612, 400, 900), placement);
    }

    [Fact]
    public void FitIn_WhenTheScreenIsSmaller_ThenShrinksToIt()
    {
        // Act
        var size = new WindowSize(2400, 1400).FitIn(_screen);

        // Assert
        Assert.Equal(new WindowSize(1920, 1080), size);
    }

    [Fact]
    public void FitIn_WhenTheHeightFollowsTheContent_ThenKeepsItThatWay()
    {
        // Act
        var size = new WindowSize(1080, double.NaN).FitIn(_screen);

        // Assert
        Assert.True(double.IsNaN(size.Height));
    }
}
