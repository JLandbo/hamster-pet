using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media.Control;

namespace Hamster;

public sealed record Track(string Title, string Artist, bool Playing)
{
    public string Text => Artist.Length > 0 ? $"{Title} – {Artist}" : Title;

    public static Track? From(string? title, string? artist, bool playing) =>
        string.IsNullOrWhiteSpace(title) ? null : new(title.Trim(), artist?.Trim() ?? "", playing);
}

public sealed class MusicPlayer
{
    readonly GlobalSystemMediaTransportControlsSessionManager manager;
    readonly SynchronizationContext context;
    GlobalSystemMediaTransportControlsSession? session;

    MusicPlayer(GlobalSystemMediaTransportControlsSessionManager manager, SynchronizationContext context) =>
        (this.manager, this.context) = (manager, context);

    public event Action? Changed;

    public Track? Track { get; private set; }

    public static async Task<MusicPlayer> StartAsync()
    {
        var player = new MusicPlayer(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(), SynchronizationContext.Current ?? new());
        player.manager.CurrentSessionChanged += (_, _) => player.Post(player.FollowAsync);
        await player.FollowAsync();
        return player;
    }

    public Task PlayPauseAsync() => SendAsync(session => session.TryTogglePlayPauseAsync());

    public Task NextAsync() => SendAsync(session => session.TrySkipNextAsync());

    public Task PreviousAsync() => SendAsync(session => session.TrySkipPreviousAsync());

    async Task SendAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        if (session is not { } current)
            return;
        try
        {
            await command(current);
        }
        catch (COMException)
        {
        }
    }

    void Post(Func<Task> action) => context.Post(async _ => await action(), null);

    async Task FollowAsync()
    {
        if (session is not null)
        {
            session.MediaPropertiesChanged -= SessionChanged;
            session.PlaybackInfoChanged -= SessionChanged;
        }
        session = manager.GetCurrentSession();
        if (session is not null)
        {
            session.MediaPropertiesChanged += SessionChanged;
            session.PlaybackInfoChanged += SessionChanged;
        }
        await ReadAsync();
    }

    void SessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => Post(ReadAsync);

    async Task ReadAsync()
    {
        var current = session;
        Track? next = null;
        try
        {
            if (current is not null && await current.TryGetMediaPropertiesAsync() is { } properties)
                next = Track.From(properties.Title, properties.Artist,
                    current.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }
        catch (COMException)
        {
        }
        if (current != session || next == Track)
            return;
        Track = next;
        Changed?.Invoke();
    }
}
