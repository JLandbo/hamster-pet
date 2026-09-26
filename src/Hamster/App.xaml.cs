using System.Windows;

namespace Hamster;

public partial class App : Application
{
    ResourceDictionary? theme;
    ResourceDictionary? translation;

    public ResourceDictionary DefaultColors => Resources.MergedDictionaries[0];

    public void Use(Theme next) => Swap(ref theme, next.ToResources());

    public void Use(Translation next)
    {
        Swap(ref translation, next.ToResources());
        Strings.Use(next);
    }

    void Swap(ref ResourceDictionary? current, ResourceDictionary next)
    {
        if (current is not null)
            Resources.MergedDictionaries.Remove(current);
        current = next;
        Resources.MergedDictionaries.Add(next);
    }
}
