using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Hamster;

public sealed class WebSession(string folder)
{
    const int HiddenPopup = unchecked((int)0x80000000);
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

    Task<CoreWebView2Environment>? environment;
    Task<CoreWebView2Controller>? cookies;
    HwndSource? host;

    public Task<CoreWebView2Environment> EnvironmentAsync() => environment ??= CoreWebView2Environment.CreateAsync(null, folder);

    public string? Login(Window owner, Connector connector, string site)
    {
        var window = new LoginWindow(this, connector, site) { Owner = owner };
        window.ShowDialog();
        return window.Site;
    }

    public async Task LogoutAsync() => (await CookiesAsync()).CookieManager.DeleteAllCookies();

    public async Task<bool> IsLoggedInAsync(string site, WebLogin login)
    {
        using var response = await SendAsync(site + login.CheckPath, await CookieHeaderAsync(site));
        return IsLoggedIn(response);
    }

    public static bool IsLoggedIn(HttpResponseMessage response) =>
        response.IsSuccessStatusCode
        || (response.StatusCode == HttpStatusCode.Unauthorized
            ? false
            : throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}"));

    public async Task<string> GetAsync(string url) => await (await ReaderAsync(url))(url);

    public async Task<Func<string, Task<string>>> ReaderAsync(string site)
    {
        var cookies = await CookieHeaderAsync(site);
        return url => ReadAsync(url, cookies);
    }

    static async Task<string> ReadAsync(string url, string cookies)
    {
        using var response = await SendAsync(url, cookies);
        return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? throw new InvalidOperationException(Strings.Of("Web.LogInAgain"))
            : response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync()
                : throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    async Task<string> CookieHeaderAsync(string url) =>
        string.Join("; ", (await (await CookiesAsync()).CookieManager.GetCookiesAsync(url)).Select(cookie => $"{cookie.Name}={cookie.Value}"));

    static async Task<HttpResponseMessage> SendAsync(string url, string cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookies);
        request.Headers.Accept.ParseAdd("application/json");
        return await Http.SendAsync(request);
    }

    async Task<CoreWebView2> CookiesAsync()
    {
        host ??= new HwndSource(new HwndSourceParameters("Hamster") { WindowStyle = HiddenPopup, Width = 1, Height = 1 });
        cookies ??= CreateCookiesAsync(host.Handle);
        try
        {
            return (await cookies).CoreWebView2;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            cookies = null;
            throw;
        }
    }

    async Task<CoreWebView2Controller> CreateCookiesAsync(nint parent)
    {
        var controller = await (await EnvironmentAsync()).CreateCoreWebView2ControllerAsync(parent);
        controller.CoreWebView2.ProcessFailed += (_, failure) =>
        {
            if (failure.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                cookies = null;
        };
        return controller;
    }
}
