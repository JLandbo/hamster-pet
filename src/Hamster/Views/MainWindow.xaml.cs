using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Hamster.Core.Chats;
using Hamster.Core.Claude;
using Hamster.Core.Connections;
using Hamster.Core.Feeds;
using Hamster.Core.Languages;
using Hamster.Core.Music;
using Hamster.Core.Pet;
using Hamster.Core.Prompts;
using Hamster.Core.Storage;
using Hamster.Desktop;
using Hamster.Rendering;
using Hamster.Themes;

namespace Hamster.Views;

public partial class MainWindow : Window
{
    const double DefaultWidth = 396;
    const double DefaultChatHeight = 900;
    const double MinChatHeight = 120;
    const double PetWidth = 160;
    const double PetHeight = 144;
    static readonly TimeSpan GiggleTime = TimeSpan.FromSeconds(1.5);
    static readonly TimeSpan HappyTime = TimeSpan.FromSeconds(4);
    static readonly TimeSpan SadTime = TimeSpan.FromSeconds(4);
    const double TiredUsage = 0.9;
    static readonly TimeSpan AwakeTime = TimeSpan.FromMinutes(1);
    static readonly TimeSpan MovingTime = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan ClickTime = TimeSpan.FromMilliseconds(300);
    const string CollapseIcon = "\uE70D";
    const string ExpandIcon = "\uE70E";
    const string PlayIcon = "\uE768";
    const string PauseIcon = "\uE769";

    readonly ClaudeClient claude;
    readonly Conversation conversation;
    readonly JsonFile<Placement> placementFile;
    readonly JsonFile<WindowSize?> settingsSize;
    readonly JsonFile<WindowSize?> feedSize;
    readonly string instructionsFile;
    readonly string data;
    readonly ChatOwner owner = new();
    readonly ChatOwner settingsOwner = new();
    readonly bool chatOnly;
    readonly List<string> attachedFiles = [];
    readonly List<ImageAttachment> attachedImages = [];
    readonly CharacterLibrary characters;
    readonly PromptLibrary prompts;
    readonly ThemeLibrary themes;
    readonly JsonFile<PetSettings> petFile;
    readonly Connectors connectors;
    readonly Subscriptions subscriptions;
    readonly WebSession web;
    readonly Feed feed;
    readonly DispatcherTimer timer = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer feedTimer = new() { Interval = Subscriptions.Interval };
    readonly HashSet<ConnectorProblem> dismissedProblems = [];
    readonly Window petWindow;

    IReadOnlyList<ConnectorProblem> problems = [];
    string lastPrompt = "";
    DateTime feedNewsAt;
    MusicPlayer? music;
    Character character = Character.Hamster;
    Dictionary<Mood, BitmapSource[]> images = [];
    SettingsWindow? settings;
    FeedWindow? feedWindow;
    Mood mood;
    int frame;
    bool feedFetched, resizeQueued, pressed, dragging, chatsExpanded, shown = true;
    Point dragStart;
    DateTime pressedAt, giggleUntil, lastMove, lastActivity = DateTime.UtcNow, newsSeenAt = DateTime.UtcNow;
    Placement placement;
    Placement? beforeFullScreen;
    (Point Mouse, double Width, double ChatHeight) resizeStart;
    (DateTime AnsweredAt, bool Waiting) poppedUp;
    TimeSpan hideTime;
    DateTime hiddenAt;
    DateTime frontAt;
    double? readingOffset;
    Button? closedByItsButton;

