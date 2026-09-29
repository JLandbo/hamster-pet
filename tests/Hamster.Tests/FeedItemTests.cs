namespace Hamster.Tests;

public sealed class FeedItemTests
{
    [Theory]
    [InlineData(0.5, "nu")]
    [InlineData(12, "12 min")]
    [InlineData(150, "2 t")]
    [InlineData(1500, "i går")]
    [InlineData(4400, "3 dage")]
    public void Ago_WhenTimeHasPassed_ThenSaysHowLongAgo(double minutes, string expected)
    {
        // Act
        var ago = FeedItem.Ago(TimeSpan.FromMinutes(minutes));

        // Assert
        Assert.Equal(expected, ago);
    }

    [Fact]
    public void DetailAt_WhenTheItemHasARepositoryAndUpdateTime_ThenShowsBoth()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var item = new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", now.AddHours(-2));

        // Act
        var detail = item.DetailAt(now);

        // Assert
        Assert.Equal("app · 2 t", detail);
    }

    [Fact]
    public void DetailAt_WhenThePullRequestIsADraft_ThenSaysSo()
    {
        // Arrange
        var item = new FeedItem("GitHub", "Mine åbne PR'er", "#11", "Ny knap", "https://github.com/AciesDK/app/pull/11", "app", Draft: true);

        // Act
        var detail = item.DetailAt(DateTimeOffset.Now);

        // Assert
        Assert.Equal("app · kladde", detail);
    }

    [Fact]
    public void KeyOrder_WhenKeysHaveNumbers_ThenSortsByTheirNumber()
    {
        // Arrange
        string[] keys = ["ACS-17578", "#498", "ACS-5588", "#11"];

        // Act
        var sorted = keys.Order(FeedItem.KeyOrder);

        // Assert
        Assert.Equal(["#11", "#498", "ACS-5588", "ACS-17578"], sorted);
    }
}
