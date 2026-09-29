using System.Collections.ObjectModel;

namespace Hamster.Tests;

public sealed class RevealedChatsTests
{
    readonly ObservableCollection<ChatItem> _chats = [new("første"), new("anden"), new("tredje"), new("fjerde")];
    readonly RevealedChats _revealed;

    public RevealedChatsTests() => _revealed = new(_chats);

    bool[] Revealed() => [.. _chats.Index().Select(chat => _revealed.Contains(chat.Index))];

    [Fact]
    public void Contains_WhenStartedFromNewest_ThenOnlyTheNewestIsRevealed()
    {
        // Act
        _revealed.StartFromNewest();

        // Assert
        Assert.Equal([false, false, false, true], Revealed());
    }

    [Fact]
    public void RevealOlder_WhenCalled_ThenRevealsTheNextChatFromTheBottom()
    {
        // Arrange
        _revealed.StartFromNewest();

        // Act
        _revealed.RevealOlder();

        // Assert
        Assert.Equal([false, false, true, true], Revealed());
    }

    [Fact]
    public void RevealOlder_WhenEveryChatIsRevealed_ThenSaysSo()
    {
        // Arrange
        _revealed.StartFromNewest();

        // Act
        bool[] revealedMore = [_revealed.RevealOlder(), _revealed.RevealOlder(), _revealed.RevealOlder(), _revealed.RevealOlder()];

        // Assert
        Assert.Equal([true, true, true, false], revealedMore);
    }

    [Fact]
    public void Contains_WhenAChatIsAddedWhileRevealing_ThenItIsRevealedWithTheOthers()
    {
        // Arrange
        _revealed.StartFromNewest();
        _revealed.RevealOlder();

        // Act
        _chats.Add(new("femte"));

        // Assert
        Assert.Equal([false, false, true, true, true], Revealed());
    }

    [Fact]
    public void Contains_WhenAnOlderHiddenChatIsRemoved_ThenTheRevealedChatsStayRevealed()
    {
        // Arrange
        _revealed.StartFromNewest();
        _revealed.RevealOlder();

        // Act
        _chats.RemoveAt(0);

        // Assert
        Assert.Equal([false, true, true], Revealed());
    }
}
