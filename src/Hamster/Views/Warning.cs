using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Hamster.Views;

public static class Warning
{
    public static void Show(Window owner, string text) => MessageBox.Show(owner, text, "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Open(Window owner, ProcessStartInfo start, string failure)
    {
        try
        {
            Process.Start(start);
        }
        catch (Win32Exception)
        {
            Show(owner, failure);
        }
    }
}
