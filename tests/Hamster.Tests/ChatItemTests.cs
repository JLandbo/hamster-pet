namespace Hamster.Tests;

public class ChatItemTests
{
    [Theory]
    [InlineData(ChatStatus.Busy, "Tygger…")]
    [InlineData(ChatStatus.Done, "Svar")]
    public void DisplayAnswer_WhenAnswered_ThenShowsAnswerUnlessBusy(ChatStatus status, string expected)
    {
        // Arrange
        var chat = new ChatItem("hej") { Answer = "Svar", Status = status };

        // Act
        var shown = chat.DisplayAnswer;

        // Assert
        Assert.Equal(expected, shown);
    }

    [Fact]
    public void Status_WhenAnswered_ThenNotifiesDisplayAnswer()
    {
        // Arrange
        var chat = new ChatItem("hej");
        List<string?> changed = [];
        chat.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        chat.Status = ChatStatus.Done;

        // Assert
        Assert.Contains(nameof(ChatItem.DisplayAnswer), changed);
    }
}
