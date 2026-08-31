using System.Diagnostics;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsWindowIntrospection
{
    private readonly int _currentProcessId = Environment.ProcessId;

    public WindowDescriptor? CreateDescriptor(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindowVisible(hwnd) || DwmApi.IsWindowCloaked(hwnd))
        {
            return null;
        }

        var title = User32.GetWindowText(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var exStyle = User32.GetWindowLongPtr(hwnd, User32.GwlExstyle).ToInt64();
        if ((exStyle & User32.WsExToolwindow) != 0)
        {
            return null;
        }

        _ = User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var isNebulaExplorerWindow = processId == _currentProcessId && IsNebulaExplorerWindow(title);
        if (processId == _currentProcessId && !isNebulaExplorerWindow)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            var executablePath = TryGetMainModuleFileName(process);
            var bounds = ReadBounds(hwnd);
            var isMinimized = User32.IsIconic(hwnd);

            if (ShouldExclude(title, processName, executablePath, bounds))
            {
                return null;
            }

            return new WindowDescriptor(hwnd, title, processName, executablePath, bounds, isMinimized);
        }
        catch
        {
            return new WindowDescriptor(hwnd, title, null, null, ReadBounds(hwnd), User32.IsIconic(hwnd));
        }
    }

    private static string? TryGetMainModuleFileName(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldExclude(string title, string? processName, string? executablePath, WindowBounds bounds)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        if (bounds.Width < 80 || bounds.Height < 60)
        {
            return true;
        }

        if (processName is null)
        {
            return false;
        }

        var normalized = processName.ToLowerInvariant();
        if (normalized is "taskmgr")
        {
            return true;
        }

        if (normalized is "shellexperiencehost" or "searchhost" or "textinputhost" or "applicationframehost")
        {
            return false;
        }

        if (normalized.Contains("startmenuexperiencehost", StringComparison.Ordinal))
        {
            return true;
        }

        return executablePath?.Contains("WindowHost.exe", StringComparison.OrdinalIgnoreCase) == true
               || executablePath?.Contains("Widgets.exe", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsNebulaExplorerWindow(string title)
    {
        return string.Equals(title, "Nebula Files", StringComparison.OrdinalIgnoreCase);
    }

    private static WindowBounds ReadBounds(nint hwnd)
    {
        if (!User32.GetWindowRect(hwnd, out var rect))
        {
            return default;
        }

        return new WindowBounds(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }
}
