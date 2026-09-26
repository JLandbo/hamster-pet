namespace Hamster.Tests;

public class ChatItemTests
{
    [Fact]
    public void Shown_WhenSetToTheSameValue_ThenDoesNotNotify()
    {
        // Arrange
        var chat = new ChatItem("hej");
        var notified = false;
        chat.PropertyChanged += (_, _) => notified = true;

        // Act
        chat.Shown = true;

        // Assert
        Assert.False(notified);
    }

    [Fact]
    public void DisplayAnswer_WhenDone_ThenShowsTheAnswer()
    {
        // Arrange
        var chat = new ChatItem("hej") { Answer = "Svar", Status = ChatStatus.Done };

        // Act
        var shown = chat.DisplayAnswer;

        // Assert
        Assert.Equal("Svar", shown);
    }

    [Fact]
    public void DisplayAnswer_WhenBusy_ThenChewsWithTheTimeSoFar()
    {
        // Arrange
        var chat = new ChatItem("hej") { Answer = "Svar" };

        // Act
        var shown = chat.DisplayAnswer;

        // Assert
        Assert.Matches(@"^Tygger… \d+ s$", shown);
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

    [Fact]
    public void From_WhenTheSavedChatUsedTools_ThenItsCommandsAndSourcesComeBack()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.Commands.Lines.Add("Bash: dotnet test");
        chat.Sources.Lines.Add("WebFetch: https://example.com");

        // Act
        var restored = ChatItem.From(chat.ToRecord());

        // Assert
        Assert.Equal(["Bash: dotnet test"], restored.Commands.Lines);
        Assert.Equal(["WebFetch: https://example.com"], restored.Sources.Lines);
    }
}