    public MainWindow()
    {
        InitializeComponent();
        PetArea.Children.Remove(Pet);
        petWindow = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize, UseLayoutRounding = true, AllowDrop = true, Content = Pet,
        };
        petWindow.DragOver += Window_DragOver;
        petWindow.Drop += Window_Drop;
        petWindow.Closed += (_, _) => Dispatcher.BeginInvoke(Close);
        (Width, Height) = (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        data = DataFolder.Current;
        var settingsFile = Path.Combine(data, "settings.json");
        chatOnly = !settingsOwner.TryOwn(settingsFile);
        petFile = new JsonFile<PetSettings>(Path.Combine(data, "pet.json"), PetSettings.Default, chatOnly);
        var pet = petFile.Load();
        SizeSlider.Value = pet.SizePercent;
        ShowSize();
        SizeSlider.ValueChanged += SizeSlider_ValueChanged;
        AutoHideItem.IsChecked = pet.AutoHide;
        MarkdownConverter.ShowWebImages = pet.ShowWebImages;
        chatsExpanded = pet.ChatsExpanded;
        ((App)Application.Current).Use(Translation.All.FirstOrDefault(translation => translation.Name == pet.LanguageName) ?? Translation.Danish);
        instructionsFile = Path.Combine(data, "instructions.txt");
        claude = new ClaudeClient(Path.Combine(data, "workspace"), instructionsFile,
            new JsonFile<ClaudeSettings>(settingsFile, ClaudeSettings.Default, chatOnly));
        if (!chatOnly)
            SavedChats.Migrate(data, claude.Settings.WorkingDirectory);
        conversation = new Conversation(claude, StoreFor(claude.Settings.WorkingDirectory));
        hideTime = TimeSpan.FromSeconds(pet.HideSeconds);
        placementFile = new JsonFile<Placement>(Path.Combine(data, "placement.json"), DefaultPlacement, chatOnly);
        placement = placementFile.Load();
        if (chatOnly)
            placement = placement.CenteredIn(SystemParameters.WorkArea, new Size(PetArea.Width, PetArea.Height));
        settingsSize = new JsonFile<WindowSize?>(Path.Combine(data, "settings-window.json"), null, chatOnly);
        feedSize = new JsonFile<WindowSize?>(Path.Combine(data, "feed-window.json"), null, chatOnly);
        characters = new CharacterLibrary(Path.Combine(data, "Characters"));
        prompts = new PromptLibrary(Path.Combine(data, "Prompts"));
        themes = new ThemeLibrary(Path.Combine(data, "Themes"));
        connectors = new Connectors(claude, conversation.Restart, () => conversation.RestartPending);
        web = new WebSession(Path.Combine(data, "WebLogin"));
        subscriptions = new Subscriptions(connectors, new ClaudeFetcher(Path.Combine(data, "workspace")), web,
            new JsonFile<SubscriptionSettings>(Path.Combine(data, "subscriptions.json"), SubscriptionSettings.Empty, chatOnly));
        feed = new Feed([(Subscriptions.JiraSource, subscriptions.FetchJiraAsync), (Subscriptions.GitHubSource, subscriptions.FetchGitHubAsync)]);
        feed.Changed += news =>
        {
            if (news)
                feedNewsAt = DateTime.UtcNow;
            ShowFeed();
        };
        subscriptions.Changed += () => _ = feed.RefreshAsync(subscriptionsChanged: true);
        feedTimer.Tick += (_, _) => _ = feed.RefreshAsync();
        if (!ModeButton.ContextMenu.Items.OfType<MenuItem>().Any(item => (string)item.Tag == claude.Settings.PermissionMode))
            claude.Settings = claude.Settings with { PermissionMode = ClaudeSettings.Default.PermissionMode };
        Choose(ModelButton, claude.Settings.Model);
        Choose(EffortButton, claude.Settings.Effort);
        Choose(ModeButton, claude.Settings.PermissionMode);
        ToggleChats.Content = chatsExpanded ? CollapseIcon : ExpandIcon;
        ShowChatOnly();
        ShowFolder();
        ChatList.ItemsSource = conversation.Chats;
        UpdateChatList();
        conversation.Changed += Conversation_Changed;
        conversation.Started += () =>
        {
            if (chatOnly)
                return;
            _ = CheckConnectorsAsync();
            if (feedFetched)
                return;
            feedFetched = true;
            _ = feed.RefreshAsync();
        };
        conversation.PermissionModeChanged += mode => claude.Settings = claude.Settings with { PermissionMode = Choose(ModeButton, mode) };
        timer.Tick += (_, _) =>
        {
            FadeWhenIdle();
            Animate();
        };
        clock.Tick += (_, _) =>
        {
            foreach (var chat in conversation.Chats)
                chat.RefreshElapsed();
        };
        Strings.Changed += () =>
        {
            ShowChatOnly();
            UpdateToolbar();
            ShowMusic();
            UpdateAttachments();
            ShowProblems();
            _ = feed.RefreshAsync();
        };
        ((App)Application.Current).Use(themes.Find(pet.ThemeName));
        UseCharacter(LoadCharacter(pet.CharacterName));
    }

    static string SwitchWhenIdle => Strings.Of("Main.SwitchWhenIdle");

    static Placement DefaultPlacement => new(SystemParameters.WorkArea.Right, SystemParameters.WorkArea.Bottom, DefaultWidth, DefaultChatHeight);

