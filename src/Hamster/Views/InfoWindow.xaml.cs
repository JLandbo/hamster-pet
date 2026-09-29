using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Hamster.Core.Languages;

namespace Hamster.Views;

public partial class InfoWindow : Window
{
    public InfoWindow() => InitializeComponent();

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Warning.Open(this, new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }, Strings.Format("Main.LinkFailed", e.Uri.AbsoluteUri));
        e.Handled = true;
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
