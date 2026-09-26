using System.IO;
using System.Windows.Media;

namespace Hamster;

public sealed class ThemeLibrary(string folder)
{
    const string BuiltInPrefix = "Themes/";

    public static Theme Default { get; } = new("Sort og gul", new Dictionary<string, Color>());

    static readonly Theme[] BuiltIns = [.. ReadBuiltIns()];

    public IEnumerable<Theme> Themes =>
        [Default, .. BuiltIns.Concat(Own.Where(theme => !IsBuiltIn(theme.Name))).OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)];

    IEnumerable<Theme> Own => Files.Select(Theme.Read).OfType<Theme>();

    static bool IsBuiltIn(string name) => BuiltIns.Prepend(Default).Any(theme => theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    string[] Files
    {
        get
        {
            try
            {
                return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    public Theme Find(string? name) => Find(Themes, name);

    public static Theme Find(IEnumerable<Theme> themes, string? name) => themes.FirstOrDefault(theme => theme.Name == name) ?? Default;

    static IEnumerable<Theme> ReadBuiltIns()
    {
        var assembly = typeof(ThemeLibrary).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(BuiltInPrefix, StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            yield return Theme.Parse(Path.GetFileNameWithoutExtension(resource), reader.ReadToEnd());
        }
    }
}
