using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Hamster.Core.Chats;
using Hamster.Core.Claude;
using Hamster.Core.Connections;
using Hamster.Core.Feeds;
using Hamster.Core.Languages;
using Hamster.Core.Pet;
using Hamster.Core.Prompts;
using Hamster.Core.Storage;
using Hamster.Desktop;
using Hamster.Rendering;
using Hamster.Themes;

namespace Hamster.Views;

public partial class MainWindow : Window
{
    const double _defaultWidth = 396;
    const double _defaultChatHeight = 900;
    const double _minChatHeight = 120;
    const double _petWidth = 160;
    const double _petHeight = 144;
    static readonly TimeSpan _giggleTime = TimeSpan.FromSeconds(1.5);
    static readonly TimeSpan _happyTime = TimeSpan.FromSeconds(4);
    static readonly TimeSpan _sadTime = TimeSpan.FromSeconds(4);
    const double _tiredUsage = 0.9;
    static readonly TimeSpan _awakeTime = TimeSpan.FromMinutes(1);
    static readonly TimeSpan _movingTime = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan _clickTime = TimeSpan.FromMilliseconds(300);
    const string _collapseIcon = "\uE70D";
    const string _expandIcon = "\uE70E";

    readonly DataFiles _files;
    readonly ClaudeClient _claude;
    readonly Conversation _conversation;
    readonly JsonFile<Placement> _placementFile;
    readonly JsonFile<WindowSize?> _settingsSize;
    readonly JsonFile<WindowSize?> _feedSize;
    readonly bool _chatOnly;
    readonly List<string> _attachedFiles = [];
    readonly List<ImageAttachment> _attachedImages = [];
    readonly CharacterLibrary _characters;
    readonly PromptLibrary _prompts;
    readonly ThemeLibrary _themes;
    readonly JsonFile<PetSettings> _petFile;
    readonly Connectors _connectors;
    readonly Subscriptions _subscriptions;
    readonly WebSession _web;
    readonly Feed _feed;
    readonly SystemVolume _volume;
    readonly RevealedChats _revealed;
    readonly DispatcherTimer _timer = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer _feedTimer = new() { Interval = Subscriptions.Interval };
    readonly HashSet<ConnectorProblem> _dismissedProblems = [];
    readonly Window _petWindow;

    IReadOnlyList<ConnectorProblem> _problems = [];
    string _lastPrompt = "";
    Task _attaching = Task.CompletedTask;
    DateTime _feedNewsAt;
    Character _character = Character.Hamster;
    Dictionary<Mood, BitmapSource[]> _images = [];
    SettingsWindow? _settingsWindow;
    FeedWindow? _feedWindow;
    Mood _mood;
    int _frame;
    bool _feedFetched, _resizeQueued, _pressed, _dragging, _chatsExpanded, _revealing, _shown = true, _lastResponseVisible = true;
    Point _dragStart;
    DateTime _pressedAt, _giggleUntil, _lastMove, _lastActivity = DateTime.UtcNow, _newsSeenAt = DateTime.UtcNow;
    Placement _placement;
    Placement? _beforeFullScreen;
    (Point Mouse, double Width, double ChatHeight) _resizeStart;
    (DateTime AnsweredAt, bool Waiting) _poppedUp;
    TimeSpan _hideTime, _lastResponseTime;
    DateTime _sentAt;
    DateTime _lastResponseSeenAt;
    DateTime _frontAt;
    Button? _closedByItsButton;
    InfoWindow? _infoWindow;