    PetStatus Status => new(
        Pressed: pressed,
        Moving: DateTime.UtcNow - lastMove < MovingTime,
        Giggling: DateTime.UtcNow < giggleUntil,
        WaitingForUser: conversation.IsWaitingForUser,
        BrowsingWeb: conversation.IsBrowsingWeb,
        Busy: conversation.IsBusy,
        Celebrating: conversation.AnsweredWithin(HappyTime, DateTime.UtcNow),
        Failed: conversation.FailedWithin(SadTime, DateTime.UtcNow) || conversation.FailedSince(AnswerSeenAt),
        Typing: Input.IsKeyboardFocused && Input.Text.Length > 0,
        HasNews: conversation.AnsweredAt > AnswerSeenAt,
        HasTaskNews: feedNewsAt > newsSeenAt,
        BackgroundWork: conversation.BackgroundTasks > 0,
        Music: music?.Track?.Playing == true,
        Tired: conversation.Usage?.HighestAt(DateTimeOffset.UtcNow) >= TiredUsage,
        Hovered: Pet.IsMouseOver,
        Awake: Input.IsKeyboardFocused || DateTime.UtcNow - lastActivity < AwakeTime);

    void Animate()
    {
        var next = MoodTransition.Next(mood, frame, Status.Mood, character.Animations[Mood.Spin].Length);
        if (next != mood)
            (mood, frame) = (next, 0);
        var frames = character.Animations[mood];
        var index = frame++ % frames.Length;
        Pet.Source = images[mood][index];
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(frames[index].Milliseconds);
        timer.Start();
    }

    Character LoadCharacter(string name)
    {
        try
        {
            return characters.Load(name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Character.Hamster;
        }
    }

    void UseCharacter(Character next)
    {
        character = next;
        images = next.Animations.ToDictionary(animation => animation.Key, animation => animation.Value.Select(frame => SpriteRenderer.Render(frame.Rows, next.Palette)).ToArray());
        frame = 0;
        Animate();
    }

    void ShowChatOnly()
    {
        if (chatOnly)
            foreach (var button in new[] { MoreButton, FeedButton })
                (button.IsEnabled, button.ToolTip, button.Opacity) = (false, Strings.Of("Main.OnlyInFirstHamster"), 0.4);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settings is { IsVisible: true })
            settings.Close();
        else
            ShowSettings();
    }

    void ShowSettings()
    {
        if (settings is { IsVisible: true })
        {
            settings.Activate();
            return;
        }
        var window = settings = new SettingsWindow(ChooseLanguage, (int)hideTime.TotalSeconds, ChooseHideSeconds, MarkdownConverter.ShowWebImages, ChooseWebImages, themes, petFile.Load().ThemeName, ChooseTheme, characters, character.Name, ChooseCharacter, connectors, subscriptions, web);
        RememberSize(window, settingsSize);
        window.Closed += (_, _) => _ = CheckConnectorsAsync();
        window.Show();
    }

    void RememberSize(Window window, JsonFile<WindowSize?> file)
    {
        var size = (file.Load() ?? new WindowSize(window.Width, window.Height)).FitIn(ScreenArea.Of(PetArea));
        window.Width = size.Width;
        if (!double.IsNaN(size.Height))
            (window.SizeToContent, window.MaxHeight, window.Height) = (SizeToContent.Manual, double.PositiveInfinity, size.Height);
        window.Closed += (_, _) =>
        {
            if (window.WindowState == WindowState.Normal)
                file.Save(new WindowSize(window.ActualWidth, window.ActualHeight));
        };
    }

    async Task CheckConnectorsAsync()
    {
        try
        {
            problems = await connectors.ProblemsAsync(Connectors.StatusInterval);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return;
        }
        ShowProblems();
    }

    void ShowProblems()
    {
        dismissedProblems.IntersectWith(problems);
        ProblemList.ItemsSource = problems.Except(dismissedProblems).ToArray();
    }

    void Problem_Click(object sender, MouseButtonEventArgs e) => ShowSettings();

    void DismissProblem_Click(object sender, RoutedEventArgs e)
    {
        dismissedProblems.Add((ConnectorProblem)((FrameworkElement)sender).DataContext);
        ShowProblems();
    }

    void ShowFeed()
    {
        var any = feed.Items.Count > 0;
        FeedCount.Text = feed.Items.Count.ToString(CultureInfo.CurrentCulture);
        FeedBadge.SetResourceReference(Border.BackgroundProperty, any ? "Attention" : "Muted");
        FeedButton.SetResourceReference(ForegroundProperty, any ? "Attention" : "Muted");
        FeedCount.SetResourceReference(TextBlock.ForegroundProperty, any ? "OnAttention" : "Surface");
        if (any)
            FeedButton.SetResourceReference(BackgroundProperty, "AttentionSoft");
        else
            FeedButton.ClearValue(BackgroundProperty);
        feedWindow?.ShowFeed();
        if (Status.Mood != mood)
            Animate();
    }

    void Feed_Click(object sender, RoutedEventArgs e)
    {
        if (feedWindow is { IsVisible: true })
        {
            feedWindow.Close();
            return;
        }
        feedWindow = new FeedWindow(feed, subscriptions);
        RememberSize(feedWindow, feedSize);
        feedWindow.Closed += (_, _) => feedWindow = null;
        feedWindow.Show();
    }

