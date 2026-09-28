namespace Hamster.Tests;

static class Documents
{
    public static IEnumerable<Image> Images(DependencyObject element)
    {
        return LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(child => child is Image image ? [image] : Images(child));
    }

    public static IEnumerable<Hyperlink> Links(DependencyObject element)
    {
        return LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(child => child is Hyperlink link ? Links(child).Prepend(link) : Links(child));
    }

    public static FlowDocument Loaded(FlowDocument document)
    {
        UiThread.Until(() => Images(document).All(image => image.Source is not null));
        return document;
    }
}
