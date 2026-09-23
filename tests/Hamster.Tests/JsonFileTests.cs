namespace Hamster.Tests;

public sealed class JsonFileTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string FilePath => Path.Combine(directory, "chats.json");

    JsonFile<SavedChats> Store() => new(FilePath, SavedChats.Empty);

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ThenReturnsEmpty()
    {
        // Act
        var chats = Store().Load();

        // Assert
        Assert.Same(SavedChats.Empty, chats);
    }

    [Fact]
    public void Load_WhenFileCorrupt_ThenReturnsEmpty()
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{");

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
}
