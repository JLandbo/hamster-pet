namespace Hamster.Tests;

public sealed class UsageTests
{
    static readonly DateTimeOffset _reset = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, 0.95)]
    [InlineData(1, 0)]
    public void FiveHourAt_WhenTheWindowResets_ThenCountsFromZero(int minutesAfterReset, double expected)
    {
        // Arrange
        var usage = new Usage(0.95, 0.5, _reset, _reset.AddDays(3));

        // Act
        var fiveHour = usage.FiveHourAt(_reset.AddMinutes(minutesAfterReset));

        // Assert
        Assert.Equal(expected, fiveHour);
    }

    [Theory]
    [InlineData(-1, 0.5)]
    [InlineData(1, 0)]
    public void SevenDayAt_WhenTheWindowResets_ThenCountsFromZero(int minutesAfterReset, double expected)
    {
        // Arrange
        var usage = new Usage(0.95, 0.5, _reset.AddDays(-1), _reset);

        // Act
        var sevenDay = usage.SevenDayAt(_reset.AddMinutes(minutesAfterReset));

        // Assert
        Assert.Equal(expected, sevenDay);
    }

    [Theory]
    [InlineData(0.95, 0.5, 0.95)]
    [InlineData(0.2, 0.92, 0.92)]
    public void HighestAt_WhenEitherWindowIsHigher_ThenReturnsIt(double fiveHour, double sevenDay, double expected)
    {
        // Act
        var highest = new Usage(fiveHour, sevenDay).HighestAt(_reset);

        // Assert
        Assert.Equal(expected, highest);
    }

    [Fact]
    public void HighestAt_WhenTheFiveHourWindowHasReset_ThenReturnsTheWeek()
    {
        // Act
        var highest = new Usage(0.95, 0.5, _reset.AddMinutes(-1), _reset.AddDays(3)).HighestAt(_reset);

        // Assert
        Assert.Equal(0.5, highest);
    }

    [Fact]
    public void FiveHourAt_WhenTheResetTimeIsUnknown_ThenKeepsTheUsage()
    {
        // Act
        var fiveHour = new Usage(0.95, 0.5).FiveHourAt(_reset);

        // Assert
        Assert.Equal(0.95, fiveHour);
    }
}
