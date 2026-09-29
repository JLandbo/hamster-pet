using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using Hamster.Core.Feeds;
using Hamster.Core.Languages;

namespace Hamster.Views;

public partial class FeedWindow : Window
{
    readonly Feed _feed;
    readonly Subscriptions _subscriptions;
    readonly HashSet<string> _expanded = [];

    public FeedWindow(Feed feed, Subscriptions subscriptions)
    {
        InitializeComponent();
        (_feed, _subscriptions) = (feed, subscriptions);
        ShowFeed();
        Strings.Changed += ShowFeed;
        Closed += (_, _) => Strings.Changed -= ShowFeed;
    }

    public void ShowFeed()
    {
        FeedSource[] sources = [.. _feed.SourcesAt(DateTimeOffset.Now, _subscriptions.Slots)];
        SourceList.ItemsSource = sources;
        EmptyText.Visibility = sources.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatedText.Text = _feed.UpdatedAt is { } updated ? Strings.Format("Tasks.Updated", updated) : "";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        (RefreshButton.IsEnabled, UpdatedText.Text) = (false, Strings.Of("Common.Fetching"));
        await _feed.RefreshAsync();
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
        toggle.IsChecked = toggle.DataContext is FeedRow { Details: not null } row && _expanded.Contains(row.Url);
    }

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
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
        {
            _expanded.Add(url);
        }
        else
        {
            _expanded.Remove(url);
        }
        e.Handled = true;
    }

    void Window_ContentRendered(object? sender, EventArgs e) => (SizeToContent, MaxHeight) = (SizeToContent.Manual, double.PositiveInfinity);

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
