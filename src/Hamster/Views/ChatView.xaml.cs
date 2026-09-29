using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Hamster.Core.Chats;
using Hamster.Core.Claude;
using Hamster.Core.Languages;
using Hamster.Desktop;

namespace Hamster.Views;

public partial class ChatView : UserControl
{
    Conversation? _conversation;

    public ChatView() => InitializeComponent();

    public void Show(Conversation conversation)
    {
        _conversation = conversation;
        ChatList.ItemsSource = conversation.Chats;
    }

    public bool IsAtBottom => ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 1;

    public void ThawChats()
    {
        if (ChatContainers.FirstOrDefault(chat => !double.IsNaN(chat.Width)) is not { } frozen)
        {
            return;
        }
        frozen.ClearValue(WidthProperty);
        Dispatcher.BeginInvoke(ThawChats, DispatcherPriority.ApplicationIdle);
    }

    IEnumerable<FrameworkElement> ChatContainers => Enumerable.Range(0, ChatList.Items.Count).Select(ChatList.ItemContainerGenerator.ContainerFromIndex).OfType<FrameworkElement>();

    public void FreezeHiddenChats()
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
        {
            chat.Width = chat.ActualWidth;
        }
    }

    bool IsInView(FrameworkElement chat) => chat.TranslatePoint(new Point(), ChatScroll).Y is var top && top < ChatScroll.ActualHeight && top + chat.ActualHeight > 0;

    void ChatScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChatScroll.ScrollToVerticalOffset(ChatScroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    void Login_Click(object sender, RoutedEventArgs e) => Warning.Open(Window.GetWindow(this), new ProcessStartInfo("claude", "auth login"), Strings.Of("Main.LoginFailed"));

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        var target = e.Uri.IsFile ? e.Uri.LocalPath : e.Uri.AbsoluteUri;
        Warning.Open(Window.GetWindow(this), new ProcessStartInfo(target) { UseShellExecute = true }, Strings.Format("Main.LinkFailed", target));
        e.Handled = true;
    }

    void CopyAnswer_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        ClipboardText.Copy(((ChatItem)button.DataContext).Answer, icon => button.Content = icon);
    }

    void DeleteChat_Click(object sender, RoutedEventArgs e) => _conversation?.Delete((ChatItem)((FrameworkElement)sender).DataContext);

    void Allow_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.Respond(true);

    void AllowAlways_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.RespondAlways();

    void Deny_Click(object sender, RoutedEventArgs e) => ClickedRequest(sender)?.Respond(false);

    static UserRequest? ClickedRequest(object sender) => ((FrameworkElement)sender).DataContext is UserRequest request && request.CanBeClicked(DateTime.UtcNow) ? request : null;

    public void ScrollToNewest() => ChatScroll.ScrollToEnd();
}
