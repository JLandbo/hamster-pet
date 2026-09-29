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
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), Png(40, 10));
        File.WriteAllText(Path.Combine(_folder, "timing.txt"), "awake 100 250\n");

        // Act
        var character = Character.FromFolder(_folder);

        // Assert
        Assert.Same(Character.Hamster.Animations[Mood.Dance], character.Animations[Mood.Dance]);
    }

    [Fact]
    public void FromFolder_WhenAnAnimationIsAPng_ThenSplitsItIntoFramesWithTheirTimings()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), Png(40, 10));
        File.WriteAllText(Path.Combine(_folder, "timing.txt"), "awake 100 250\n");

        // Act
        var frames = Character.FromFolder(_folder).Animations[Mood.Awake];

        // Assert
        Assert.Equal([(0, 20, 10, 100), (1, 20, 10, 250)], frames.Select(frame => (frame.Index, frame.Width, frame.Height, frame.Milliseconds)));
    }

    [Fact]
    public void FromFolder_WhenTheTimingStartsWithAByteOrderMark_ThenStillReadsIt()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), Png(40, 10));
        File.WriteAllText(Path.Combine(_folder, "timing.txt"), "awake 100 250\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var frames = Character.FromFolder(_folder).Animations[Mood.Awake];

        // Assert
        Assert.Equal(2, frames.Length);
    }

    [Fact]
    public void FromFolder_WhenThePngsWidthDoesNotFitTheFrames_ThenSaysWhichFile()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), Png(30, 10));
        File.WriteAllText(Path.Combine(_folder, "timing.txt"), "awake 100 100 100 100\n");

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.StartsWith("awake.png:", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Theory]
    [InlineData("sleep 100\n")]
    [InlineData(null)]
    public void FromFolder_WhenThePngHasNoTiming_ThenSaysSo(string? timing)
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_folder, "awake.png"), Png(40, 10));
        if (timing is not null)
        {
            File.WriteAllText(Path.Combine(_folder, "timing.txt"), timing);
        }

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.Contains("timing.txt", Assert.Throws<InvalidDataException>(loading).Message);
    }

    [Theory]
    [InlineData("awake 100 x\n")]
    [InlineData("awake\n")]
    [InlineData("awake 0\n")]
    public void FromFolder_WhenTheTimingIsBroken_ThenSaysSo(string timing)
    {
        // Arrange
        File.WriteAllText(Path.Combine(_folder, "timing.txt"), timing);

        // Act
        var loading = () => Character.FromFolder(_folder);

        // Assert
        Assert.StartsWith("timing.txt:", Assert.Throws<InvalidDataException>(loading).Message);
    }

    static byte[] Png(int width, int height)
    {
        byte[] header = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), height);
        return header;
    }
}