    async Task StartMusicAsync()
    {
        try
        {
            music = await MusicPlayer.StartAsync();
        }
        catch (COMException)
        {
            return;
        }
        music.Changed += ShowMusic;
        ShowMusic();
    }

    void ShowMusic()
    {
        var track = music?.Track;
        MusicBubble.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        (TrackText.Text, MusicBubble.ToolTip) = (track?.Text, track?.Text);
        (PlayPauseButton.Content, PlayPauseButton.ToolTip) = track?.Playing == true ? (PauseIcon, Strings.Of("Main.Pause")) : (PlayIcon, Strings.Of("Main.Play"));
        if (Status.Mood != mood)
            Animate();
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e) => await (music?.PlayPauseAsync() ?? Task.CompletedTask);

    async void NextTrack_Click(object sender, RoutedEventArgs e) => await (music?.NextAsync() ?? Task.CompletedTask);

    async void PreviousTrack_Click(object sender, RoutedEventArgs e) => await (music?.PreviousAsync() ?? Task.CompletedTask);

    void ChooseHideSeconds(int seconds)
    {
        petFile.Save(petFile.Load() with { HideSeconds = seconds });
        hideTime = TimeSpan.FromSeconds(seconds);
        FadeWhenIdle();
    }

    void ChooseWebImages(bool show)
    {
        petFile.Save(petFile.Load() with { ShowWebImages = show });
        MarkdownConverter.ShowWebImages = show;
        ChatList.Items.Refresh();
    }

    void ChooseCharacter(Character chosen)
    {
        petFile.Save(petFile.Load() with { CharacterName = chosen.Name });
        UseCharacter(chosen);
    }

    void ChooseLanguage(Translation chosen)
    {
        if (chosen == Strings.Current)
            return;
        petFile.Save(petFile.Load() with { LanguageName = chosen.Name });
        ((App)Application.Current).Use(chosen);
    }

    void ChooseTheme(Theme chosen)
    {
        petFile.Save(petFile.Load() with { ThemeName = chosen.Name });
        ((App)Application.Current).Use(chosen);
    }

    void Touch()
    {
        lastActivity = DateTime.UtcNow;
        FadeWhenIdle();
    }

    DateTime AnswerSeenAt => newsSeenAt > frontAt ? newsSeenAt : frontAt;

    void FadeWhenIdle()
    {
        var now = DateTime.UtcNow;
        if (ForegroundWindow.IsThisApp())
            frontAt = now;
        var show = !AutoHideItem.IsChecked || Input.Text.Length > 0 || IsMouseOver || shown && Pet.IsMouseOver
            || conversation.IsWaitingForUser || conversation.AnsweredAt > AnswerSeenAt || now - lastActivity < hideTime || now - frontAt < hideTime;
        if (show == shown)
            return;
        shown = show;
        if (shown)
            UpdateChatList();
        else
            hiddenAt = now;
        Fade(ChatArea, shown);
        Fade(TemporaryBanner, shown);
        Fade(ToolbarArea, shown);
    }

    static TimeSpan FadeTime(bool show) => TimeSpan.FromMilliseconds(show ? 150 : 300);

