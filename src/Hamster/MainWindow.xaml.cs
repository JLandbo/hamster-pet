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
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Hamster;

public partial class MainWindow : Window
{
    const double DefaultWidth = 396;
    const double DefaultChatHeight = 900;
    const double MinChatHeight = 120;
    static readonly TimeSpan GiggleTime = TimeSpan.FromSeconds(1.5);
    static readonly TimeSpan HappyTime = TimeSpan.FromSeconds(4);
    static readonly TimeSpan AwakeTime = TimeSpan.FromMinutes(1);
    static readonly TimeSpan MovingTime = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan ClickTime = TimeSpan.FromMilliseconds(300);
    static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(60);
    const string CollapseIcon = "\uE70D";
    const string ExpandIcon = "\uE70E";

    readonly ClaudeClient claude;
    readonly Conversation conversation;
    readonly JsonFile<Placement> placementFile;
    readonly string instructionsFile;
    readonly List<string> attachedFiles = [];
    readonly List<ImageAttachment> attachedImages = [];
    readonly Dictionary<Mood, BitmapSource[]> images = Sprites.Animations.ToDictionary(
        animation => animation.Key, animation => animation.Value.Select(frame => SpriteRenderer.Render(frame.Rows)).ToArray());
    readonly DispatcherTimer timer = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };

    Mood mood;
    int frame;
    bool pressed, dragging, chatsExpanded = true, uiShown = true;
    Point dragStart;
    DateTime pressedAt, giggleUntil, lastMove, lastActivity = DateTime.UtcNow;
    Placement placement;
    Placement? beforeFullScreen;
    (Point Mouse, double Width, double ChatHeight) resizeStart;
    double? readingOffset;
    Button? closedByItsButton;

    public MainWindow()
    {
        InitializeComponent();
        // Resizing a transparent window makes everything flicker, so the window covers the screen and only its content changes size.
        (Width, Height) = (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hamster");
        instructionsFile = Path.Combine(data, "instructions.txt");
        claude = new ClaudeClient(Path.Combine(data, "workspace"), instructionsFile,
            new JsonFile<ClaudeSettings>(Path.Combine(data, "settings.json"), ClaudeSettings.Default));
        conversation = new Conversation(claude, new JsonFile<SavedChats>(Path.Combine(data, "chats.json"), SavedChats.Empty));
        placementFile = new JsonFile<Placement>(Path.Combine(data, "placement.json"), DefaultPlacement);
        placement = placementFile.Load();
        Choose(ModelButton, claude.Settings.Model);
        Choose(EffortButton, claude.Settings.Effort);
        Choose(ModeButton, claude.Settings.PermissionMode);
        ToggleChats.Content = CollapseIcon;
        conversation.Changed += Conversation_Changed;
        timer.Tick += (_, _) =>
        {
            UpdateChatList();
            FadeWhenIdle();
            Animate();
        };
        clock.Tick += (_, _) =>
        {
            foreach (var chat in conversation.Chats)
                chat.RefreshElapsed();
        };
        // Before Loaded: without a frame the hamster has no size when the window is placed.
        Animate();
    }

    static Placement DefaultPlacement => new(SystemParameters.WorkArea.Right, SystemParameters.WorkArea.Bottom, DefaultWidth, DefaultChatHeight);

    PetStatus Status => new(
        Pressed: pressed,
        Moving: DateTime.UtcNow - lastMove < MovingTime,
        Giggling: DateTime.UtcNow < giggleUntil,
        WaitingForUser: conversation.IsWaitingForUser,
        BrowsingWeb: conversation.IsBrowsingWeb,
        Busy: conversation.IsBusy,
        Celebrating: conversation.AnsweredWithin(HappyTime, DateTime.UtcNow),
        Hovered: Pet.IsMouseOver,
        Awake: InputBox.Visibility == Visibility.Visible || DateTime.UtcNow - lastActivity < AwakeTime);

    void Animate()
    {
        var next = MoodTransition.Next(mood, frame, Status.Mood);
        if (next != mood)
            (mood, frame) = (next, 0);
        var frames = Sprites.Animations[mood];
        var index = frame++ % frames.Length;
        Pet.Source = images[mood][index];
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(frames[index].Milliseconds);
        timer.Start();
    }

    void Touch()
    {
        lastActivity = DateTime.UtcNow;
        FadeWhenIdle();
    }

    void FadeWhenIdle()
    {
        var show = InputBox.Visibility == Visibility.Visible || DateTime.UtcNow - lastActivity < IdleTime;
        if (show == uiShown)
            return;
        uiShown = show;
        var duration = TimeSpan.FromMilliseconds(show ? 200 : 600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fade = new DoubleAnimation(show ? 1 : 0, duration);
        Toolbar.BeginAnimation(OpacityProperty, fade);
        ChatArea.BeginAnimation(OpacityProperty, fade);
        IdleOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(show ? 0 : Toolbar.ActualHeight + Toolbar.Margin.Top, duration) { EasingFunction = ease });
    }

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Apply(OnScreen(placement));
        UpdateToolbar();
        UpdateChatList();
        ScrollToNewest();
    }

    Placement OnScreen(Placement saved)
    {
        var pet = Pet.TranslatePoint(new Point(), this);
        var (petLeftToRight, petTopToBottom) = (ActualWidth - pet.X, ActualHeight - pet.Y);
        var corners = new Rect(SystemParameters.VirtualScreenLeft + petLeftToRight, SystemParameters.VirtualScreenTop + petTopToBottom,
            SystemParameters.VirtualScreenWidth - Pet.ActualWidth, SystemParameters.VirtualScreenHeight - petTopToBottom);
        return saved.ClampedTo(corners);
    }

    void Apply(Placement next)
    {
        placement = next with
        {
            Width = Math.Min(Math.Max(next.Width, NarrowestWidth()), ActualWidth),
            ChatHeight = Math.Max(next.ChatHeight, MinChatHeight),
        };
        Root.Width = placement.Width;
        Place();
        FitChatHeight();
    }

    // Narrower than its buttons, the toolbar would overlap them.
    double NarrowestWidth()
    {
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Toolbar.DesiredSize.Width;
    }

    void Place()
    {
        Left = placement.Right - ActualWidth;
        Top = placement.Bottom - ActualHeight;
    }

    void Root_SizeChanged(object sender, SizeChangedEventArgs e) => FitChatHeight();

    // Up to the top of the screen, and never above the window's own top, which would cut off the chats and the resize grip.
    void FitChatHeight() =>
        ChatScroll.MaxHeight = Math.Clamp(Math.Min(placement.Bottom - SystemParameters.WorkArea.Top, ActualHeight) - (Root.ActualHeight - ChatScroll.ActualHeight),
            0, placement.ChatHeight);

    void Window_Closed(object sender, EventArgs e) => conversation.Cancel();

    void Window_MouseMove(object sender, MouseEventArgs e)
    {
        Touch();
        if (Status.Mood != mood)
            Animate();
    }

    void Conversation_Changed()
    {
        Touch();
        var following = IsAtBottom;
        UpdateToolbar();
        UpdateChatList();
        if (following)
            ScrollToNewest();
        Animate();
    }

    void UpdateToolbar()
    {
        CostText.Text = conversation.Cost.ToString("$0.00", CultureInfo.InvariantCulture);
        StopButton.Visibility = conversation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        clock.IsEnabled = conversation.IsBusy;
    }

    void UpdateChatList()
    {
        if (chatsExpanded)
            ChatList.ItemsSource = conversation.Chats;
        else
        {
            var current = conversation.Chats.Where(chat => conversation.IsCurrent(chat, DateTime.UtcNow)).ToArray();
            if (ChatList.ItemsSource is not ChatItem[] shown || !shown.SequenceEqual(current))
                ChatList.ItemsSource = current;
        }
        ChatArea.Visibility = ChatList.HasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    bool IsAtBottom => ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 1;

    void Pet_MouseEnterOrLeave(object sender, MouseEventArgs e)
    {
        Touch();
        Animate();
    }

    void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        (pressed, dragging, dragStart, pressedAt) = (true, false, e.GetPosition(this), DateTime.UtcNow);
        Touch();
        Pet.CaptureMouse();
        Animate();
    }

    void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!pressed)
            return;
        var offset = e.GetPosition(this) - dragStart;
        if (!dragging)
        {
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragging = true;
        }
        // Moving the window under a still cursor raises MouseMove too; only real movement counts as running.
        if (offset.Length < 1)
            return;
        placement = placement with { Right = placement.Right + offset.X, Bottom = placement.Bottom + offset.Y };
        Place();
        // The sprite faces left, so mirror it when running right.
        if (Math.Abs(offset.X) >= 1)
            Facing.ScaleX = offset.X > 0 ? -1 : 1;
        lastMove = DateTime.UtcNow;
        if (Status.Mood != mood)
            Animate();
    }

    void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var clicked = pressed && !dragging && DateTime.UtcNow - pressedAt < ClickTime;
        Pet.ReleaseMouseCapture();
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
        FitChatHeight();
        Animate();
    }

    void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        beforeFullScreen = null;
        resizeStart = (Mouse.GetPosition(this), Root.ActualWidth, ChatScroll.MaxHeight);
    }

    void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var moved = resizeStart.Mouse - Mouse.GetPosition(this);
        Apply(placement with { Width = resizeStart.Width + moved.X, ChatHeight = resizeStart.ChatHeight + moved.Y });
    }

    void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e) => placementFile.Save(placement);

    void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (beforeFullScreen is { } previous)
        {
            beforeFullScreen = null;
            Apply(previous);
        }
        else
        {
            beforeFullScreen = placement;
            Apply(DefaultPlacement with { Width = SystemParameters.WorkArea.Width, ChatHeight = SystemParameters.WorkArea.Height });
        }
        Touch();
        ScrollToNewest();
    }

    void Write_Click(object sender, RoutedEventArgs e)
    {
        if (InputBox.Visibility == Visibility.Visible)
            HideInput();
        else
            ShowInput();
    }

    void ShowInput()
    {
        InputBox.Visibility = Visibility.Visible;
        Activate();
        Keyboard.Focus(Input);
        Touch();
        Animate();
    }

    void HideInput()
    {
        InputBox.Visibility = Visibility.Hidden;
        Touch();
        Animate();
    }

    async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        Touch();
        if (e.Key == Key.Escape)
            HideInput();
        else if (e.Key == Key.Enter && (Input.Text.Trim().Length > 0 || attachedFiles.Count + attachedImages.Count > 0) && !conversation.IsBusy)
        {
            e.Handled = true;
            var prompt = Conversation.WithAttachments(Input.Text.Trim(), attachedFiles, attachedImages);
            ImageAttachment[] images = [.. attachedImages];
            Input.Clear();
            ClearAttachments();
            HideInput();
            ScrollToNewest();
            await conversation.SendAsync(prompt, images);
        }
    }

    void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || !Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
            return;
        e.Handled = true;
        attachedImages.Add(new ImageAttachment("screenshot.png", "image/png", EncodePng(image)));
        UpdateAttachments();
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
        ShowInput();
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
        AttachmentChip.ToolTip = $"{string.Join('\n', names)}\n(klik for at fjerne)";
        AttachmentCount.Text = $"{names.Length}";
    }

    void ToggleChats_Click(object sender, RoutedEventArgs e)
    {
        if (chatsExpanded)
            readingOffset = IsAtBottom ? null : ChatScroll.VerticalOffset;
        chatsExpanded = !chatsExpanded;
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
        // The answer viewers would otherwise swallow the wheel.
        ChatScroll.ScrollToVerticalOffset(ChatScroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    void NewChat_Click(object sender, RoutedEventArgs e) => conversation.Reset();

    void Stop_Click(object sender, RoutedEventArgs e) => conversation.Cancel();

    void MoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        var folder = claude.Settings.WorkingDirectory;
        FolderItem.Header = folder is null ? "Vælg mappe…" : $"Mappe: {new DirectoryInfo(folder).Name}";
        FolderItem.ToolTip = folder;
        FolderItem.IsChecked = folder is not null;
        NoFolderItem.IsChecked = folder is null;
        FullScreenItem.IsChecked = beforeFullScreen is not null;
        UsageItem.Header = conversation.Usage is { } usage
            ? $"Forbrug: 5 t {usage.FiveHour:P0} · uge {usage.SevenDay:P0}"
            : "Forbrug vises efter første svar";
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = claude.Settings.WorkingDirectory ?? "" };
        if (dialog.ShowDialog(this) == true)
            UseFolder(dialog.FolderName);
    }

    void NoFolder_Click(object sender, RoutedEventArgs e) => UseFolder(null);

    void UseFolder(string? folder)
    {
        if (folder == claude.Settings.WorkingDirectory)
            return;
        claude.Settings = claude.Settings with { WorkingDirectory = folder };
        conversation.Reset();
    }

    void Instructions_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(instructionsFile)!);
        File.AppendAllText(instructionsFile, "");
        Open(new ProcessStartInfo(instructionsFile) { UseShellExecute = true }, "Kunne ikke åbne instruktionerne.");
    }

    void OpenClaude_Click(object sender, RoutedEventArgs e) =>
        Open(new ProcessStartInfo("claude://") { UseShellExecute = true }, "Kunne ikke åbne Claude-appen. Er den installeret?");

    void Login_Click(object sender, RoutedEventArgs e) => Open(new ProcessStartInfo("claude", "auth login"), "Kunne ikke starte claude. Er den installeret?");

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
        Open(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }, $"Kunne ikke åbne {e.Uri.AbsoluteUri}");
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

    // The click that closes an open menu also reaches its button, which would open the menu again straight away.
    void Choices_Closed(object sender, RoutedEventArgs e) =>
        closedByItsButton = ((ContextMenu)sender).PlacementTarget is Button button && Mouse.LeftButton == MouseButtonState.Pressed
            && new Rect(button.RenderSize).Contains(Mouse.GetPosition(button)) ? button : null;

    void Model_Click(object sender, RoutedEventArgs e) =>
        claude.Settings = claude.Settings with { Model = Choose(ModelButton, (string)((MenuItem)e.OriginalSource).Tag) };

    void Effort_Click(object sender, RoutedEventArgs e) =>
        claude.Settings = claude.Settings with { Effort = Choose(EffortButton, (string)((MenuItem)e.OriginalSource).Tag) };

    void Mode_Click(object sender, RoutedEventArgs e) =>
        claude.Settings = claude.Settings with { PermissionMode = Choose(ModeButton, (string)((MenuItem)e.OriginalSource).Tag) };

    static string Choose(Button button, string value)
    {
        button.Content = value;
        foreach (var item in button.ContextMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = (string)item.Tag == value;
            if (item.IsChecked)
                button.Content = item.Header;
        }
        return value;
    }

    void DeleteChat_Click(object sender, RoutedEventArgs e) => conversation.Delete((ChatItem)((FrameworkElement)sender).DataContext);

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void Allow_Click(object sender, RoutedEventArgs e) => ((UserRequest)((FrameworkElement)sender).DataContext).Respond(true);

    void Deny_Click(object sender, RoutedEventArgs e) => ((UserRequest)((FrameworkElement)sender).DataContext).Respond(false);

    // Clicking an answer focuses it, and WPF would then scroll the whole answer into view under the cursor.
    void Answer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject == sender)
            e.Handled = true;
    }

    void ScrollToNewest() => ChatScroll.ScrollToEnd();
}
