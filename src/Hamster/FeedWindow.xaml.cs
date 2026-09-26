using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Hamster;

public partial class FeedWindow : Window
{
    readonly Feed feed;
    readonly Subscriptions subscriptions;

    public FeedWindow(Feed feed, Subscriptions subscriptions)
    {
        InitializeComponent();
        (this.feed, this.subscriptions) = (feed, subscriptions);
        ShowFeed();
    }

    public void ShowFeed()
    {
        FeedSource[] sources = [.. feed.SourcesAt(DateTimeOffset.Now, subscriptions.Slots)];
        SourceList.ItemsSource = sources;
        EmptyText.Visibility = sources.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatedText.Text = feed.UpdatedAt is { } updated ? $"Opdateret {updated:HH:mm}" : "";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        (RefreshButton.IsEnabled, UpdatedText.Text) = (false, "Henter…");
        await feed.RefreshAsync();
        RefreshButton.IsEnabled = true;
        ShowFeed();
    }

    void Row_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(((FeedRow)((FrameworkElement)sender).DataContext).Url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }

    void Window_ContentRendered(object? sender, EventArgs e) => (SizeToContent, MaxHeight) = (SizeToContent.Manual, double.PositiveInfinity);

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
