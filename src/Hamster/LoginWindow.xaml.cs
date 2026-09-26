using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Hamster;

public partial class LoginWindow : Window
{
    readonly WebSession session;
    readonly WebLogin login;
    readonly string start;

    public LoginWindow(WebSession session, Connector connector, string site)
    {
        InitializeComponent();
        (this.session, login, start) = (session, connector.Login!, site);
        Title = $"Log ind – {new Uri(site).Host}";
        Hint.Text = $"Log ind på {new Uri(site).Host}. Vinduet lukker af sig selv, når du er logget ind.";
    }

    public string? Site { get; private set; }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await Web.EnsureCoreWebView2Async(await session.EnvironmentAsync());
        Web.Source = new Uri(start);
    }

    async void Web_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (Site is not null || login.SiteOf(Web.Source) is not { } site)
            return;
        try
        {
            if (!await session.IsLoggedInAsync(site, login))
                return;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return;
        }
        Site = site;
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
