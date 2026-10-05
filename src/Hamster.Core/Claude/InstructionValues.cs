using System.Text.RegularExpressions;
using Hamster.Core.Storage;

namespace Hamster.Core.Claude;

// The instructions can name the app's folders as {{name}}, so they hold for any user and wherever the app is installed.
public static partial class InstructionValues
{
    public static IReadOnlyDictionary<string, string> Of(DataFiles files) => new Dictionary<string, string>
    {
        ["appFolder"] = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
        ["dataFolder"] = files.Folder,
    };

    // The workspace is the folder Claude starts in, which the user can choose, so it is only known when it starts.
    public static IReadOnlyDictionary<string, string> WithWorkspace(IReadOnlyDictionary<string, string>? values, string workspace) =>
        new Dictionary<string, string>(values ?? new Dictionary<string, string>()) { ["workspace"] = workspace };

    // A name it does not know is left as written, as instructions can hold {{...}} for other reasons.
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    [GeneratedRegex(@"\{\{([^{}]+)\}\}")]
    private static partial Regex Placeholder();
}
