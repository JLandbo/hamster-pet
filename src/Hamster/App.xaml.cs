using System.Windows;

namespace Hamster;

public partial class App : Application
{
    ResourceDictionary? theme;

    public ResourceDictionary DefaultColors => Resources.MergedDictionaries[0];

    public void Use(Theme next)
    {
        if (theme is not null)
            Resources.MergedDictionaries.Remove(theme);
        theme = next.ToResources();
        Resources.MergedDictionaries.Add(theme);
    }
}
