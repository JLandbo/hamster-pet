namespace Hamster.Tests;

public sealed class PetSettingsTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    public void Dispose() => File.Delete(file);

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeSizes_ThenSizeIs100Percent()
    {
        // Arrange
        File.WriteAllText(file, """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default).Load();

        // Assert
        Assert.Equal(100, pet.SizePercent);
    }

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeAutoHide_ThenAutoHideIsOn()
    {
        // Arrange
        File.WriteAllText(file, """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default).Load();

        // Assert
        Assert.True(pet.AutoHide);
    }

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeHideSeconds_ThenHidesAfter60Seconds()
    {
        // Arrange
        File.WriteAllText(file, """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default).Load();

        // Assert
        Assert.Equal(60, pet.HideSeconds);
    }

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeChatsExpanded_ThenChatsAreExpanded()
    {
        // Arrange
        File.WriteAllText(file, """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default).Load();

        // Assert
        Assert.True(pet.ChatsExpanded);
    }

    [Fact]
    public void Save_WhenChatsAreCollapsed_ThenLoadReturnsIt()
    {
        // Arrange
        var store = new JsonFile<PetSettings>(file, PetSettings.Default);

        // Act
        store.Save(PetSettings.Default with { ChatsExpanded = false });

        // Assert
        Assert.False(store.Load().ChatsExpanded);
    }

    [Fact]
    public void Load_WhenPetSettingsWereSavedBeforeWebImages_ThenWebImagesAreOff()
    {
        // Arrange
        File.WriteAllText(file, """{"CharacterName": "Hamster"}""");

        // Act
        var pet = new JsonFile<PetSettings>(file, PetSettings.Default).Load();

        // Assert
        Assert.False(pet.ShowWebImages);
    }

    [Fact]
    public void Save_WhenSizeChanges_ThenLoadReturnsIt()
    {
        // Arrange
        var store = new JsonFile<PetSettings>(file, PetSettings.Default);

        // Act
        store.Save(PetSettings.Default with { SizePercent = 250 });

        // Assert
        Assert.Equal(250, store.Load().SizePercent);
    }
}
