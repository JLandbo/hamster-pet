using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Hamster.Core.Languages;
using Hamster.Core.Music;

namespace Hamster.Views;

public partial class MusicBubble : UserControl
{
    const string _playIcon = "\uE768";
    const string _pauseIcon = "\uE769";

    MusicPlayer? _music;

    public MusicBubble() => InitializeComponent();

    public event Action? Changed;

    public bool Playing => _music?.Track?.Playing == true;

    public async Task StartAsync()
    {
        try
        {
            _music = await MusicPlayer.StartAsync();
        }
        catch (COMException)
        {
            return;
        }
        _music.Changed += Show;
        Show();
    }

    public void Show()
    {
        var track = _music?.Track;
        Bubble.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        (TrackText.Text, Bubble.ToolTip) = (track?.Text, track?.Text);
        (PlayPauseButton.Content, PlayPauseButton.ToolTip) = track?.Playing == true ? (_pauseIcon, Strings.Of("Main.Pause")) : (_playIcon, Strings.Of("Main.Play"));
        Changed?.Invoke();
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e) => await (_music?.PlayPauseAsync() ?? Task.CompletedTask);

    async void NextTrack_Click(object sender, RoutedEventArgs e) => await (_music?.NextAsync() ?? Task.CompletedTask);

    async void PreviousTrack_Click(object sender, RoutedEventArgs e) => await (_music?.PreviousAsync() ?? Task.CompletedTask);
}
