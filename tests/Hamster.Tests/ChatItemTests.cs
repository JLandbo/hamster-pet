namespace Hamster.Tests;

public class ChatItemTests
{
    [Theory]
    [InlineData(ChatStatus.Busy, "Tygger…")]
    [InlineData(ChatStatus.Done, "")]
    public void DisplayAnswer_WhenAnswerIsEmpty_ThenOnlyChewsWhileBusy(ChatStatus status, string expected)
    {
        // Arrange
        var chat = new ChatItem("hej") { Status = status };

        // Act
        var shown = chat.DisplayAnswer;

        // Assert
        Assert.Equal(expected, shown);
    }
}
