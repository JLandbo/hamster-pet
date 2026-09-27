namespace Hamster.Tests;

public class UserRequestTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void CanBeClicked_WhenShown_ThenOnlyAfterASecond(int secondsLater, bool expected)
    {
        // Arrange
        var request = new UserRequest("Kør kommando", "dir");

        // Act
        var clickable = request.CanBeClicked(DateTime.UtcNow.AddSeconds(secondsLater));

        // Assert
        Assert.Equal(expected, clickable);
    }
}
