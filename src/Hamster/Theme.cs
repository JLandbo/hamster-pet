using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Hamster;

public sealed record Theme(string Name, IReadOnlyDictionary<string, Color> Colors)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Theme? Read(string file)
    {
        try
        {
            return Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static Theme Parse(string name, string json) => new(name, ColorsOf(JsonSerializer.Deserialize<SavedTheme>(json, Options)?.Colors ?? []));

    public ResourceDictionary ToResources()
    {
        var resources = new ResourceDictionary();
        foreach (var (name, color) in Colors)
            resources[name] = BrushOf(color);
        return resources;
    }

    public Brush BrushOf(string name, ResourceDictionary defaults) => Colors.TryGetValue(name, out var color) ? BrushOf(color) : (Brush)defaults[name];

    static SolidColorBrush BrushOf(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Dictionary<string, Color> ColorsOf(Dictionary<string, string?> colors) =>
        colors.Select(entry => (entry.Key, Color: ColorOf(entry.Value))).Where(entry => entry.Color is not null).ToDictionary(entry => entry.Key, entry => entry.Color!.Value);

    static Color? ColorOf(string? text)
    {
        try
        {
            return text is null || text.TrimStart().StartsWith("ContextColor", StringComparison.OrdinalIgnoreCase) ? null : (Color)ColorConverter.ConvertFromString(text);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    sealed record SavedTheme(Dictionary<string, string?>? Colors);
}