    static void Fade(UIElement element, bool show) => element.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, FadeTime(show)));

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Apply(OnScreen());
        FitToScreen();
        petWindow.Show();
        SystemEvents.DisplaySettingsChanged += Display_Changed;
        UpdateToolbar();
        ScrollToNewest();
        _ = conversation.StartAsync();
        _ = StartMusicAsync();
        if (!chatOnly)
            feedTimer.Start();
    }

    Placement OnScreen()
    {
        Place();
        var screen = ScreenArea.Of(PetArea);
        var pet = PetArea.TranslatePoint(new Point(), this);
        var (petLeftToRight, petTopToBottom) = (ActualWidth - pet.X, ActualHeight - pet.Y);
        var corners = new Rect(screen.Left + petLeftToRight, screen.Top + petTopToBottom, screen.Width - PetArea.ActualWidth, screen.Height - petTopToBottom);
        return placement.ClampedTo(corners);
    }

    Placement PlacementAtPet => placement with { Right = petWindow.Left + PetArea.Width + PetArea.Margin.Right, Bottom = petWindow.Top + PetArea.Height };

    void Display_Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        placement = PlacementAtPet;
        Apply(OnScreen());
        FitToScreen();
    });

    void Apply(Placement next)
    {
        placement = next with { ChatHeight = Math.Max(next.ChatHeight, MinChatHeight) };
        Root.Width = Math.Min(Math.Max(placement.Width, NarrowestWidth()), Width);
        Place();
        FitChatHeight();
    }

    double NarrowestWidth()
    {
        Buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarEdges = Toolbar.Margin.Left + Toolbar.Margin.Right + Toolbar.Padding.Left + Toolbar.Padding.Right + Toolbar.BorderThickness.Left + Toolbar.BorderThickness.Right;
        return Buttons.DesiredSize.Width + toolbarEdges + PetArea.Width + PetArea.Margin.Left + PetArea.Margin.Right;
    }

    void Place()
    {
        Left = placement.Right - Width;
        Top = placement.Bottom - Height;
        (petWindow.Left, petWindow.Top) = (placement.Right - PetArea.Margin.Right - PetArea.Width, placement.Bottom - PetArea.Height);
    }

    void FitToScreen()
    {
        var screen = ScreenArea.Of(PetArea);
        (Width, Height) = (screen.Width, screen.Height);
        Apply(placement);
    }

    void Root_SizeChanged(object sender, SizeChangedEventArgs e) => FitChatHeight();

    void FitChatHeight() =>
        ChatScroll.Height = Math.Clamp(Math.Min(placement.Bottom - ScreenArea.Of(PetArea).Top, Height) - (Root.ActualHeight - ChatScroll.ActualHeight),
            0, placement.ChatHeight);

    static void BringToFront(Window window)
    {
        window.Topmost = true;
        window.Topmost = false;
    }

    void Window_Deactivated(object sender, EventArgs e) => FocusManager.SetFocusedElement(this, null);

    void Window_Closed(object sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= Display_Changed;
        conversation.Save();
        settings?.Close();
        feedWindow?.Close();
        petWindow.Close();
        owner.Dispose();
        settingsOwner.Dispose();
        claude.End();
    }

    void Window_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateNews();
        Touch();
        if (Status.Mood != mood)
            Animate();
    }

    void Conversation_Changed()
    {
        if (conversation.AnsweredAt > poppedUp.AnsweredAt || conversation.IsWaitingForUser && !poppedUp.Waiting)
            BringToFront(this);
        poppedUp = (conversation.AnsweredAt, conversation.IsWaitingForUser);
        lastActivity = DateTime.UtcNow;
        FadeWhenIdle();
        var following = IsAtBottom;
        UpdateToolbar();
        UpdateNews();
        UpdateChatList();
        if (following)
            ScrollToNewest();
        Animate();
    }

    void UpdateToolbar()
    {
        CostText.Text = conversation.Cost.ToString("$0.00", CultureInfo.InvariantCulture);
        StopButton.Visibility = conversation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        TasksText.Text = Strings.Format("Main.TasksRunning", conversation.BackgroundTasks);
        TasksText.Visibility = conversation.BackgroundTasks > 0 && !conversation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        PromptsButton.Visibility = StopButton.Visibility == Visibility.Collapsed && TasksText.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        clock.IsEnabled = conversation.IsBusy;
        ShowFolder();
        FitWidth();
    }

    void UpdateNews()
    {
        if (IsMouseOver || Input.IsKeyboardFocused)
            newsSeenAt = DateTime.UtcNow;
    }

    void UpdateChatList()
    {
        if (!shown)
            return;
        var any = false;
        foreach (var chat in conversation.Chats)
        {
            chat.Shown = chatsExpanded || conversation.IsCurrent(chat, hiddenAt);
            any |= chat.Shown;
        }
        ChatArea.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    bool IsAtBottom => ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 1;

    void Pet_MouseEnterOrLeave(object sender, MouseEventArgs e) => Animate();

    void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        placement = PlacementAtPet;
        (pressed, dragging, dragStart, pressedAt) = (true, false, e.GetPosition(petWindow), DateTime.UtcNow);
        Touch();
        Pet.CaptureMouse();
        Animate();
    }

    void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!pressed)
            return;
        var offset = e.GetPosition(petWindow) - dragStart;
        if (!dragging)
        {
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragging = true;
        }
        if (offset.Length < 1)
            return;
        placement = placement with { Right = placement.Right + offset.X, Bottom = placement.Bottom + offset.Y };
        Place();
        if (Math.Abs(offset.X) >= 1)
            Facing.ScaleX = offset.X > 0 ? -1 : 1;
        lastMove = DateTime.UtcNow;
        if (Status.Mood != mood)
            Animate();
    }

    void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var clicked = pressed && !dragging && DateTime.UtcNow - pressedAt < ClickTime;
        newsSeenAt = DateTime.UtcNow;
        Pet.ReleaseMouseCapture();
        Activate();
        foreach (var window in new Window?[] { settings, feedWindow }.OfType<Window>().Where(window => window.IsVisible))
            BringToFront(window);
        if (!clicked)
            return;
        giggleUntil = DateTime.UtcNow + GiggleTime;
        Animate();
    }

    void Pet_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (dragging)
            placementFile.Save(beforeFullScreen ?? placement);
        (pressed, dragging) = (false, false);
        Facing.ScaleX = 1;
        FitToScreen();
        Animate();
    }

    void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        beforeFullScreen = null;
        resizeStart = (Mouse.GetPosition(this), Root.ActualWidth, ChatScroll.Height);
        FreezeHiddenChats();
    }

    void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var moved = resizeStart.Mouse - Mouse.GetPosition(this);
        var dragged = placement.DraggedTo(resizeStart.Width + moved.X, NarrowestWidth());
        Apply(sender == WidthGrip ? dragged : dragged with { ChatHeight = resizeStart.ChatHeight + moved.Y });
        FreezeHiddenChats();
    }

    void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        ThawChats();
        placementFile.Save(placement);
    }

    void ThawChats()
    {
        if (ChatContainers.FirstOrDefault(chat => !double.IsNaN(chat.Width)) is not { } frozen)
            return;
        frozen.ClearValue(WidthProperty);
        Dispatcher.BeginInvoke(ThawChats, DispatcherPriority.ApplicationIdle);
    }

    IEnumerable<FrameworkElement> ChatContainers =>
        Enumerable.Range(0, ChatList.Items.Count).Select(ChatList.ItemContainerGenerator.ContainerFromIndex).OfType<FrameworkElement>();

    void FreezeHiddenChats()
    {
        bool thawed;
        do
        {
            ChatList.UpdateLayout();
            thawed = false;
            foreach (var chat in ChatContainers.Where(chat => !double.IsNaN(chat.Width) && IsInView(chat)))
            {
                chat.ClearValue(WidthProperty);
                thawed = true;
            }
        }
        while (thawed);
        foreach (var chat in ChatContainers.Where(chat => double.IsNaN(chat.Width) && !IsInView(chat)))
            chat.Width = chat.ActualWidth;
    }

    bool IsInView(FrameworkElement chat) =>
        chat.TranslatePoint(new Point(), ChatScroll).Y is var top && top < ChatScroll.ActualHeight && top + chat.ActualHeight > 0;

    void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (beforeFullScreen is { } previous)
        {
            beforeFullScreen = null;
            placement = previous;
            Apply(OnScreen());
        }
        else
        {
            beforeFullScreen = placement;
            var screen = ScreenArea.Of(PetArea);
            Apply(new Placement(screen.Right, screen.Bottom, screen.Width, screen.Height));
        }
        Touch();
        ScrollToNewest();
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
            LeaveInput();
    }

    async void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (Input.Text.Trim().Length == 0 && attachedFiles.Count + attachedImages.Count == 0)
                return;
            var text = Input.Text.Trim();
            Input.Clear();
            await SendAsync(text);
        }
        else if (e.Key == Key.Up && Input.Text.Length == 0 && lastPrompt.Length > 0)
        {
            e.Handled = true;
            Input.Text = lastPrompt;
            Input.CaretIndex = lastPrompt.Length;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && ClipboardFiles() is { } files)
        {
            e.Handled = true;
            Attach(files);
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && ClipboardImage() is { } image)
        {
            e.Handled = true;
            attachedImages.Add(new ImageAttachment("screenshot.png", "image/png", EncodePng(image)));
            UpdateAttachments();
        }
    }

    static IEnumerable<string>? ClipboardFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    static BitmapSource? ClipboardImage()
    {
        try
        {
            return Clipboard.ContainsImage() && !Clipboard.ContainsText() ? Clipboard.GetImage() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    async void Snip_Click(object sender, RoutedEventArgs e)
    {
        SnipButton.IsEnabled = false;
        try
        {
            if (await ScreenSnip.CaptureAsync() is not { } image)
                return;
            attachedImages.Add(new ImageAttachment("screenshot.png", "image/png", EncodePng(image)));
            UpdateAttachments();
            FocusInput();
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, Strings.Of("Main.SnipFailed"), "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SnipButton.IsEnabled = true;
        }
    }

    static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            Attach(files);
    }

    void Attach(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (ImageAttachment.FromFile(file) is { } image)
                attachedImages.Add(image);
            else
                attachedFiles.Add(file);
        }
        UpdateAttachments();
        FocusInput();
    }

    void ClearAttachments_Click(object sender, RoutedEventArgs e) => ClearAttachments();

    void ClearAttachments()
    {
        attachedFiles.Clear();
        attachedImages.Clear();
        UpdateAttachments();
    }

    void UpdateAttachments()
    {
        var names = attachedFiles.Select(Path.GetFileName).Concat(attachedImages.Select(image => image.Name)).ToArray();
        AttachmentChip.Visibility = names.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentChip.ToolTip = $"{string.Join('\n', names)}\n{Strings.Of("Main.ClickToRemove")}";
        AttachmentCount.Text = $"{names.Length}";
    }

    void ToggleChats_Click(object sender, RoutedEventArgs e)
    {
        if (chatsExpanded)
            readingOffset = IsAtBottom ? null : ChatScroll.VerticalOffset;
        chatsExpanded = !chatsExpanded;
        petFile.Save(petFile.Load() with { ChatsExpanded = chatsExpanded });
        ToggleChats.Content = chatsExpanded ? CollapseIcon : ExpandIcon;
        Touch();
        UpdateChatList();
        if (chatsExpanded && readingOffset is { } offset)
            ChatScroll.ScrollToVerticalOffset(offset);
        else
            ScrollToNewest();
        Animate();
    }

    void ChatScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChatScroll.ScrollToVerticalOffset(ChatScroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    void NewChat_Click(object sender, RoutedEventArgs e) => conversation.Reset();

    void Stop_Click(object sender, RoutedEventArgs e) => conversation.Cancel();

    void MoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        FullScreenItem.IsChecked = beforeFullScreen is not null;
        SaveChatItem.IsEnabled = conversation.Chats.Count > 0;
        UsageItem.Header = conversation.Usage is { } usage
            ? Strings.Format("Main.Usage", usage.FiveHourAt(DateTimeOffset.UtcNow), usage.SevenDayAt(DateTimeOffset.UtcNow))
            : Strings.Of("Main.UsageLater");
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = (claude.Settings.WorkingDirectory ?? claude.Settings.LastFolder) is { } last && Directory.Exists(last) ? last : "" };
        if (dialog.ShowDialog(this) == true)
            UseFolder(dialog.FolderName);
        ShowFolder();
    }

    void NewHamster_Click(object sender, RoutedEventArgs e) => Open(new ProcessStartInfo(Environment.ProcessPath!), Strings.Of("Main.NewHamsterFailed"));

    void Chat_Click(object sender, RoutedEventArgs e)
    {
        UseFolder(null);
        ShowFolder();
    }

    void UseFolder(string? folder)
    {
        if (folder == claude.Settings.WorkingDirectory || !CanSwitch)
            return;
        claude.Settings = claude.Settings with { WorkingDirectory = folder, LastFolder = folder ?? claude.Settings.WorkingDirectory };
        conversation.Switch(() => StoreFor(folder));
    }

    bool CanSwitch => !conversation.IsBusy && conversation.BackgroundTasks == 0;

    JsonFile<SavedChats>? StoreFor(string? folder) =>
        SavedChats.FileFor(data, folder) is var file && owner.TryOwn(file) ? new JsonFile<SavedChats>(file, SavedChats.Empty) : null;

    void ShowFolder()
    {
        var folder = claude.Settings.WorkingDirectory;
        var idle = CanSwitch;
        (ChatButton.IsChecked, FolderButton.IsChecked, ChatButton.IsEnabled, FolderButton.IsEnabled) = (folder is null, folder is not null, idle, idle);
        (ChatButton.ToolTip, FolderButton.ToolTip) = idle ? (Strings.Of("Main.ChatHint"), folder ?? Strings.Of("Main.FolderHint")) : (SwitchWhenIdle, SwitchWhenIdle);
        TemporaryBanner.Visibility = conversation.IsTemporary ? Visibility.Visible : Visibility.Collapsed;
        ShowInputHint();
    }

    void Input_TextChanged(object sender, TextChangedEventArgs e) => ShowInputHint();

    void ShowInputHint() => InputHint.Visibility = conversation.IsTemporary && Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Instructions_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(instructionsFile)!);
        File.AppendAllText(instructionsFile, "");
        Open(new ProcessStartInfo(instructionsFile) { UseShellExecute = true }, Strings.Of("Main.InstructionsFailed"));
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
            return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, ChatExport.ToMarkdown(conversation.Chats.Select(chat => chat.ToRecord())));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Strings.Format("Main.SaveChatFailed", exception.Message), "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void PromptsMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Items.Clear();
        try
        {
            prompts.EnsureFolder();
            foreach (var prompt in prompts.Prompts)
                menu.Items.Add(new MenuItem { Header = prompt.Name, Tag = prompt });
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
            Open(new ProcessStartInfo(prompts.Folder) { UseShellExecute = true }, Strings.Of("Main.PromptsFolderFailed"));
            return;
        }
        try
        {
            var text = (await File.ReadAllTextAsync(prompt.File)).Trim();
            if (text.Length == 0)
                return;
            await SendAsync(text, prompt.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Strings.Format("Main.PromptFailed", exception.Message), "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OpenClaude_Click(object sender, RoutedEventArgs e) =>
        Open(new ProcessStartInfo("claude://") { UseShellExecute = true }, Strings.Of("Main.OpenClaudeFailed"));

    void Login_Click(object sender, RoutedEventArgs e) => Open(new ProcessStartInfo("claude", "auth login"), Strings.Of("Main.LoginFailed"));

    void Open(ProcessStartInfo start, string failure)
    {
        try
        {
            Process.Start(start);
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, failure, "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        var target = e.Uri.IsFile ? e.Uri.LocalPath : e.Uri.AbsoluteUri;
        Open(new ProcessStartInfo(target) { UseShellExecute = true }, Strings.Format("Main.LinkFailed", target));
        e.Handled = true;
    }

    void ShowChoices_Click(object sender, RoutedEventArgs e)
    {
        if (sender == closedByItsButton)
        {
            closedByItsButton = null;
            return;
        }
        var choices = ((Button)sender).ContextMenu;
        choices.PlacementTarget = (UIElement)sender;
        choices.Placement = PlacementMode.Top;
        choices.IsOpen = true;
    }

    void Choices_Closed(object sender, RoutedEventArgs e) =>
        closedByItsButton = ((ContextMenu)sender).PlacementTarget is Button button && Mouse.LeftButton == MouseButtonState.Pressed
            && new Rect(button.RenderSize).Contains(Mouse.GetPosition(button)) ? button : null;

    void Model_Click(object sender, RoutedEventArgs e)
    {
        var model = Choose(ModelButton, (string)((MenuItem)e.OriginalSource).Tag);
        claude.Choose(claude.Settings with { Model = model }, ClaudeProtocol.SetModel(model));
    }

    void Effort_Click(object sender, RoutedEventArgs e)
    {
        var effort = Choose(EffortButton, (string)((MenuItem)e.OriginalSource).Tag);
        claude.Choose(claude.Settings with { Effort = effort }, ClaudeProtocol.SetEffort(effort));
    }

    void Mode_Click(object sender, RoutedEventArgs e)
    {
        var mode = Choose(ModeButton, (string)((MenuItem)e.OriginalSource).Tag);
        claude.Choose(claude.Settings with { PermissionMode = mode }, ClaudeProtocol.SetPermissionMode(mode));
    }

    string Choose(Button button, string value)
    {
        button.Content = value;
        foreach (var item in button.ContextMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = (string)item.Tag == value;
            if (item.IsChecked)
                button.Content = item.Header;
        }
        FitWidth();
        return value;
    }

    void FitWidth()
    {
        if (!IsLoaded)
            return;
        UpdateLayout();
        Apply(placement);
    }

    void CopyAnswer_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        ClipboardText.Copy(((ChatItem)button.DataContext).Answer, icon => button.Content = icon);
    }

    void DeleteChat_Click(object sender, RoutedEventArgs e) => conversation.Delete((ChatItem)((FrameworkElement)sender).DataContext);

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (resizeQueued)
            return;
        resizeQueued = true;
        Dispatcher.BeginInvoke(ResizePet, DispatcherPriority.Background);
    }

    void ResizePet()
    {
        resizeQueued = false;
        ShowSize();
        UpdateLayout();
        Apply(OnScreen());
        FreezeHiddenChats();
    }

    void SizeSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        Dispatcher.BeginInvoke(ThawChats, DispatcherPriority.Background);
        petFile.Save(petFile.Load() with { SizePercent = (int)SizeSlider.Value });
    }

    void AutoHide_Click(object sender, RoutedEventArgs e)
    {
        petFile.Save(petFile.Load() with { AutoHide = AutoHideItem.IsChecked });
        FadeWhenIdle();
    }

    void ShowSize()
    {
        var scale = SizeSlider.Value / 100;
        (PetArea.Width, PetArea.Height) = (PetWidth * scale, PetHeight * scale);
        (petWindow.Width, petWindow.Height) = (PetArea.Width, PetArea.Height);
        SizeText.Text = $"{SizeSlider.Value}%";
    }

    void Allow_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.Respond(true);

    void AllowAlways_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.RespondAlways();

    void Deny_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.Respond(false);

    static UserRequest? ClickedRequest(object sender) =>
        ((FrameworkElement)sender).DataContext is UserRequest request && request.CanBeClicked(DateTime.UtcNow) ? request : null;

    void Answer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject == sender)
            e.Handled = true;
    }

    async Task SendAsync(string text, string? title = null)
    {
        lastPrompt = text;
        var prompt = Conversation.WithAttachments(text, attachedFiles, attachedImages);
        ImageAttachment[] images = [.. attachedImages];
        ClearAttachments();
        ScrollToNewest();
        await conversation.SendAsync(prompt, images, title);
    }

    void ScrollToNewest() => ChatScroll.ScrollToEnd();
}
