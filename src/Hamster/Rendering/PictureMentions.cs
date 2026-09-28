using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Hamster.Rendering;

public static partial class PictureMentions
{
    public static IEnumerable<Uri> In(string text) => Mention().Matches(text).Select(match => Local(match.Value)).OfType<Uri>().DistinctBy(uri => uri.LocalPath, StringComparer.OrdinalIgnoreCase);

    static Uri? Local(string mention)
    {
        if (!Uri.TryCreate(mention, UriKind.Absolute, out var uri) || uri is not { IsFile: true, IsUnc: false } || uri.LocalPath.Contains('\0'))
        {
            return null;
        }
        return new Uri(Path.GetFullPath(uri.LocalPath));
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:file:///)?[A-Za-z]:[\\/](?:(?![A-Za-z]:[\\/])[^<>""|?*`\r\n])*?\.(?:png|jpe?g|gif|bmp|tiff?|ico|webp)(?!_*[A-Za-z0-9]|\.[A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Mention();
}

public sealed class ChatMarkdownConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => MarkdownConverter.Render((string)value, PictureMentions.In);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
