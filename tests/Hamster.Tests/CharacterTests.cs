namespace Hamster.Tests;

public sealed class CharacterTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public CharacterTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void Hamster_WhenLoaded_ThenEveryMoodHasFrames()
    {
        // Act
        var moods = Character.Hamster.Animations.Where(animation => animation.Value.Length > 0).Select(animation => animation.Key);

        // Assert
        Assert.Equal(Enum.GetValues<Mood>().Order(), moods.Order());
    }

    [Fact]
    public void Hamster_WhenLoaded_ThenSpinStartsOnTheNormalBody()
    {
        // Act
        var firstSpinFrame = Character.Hamster.Animations[Mood.Spin][0].Rows;

        // Assert
        Assert.Equal(Character.Hamster.Animations[Mood.Awake][0].Rows, firstSpinFrame);
    }

    [Fact]
    public void Hamster_WhenLoaded_ThenFramesOnlyUseItsPalette()
    {
        // Act
        var colors = Character.Hamster.Animations.Values.SelectMany(frames => frames).SelectMany(frame => frame.Rows).SelectMany(row => row).Distinct();

        // Assert
        Assert.All(colors, color => Assert.True(color == '.' || Character.Hamster.Palette.ContainsKey(color), $"Unknown color '{color}'"));
    }

    [Fact]
    public void FromFolder_WhenFilesAreMissing_ThenUsesTheHamsters()
    {
        // Arrange
        File.WriteAllText(Path.Combine(folder, "palette.txt"), "a 112233\n");

        // Act
        var character = Character.FromFolder(folder);

        // Assert
        Assert.Equal(0xFF112233u, character.Palette['a']);
        Assert.Same(Character.Hamster.Animations[Mood.Dance], character.Animations[Mood.Dance]);
    }

    [Fact]
    public void FromFolder_WhenAFrameHasTheWrongSize_ThenSaysWhichFile()
    {
        // Arrange
        File.WriteAllText(Path.Combine(folder, "sleep.txt"), "700\n....\n");

        // Act
        var loading = () => Character.FromFolder(folder);

        // Assert
        Assert.StartsWith("sleep.txt:", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Fact]
    public void FromFolder_WhenThePaletteIsBroken_ThenSaysSo()
    {
        // Arrange
        File.WriteAllText(Path.Combine(folder, "palette.txt"), "a blå\n");

        // Act
        var loading = () => Character.FromFolder(folder);

        // Assert
        Assert.StartsWith("palette.txt:", Assert.Throws<InvalidDataException>(loading).Message);
    }
}
