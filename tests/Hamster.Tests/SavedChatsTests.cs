namespace Hamster.Tests;

public sealed class SavedChatsTests : IDisposable
{
    readonly string data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(data))
            Directory.Delete(data, recursive: true);
    }

    [Fact]
    public void FileFor_WhenThereIsNoFolder_ThenIsChatsJson()
    {
        // Act
        var file = SavedChats.FileFor(data, null);

        // Assert
        Assert.Equal(Path.Combine(data, "chats.json"), file);
    }

    [Fact]
    public void FileFor_WhenFoldersDiffer_ThenTheirFilesDiffer()
    {
        // Act
        var (first, second) = (SavedChats.FileFor(data, @"C:\Kode\a"), SavedChats.FileFor(data, @"C:\Kode\b"));

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FileFor_WhenTheSameFolderIsWrittenDifferently_ThenIsTheSameFile()
    {
        // Act
        var (first, second) = (SavedChats.FileFor(data, @"C:\Kode\Hamster"), SavedChats.FileFor(data, @"c:\kode\hamster\"));

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Migrate_WhenUpgradingWhileInAFolder_ThenTheChatsBelongToThatFolder()
    {
        // Arrange
        Directory.CreateDirectory(data);
        File.WriteAllText(SavedChats.FileFor(data, null), "{}");

        // Act
        SavedChats.Migrate(data, @"C:\Kode\Hamster");

        // Assert
        Assert.Equal((false, true), (File.Exists(SavedChats.FileFor(data, null)), File.Exists(SavedChats.FileFor(data, @"C:\Kode\Hamster"))));
    }

    [Fact]
    public void Migrate_WhenFoldersAlreadyHaveTheirOwnChats_ThenLeavesTheChatChatsAlone()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(data, "Chats"));
        File.WriteAllText(SavedChats.FileFor(data, null), "{}");

        // Act
        SavedChats.Migrate(data, @"C:\Kode\Hamster");

        // Assert
        Assert.True(File.Exists(SavedChats.FileFor(data, null)));
    }
}