    public MainWindow(DataFiles files, ClaudeClient claude, Conversation conversation, Connectors connectors, Subscriptions subscriptions, Feed feed,
        WebSession web, CharacterLibrary characters, PromptLibrary prompts, ThemeLibrary themes, SystemVolume volume)
    {
        (_files, _claude, _conversation, _connectors, _subscriptions) = (files, claude, conversation, connectors, subscriptions);
        (_feed, _web, _characters, _prompts, _themes, _volume) = (feed, web, characters, prompts, themes, volume);
        InitializeComponent();
        PetArea.Children.Remove(Pet);
        _petWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            UseLayoutRounding = true,
            AllowDrop = true,
            Content = Pet,
        };
        _petWindow.DragOver += Window_DragOver;
        _petWindow.Drop += Window_Drop;
        _petWindow.Closed += (_, _) => Dispatcher.BeginInvoke(Close);
        (Width, Height) = (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        _chatOnly = files.ChatOnly;
        _petFile = files.Pet;
        var pet = _petFile.Load();
        SizeSlider.Value = pet.SizePercent;
        ShowSize();
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;
        AutoHideItem.IsChecked = pet.AutoHide;
        KeepOnTopItem.IsChecked = Topmost = pet.KeepWindowsOnTop;
        MarkdownConverter.ShowWebImages = pet.ShowWebImages;
        _chatsExpanded = pet.ChatsExpanded;
        _hideTime = TimeSpan.FromSeconds(pet.HideSeconds);
        _lastResponseTime = TimeSpan.FromSeconds(pet.LastResponseSeconds);
        Music.Configure(MusicBarOptionsOf(pet));
        _placementFile = files.Store("placement.json", DefaultPlacement);
        _placement = _placementFile.Load();
        if (_chatOnly)
        {
            _placement = _placement.CenteredIn(SystemParameters.WorkArea, new Size(PetArea.Width, PetArea.Height));
        }
        _settingsSize = files.Store<WindowSize?>("settings-window.json", null);
        _feedSize = files.Store<WindowSize?>("feed-window.json", null);
        feed.Changed += news =>
        {
            if (news)
            {
                _feedNewsAt = DateTime.UtcNow;
            }
            ShowFeed();
        };
        subscriptions.Changed += () => _ = feed.RefreshAsync(subscriptionsChanged: true);
        _feedTimer.Tick += (_, _) => _ = feed.RefreshAsync();
        if (!ModeButton.ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Tag == claude.Settings.PermissionMode))
        {
            claude.Settings = claude.Settings with { PermissionMode = ClaudeSettings.Default.PermissionMode };
        }
        Choose(ModelButton, claude.Settings.Model);
        Choose(EffortButton, claude.Settings.Effort);
        Choose(ModeButton, claude.Settings.PermissionMode);
        ToggleChats.Content = _chatsExpanded ? _collapseIcon : _expandIcon;
        Root.Width = RootWidth();
        ShowChatOnly();
        ShowFolder();
        Chats.Show(conversation);
        _revealed = new RevealedChats(conversation.Chats);
        UpdateChatList();
        RevealOlderChats();
        conversation.Changed += Conversation_Changed;
        conversation.Started += () =>
        {
            if (_chatOnly)
            {
                return;
            }
            _ = CheckConnectorsAsync();
            if (_feedFetched)
            {
                return;
            }
            _feedFetched = true;
            _ = feed.RefreshAsync();
        };
        conversation.PermissionModeChanged += mode => claude.Settings = claude.Settings with { PermissionMode = Choose(ModeButton, mode) };
        _timer.Tick += (_, _) =>
        {
            FadeWhenIdle();
            RefreshLastResponseVisibility();
            Animate();
        };
        _clock.Tick += (_, _) =>
        {
            var following = Chats.IsAtBottom;
            var refreshed = false;
            foreach (var chat in conversation.Chats)
            {
                refreshed |= chat.RefreshDisplayAnswer();
            }
            if (refreshed && following)
            {
                Dispatcher.BeginInvoke(Chats.ScrollToNewest, DispatcherPriority.Background);
            }
        };
        Music.Changed += AnimateIfMoodChanged;
        Strings.Changed += () =>
        {
            ShowChatOnly();
            UpdateToolbar();
            Music.RefreshText();
            UpdateAttachments();
            ShowProblems();
            _ = feed.RefreshAsync();
        };
        ((App)Application.Current).Use(themes.Find(pet.ThemeName));
        UseCharacter(LoadCharacter(pet.CharacterName));
    }

    static string SwitchWhenIdle => Strings.Of("Main.SwitchWhenIdle");

    static Placement DefaultPlacement => new(SystemParameters.WorkArea.Right, SystemParameters.WorkArea.Bottom, _defaultWidth, _defaultChatHeight);

    static MusicBarOptions MusicBarOptionsOf(PetSettings pet) => new(pet.ShowMusicBar, pet.AlwaysShowMusicBar, pet.MusicBarHideSeconds);

    PetStatus Status => new(
        Pressed: _pressed,
        Moving: DateTime.UtcNow - _lastMove < _movingTime,
        Giggling: DateTime.UtcNow < _giggleUntil,
        WaitingForUser: _conversation.IsWaitingForUser,
        BrowsingWeb: _conversation.IsBrowsingWeb,
        Busy: _conversation.IsBusy,
        Celebrating: _conversation.AnsweredWithin(_happyTime, DateTime.UtcNow),
        Failed: _conversation.FailedWithin(_sadTime, DateTime.UtcNow) || _conversation.FailedSince(AnswerSeenAt),
        Typing: Input.IsKeyboardFocused && Input.Text.Length > 0,
        HasNews: _conversation.AnsweredAt > AnswerSeenAt,
        HasTaskNews: _feedNewsAt > _newsSeenAt,
        BackgroundWork: _conversation.BackgroundTasks > 0,
        Music: Music.Playing,
        Tired: _conversation.Usage?.HighestAt(DateTimeOffset.UtcNow) >= _tiredUsage,
        Hovered: Pet.IsMouseOver,
        Awake: Input.IsKeyboardFocused || DateTime.UtcNow - _lastActivity < _awakeTime);

    void Animate()
    {
        var next = MoodTransition.Next(_mood, _frame, Status.Mood, _character.Animations[Mood.Spin].Length);
        if (next != _mood)
        {
            (_mood, _frame) = (next, 0);
        }
        var frames = _character.Animations[_mood];
        var index = _frame++ % frames.Length;
        Pet.Source = _images[_mood][index];
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(frames[index].Milliseconds);
        _timer.Start();
    }

    void AnimateIfMoodChanged()
    {
        if (Status.Mood != _mood)
        {
            Animate();
        }
    }

    Character LoadCharacter(string name)
    {
        try
        {
            return _characters.Load(name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Character.Hamster;
        }
    }

    void UseCharacter(Character next)
    {
        try
        {
            (_images, _character) = (ImagesOf(next), next);
        }
        catch (InvalidDataException)
        {
            (_images, _character) = (ImagesOf(Character.Hamster), Character.Hamster);
        }
        _frame = 0;
        Animate();
    }

    static Dictionary<Mood, BitmapSource[]> ImagesOf(Character character) => character.Animations.ToDictionary(animation => animation.Key, animation => SpriteRenderer.Render(animation.Value));

    void ShowChatOnly()
    {
        if (_chatOnly)
        {
            foreach (var button in new[] { MoreButton, FeedButton })
            {
                (button.IsEnabled, button.ToolTip, button.Opacity) = (false, Strings.Of("Main.OnlyInFirstHamster"), 0.4);
            }
        }
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Close();
        }
        else
        {
            ShowSettings();
        }
    }

    void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        var pet = _petFile.Load();
        var window = _settingsWindow = new SettingsWindow(ChooseLanguage, (int)_hideTime.TotalSeconds, ChooseHideSeconds, (int)_lastResponseTime.TotalSeconds, ChooseLastResponseSeconds, MarkdownConverter.ShowWebImages, ChooseWebImages, _claude.Settings.EnablePartialMessages, ChoosePartialMessages, MusicBarOptionsOf(pet), ChooseMusicBar, _themes, pet.ThemeName, ChooseTheme, _characters, _character.Name, ChooseCharacter, _files.Folder, _connectors, _subscriptions, _web) { Topmost = KeepOnTopItem.IsChecked };
        RememberSize(window, _settingsSize);
        window.Closed += (_, _) => _ = CheckConnectorsAsync();
        window.Show();
    }

    void InfoAndLicenses_Click(object sender, RoutedEventArgs e)
    {
        if (_infoWindow is { IsVisible: true })
        {
            _infoWindow.Close();
            return;
        }
        var window = _infoWindow = new InfoWindow { Topmost = KeepOnTopItem.IsChecked };
        window.Closed += (_, _) => _infoWindow = null;
        window.Show();
    }

    void RememberSize(Window window, JsonFile<WindowSize?> file)
    {
        var size = (file.Load() ?? new WindowSize(window.Width, window.Height)).FitIn(ScreenArea.Of(PetArea));
        window.Width = size.Width;
        if (!double.IsNaN(size.Height))
        {
            (window.SizeToContent, window.MaxHeight, window.Height) = (SizeToContent.Manual, double.PositiveInfinity, size.Height);
        }
        window.Closed += (_, _) =>
        {
            if (window.WindowState == WindowState.Normal)
            {
                file.Save(new WindowSize(window.ActualWidth, window.ActualHeight));
            }
        };
    }

    async Task CheckConnectorsAsync()
    {
        try
        {
            _problems = await _connectors.ProblemsAsync(Connectors.StatusInterval);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return;
        }
        ShowProblems();
    }

    void ShowProblems()
    {
        _dismissedProblems.IntersectWith(_problems);
        ProblemList.ItemsSource = _problems.Except(_dismissedProblems).ToArray();
    }

    void Problem_Click(object sender, MouseButtonEventArgs e) => ShowSettings();

    void DismissProblem_Click(object sender, RoutedEventArgs e)
    {
        _dismissedProblems.Add((ConnectorProblem)((FrameworkElement)sender).DataContext);
        ShowProblems();
    }

    void ShowFeed()
    {
        var any = _feed.Items.Count > 0;
        FeedCount.Text = _feed.Items.Count.ToString(CultureInfo.CurrentCulture);
        FeedBadge.SetResourceReference(Border.BackgroundProperty, any ? "Attention" : "Muted");
        FeedButton.SetResourceReference(ForegroundProperty, any ? "Attention" : "Muted");
        FeedCount.SetResourceReference(TextBlock.ForegroundProperty, any ? "OnAttention" : "Surface");
        if (any)
        {
            FeedButton.SetResourceReference(BackgroundProperty, "AttentionSoft");
        }
        else
        {
            FeedButton.ClearValue(BackgroundProperty);
        }
        _feedWindow?.ShowFeed();
        AnimateIfMoodChanged();
    }

    void Feed_Click(object sender, RoutedEventArgs e)
    {
        if (_feedWindow is { IsVisible: true })
        {
            _feedWindow.Close();
            return;
        }
        _feedWindow = new FeedWindow(_feed, _subscriptions) { Topmost = KeepOnTopItem.IsChecked };
        RememberSize(_feedWindow, _feedSize);
        _feedWindow.Closed += (_, _) => _feedWindow = null;
        _feedWindow.Show();
    }

    void ChooseHideSeconds(int seconds)
    {
        _petFile.Save(_petFile.Load() with { HideSeconds = seconds });
        _hideTime = TimeSpan.FromSeconds(seconds);
        FadeWhenIdle();
    }

    void ChooseLastResponseSeconds(int seconds)
    {
        _petFile.Save(_petFile.Load() with { LastResponseSeconds = seconds });
        _lastResponseTime = TimeSpan.FromSeconds(seconds);
        UpdateChatList();
    }

    void ChooseWebImages(bool show)
    {
        _petFile.Save(_petFile.Load() with { ShowWebImages = show });
        MarkdownConverter.ShowWebImages = show;
        Chats.ChatList.Items.Refresh();
    }

    void ChoosePartialMessages(bool enabled)
    {
        _claude.Settings = _claude.Settings with { EnablePartialMessages = enabled };
        _conversation.Restart();
    }

    void ChooseMusicBar(MusicBarOptions options)
    {
        _petFile.Save(_petFile.Load() with { ShowMusicBar = options.Show, AlwaysShowMusicBar = options.Always, MusicBarHideSeconds = options.HideSeconds });
        Music.Configure(options);
    }

    void ChooseCharacter(Character chosen)
    {
        _petFile.Save(_petFile.Load() with { CharacterName = chosen.Name });
        UseCharacter(chosen);
    }

    void ChooseLanguage(Translation chosen)
    {
        if (chosen == Strings.Current)
        {
            return;
        }
        _petFile.Save(_petFile.Load() with { LanguageName = chosen.Name });
        ((App)Application.Current).Use(chosen);
    }

    void ChooseTheme(Theme chosen)
    {
        _petFile.Save(_petFile.Load() with { ThemeName = chosen.Name });
        ((App)Application.Current).Use(chosen);
    }

    void Touch()
    {
        _lastActivity = DateTime.UtcNow;
        FadeWhenIdle();
    }

    DateTime AnswerSeenAt => _newsSeenAt > _frontAt ? _newsSeenAt : _frontAt;

    void FadeWhenIdle()
    {
        var now = DateTime.UtcNow;
        if (ForegroundWindow.IsThisApp())
        {
            MarkLastResponseSeen(now);
            _frontAt = now;
        }
        var show = !AutoHideItem.IsChecked || Input.Text.Length > 0 || IsMouseOver || _shown && Pet.IsMouseOver
            || _conversation.IsWaitingForUser || _conversation.AnsweredAt > AnswerSeenAt || now - _lastActivity < _hideTime || now - _frontAt < _hideTime;
        if (show == _shown)
        {
            return;
        }
        _shown = show;
        if (_shown)
        {
            UpdateChatList();
            RevealOlderChats();
        }
        Fade(ChatArea, _shown);
        Fade(TemporaryBanner, _shown);
        Fade(ToolbarArea, _shown);
    }

    static TimeSpan FadeTime(bool show) => TimeSpan.FromMilliseconds(show ? 150 : 300);

    static void Fade(UIElement element, bool show) => element.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, FadeTime(show)));

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Apply(OnScreen());
        FitToScreen();
        _petWindow.Show();
        SystemEvents.DisplaySettingsChanged += Display_Changed;
        UpdateToolbar();
        Chats.ScrollToNewest();
        _ = _conversation.StartAsync();
        _ = Music.StartAsync(_volume);
        if (!_chatOnly)
        {
            _feedTimer.Start();
        }
    }

    Placement OnScreen()
    {
        Place();
        var screen = ScreenArea.Of(PetArea);
        var pet = PetArea.TranslatePoint(new Point(), this);
        var (petLeftToRight, petTopToBottom) = (ActualWidth - pet.X, ActualHeight - pet.Y);
        var corners = new Rect(screen.Left + petLeftToRight, screen.Top + petTopToBottom, screen.Width - PetArea.ActualWidth, screen.Height - petTopToBottom);
        return _placement.ClampedTo(corners);
    }

    Placement PlacementAtPet => _placement with { Right = _petWindow.Left + PetArea.Width + PetArea.Margin.Right, Bottom = _petWindow.Top + PetArea.Height };

    void Display_Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _placement = PlacementAtPet;
        Apply(OnScreen());
        FitToScreen();
    });

    void Apply(Placement next)
    {
        _placement = next with { ChatHeight = Math.Max(next.ChatHeight, _minChatHeight) };
        Root.Width = RootWidth();
        Place();
        FitChatHeight();
    }

    double RootWidth() => Math.Min(Math.Max(_placement.Width, NarrowestWidth()), Width);

    double NarrowestWidth()
    {
        Buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarEdges = Toolbar.Margin.Left + Toolbar.Margin.Right + Toolbar.Padding.Left + Toolbar.Padding.Right + Toolbar.BorderThickness.Left + Toolbar.BorderThickness.Right;
        return Buttons.DesiredSize.Width + toolbarEdges + PetArea.Width + PetArea.Margin.Left + PetArea.Margin.Right;
    }

    void Place()
    {
        Left = _placement.Right - Width;
        Top = _placement.Bottom - Height;
        (_petWindow.Left, _petWindow.Top) = (_placement.Right - PetArea.Margin.Right - PetArea.Width, _placement.Bottom - PetArea.Height);
    }

    void FitToScreen()
    {
        var screen = ScreenArea.Of(PetArea);
        (Width, Height) = (screen.Width, screen.Height);
        Apply(_placement);
    }

    void Root_SizeChanged(object sender, SizeChangedEventArgs e) => FitChatHeight();

    void FitChatHeight()
    {
        Chats.ChatScroll.Height = Math.Clamp(Math.Min(_placement.Bottom - ScreenArea.Of(PetArea).Top, Height) - (Root.ActualHeight - Chats.ChatScroll.ActualHeight), 0, _placement.ChatHeight);
    }

    void BringToFront(Window window)
    {
        // Toggling Topmost raises the window to the top of its z-order band.
        window.Topmost = !KeepOnTopItem.IsChecked;
        window.Topmost = KeepOnTopItem.IsChecked;
    }

    void Window_Deactivated(object sender, EventArgs e) => FocusManager.SetFocusedElement(this, null);

    void Window_Closed(object sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= Display_Changed;
        _conversation.Save();
        _settingsWindow?.Close();
        _feedWindow?.Close();
        _infoWindow?.Close();
        _petWindow.Close();
        _claude.End();
    }

    void Window_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateNews();
        Touch();
        AnimateIfMoodChanged();
    }

    void Conversation_Changed()
    {
        if (_conversation.AnsweredAt > _poppedUp.AnsweredAt || _conversation.IsWaitingForUser && !_poppedUp.Waiting)
        {
            BringToFront(this);
        }
        _poppedUp = (_conversation.AnsweredAt, _conversation.IsWaitingForUser);
        FadeWhenIdle();
        var following = Chats.IsAtBottom;
        UpdateNews();
        UpdateChatList();
        UpdateToolbar();
        if (following)
        {
            Chats.ScrollToNewest();
        }
        Animate();
    }

    void UpdateToolbar()
    {
        CostText.Text = _conversation.Cost.ToString("$0.00", CultureInfo.InvariantCulture);
        StopButton.Visibility = _conversation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        TasksText.Text = Strings.Format("Main.TasksRunning", _conversation.BackgroundTasks);
        TasksText.Visibility = _conversation.BackgroundTasks > 0 && !_conversation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        PromptsButton.Visibility = StopButton.Visibility == Visibility.Collapsed && TasksText.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        _clock.IsEnabled = _conversation.IsBusy;
        ShowFolder();
        FitWidth();
    }

    void UpdateNews()
    {
        if (IsMouseOver || Input.IsKeyboardFocused)
        {
            var now = DateTime.UtcNow;
            MarkLastResponseSeen(now);
            _newsSeenAt = now;
        }
    }

    void UpdateChatList()
    {
        if (!_shown)
        {
            return;
        }
        _lastResponseVisible = IsLastResponseVisible(DateTime.UtcNow);
        var any = false;
        foreach (var (index, chat) in _conversation.Chats.Index())
        {
            chat.Shown = _chatsExpanded && _revealed.Contains(index) || _conversation.IsCurrent(chat, _lastResponseVisible, _sentAt);
            any |= chat.Shown;
        }
        ChatArea.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    void RevealOlderChats()
    {
        if (_chatsExpanded && !_revealing)
        {
            _revealing = true;
            Dispatcher.BeginInvoke(RevealOlderChat, DispatcherPriority.Background);
        }
    }

    void RevealOlderChat()
    {
        _revealing = false;
        if (!_chatsExpanded || !_shown || !_revealed.RevealOlder())
        {
            return;
        }
        var scroll = Chats.ChatScroll;
        var fromBottom = scroll.ScrollableHeight - scroll.VerticalOffset;
        UpdateChatList();
        scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(scroll.ScrollableHeight - fromBottom);
        RevealOlderChats();
    }

    bool IsLastResponseVisible(DateTime now) => _conversation.AnsweredAt > _lastResponseSeenAt || now - _lastResponseSeenAt < _lastResponseTime;

    void MarkLastResponseSeen(DateTime now)
    {
        if (_conversation.AnsweredAt > _lastResponseSeenAt)
        {
            _lastResponseSeenAt = now;
        }
    }

    void RefreshLastResponseVisibility()
    {
        if (_shown && IsLastResponseVisible(DateTime.UtcNow) != _lastResponseVisible)
        {
            UpdateChatList();
        }
    }

    void Pet_MouseEnterOrLeave(object sender, MouseEventArgs e) => Animate();

    void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _placement = PlacementAtPet;
        (_pressed, _dragging, _dragStart, _pressedAt) = (true, false, e.GetPosition(_petWindow), DateTime.UtcNow);
        Touch();
        Pet.CaptureMouse();
        Animate();
    }

    void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }
        var offset = e.GetPosition(_petWindow) - _dragStart;
        if (!_dragging)
        {
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            _dragging = true;
        }
        if (offset.Length < 1)
        {
            return;
        }
        _placement = _placement with { Right = _placement.Right + offset.X, Bottom = _placement.Bottom + offset.Y };
        Place();
        if (Math.Abs(offset.X) >= 1)
        {
            Facing.ScaleX = offset.X > 0 ? -1 : 1;
        }
        _lastMove = DateTime.UtcNow;
        AnimateIfMoodChanged();
    }

    void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var clicked = _pressed && !_dragging && DateTime.UtcNow - _pressedAt < _clickTime;
        var now = DateTime.UtcNow;
        MarkLastResponseSeen(now);
        _newsSeenAt = now;
        Pet.ReleaseMouseCapture();
        Activate();
        foreach (var window in new Window?[] { _settingsWindow, _feedWindow, _infoWindow }.OfType<Window>().Where(window => window.IsVisible))
        {
            BringToFront(window);
        }
        if (!clicked)
        {
            return;
        }
        _giggleUntil = DateTime.UtcNow + _giggleTime;
        Animate();
    }

    void Pet_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            _placementFile.Save(_beforeFullScreen ?? _placement);
        }
        (_pressed, _dragging) = (false, false);
        Facing.ScaleX = 1;
        FitToScreen();
        Animate();
    }

    void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _beforeFullScreen = null;
        _resizeStart = (Mouse.GetPosition(this), Root.ActualWidth, Chats.ChatScroll.Height);
        Chats.ScrollToNewest();
        Chats.FreezeHiddenChats();
    }

    void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var moved = _resizeStart.Mouse - Mouse.GetPosition(this);
        var dragged = _placement.DraggedTo(_resizeStart.Width + moved.X, NarrowestWidth());
        Apply(sender == WidthGrip ? dragged : dragged with { ChatHeight = _resizeStart.ChatHeight + moved.Y });
        Chats.FreezeHiddenChats();
    }

    void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Chats.ThawChats();
        _placementFile.Save(_placement);
    }

    void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_beforeFullScreen is { } previous)
        {
            _beforeFullScreen = null;
            _placement = previous;
            Apply(OnScreen());
        }
        else
        {
            _beforeFullScreen = _placement;
            var screen = ScreenArea.Of(PetArea);
            Apply(new Placement(screen.Right, screen.Bottom, screen.Width, screen.Height));
        }
        Touch();
        Chats.ScrollToNewest();
    }

    void FocusInput()
    {
        Activate();
        Keyboard.Focus(Input);
        Touch();
        Animate();
    }

    void LeaveInput()
    {
        FocusManager.SetFocusedElement(this, null);
        Keyboard.ClearFocus();
        Touch();
        Animate();
    }

    void Input_KeyDown(object sender, KeyEventArgs e)
    {
        UpdateNews();
        Touch();
        if (e.Key == Key.Escape)
        {
            LeaveInput();
        }
    }

    async void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            await _attaching;
            if (Input.Text.Trim().Length == 0 && _attachedFiles.Count + _attachedImages.Count == 0)
            {
                return;
            }
            var text = Input.Text.Trim();
            Input.Clear();
            await SendAsync(text);
        }
        else if (e.Key == Key.Up && Input.Text.Length == 0 && _lastPrompt.Length > 0)
        {
            e.Handled = true;
            Input.Text = _lastPrompt;
            Input.CaretIndex = _lastPrompt.Length;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && ClipboardAttachments.ClipboardFiles() is { } files)
        {
            e.Handled = true;
            await AttachAsync(files);
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && ClipboardAttachments.ClipboardImage() is { } image)
        {
            e.Handled = true;
            AttachScreenshot(image);
        }
    }

    async void Snip_Click(object sender, RoutedEventArgs e)
    {
        SnipButton.IsEnabled = false;
        try
        {
            if (await ScreenSnip.CaptureAsync() is not { } image)
            {
                return;
            }
            AttachScreenshot(image);
            FocusInput();
        }
        catch (Win32Exception)
        {
            Warning.Show(this, Strings.Of("Main.SnipFailed"));
        }
        finally
        {
            SnipButton.IsEnabled = true;
        }
    }

    void AttachScreenshot(BitmapSource image)
    {
        _attachedImages.Add(new ImageAttachment("screenshot.png", "image/png", ClipboardAttachments.EncodePng(image)));
        UpdateAttachments();
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await AttachAsync(files);
        }
    }

    Task AttachAsync(IEnumerable<string> files) => _attaching = ReadAttachmentsAsync([.. files], _attaching);

    async Task ReadAttachmentsAsync(string[] files, Task previous)
    {
        var images = await Task.Run(() => files.Select(ImageAttachment.FromFile).ToArray());
        await previous;
        foreach (var (file, image) in files.Zip(images))
        {
            if (image is not null)
            {
                _attachedImages.Add(image);
            }
            else
            {
                _attachedFiles.Add(file);
            }
        }
        UpdateAttachments();
        FocusInput();
    }

    void ClearAttachments_Click(object sender, RoutedEventArgs e) => ClearAttachments();

    void ClearAttachments()
    {
        _attachedFiles.Clear();
        _attachedImages.Clear();
        UpdateAttachments();
    }

    void UpdateAttachments()
    {
        var names = _attachedFiles.Select(Path.GetFileName).Concat(_attachedImages.Select(image => image.Name)).ToArray();
        AttachmentChip.Visibility = names.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentChip.ToolTip = $"{string.Join('\n', names)}\n{Strings.Of("Main.ClickToRemove")}";
        AttachmentCount.Text = $"{names.Length}";
    }

    void ToggleChats_Click(object sender, RoutedEventArgs e)
    {
        _chatsExpanded = !_chatsExpanded;
        _petFile.Save(_petFile.Load() with { ChatsExpanded = _chatsExpanded });
        ToggleChats.Content = _chatsExpanded ? _collapseIcon : _expandIcon;
        _revealed.StartFromNewest();
        Touch();
        UpdateChatList();
        Chats.ScrollToNewest();
        RevealOlderChats();
        Animate();
    }

    void NewChat_Click(object sender, RoutedEventArgs e) => _conversation.Reset();

    void Stop_Click(object sender, RoutedEventArgs e) => _conversation.Cancel();

    void MoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        FullScreenItem.IsChecked = _beforeFullScreen is not null;
        SaveChatItem.IsEnabled = _conversation.Chats.Count > 0;
        UsageItem.Header = _conversation.Usage is { } usage
            ? Strings.Format("Main.Usage", usage.FiveHourAt(DateTimeOffset.UtcNow), usage.SevenDayAt(DateTimeOffset.UtcNow))
            : Strings.Of("Main.UsageLater");
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = (_claude.Settings.WorkingDirectory ?? _claude.Settings.LastFolder) is { } last && Directory.Exists(last) ? last : "" };
        if (dialog.ShowDialog(this) == true)
        {
            UseFolder(dialog.FolderName);
        }
        ShowFolder();
    }

    void NewHamster_Click(object sender, RoutedEventArgs e) => Warning.Open(this, new ProcessStartInfo(Environment.ProcessPath!), Strings.Of("Main.NewHamsterFailed"));

    void Chat_Click(object sender, RoutedEventArgs e)
    {
        UseFolder(null);
        ShowFolder();
    }

    void UseFolder(string? folder)
    {
        if (folder == _claude.Settings.WorkingDirectory || !CanSwitch)
        {
            return;
        }
        _claude.Settings = _claude.Settings with { WorkingDirectory = folder, LastFolder = folder ?? _claude.Settings.WorkingDirectory };
        _revealed.StartFromNewest();
        _conversation.Switch(() => _files.Chats(folder));
        Chats.ScrollToNewest();
        RevealOlderChats();
    }

    bool CanSwitch => !_conversation.IsBusy && _conversation.BackgroundTasks == 0;

    void ShowFolder()
    {
        var folder = _claude.Settings.WorkingDirectory;
        var idle = CanSwitch;
        (ChatButton.IsChecked, FolderButton.IsChecked, ChatButton.IsEnabled, FolderButton.IsEnabled) = (folder is null, folder is not null, idle, idle);
        (ChatButton.ToolTip, FolderButton.ToolTip) = idle ? (Strings.Of("Main.ChatHint"), folder ?? Strings.Of("Main.FolderHint")) : (SwitchWhenIdle, SwitchWhenIdle);
        TemporaryBanner.Visibility = _conversation.IsTemporary ? Visibility.Visible : Visibility.Collapsed;
        ShowInputHint();
    }

    void Input_TextChanged(object sender, TextChangedEventArgs e) => ShowInputHint();

    void ShowInputHint() => InputHint.Visibility = _conversation.IsTemporary && Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Instructions_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_files.Instructions)!);
        File.AppendAllText(_files.Instructions, "");
        Warning.Open(this, new ProcessStartInfo(_files.Instructions) { UseShellExecute = true }, Strings.Of("Main.InstructionsFailed"));
    }

    async void SaveChat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"Hamster-chat {DateTime.Now:yyyy-MM-dd HH.mm}",
            DefaultExt = ".md",
            Filter = $"Markdown (*.md)|*.md|{Strings.Of("Main.TextFiles")} (*.txt)|*.txt",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, ChatExport.ToMarkdown(_conversation.Chats.Select(chat => chat.ToRecord())));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warning.Show(this, Strings.Format("Main.SaveChatFailed", exception.Message));
        }
    }

    void PromptsMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Items.Clear();
        try
        {
            _prompts.EnsureFolder();
            foreach (var prompt in _prompts.Prompts)
            {
                menu.Items.Add(new MenuItem { Header = prompt.Name, Tag = prompt });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        menu.Items.Add(new MenuItem { Header = Strings.Of("Main.AddPrompt") });
    }

    async void Prompt_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuItem)e.OriginalSource).Tag is not Prompt prompt)
        {
            Warning.Open(this, new ProcessStartInfo(_prompts.Folder) { UseShellExecute = true }, Strings.Of("Main.PromptsFolderFailed"));
            return;
        }
        try
        {
            var text = (await File.ReadAllTextAsync(prompt.File)).Trim();
            if (text.Length == 0)
            {
                return;
            }
            await SendAsync(text, prompt.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warning.Show(this, Strings.Format("Main.PromptFailed", exception.Message));
        }
    }

    void OpenClaude_Click(object sender, RoutedEventArgs e) => Warning.Open(this, new ProcessStartInfo("claude://") { UseShellExecute = true }, Strings.Of("Main.OpenClaudeFailed"));

    void ShowChoices_Click(object sender, RoutedEventArgs e)
    {
        if (sender == _closedByItsButton)
        {
            _closedByItsButton = null;
            return;
        }
        var choices = ((Button)sender).ContextMenu;
        choices.PlacementTarget = (UIElement)sender;
        choices.Placement = PlacementMode.Top;
        choices.IsOpen = true;
    }

    void Choices_Closed(object sender, RoutedEventArgs e)
    {
        _closedByItsButton = ((ContextMenu)sender).PlacementTarget is Button button && Mouse.LeftButton == MouseButtonState.Pressed
            && new Rect(button.RenderSize).Contains(Mouse.GetPosition(button)) ? button : null;
    }

    void Model_Click(object sender, RoutedEventArgs e)
    {
        var model = Choose(ModelButton, (string)((MenuItem)e.OriginalSource).Tag);
        _claude.Choose(_claude.Settings with { Model = model }, ClaudeProtocol.SetModel(model));
    }

    void Effort_Click(object sender, RoutedEventArgs e)
    {
        var effort = Choose(EffortButton, (string)((MenuItem)e.OriginalSource).Tag);
        _claude.Choose(_claude.Settings with { Effort = effort }, ClaudeProtocol.SetEffort(effort));
    }

    void Mode_Click(object sender, RoutedEventArgs e)
    {
        var mode = Choose(ModeButton, (string)((MenuItem)e.OriginalSource).Tag);
        _claude.Choose(_claude.Settings with { PermissionMode = mode }, ClaudeProtocol.SetPermissionMode(mode));
    }

    string Choose(Button button, string value)
    {
        button.Content = value;
        foreach (var item in button.ContextMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = (string)item.Tag == value;
            if (item.IsChecked)
            {
                button.Content = item.Header;
            }
        }
        FitWidth();
        return value;
    }

    void FitWidth()
    {
        if (!IsLoaded)
        {
            return;
        }
        UpdateLayout();
        Apply(_placement);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_resizeQueued)
        {
            return;
        }
        _resizeQueued = true;
        Dispatcher.BeginInvoke(ResizePet, DispatcherPriority.Background);
    }

    void ResizePet()
    {
        _resizeQueued = false;
        Chats.FreezeHiddenChats();
        ShowSize();
        UpdateLayout();
        Apply(OnScreen());
        Chats.FreezeHiddenChats();
    }

    void SizeSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        Dispatcher.BeginInvoke(Chats.ThawChats, DispatcherPriority.Background);
        _petFile.Save(_petFile.Load() with { SizePercent = (int)SizeSlider.Value });
    }

    void AutoHide_Click(object sender, RoutedEventArgs e)
    {
        _petFile.Save(_petFile.Load() with { AutoHide = AutoHideItem.IsChecked });
        FadeWhenIdle();
    }

    void KeepOnTop_Click(object sender, RoutedEventArgs e)
    {
        _petFile.Save(_petFile.Load() with { KeepWindowsOnTop = KeepOnTopItem.IsChecked });
        foreach (var window in new Window?[] { this, _settingsWindow, _feedWindow, _infoWindow }.OfType<Window>().Where(window => window.IsVisible))
        {
            window.Topmost = KeepOnTopItem.IsChecked;
        }
    }

    void ShowSize()
    {
        var scale = SizeSlider.Value / 100;
        (PetArea.Width, PetArea.Height) = (_petWidth * scale, _petHeight * scale);
        (_petWindow.Width, _petWindow.Height) = (PetArea.Width, PetArea.Height);
        SizeText.Text = $"{SizeSlider.Value}%";
    }

    async Task SendAsync(string text, string? title = null)
    {
        await _attaching;
        _lastPrompt = text;
        var prompt = Conversation.WithAttachments(text, _attachedFiles, _attachedImages);
        ImageAttachment[] images = [.. _attachedImages];
        ClearAttachments();
        Chats.ScrollToNewest();
        _sentAt = DateTime.UtcNow;
        await _conversation.SendAsync(prompt, images, title);
    }
}
