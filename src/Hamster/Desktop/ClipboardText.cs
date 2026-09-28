using System.Runtime.InteropServices;
using System.Windows;

namespace Hamster.Desktop;

public static class ClipboardText
{
    public const string CopyIcon = "\uE8C8";
    const string _copiedIcon = "\uE73E";
    static readonly TimeSpan _copiedTime = TimeSpan.FromSeconds(1.5);

    public static async void Copy(string text, Action<string> showIcon)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            return;
        }
        showIcon(_copiedIcon);
        await Task.Delay(_copiedTime);
        showIcon(CopyIcon);
    }
}
