using Overlay = (string[] Glyph, int X, int Y);

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

    static readonly Overlay EyesClosed = (["cc.......cc"], 5, 7);
    static readonly Overlay EyesHappy = (["hcch.....hcch"], 4, 8);
    static readonly Overlay MouthOpen = (["h"], 9, 10);
    static readonly Overlay Blush = (["pp..............pp"], 2, 10);
    static readonly Overlay Glint = (["w"], 4, 6);
    static readonly Overlay Glasses =
    ([
        ".kkkk.....kkkk",
        "k....k...k....kkkk",
        "k....kkkkk....k",
        "k....k...k....k",
        "k....k...k....k",
        ".kkkk.....kkkk",
    ], 3, 5);

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
            [Mood.Awake] = Idle(Body),
            [Mood.Curious] = Idle(openMouth),
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

    static Frame[] Idle(string[] face) =>
    [
        new(Compose(face), 600),
        new(Compose(face, dy: -1), 600),
        new(Compose(face), 600),
        new(Compose(face, dy: -1), 600),
        new(Compose(Edit(face, EyesClosed)), 150),
    ];

    static string[] Edit(string[] rows, params Overlay[] overlays)
    {
        var grid = rows.Select(row => row.ToCharArray()).ToArray();
        foreach (var (glyph, left, top) in overlays)
            for (var y = 0; y < glyph.Length; y++)
                for (var x = 0; x < glyph[y].Length; x++)
                    if (glyph[y][x] != '.')
                        grid[top + y][left + x] = glyph[y][x];
        return [.. grid.Select(row => new string(row))];
    }

    static string[] Squash(string[] rows) => [new string('.', rows[0].Length), .. rows[..^2], rows[^1]];

    static string[] Compose(string[] body, int dx = 0, int dy = 0, params Overlay[] overlays) =>
        Edit([.. Enumerable.Repeat(new string('.', Width), Height)], [(body, BodyLeft + dx, Height - body.Length + dy), .. overlays]);
}
