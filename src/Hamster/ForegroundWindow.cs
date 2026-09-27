using System.Runtime.InteropServices;

namespace Hamster;

public static partial class ForegroundWindow
{
    public static bool IsThisApp()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        return process == Environment.ProcessId;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out int process);
}
