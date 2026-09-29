namespace Hamster.Tests;

public sealed class ChatItemTests
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

    [Fact]
    public void DisplayAnswer_WhenPartialTextHasNotBeenRefreshed_ThenStillChews()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.StartPartialAnswer();
        chat.AppendPartialAnswer("Delvist svar");

        // Act
        var shown = chat.DisplayAnswer;

        // Assert
        Assert.Matches(@"^Tygger… \d+ s$", shown);
    }

    [Fact]
    public void RefreshDisplayAnswer_WhenPartialTextArrived_ThenShowsAllChunks()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.StartPartialAnswer();
        chat.AppendPartialAnswer("Delvist ");
        chat.AppendPartialAnswer("svar");

        // Act
        chat.RefreshDisplayAnswer();

        // Assert
        Assert.Equal("Delvist svar", chat.DisplayAnswer);
    }

    [Fact]
    public void StartPartialAnswer_WhenAnotherClaudeMessageStarts_ThenKeepsEarlierTextUntilNewTextArrives()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.StartPartialAnswer();
        chat.AppendPartialAnswer("Jeg undersøger det.");
        chat.RefreshDisplayAnswer();

        // Act
        chat.StartPartialAnswer();
        chat.RefreshDisplayAnswer();
        var beforeNewText = chat.DisplayAnswer;
        chat.AppendPartialAnswer("Her er svaret.");
        chat.RefreshDisplayAnswer();

        // Assert
        Assert.Equal("Jeg undersøger det.", beforeNewText);
        Assert.Equal("Jeg undersøger det.\n\nHer er svaret.", chat.DisplayAnswer);
    }

    [Fact]
    public void CompletePartialAnswer_WhenTheCompleteBlockArrives_ThenReplacesItsPreviewWithoutDuplication()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.StartPartialAnswer();
        chat.AppendPartialAnswer("Delvist svar");

        // Act
        chat.CompletePartialAnswer("Det komplette svar", showWhileBusy: true);
        chat.RefreshDisplayAnswer();

        // Assert
        Assert.Equal(("Det komplette svar", "Det komplette svar"), (chat.Answer, chat.DisplayAnswer));
    }

    [Fact]
    public void CompleteResult_WhenItMatchesTheLastCompleteBlock_ThenDoesNotDuplicateIt()
    {
        // Arrange
        var chat = new ChatItem("hej");
        chat.CompletePartialAnswer("Svar", showWhileBusy: true);

        // Act
        chat.CompleteResult("Svar");
        chat.Status = ChatStatus.Done;

        // Assert
        Assert.Equal("Svar", chat.Answer);
    }

    [Fact]
    public void CompletePartialAnswer_WhenLiveTextIsDisabled_ThenHidesItUntilDone()
    {
        // Arrange
        var chat = new ChatItem("hej");

        // Act
        chat.CompletePartialAnswer("Svar", showWhileBusy: false);
        var busy = chat.DisplayAnswer;
        chat.Status = ChatStatus.Done;

        // Assert
        Assert.Matches(@"^Tygger… \d+ s$", busy);
        Assert.Equal("Svar", chat.DisplayAnswer);
    }

    [Fact]
    public void AddPrompt_WhenAFollowUpIsAdded_ThenShowsBothInTheSameChat()
    {
        // Arrange
        var chat = new ChatItem("første");

        // Act
        chat.AddPrompt("anden", title: null);

        // Assert
        Assert.Equal((2, "første\n\nanden"), (chat.Prompts.Count, chat.DisplayPrompt));
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
    public void RefreshDisplayAnswer_WhenCalled_ThenNotifiesOnlyWhileBusy(ChatStatus status, bool expected)
    {
        // Arrange
        var chat = new ChatItem("hej") { Status = status };
        var notified = false;
        chat.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(ChatItem.DisplayAnswer);

        // Act
        chat.RefreshDisplayAnswer();

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
