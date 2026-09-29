using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Hamster.Core.Connections;
using Hamster.Core.Languages;
using Hamster.Desktop;

namespace Hamster.Views;

public partial class LoginWindow : Window
{
    readonly WebSession _session;
    readonly WebLogin _login;
    readonly string _start;

    public LoginWindow(WebSession session, Connector connector, string site)
    {
        InitializeComponent();
        (_session, _login, _start) = (session, connector.Login!, site);
        Title = Strings.Format("Login.Title", new Uri(site).Host);
        Hint.Text = Strings.Format("Login.Hint", new Uri(site).Host);
    }

    public string? Site { get; private set; }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Web.EnsureCoreWebView2Async(await _session.EnvironmentAsync());
            Web.Source = new Uri(_start);
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Warning.Show(this, Strings.Format("Login.CouldNotOpen", exception.Message));
            Close();
        }
    }

    async void Web_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (Site is not null || _login.SiteOf(Web.Source) is not { } site)
        {
            return;
        }
        try
        {
            if (!await _session.IsLoggedInAsync(site, _login))
            {
                return;
            }
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
