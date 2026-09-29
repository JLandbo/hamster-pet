using System.Buffers.Binary;
using System.Globalization;
using Hamster.Core.Languages;

namespace Hamster.Core.Pet;

public sealed record Frame(byte[] Sheet, int Index, int Width, int Height, int Milliseconds);

public sealed record Character(string Name, IReadOnlyDictionary<Mood, Frame[]> Animations)
{
    const string _timingFile = "timing.txt";

    public static IEnumerable<string> Files => [_timingFile, .. Enum.GetValues<Mood>().Select(SheetOf)];

    public static Character Hamster { get; } = Read("Hamster", ReadBuiltIn, fallback: null);

    public static Character FromFolder(string folder) => Read(Path.GetFileName(folder), file => Path.Combine(folder, file) is var path && File.Exists(path) ? File.ReadAllBytes(path) : null, Hamster);

    public static byte[]? ReadBuiltIn(string file)
    {
        using var stream = typeof(Character).Assembly.GetManifestResourceStream(file);
        if (stream is null)
        {
            return null;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static string NameOf(Mood mood) => mood.ToString().ToLowerInvariant();

    static string SheetOf(Mood mood) => $"{NameOf(mood)}.png";

    static Character Read(string name, Func<string, byte[]?> read, Character? fallback)
    {
        var timings = read(_timingFile) is { } timing ? ParseTimings(TextOf(timing)) : null;
        return new(name, Enum.GetValues<Mood>().ToDictionary(mood => mood, mood => AnimationOf(mood) ?? fallback?.Animations[mood] ?? throw Missing(name, SheetOf(mood))));

        Frame[]? AnimationOf(Mood mood)
        {
            if (read(SheetOf(mood)) is not { } sheet)
            {
                return null;
            }
            var milliseconds = (timings ?? throw Missing(name, _timingFile)).GetValueOrDefault(NameOf(mood))
                ?? throw new InvalidDataException(Strings.Format("Character.MissingTiming", _timingFile, SheetOf(mood)));
            return ParseSheet(sheet, SheetOf(mood), milliseconds);
        }
    }

    static string TextOf(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes));
        return reader.ReadToEnd();
    }

    static IReadOnlyDictionary<string, int[]> ParseTimings(string text)
    {
        try
        {
            var timings = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .ToDictionary(parts => parts[0], parts => parts[1..].Select(number => int.Parse(number, CultureInfo.InvariantCulture)).ToArray(), StringComparer.OrdinalIgnoreCase);
            return timings.Values.All(milliseconds => milliseconds.Length > 0 && milliseconds.All(duration => duration > 0)) ? timings : throw BadTiming();
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw BadTiming(exception);
        }
    }

    static InvalidDataException BadTiming(Exception? inner = null) => new(Strings.Format("Character.BadTiming", _timingFile), inner);

    static Frame[] ParseSheet(byte[] png, string file, int[] milliseconds)
    {
        var (width, height) = SizeOf(png);
        return width >= milliseconds.Length && width % milliseconds.Length == 0 && height > 0
            ? [.. milliseconds.Select((duration, index) => new Frame(png, index, width / milliseconds.Length, height, duration))]
            : throw new InvalidDataException(Strings.Format("Character.BadSheet", file, milliseconds.Length));
    }

    static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    static (int Width, int Height) SizeOf(byte[] png) => png.Length >= 24 && png.AsSpan(0, 8).SequenceEqual(PngSignature) && png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
        ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)))
        : (0, 0);

    static InvalidDataException Missing(string name, string file) => new(Strings.Format("Character.MissingFile", name, file));
}
