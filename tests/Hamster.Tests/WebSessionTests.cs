using System.Net;
using System.Net.Http;

namespace Hamster.Tests;

public sealed class WebSessionTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void IsLoggedIn_WhenTheSiteAnswers_ThenTellsWhetherTheSessionWorks(HttpStatusCode status, bool expected)
    {
        // Arrange
        using var response = new HttpResponseMessage(status);

        // Act
        var loggedIn = WebSession.IsLoggedIn(response);

        // Assert
        Assert.Equal(expected, loggedIn);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void IsLoggedIn_WhenTheSiteRefusesOrFails_ThenThrowsInsteadOfLoggingOut(HttpStatusCode status)
    {
        // Arrange
        using var response = new HttpResponseMessage(status);

        // Act
        var exception = Record.Exception(() => WebSession.IsLoggedIn(response));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
