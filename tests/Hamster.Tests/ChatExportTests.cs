namespace Hamster.Tests;

public sealed class ChatExportTests
{
    [Fact]
    public void ToMarkdown_WhenTwoChats_ThenWritesPromptsAndAnswersInOrder()
    {
        // Arrange
        ChatRecord[] chats = [new("hej", "Hej med dig", ChatStatus.Done), new("hvad er 2+2?", "4", ChatStatus.Done)];

        // Act
        var markdown = ChatExport.ToMarkdown(chats);

        // Assert
        Assert.Equal("## Du\n\nhej\n\n## Claude\n\nHej med dig\n\n---\n\n## Du\n\nhvad er 2+2?\n\n## Claude\n\n4\n", markdown);
    }

    [Fact]
    public void ToMarkdown_WhenAChatIsStillAnswering_ThenSaysSo()
    {
        // Arrange
        ChatRecord[] chats = [new("skriv et digt", "", ChatStatus.Busy)];

        // Act
        var markdown = ChatExport.ToMarkdown(chats);

        // Assert
        Assert.Equal("## Du\n\nskriv et digt\n\n## Claude\n\n(svarer stadig)\n", markdown);
    }
}
