using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hamster.Core.Languages;

namespace Hamster.Core.Pet;

public sealed record Frame(byte[] Sheet, int Index, int Width, int Height, int Milliseconds);

public sealed record Character(string Name, IReadOnlyDictionary<Mood, Frame[]> Animations)
{
    const string _spriteFile = "sprite.json";

    sealed record Sprite(int Width, int Height, JsonObject Animations);

    public static IEnumerable<string> Files => [_spriteFile, .. Enum.GetValues<Mood>().Select(SheetOf)];

    public static Character Hamster { get; } = Read("Hamster", ReadBuiltIn, fallback: null);

    public static Character FromFolder(string folder) => Read(Path.GetFileName(folder), file => Path.Combine(folder, file) is var path && File.Exists(path) ? File.ReadAllBytes(path) : null, Hamster);

    public static byte[]? ReadBuiltIn(string file)
    {
        using var stream = typeof(Character).Assembly.GetManifestResourceStream(file);
        if (stream is null)
        {
            return null;
        }
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    static string NameOf(Mood mood) => mood.ToString().ToLowerInvariant();

    static string SheetOf(Mood mood) => $"{NameOf(mood)}.png";

    static Character Read(string name, Func<string, byte[]?> read, Character? fallback)
    {
        var sprite = read(_spriteFile) is { } json ? ParseSprite(TextOf(json)) : null;
        return new(name, Enum.GetValues<Mood>().ToDictionary(mood => mood, mood => AnimationOf(mood) ?? fallback?.Animations[mood] ?? throw Missing(name, SheetOf(mood))));

        Frame[]? AnimationOf(Mood mood)
        {
            if (read(SheetOf(mood)) is not { } sheet)
            {
                return null;
            }
            var found = sprite ?? throw Missing(name, _spriteFile);
            var timing = found.Animations[NameOf(mood)] ?? throw new InvalidDataException(Strings.Format("Character.MissingTiming", _spriteFile, SheetOf(mood)));
            return ParseSheet(sheet, SheetOf(mood), found, timing);
        }
    }

    static string TextOf(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes));
        return reader.ReadToEnd();
    }

    static Sprite ParseSprite(string json)
    {
        JsonNode? sprite;
        try
        {
            sprite = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw BadSprite(exception);
        }
        return sprite is JsonObject found && found["animations"] is JsonObject animations
            ? new(PositiveOf(found["width"]), PositiveOf(found["height"]), animations)
            : throw BadSprite();
    }

    static int PositiveOf(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int number) && number > 0 ? number : throw BadSprite();

    static InvalidDataException BadSprite(Exception? inner = null) => new(Strings.Format("Character.BadSprite", _spriteFile), inner);

    static Frame[] ParseSheet(byte[] png, string file, Sprite sprite, JsonNode timing)
    {
        var (width, height) = SizeOf(png);
        int[] milliseconds = timing switch
        {
            JsonArray { Count: > 0 } each => [.. each.Select(PositiveOf)],
            JsonValue all => [.. Enumerable.Repeat(PositiveOf(all), width / sprite.Width)],
            _ => throw BadSprite(),
        };
        return milliseconds.Length > 0 && width == milliseconds.Length * sprite.Width && height == sprite.Height
            ? [.. milliseconds.Select((duration, index) => new Frame(png, index, sprite.Width, sprite.Height, duration))]
            : throw new InvalidDataException(Strings.Format("Character.BadSheet", file, sprite.Width, sprite.Height));
    }

    static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    static (int Width, int Height) SizeOf(byte[] png) => png.Length >= 24 && png.AsSpan(0, 8).SequenceEqual(PngSignature) && png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
        && (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) is (> 0, > 0) size
            ? size
            : (0, 0);

    static InvalidDataException Missing(string name, string file) => new(Strings.Format("Character.MissingFile", name, file));
}
