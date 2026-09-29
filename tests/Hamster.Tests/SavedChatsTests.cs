namespace Hamster.Tests;

public sealed class SavedChatsTests : IDisposable
{
    readonly string _data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_data))
        {
            Directory.Delete(_data, recursive: true);
        }
    }

    [Fact]
    public void FileFor_WhenThereIsNoFolder_ThenIsChatsJson()
    {
        // Act
        var file = SavedChats.FileFor(_data, null);

        // Assert
        Assert.Equal(Path.Combine(_data, "chats.json"), file);
    }

    [Fact]
    public void FileFor_WhenFoldersDiffer_ThenTheirFilesDiffer()
    {
        // Act
        var (first, second) = (SavedChats.FileFor(_data, @"C:\Kode\a"), SavedChats.FileFor(_data, @"C:\Kode\b"));

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FileFor_WhenTheSameFolderIsWrittenDifferently_ThenIsTheSameFile()
    {
        // Act
        var (first, second) = (SavedChats.FileFor(_data, @"C:\Kode\Hamster"), SavedChats.FileFor(_data, @"c:\kode\hamster\"));

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Migrate_WhenUpgradingWhileInAFolder_ThenTheChatsBelongToThatFolder()
    {
        // Arrange
        Directory.CreateDirectory(_data);
        File.WriteAllText(SavedChats.FileFor(_data, null), "{}");

        // Act
        SavedChats.Migrate(_data, @"C:\Kode\Hamster");

        // Assert
        Assert.Equal((false, true), (File.Exists(SavedChats.FileFor(_data, null)), File.Exists(SavedChats.FileFor(_data, @"C:\Kode\Hamster"))));
    }

    [Fact]
    public void Migrate_WhenFoldersAlreadyHaveTheirOwnChats_ThenLeavesTheChatChatsAlone()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_data, "Chats"));
        File.WriteAllText(SavedChats.FileFor(_data, null), "{}");

        // Act
        SavedChats.Migrate(_data, @"C:\Kode\Hamster");

        // Assert
        Assert.True(File.Exists(SavedChats.FileFor(_data, null)));
    }
}
