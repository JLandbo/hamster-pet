using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;

namespace Hamster;

public partial class FeedWindow : Window
{
    readonly Feed feed;
    readonly Subscriptions subscriptions;
    readonly HashSet<string> expanded = [];

    public FeedWindow(Feed feed, Subscriptions subscriptions)
    {
        InitializeComponent();
        (this.feed, this.subscriptions) = (feed, subscriptions);
        ShowFeed();
        Strings.Changed += ShowFeed;
        Closed += (_, _) => Strings.Changed -= ShowFeed;
    }

    public void ShowFeed()
    {
        FeedSource[] sources = [.. feed.SourcesAt(DateTimeOffset.Now, subscriptions.Slots)];
        SourceList.ItemsSource = sources;
        EmptyText.Visibility = sources.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatedText.Text = feed.UpdatedAt is { } updated ? Strings.Format("Tasks.Updated", updated) : "";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        (RefreshButton.IsEnabled, UpdatedText.Text) = (false, Strings.Of("Common.Fetching"));
        await feed.RefreshAsync();
        RefreshButton.IsEnabled = true;
        ShowFeed();
    }

    void Row_Click(object sender, RoutedEventArgs e) => Open(((FeedRow)((FrameworkElement)sender).DataContext).Url);

    static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }

    void Expand_Loaded(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        toggle.IsChecked = toggle.DataContext is FeedRow { Details: not null } row && expanded.Contains(row.Url);
    }

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    void Description_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject == sender)
            e.Handled = true;
    }

    void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        List.ScrollToVerticalOffset(List.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    void Expand_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        var url = ((FeedRow)toggle.DataContext).Url;
        if (toggle.IsChecked == true)
            expanded.Add(url);
        else
            expanded.Remove(url);
        e.Handled = true;
    }

    void Window_ContentRendered(object? sender, EventArgs e) => (SizeToContent, MaxHeight) = (SizeToContent.Manual, double.PositiveInfinity);

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
