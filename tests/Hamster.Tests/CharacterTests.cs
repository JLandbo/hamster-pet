using System.Buffers.Binary;
using System.Text;

namespace Hamster.Tests;

public sealed class CharacterTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public CharacterTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Hamster_WhenLoaded_ThenEveryMoodHasFrames()
    {
        // Act
        var moods = Character.Hamster.Animations.Where(animation => animation.Value.Length > 0).Select(animation => animation.Key);

        // Assert
        Assert.Equal(Enum.GetValues<Mood>().Order(), moods.Order());
    }

    [Fact]
    public void FromFolder_WhenAnimationsAreMissing_ThenUsesTheHamsters()
    {
        // Arrange
        WriteAwake(40, 10, """{"width": 20, "height": 10, "animations": {"awake": 100}}""");

        // Act
        var character = Character.FromFolder(_folder);

        // Assert
        Assert.Same(Character.Hamster.Animations[Mood.Dance], character.Animations[Mood.Dance]);
    }

    [Fact]
    public void FromFolder_WhenEachFrameHasADuration_ThenSplitsThePngIntoThoseFrames()
    {
        // Arrange
        WriteAwake(40, 10, """{"width": 20, "height": 10, "animations": {"awake": [100, 250]}}""");

        // Act
        var frames = Character.FromFolder(_folder).Animations[Mood.Awake];

        // Assert
        Assert.Equal([(0, 20, 10, 100), (1, 20, 10, 250)], frames.Select(frame => (frame.Index, frame.Width, frame.Height, frame.Milliseconds)));
    }

    [Fact]
    public void FromFolder_WhenAnAnimationHasOneDuration_ThenEveryFrameInThePngUsesIt()
    {
        // Arrange
        WriteAwake(60, 10, """{"width": 20, "height": 10, "animations": {"awake": 150}}""");

        // Act
        var frames = Character.FromFolder(_folder).Animations[Mood.Awake];

        // Assert
        Assert.Equal([(0, 150), (1, 150), (2, 150)], frames.Select(frame => (frame.Index, frame.Milliseconds)));
    }

    [Fact]
    public void FromFolder_WhenTheSpriteStartsWithAByteOrderMark_ThenStillReadsIt()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), PngHeader(40, 10));
        File.WriteAllText(Path.Combine(_folder, "sprite.json"), """{"width": 20, "height": 10, "animations": {"awake": 100}}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var frames = Character.FromFolder(_folder).Animations[Mood.Awake];

        // Assert
        Assert.Equal(2, frames.Length);
    }

    [Theory]
    [InlineData(30, 10, "[100, 100]")]
    [InlineData(30, 10, "100")]
    [InlineData(40, 12, "100")]
    public void FromFolder_WhenThePngDoesNotFitTheFrames_ThenSaysWhichFile(int width, int height, string awake)
    {
        // Arrange
        WriteAwake(width, height, """{"width": 20, "height": 10, "animations": {"awake": AWAKE}}""".Replace("AWAKE", awake));

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.StartsWith("awake.png:", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Fact]
    public void FromFolder_WhenThereIsNoSpriteFile_ThenSaysSo()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), PngHeader(40, 10));

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.Contains("sprite.json", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Fact]
    public void FromFolder_WhenTheSpriteLacksTheAnimation_ThenSaysWhichPng()
    {
        // Arrange
        WriteAwake(40, 10, """{"width": 20, "height": 10, "animations": {"sleep": 100}}""");

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.Contains("awake.png", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Theory]
    [InlineData("""{"width": 20""")]
    [InlineData("""{"width": 0, "height": 10, "animations": {"awake": 100}}""")]
    [InlineData("""{"width": 20, "height": 10}""")]
    [InlineData("""{"width": 20, "height": 10, "animations": {"awake": "x"}}""")]
    [InlineData("""{"width": 20, "height": 10, "animations": {"awake": []}}""")]
    [InlineData("""{"width": 20, "height": 10, "animations": {"awake": [100, 0]}}""")]
    public void FromFolder_WhenTheSpriteIsBroken_ThenSaysSo(string sprite)
    {
        // Arrange
        WriteAwake(40, 10, sprite);

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.StartsWith("sprite.json:", Assert.Throws<InvalidDataException>(loading).Message);
    }

    void WriteAwake(int width, int height, string sprite)
    {
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), PngHeader(width, height));
        File.WriteAllText(Path.Combine(_folder, "sprite.json"), sprite);
    }

    static byte[] PngHeader(int width, int height)
    {
        byte[] header = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), height);
        return header;
    }
}
