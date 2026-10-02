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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormatResetCountdown_WhenTheResetTimeIsUnknown_ThenReturnsADash(bool includeDays)
    {
        // Act
        var countdown = Usage.FormatResetCountdown(null, _reset, includeDays);

        // Assert
        Assert.Equal("—", countdown);
    }

    [Theory]
    [InlineData(-86400, "00:00", "00:00:00")]
    [InlineData(-1, "00:00", "00:00:00")]
    [InlineData(0, "00:00", "00:00:00")]
    [InlineData(1, "00:01", "00:00:01")]
    [InlineData(59, "00:01", "00:00:01")]
    [InlineData(60, "00:01", "00:00:01")]
    [InlineData(61, "00:02", "00:00:02")]
    [InlineData(1860, "00:31", "00:00:31")]
    [InlineData(3540, "00:59", "00:00:59")]
    [InlineData(3599, "01:00", "00:01:00")]
    [InlineData(3600, "01:00", "00:01:00")]
    [InlineData(3601, "01:01", "00:01:01")]
    [InlineData(9060, "02:31", "00:02:31")]
    [InlineData(86399, "24:00", "01:00:00")]
    [InlineData(86400, "24:00", "01:00:00")]
    [InlineData(86401, "24:01", "01:00:01")]
    [InlineData(365460, "101:31", "04:05:31")]
    public void FormatResetCountdown_WhenTheResetTimeIsKnown_ThenFormatsTheRemainingTime(int secondsUntilReset, string expectedHours, string expectedDays)
    {
        // Act
        var countdownHours = Usage.FormatResetCountdown(_reset.AddSeconds(secondsUntilReset), _reset, includeDays: false);
        var countdownDays = Usage.FormatResetCountdown(_reset.AddSeconds(secondsUntilReset), _reset, includeDays: true);

        // Assert
        Assert.Equal(expectedHours, countdownHours);
        Assert.Equal(expectedDays, countdownDays);
    }

    [Theory]
    [InlineData(false, "00:01")]
    [InlineData(true, "00:00:01")]
    public void FormatResetCountdown_WhenOneTickRemains_ThenRoundsUpToOneMinute(bool includeDays, string expected)
    {
        // Act
        var countdown = Usage.FormatResetCountdown(_reset.AddTicks(1), _reset, includeDays);

        // Assert
        Assert.Equal(expected, countdown);
    }

    [Theory]
    [InlineData(false, "02:31")]
    [InlineData(true, "00:02:31")]
    public void FormatResetCountdown_WhenTheTimeOffsetsDiffer_ThenUsesTheActualRemainingTime(bool includeDays, string expected)
    {
        // Arrange
        var resetTime = new DateTimeOffset(2026, 9, 28, 16, 31, 0, TimeSpan.FromHours(2));
        var now = _reset.ToOffset(TimeSpan.FromHours(-5));

        // Act
        var countdown = Usage.FormatResetCountdown(resetTime, now, includeDays);

        // Assert
        Assert.Equal(expected, countdown);
    }

    [Theory]
    [InlineData(0, "02:31", "00:02:31")]
    [InlineData(1, "02:30", "00:02:30")]
    [InlineData(31, "02:00", "00:02:00")]
    [InlineData(91, "01:00", "00:01:00")]
    [InlineData(151, "00:00", "00:00:00")]
    [InlineData(152, "00:00", "00:00:00")]
    [InlineData(1440, "00:00", "00:00:00")]
    public void FormatResetCountdown_WhenTimePasses_ThenCountsDownAndKeepsExpiredValuesAtZero(int minutesElapsed, string expectedHours, string expectedDays)
    {
        // Arrange
        var resetTime = _reset.AddHours(2).AddMinutes(31);

        // Act
        var countdownHours = Usage.FormatResetCountdown(resetTime, _reset.AddMinutes(minutesElapsed), includeDays: false);
        var countdownDays = Usage.FormatResetCountdown(resetTime, _reset.AddMinutes(minutesElapsed), includeDays: true);

        // Assert
        Assert.Equal(expectedHours, countdownHours);
        Assert.Equal(expectedDays, countdownDays);
    }
}
