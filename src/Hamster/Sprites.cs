using System.Globalization;
using System.IO;

namespace Hamster;

public sealed record Frame(string[] Rows, int Milliseconds);

public static class Sprites
{
    public const int Width = 40;
    public const int Height = 36;

    public static IReadOnlyDictionary<char, uint> Palette { get; } = new Dictionary<char, uint>
    {
        ['a'] = 0xFFE3D3B7, ['b'] = 0xFFF4EBD8, ['c'] = 0xFFFCCC70, ['d'] = 0xFFE08F60, ['e'] = 0xFFA89679,
        ['f'] = 0xFFFDB029, ['g'] = 0xFFFBEAB4, ['h'] = 0xFFAC6437, ['k'] = 0xFF6B5A45, ['l'] = 0xFF7EC8F0,
        ['p'] = 0xFFF06A8A, ['w'] = 0xFFFFFFFF, ['y'] = 0xFFFFD23F, ['z'] = 0xFF5B6B8C,
    };

    public static IReadOnlyDictionary<Mood, Frame[]> Animations { get; } = Enum.GetValues<Mood>().ToDictionary(mood => mood, Load);

    static Frame[] Load(Mood mood)
    {
        var name = $"{mood.ToString().ToLowerInvariant()}.txt";
        using var stream = typeof(Sprites).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Animations/{name} mangler.");
        using var reader = new StreamReader(stream);
        return [.. reader.ReadToEnd().ReplaceLineEndings("\n").Trim('\n').Split("\n\n").Select(ParseFrame)];
    }

    static Frame ParseFrame(string text)
    {
        var lines = text.Split('\n');
        return new Frame(lines[1..], int.Parse(lines[0], CultureInfo.InvariantCulture));
    }
}
