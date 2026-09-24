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
        Assert.StartsWith(expected, shown);
    }

    [Theory]
    [InlineData(12, "12 s")]
    [InlineData(59.9, "59 s")]
    [InlineData(125, "2 min 5 s")]
    public void FormatElapsed_WhenTimePassed_ThenSecondsOrMinutes(double seconds, string expected)
    {
        // Act
        var text = ChatItem.FormatElapsed(TimeSpan.FromSeconds(seconds));

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(ChatStatus.Busy, true)]
    [InlineData(ChatStatus.Done, false)]
    public void RefreshElapsed_WhenCalled_ThenNotifiesOnlyWhileBusy(ChatStatus status, bool expected)
    {
        // Arrange
        var chat = new ChatItem("hej") { Status = status };
        var notified = false;
        chat.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(ChatItem.DisplayAnswer);

        // Act
        chat.RefreshElapsed();

        // Assert
        Assert.Equal(expected, notified);
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
