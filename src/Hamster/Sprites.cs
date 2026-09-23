using Overlay = (string[] Glyph, int X, int Y);
using Pixel = (int X, int Y, char Color);

namespace Hamster;

public sealed record Frame(string[] Rows, int Milliseconds);

public static partial class Sprites
{
    public const int Width = 40;
    public const int Height = 36;
    const int BodyLeft = 6;

    public static IReadOnlyDictionary<char, uint> Palette { get; } = new Dictionary<char, uint>
    {
        ['a'] = 0xFFE3D3B7, ['b'] = 0xFFF4EBD8, ['c'] = 0xFFFCCC70, ['d'] = 0xFFE08F60, ['e'] = 0xFFA89679,
        ['f'] = 0xFFFDB029, ['g'] = 0xFFFBEAB4, ['h'] = 0xFFAC6437, ['k'] = 0xFF6B5A45, ['p'] = 0xFFF06A8A,
        ['w'] = 0xFFFFFFFF, ['y'] = 0xFFFFD23F, ['z'] = 0xFF5B6B8C,
    };

    // Traced 1:1 from the reference hamster image.
    static readonly string[] Body =
    [
        "...dddd........ddddd........",
        "..dggggd......dgggggd.......",
        ".dgcfffcd....dgcfffcgd......",
        ".dgcfffcddddddgcfffcgd......",
        ".dgcdddcdggggdgcdddcgd......",
        ".dgcdddcccccccccdddcgd......",
        "..ddcccccccccccccccgdgdd....",
        "...dchhccccccchhccccccggd...",
        "..dgchhcdddccchhccccccccgd..",
        ".dgcccccbdbbccccccccccccfgd.",
        "dgcccccbdbdbbbbbbbbccccfffgd",
        "dfffccbbbbbbbbbbbbbbbbfffffd",
        "dfffdbbbbbbbbbbbbbbbbbfffffd",
        "ddddbbbbbbbbbbbbbbbbbafdddfd",
        "dddbbbbbbbbbbbbbbbbbbeadddfd",
        "daaeabbbaaaaaaaebbbbbeaaddfd",
        "daaeabbbaeaaaaaebbbbaeaaaddd",
        "daaeabbbaeaaaaaaebaaeaaaaadd",
        ".eaaebbaeaaaaaaaaddeaaaaaadd",
        "..eaaddeaaaaaaaaaddaaaabbbbd",
        "..eaaddaaaaaaaaaaaaaaabbbbbe",
        "...eaaaaaaaaaaaaaaaaaabbbbbe",
        "....eaaaaaeaaaaaaaaaebbbbbe.",
        ".....eaaaaaeeeeeeeeebbbbbbe.",
        "......eaaaaaae..dfffebbbbe..",
        ".......eeeeee....ddd.eeee...",
        "..........dd...........dd...",
        "..........dd...........dd...",
    ];

    static readonly string[] ZSmall = ["zzzz", "..z.", ".z..", "zzzz"];
    static readonly string[] ZBig = ["zzzzz", "...z.", "..z..", ".z...", "zzzzz"];
    static readonly string[] Heart = [".p.p.", "ppppp", ".ppp.", "..p.."];
    static readonly string[] Bang = [".kk.", "kyyk", "kyyk", "kyyk", "kyyk", ".kk.", "kyyk", ".kk."];
    static readonly string[] Sparkle = [".y.", "ywy", ".y."];
    static readonly string[] Speed = [".eeee", "", "eeeee", "", "..eee"];

    static readonly Pixel[] EyesClosed = [(5, 7, 'c'), (6, 7, 'c'), (14, 7, 'c'), (15, 7, 'c')];
    static readonly Pixel[] EyesHappy =
        [(4, 8, 'h'), (5, 8, 'c'), (6, 8, 'c'), (7, 8, 'h'), (13, 8, 'h'), (14, 8, 'c'), (15, 8, 'c'), (16, 8, 'h')];
    static readonly Pixel[] MouthOpen = [(9, 10, 'h')];
    static readonly Pixel[] Blush = [(2, 10, 'p'), (3, 10, 'p'), (18, 10, 'p'), (19, 10, 'p')];
    // Two round lenses, the bridge between them, and the arm tucked under the hamster's left ear.
    static readonly Pixel[] Glasses =
    [
        (4, 5, 'k'), (5, 5, 'k'), (6, 5, 'k'), (7, 5, 'k'), (3, 6, 'k'), (3, 7, 'k'), (3, 8, 'k'), (3, 9, 'k'),
        (8, 6, 'k'), (8, 7, 'k'), (8, 8, 'k'), (8, 9, 'k'), (4, 10, 'k'), (5, 10, 'k'), (6, 10, 'k'), (7, 10, 'k'),
        (13, 5, 'k'), (14, 5, 'k'), (15, 5, 'k'), (16, 5, 'k'), (12, 6, 'k'), (12, 7, 'k'), (12, 8, 'k'), (12, 9, 'k'),
        (17, 6, 'k'), (17, 7, 'k'), (17, 8, 'k'), (17, 9, 'k'), (13, 10, 'k'), (14, 10, 'k'), (15, 10, 'k'), (16, 10, 'k'),
        (9, 7, 'k'), (10, 7, 'k'), (11, 7, 'k'),
        (18, 6, 'k'), (19, 6, 'k'), (20, 6, 'k'),
    ];
    static readonly Pixel[] Glint = [(4, 6, 'w')];

