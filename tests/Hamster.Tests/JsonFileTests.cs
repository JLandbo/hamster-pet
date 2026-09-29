namespace Hamster.Tests;

public sealed class JsonFileTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string FilePath => Path.Combine(_directory, "chats.json");

    JsonFile<SavedChats> Store() => new(FilePath, SavedChats.Empty);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_ThenReturnsEmpty()
    {
        // Act
        var chats = Store().Load();

        // Assert
        Assert.Same(SavedChats.Empty, chats);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"SessionId":null,"Chats":null}""")]
    [InlineData("""{"SessionId":null,"Chats":[{"Prompt":null,"Answer":"","Status":"Done"}]}""")]
    public void Load_WhenFileCorrupt_ThenReturnsEmpty(string content)
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content);

        // Act
        var chats = Store().Load();

        // Assert
        Assert.Same(SavedChats.Empty, chats);
    }

    [Fact]
    public void Load_WhenSaved_ThenReturnsSameChats()
    {
        // Arrange
        ChatRecord chat = new("Hvad er klokken?", "Æblegrød-tid.", ChatStatus.Done);
        Store().Save(new SavedChats("session-1", [chat], 0.5m));

        // Act
        var chats = Store().Load();

        // Assert
        Assert.Equal(("session-1", chat, 0.5m), (chats.SessionId, Assert.Single(chats.Chats), chats.Cost));
    }

    [Fact]
    public void Save_WhenReadOnly_ThenKeepsTheFileAsItWas()
    {
        // Arrange
        Store().Save(SavedChats.Empty);

        // Act
        new JsonFile<SavedChats>(FilePath, SavedChats.Empty, readOnly: true).Save(new SavedChats("session-1", [], 0.5m));

        // Assert
        Assert.Null(Store().Load().SessionId);
    }

    [Fact]
    public void Save_WhenFileIsLocked_ThenDoesNotThrow()
    {
        // Arrange
        Store().Save(SavedChats.Empty);
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        var exception = Record.Exception(() => Store().Save(SavedChats.Empty));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Save_WhenTheFileIsLockedForAMoment_ThenSavesOnceItIsReleased()
    {
        // Arrange
        Store().Save(SavedChats.Empty);
        var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        new Thread(() =>
        {
            Thread.Sleep(20);
            locked.Dispose();
        }).Start();

        // Act
        Store().Save(new SavedChats("session-1", [], 0.5m));

        // Assert
        Assert.Equal("session-1", Store().Load().SessionId);
    }
}
