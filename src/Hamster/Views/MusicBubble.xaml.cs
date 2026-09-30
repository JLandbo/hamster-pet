using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hamster.Core.Languages;
using Hamster.Core.Music;
using Hamster.Desktop;

namespace Hamster.Views;

public sealed record MusicBarOptions(bool Show, bool Always, int HideSeconds);

public partial class MusicBubble : UserControl
{
    const string _playIcon = "\uE768";
    const string _pauseIcon = "\uE769";
    const string _volumeIcon = "\uE767";
    const string _mutedIcon = "\uE74F";
    static readonly TimeSpan _fadeTime = TimeSpan.FromMilliseconds(250);

    readonly DispatcherTimer _hideTimer = new();
    MusicPlayer? _music;
    SystemVolume? _volume;
    MusicBarOptions _options = new(true, false, 30);
    bool _requestedVisible;
    bool _muted;

    public MusicBubble()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
    }

    public event Action? Changed;

    public bool Playing => _music?.Track?.Playing == true;

    public void Configure(MusicBarOptions options)
    {
        _options = options with { HideSeconds = Math.Clamp(options.HideSeconds, 1, 240) };
        UpdateVisibility();
    }

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
        _music.Changed += TrackChanged;
        TrackChanged();
    }

    public void RefreshText()
    {
        var track = _music?.Track;
        (TrackText.Text, TrackPanel.ToolTip) = (track?.Text, track?.Text);
        (PlayPauseButton.Content, PlayPauseButton.ToolTip) = track?.Playing == true ? (_pauseIcon, Strings.Of("Main.Pause")) : (_playIcon, Strings.Of("Main.Play"));
        (MuteButton.Content, MuteButton.ToolTip) = _muted ? (_mutedIcon, Strings.Of("Main.Unmute")) : (_volumeIcon, Strings.Of("Main.Mute"));
    }

    void TrackChanged()
    {
        RefreshText();
        UpdateVisibility();
        Changed?.Invoke();
    }

    void UpdateVisibility()
    {
        var track = _music?.Track;
        if (!_options.Show || track is null)
        {
            _hideTimer.Stop();
            Collapse();
            return;
        }
        if (track.Playing || _options.Always)
        {
            _hideTimer.Stop();
            Reveal();
            return;
        }
        Reveal();
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(_options.HideSeconds);
        _hideTimer.Start();
    }

    void Reveal()
    {
        if (_requestedVisible)
        {
            return;
        }
        _requestedVisible = true;
        Bubble.Visibility = Visibility.Visible;
        Bubble.BeginAnimation(OpacityProperty, null);
        Bubble.Opacity = 1;
    }

    void FadeOut()
    {
        _requestedVisible = false;
        var fade = new DoubleAnimation(0, _fadeTime);
        fade.Completed += (_, _) =>
        {
            if (!_requestedVisible)
            {
                Collapse();
            }
        };
        Bubble.BeginAnimation(OpacityProperty, fade);
    }

    void Collapse()
    {
        _requestedVisible = false;
        Bubble.BeginAnimation(OpacityProperty, null);
        Bubble.Opacity = 0;
        Bubble.Visibility = Visibility.Collapsed;
    }

    void ReadVolume()
    {
        if (_volume?.Read() is var (level, muted))
        {
            (VolumeSlider.Value, _muted) = (level * 100, muted);
            RefreshText();
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
        if (_volume?.Read() is var (_, muted))
        {
            _volume.SetMuted(!muted);
        }
        ReadVolume();
    }
}
