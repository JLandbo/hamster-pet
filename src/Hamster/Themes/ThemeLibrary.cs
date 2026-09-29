using System.IO;
using System.Windows.Media;

namespace Hamster.Themes;

public sealed class ThemeLibrary(string folder)
{
    public static Theme Default { get; } = new("Sort og gul", new Dictionary<string, Color>());

    public IEnumerable<Theme> Themes
    {
        get
        {
            return [Default, .. Own.Where(theme => !theme.Name.Equals(Default.Name, StringComparison.OrdinalIgnoreCase)).OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
    }

    public string Folder => folder;

    IEnumerable<Theme> Own => Files.Select(Theme.Read).OfType<Theme>();

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
}