    // Declared after the sprite data above, because static initializers run in textual order.
    public static IReadOnlyDictionary<Mood, Frame[]> Animations { get; } = BuildAnimations();

    static Dictionary<Mood, Frame[]> BuildAnimations()
    {
        var sleepy = Edit(Body, EyesClosed);
        var giggly = Edit(Body, EyesHappy, MouthOpen);
        var openMouth = Edit(Body, MouthOpen);
        var joyful = Edit(giggly, Blush);
        var bespectacled = Edit(Body, Glasses);

        return new()
        {
            [Mood.Sleep] =
            [
                new(Compose(sleepy), 700),
                new(Compose(Squash(sleepy), overlays: [(ZSmall, 31, 8)]), 700),
                new(Compose(Squash(sleepy), overlays: [(ZSmall, 31, 8), (ZBig, 35, 3)]), 700),
                new(Compose(sleepy, overlays: [(ZBig, 35, 3)]), 700),
            ],
            [Mood.Awake] =
            [
                new(Compose(Body), 600),
                new(Compose(Body, dy: -1), 600),
                new(Compose(Body), 600),
                new(Compose(Body, dy: -1), 600),
                new(Compose(sleepy), 150),
            ],
            [Mood.Curious] =
            [
                new(Compose(openMouth), 600),
                new(Compose(openMouth, dy: -1), 600),
                new(Compose(openMouth), 600),
                new(Compose(openMouth, dy: -1), 600),
                new(Compose(Edit(sleepy, MouthOpen)), 150),
            ],
            [Mood.Giggle] =
            [
                new(Compose(giggly, dy: -2, overlays: [(Heart, 33, 4)]), 150),
                new(Compose(Squash(giggly)), 150),
            ],
            [Mood.Dangle] =
            [
                new(Compose(openMouth, dy: -2), 250),
                new(Compose(openMouth, dx: 1, dy: -2), 250),
                new(Compose(openMouth, dy: -2), 250),
                new(Compose(openMouth, dx: -1, dy: -2), 250),
            ],
            [Mood.Run] =
            [
                // Speed lines trail on the right, behind the left-facing sprite.
                new(Compose(openMouth, overlays: [(Speed, 35, 18)]), 120),
                new(Compose(openMouth, dy: -1, overlays: [(Speed, 34, 19)]), 120),
            ],
            [Mood.Spin] = [.. SpinBodies().Select(body => new Frame(Compose(body), 100))],
            [Mood.Research] =
            [
                new(Compose(bespectacled), 500),
                new(Compose(bespectacled, dy: -1), 500),
                new(Compose(Edit(bespectacled, Glint)), 300),
                new(Compose(bespectacled, dy: -1), 500),
            ],
            [Mood.Happy] =
            [
                new(Compose(joyful, dy: -2, overlays: [(Sparkle, 1, 8), (Sparkle, 34, 4)]), 220),
                new(Compose(joyful, overlays: [(Sparkle, 3, 12), (Sparkle, 35, 10)]), 220),
            ],
            [Mood.Alert] =
            [
                new(Compose(Body, overlays: [(Bang, 32, 1)]), 400),
                new(Compose(Body, overlays: [(Bang, 32, 0)]), 400),
            ],
        };
    }

    static string[] Edit(string[] rows, params Pixel[][] edits)
    {
        var grid = rows.Select(row => row.ToCharArray()).ToArray();
        foreach (var (x, y, color) in edits.SelectMany(pixels => pixels))
            grid[y][x] = color;
        return [.. grid.Select(row => new string(row))];
    }

    // Dropping one leg row makes the body sink a pixel - reads as breathing.
    static string[] Squash(string[] rows) => [new string('.', rows[0].Length), .. rows[..26], .. rows[27..]];

    static string[] Compose(string[] body, int dx = 0, int dy = 0, params Overlay[] overlays)
    {
        var grid = Enumerable.Range(0, Height).Select(_ => Enumerable.Repeat('.', Width).ToArray()).ToArray();
        Stamp(grid, body, BodyLeft + dx, Height - body.Length + dy);
        foreach (var (glyph, x, y) in overlays)
            Stamp(grid, glyph, x, y);
        return [.. grid.Select(row => new string(row))];
    }

    static void Stamp(char[][] grid, string[] glyph, int left, int top)
    {
        for (var y = 0; y < glyph.Length; y++)
            for (var x = 0; x < glyph[y].Length; x++)
                if (glyph[y][x] != '.' && top + y is >= 0 and < Height)
                    grid[top + y][left + x] = glyph[y][x];
    }
}
