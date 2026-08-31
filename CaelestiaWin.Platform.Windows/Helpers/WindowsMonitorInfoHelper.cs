using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Helpers;

public static class WindowsMonitorInfoHelper
{
    public static string GetPrimaryMonitorSummary()
    {
        var width = User32.GetSystemMetrics(0);
        var height = User32.GetSystemMetrics(1);
        return $"{width}x{height}";
    }
}
