using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Hamster.Core.Languages;
using Hamster.Core.Music;
using Hamster.Desktop;

namespace Hamster.Views;

public partial class MusicBubble : UserControl
{
    const string _playIcon = "";
    const string _pauseIcon = "";
    const string _volumeIcon = "";
    const string _mutedIcon = "";

    MusicPlayer? _music;
    SystemVolume? _volume;
    bool _muted;

    public MusicBubble() => InitializeComponent();

    public event Action? Changed;

    public bool Playing => _music?.Track?.Playing == true;

    public async Task StartAsync(SystemVolume volume)
    {
        _volume = volume;
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
        (TrackText.Text, TrackPanel.ToolTip) = (track?.Text, track?.Text);
        (PlayPauseButton.Content, PlayPauseButton.ToolTip) = track?.Playing == true ? (_pauseIcon, Strings.Of("Main.Pause")) : (_playIcon, Strings.Of("Main.Play"));
        (MuteButton.Content, MuteButton.ToolTip) = _muted ? (_mutedIcon, Strings.Of("Main.Unmute")) : (_volumeIcon, Strings.Of("Main.Mute"));
        Changed?.Invoke();
    }

    void ReadVolume()
    {
        if (_volume?.Read() is var (level, muted))
        {
            (VolumeSlider.Value, _muted) = (level * 100, muted);
            Show();
        }
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e) => await (_music?.PlayPauseAsync() ?? Task.CompletedTask);

    async void NextTrack_Click(object sender, RoutedEventArgs e) => await (_music?.NextAsync() ?? Task.CompletedTask);

    async void PreviousTrack_Click(object sender, RoutedEventArgs e) => await (_music?.PreviousAsync() ?? Task.CompletedTask);

    void Volume_Click(object sender, RoutedEventArgs e)
    {
        ReadVolume();
        VolumePanel.Visibility = VolumePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _volume?.SetLevel(e.NewValue / 100);

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        _volume?.SetMuted(!_muted);
        ReadVolume();
    }
}
