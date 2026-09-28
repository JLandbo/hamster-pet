using Hamster.Desktop;
using Hamster.Themes;

namespace Hamster;

public partial class App : Application
{
    ResourceDictionary? theme;
    ResourceDictionary? translation;

    public ResourceDictionary DefaultColors => Resources.MergedDictionaries[0];

    public void Use(Theme next) => Swap(ref theme, next.ToResources());

    public void Use(Translation next)
    {
        Swap(ref translation, ResourcesOf(next));
        Strings.Use(next);
    }

    static ResourceDictionary ResourcesOf(Translation translation)
    {
        var resources = new ResourceDictionary();
        foreach (var key in Translation.Danish.Texts.Keys)
        {
            resources[key] = translation.Of(key);
        }
        return resources;
    }

    void Swap(ref ResourceDictionary? current, ResourceDictionary next)
    {
        if (current is not null)
            Resources.MergedDictionaries.Remove(current);
        current = next;
        Resources.MergedDictionaries.Add(next);
    }

    void Reader_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var copyLink = ((FrameworkElement)sender).ContextMenu.Items.OfType<MenuItem>().Last();
        copyLink.Tag = LinkAt(e.OriginalSource as DependencyObject);
        copyLink.Visibility = copyLink.Tag is null ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static Uri? LinkAt(DependencyObject? element) => element switch
    {
        null => null,
        Hyperlink link => link.NavigateUri,
        _ => LinkAt(LogicalTreeHelper.GetParent(element)),
    };

    void CopyLink_Click(object sender, RoutedEventArgs e) => ClipboardText.Copy(((Uri)((MenuItem)sender).Tag).AbsoluteUri, _ => { });
}
