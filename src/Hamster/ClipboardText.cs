using System.Runtime.InteropServices;
using System.Windows;

namespace Hamster;

public static class ClipboardText
{
    public const string CopyIcon = "\uE8C8";
    const string CopiedIcon = "\uE73E";
    static readonly TimeSpan CopiedTime = TimeSpan.FromSeconds(1.5);

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
        showIcon(CopiedIcon);
        await Task.Delay(CopiedTime);
        showIcon(CopyIcon);
    }
}
