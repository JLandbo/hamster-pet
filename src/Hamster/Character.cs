using System.Globalization;
using System.IO;

namespace Hamster;

public sealed record Frame(string[] Rows, int Milliseconds)
{
    public int Width => Rows[0].Length;
    public int Height => Rows.Length;
}

public sealed record Character(string Name, IReadOnlyDictionary<char, uint> Palette, IReadOnlyDictionary<Mood, Frame[]> Animations)
{
    const string PaletteFile = "palette.txt";

    public static IEnumerable<string> Files => [PaletteFile, .. Enum.GetValues<Mood>().Select(FileOf)];

    public static Character Hamster { get; } = Read("Hamster", ReadBuiltIn, fallback: null);

    public static Character FromFolder(string folder) => Read(Path.GetFileName(folder), file =>
    {
        var path = Path.Combine(folder, file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }, Hamster);

    public static string? ReadBuiltIn(string file)
    {
        using var stream = typeof(Character).Assembly.GetManifestResourceStream(file);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string FileOf(Mood mood) => $"{mood.ToString().ToLowerInvariant()}.txt";

    static Character Read(string name, Func<string, string?> read, Character? fallback) => new(
        name,
        read(PaletteFile) is { } palette ? ParsePalette(palette) : fallback?.Palette ?? throw Missing(name, PaletteFile),
        Enum.GetValues<Mood>().ToDictionary(mood => mood, mood =>
            read(FileOf(mood)) is { } text ? ParseFrames(text, FileOf(mood)) : fallback?.Animations[mood] ?? throw Missing(name, FileOf(mood))));

    static IReadOnlyDictionary<char, uint> ParsePalette(string text)
    {
        try
        {
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToDictionary(line => line[0], line => 0xFF000000 | uint.Parse(line[1..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"{PaletteFile}: hver linje skal være et tegn og en farve, fx \"a E3D3B7\".", exception);
        }
    }

    static Frame[] ParseFrames(string text, string file)
    {
        Frame[] frames = [.. text.ReplaceLineEndings("\n").Trim('\n').Split("\n\n").Select(frame => ParseFrame(frame.Split('\n'), file))];
        return frames.All(frame => (frame.Width, frame.Height) == (frames[0].Width, frames[0].Height))
            ? frames
            : throw new InvalidDataException($"{file}: alle frames skal have samme størrelse som den første ({frames[0].Width}×{frames[0].Height}).");
    }

    static Frame ParseFrame(string[] lines, string file) =>
        int.TryParse(lines[0], CultureInfo.InvariantCulture, out var milliseconds) && milliseconds > 0
            && lines.Length > 1 && lines.Skip(1).All(row => row.Length == lines[1].Length)
            ? new Frame(lines[1..], milliseconds)
            : throw new InvalidDataException($"{file}: hver frame skal have en varighed i millisekunder og derefter linjer, der alle er lige lange.");

    static InvalidDataException Missing(string name, string file) => new($"{name} mangler {file}.");
}
