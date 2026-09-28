namespace Hamster.Core.Connections;

public interface IWebSession
{
    Task<bool> IsLoggedInAsync(string site, WebLogin login);

    Task LogoutAsync();

    Task<Func<string, Task<string>>> ReaderAsync(string site);
}
