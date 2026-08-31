using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsWindowActionService(
    WindowsWindowIntrospection introspection,
    IAppStateService appStateService,
    IDiagnosticLogService logService) : IWindowActionService
{
    private const int MinimumTopReservedSpace = 76;
    private const int MaximizedShellEdgePadding = 10;
    private readonly Dictionary<nint, WindowPlacement> _fullscreenPlacements = [];
    private readonly object _fullscreenSync = new();
    private readonly HashSet<nint> _floatingWindows = new();

    public bool CloseFocusedWindow()
    {
        var hwnd = User32.GetForegroundWindow();
        return CloseWindow(hwnd);
    }

    public bool CloseWindow(nint hwnd)
    {
        return hwnd != nint.Zero && User32.IsWindow(hwnd) && User32.PostMessageW(hwnd, User32.WmClose, nint.Zero, nint.Zero);
    }

    public WindowDescriptor? GetForegroundWindow()
    {
        return introspection.CreateDescriptor(User32.GetForegroundWindow());
    }

    public Task<string> OpenTerminalAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in GetTerminalCandidates())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = false
                });

                if (process is not null)
                {
                    return Task.FromResult(candidate);
                }
            }
            catch (Exception exception)
            {
                logService.Warn("Terminal launch candidate failed.", new Dictionary<string, object?>
                {
                    ["candidate"] = candidate,
                    ["error"] = exception.Message
                });
            }
        }

        throw new InvalidOperationException("Unable to launch Windows Terminal, PowerShell, or Command Prompt.");
    }

    private IReadOnlyList<string> GetTerminalCandidates()
    {
        var launcherConfig = appStateService.Config.Launcher;
        var orderedCandidates = new List<string>(8);

        AddProfileCandidate(launcherConfig.DefaultTerminal, launcherConfig.CustomTerminalPath, orderedCandidates);

        foreach (var fallback in new[]
        {
            TerminalProfileKind.Nebula,
            TerminalProfileKind.WindowsTerminal,
            TerminalProfileKind.PowerShell,
            TerminalProfileKind.CommandPrompt
        })
        {
            AddProfileCandidate(fallback, launcherConfig.CustomTerminalPath, orderedCandidates);
        }

        return orderedCandidates;
    }

    private static void AddProfileCandidate(TerminalProfileKind profile, string customTerminalPath, ICollection<string> candidates)
    {
        foreach (var candidate in GetProfileCandidates(profile, customTerminalPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(candidate);
            }
        }
    }

    private static IEnumerable<string> GetProfileCandidates(TerminalProfileKind profile, string customTerminalPath)
    {
        return profile switch
        {
            TerminalProfileKind.Nebula =>
            [
                Path.Combine(AppContext.BaseDirectory, "CaelestiaWin.Terminal.exe")
            ],
            TerminalProfileKind.WindowsTerminal =>
            [
                "wt.exe"
            ],
            TerminalProfileKind.PowerShell =>
            [
                "pwsh.exe",
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            ],
            TerminalProfileKind.CommandPrompt =>
            [
                Path.Combine(Environment.SystemDirectory, "cmd.exe")
            ],
            TerminalProfileKind.Custom when !string.IsNullOrWhiteSpace(customTerminalPath) =>
            [
                customTerminalPath.Trim()
            ],
            _ => []
        };
    }

    public void Lock()
    {
        _ = User32.LockWorkStation();
    }

    public void SignOut()
    {
        StartShutdownCommand("/l");
    }

    public void Restart()
    {
        StartShutdownCommand("/r /t 0");
    }

    public void Shutdown()
    {
        StartShutdownCommand("/s /t 0");
    }

    public void RebootToFirmware()
    {
        StartShutdownCommand("/r /fw /t 0");
    }

    public bool FocusWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        if (LooksLikeNativeFullscreenWindow(hwnd))
        {
            _ = User32.ShowWindow(hwnd, User32.SwShow);
        }
        else
        {
            _ = User32.ShowWindow(hwnd, User32.SwRestore);
        }

        return User32.SetForegroundWindow(hwnd);
    }

    public bool ShowWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwShowNa);
        return true;
    }

    public bool RestoreWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwRestore);
        return true;
    }

    public bool MinimizeWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwMinimize);
        return true;
    }

    public bool MaximizeWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwMaximize);
        return true;
    }

    public bool HideWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwHide);
        return true;
    }

    public bool MoveWindow(nint hwnd, WindowBounds bounds)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd) || bounds.IsEmpty)
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwRestore);
        return User32.SetWindowPos(
            hwnd,
            User32.HwndTop,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            User32.SwpNoZorder | User32.SwpNoActivate | User32.SwpShowwindow);
    }

    public bool IsWindowAlive(nint hwnd)
    {
        return hwnd != nint.Zero && User32.IsWindow(hwnd);
    }

    public WindowBounds? GetWindowBounds(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd) || !User32.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        return new WindowBounds(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }

    public WindowBounds GetMonitorWorkArea(nint hwnd)
    {
        var monitorBounds = GetMonitorBounds(hwnd);
        if (!monitorBounds.IsEmpty)
        {
            return monitorBounds;
        }

        var monitor = User32.MonitorFromWindow(hwnd, User32.MonitorDefaulttonearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != nint.Zero && User32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return new WindowBounds(
                monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Top,
                Math.Max(0, monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left),
                Math.Max(0, monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top));
        }

        return new WindowBounds(0, 0, 1600, 900);
    }

    public bool EnsureForegroundWindowUsesShellWorkArea(int topReservedSpace)
    {
        var hwnd = User32.GetForegroundWindow();
        return EnsureWindowUsesShellWorkArea(hwnd, topReservedSpace);
    }

    public bool ToggleFocusedWindowFullscreen()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        WindowPlacement previousPlacement;
        lock (_fullscreenSync)
        {
            if (_fullscreenPlacements.TryGetValue(hwnd, out previousPlacement))
            {
                _fullscreenPlacements.Remove(hwnd);
                return RestoreWindowPlacement(hwnd, previousPlacement);
            }
        }

        previousPlacement = WindowPlacement.CreateDefault();
        if (!User32.GetWindowPlacement(hwnd, ref previousPlacement))
        {
            return false;
        }

        var monitorBounds = GetMonitorBounds(hwnd);
        if (monitorBounds.IsEmpty)
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwRestore);
        var moved = User32.SetWindowPos(
            hwnd,
            User32.HwndTopmost,
            monitorBounds.Left,
            monitorBounds.Top,
            monitorBounds.Width,
            monitorBounds.Height,
            User32.SwpNoActivate | User32.SwpShowwindow);

        if (!moved)
        {
            return false;
        }

        lock (_fullscreenSync)
        {
            _fullscreenPlacements[hwnd] = previousPlacement;
        }

        return true;
    }

    public bool IsForegroundWindowFullscreen()
    {
        return IsWindowFullscreen(User32.GetForegroundWindow());
    }

    public bool IsWindowFullscreen(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        lock (_fullscreenSync)
        {
            if (_fullscreenPlacements.ContainsKey(hwnd))
            {
                return true;
            }
        }

        return LooksLikeNativeFullscreenWindow(hwnd);
    }

    public bool KillWindowProcess(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        _ = User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to force-kill a window process.", new Dictionary<string, object?>
            {
                ["hwnd"] = hwnd,
                ["processId"] = processId,
                ["error"] = exception.Message
            });
            return false;
        }
    }

    public bool ToggleFocusedWindowFloat()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        lock (_floatingWindows)
        {
            if (_floatingWindows.Contains(hwnd))
            {
                _floatingWindows.Remove(hwnd);
                _ = User32.SetWindowPos(hwnd, User32.HwndNotopmost, 0, 0, 0, 0, User32.SwpNomove | User32.SwpNosize | User32.SwpNoActivate | User32.SwpShowwindow);
                return true;
            }

            _floatingWindows.Add(hwnd);
            _ = User32.SetWindowPos(hwnd, User32.HwndTopmost, 0, 0, 0, 0, User32.SwpNomove | User32.SwpNosize | User32.SwpNoActivate | User32.SwpShowwindow);
            return true;
        }
    }

    public bool IsWindowFloating(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return false;
        }

        lock (_floatingWindows)
        {
            return _floatingWindows.Contains(hwnd);
        }
    }

    private static void StartShutdownCommand(string arguments)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private bool EnsureWindowUsesShellWorkArea(nint hwnd, int topReservedSpace)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd) || User32.IsIconic(hwnd) || IsWindowFullscreen(hwnd))
        {
            return false;
        }

        if (!User32.IsZoomed(hwnd))
        {
            return false;
        }

        var shellWorkArea = GetShellWorkArea(hwnd, topReservedSpace);
        if (shellWorkArea.IsEmpty)
        {
            return false;
        }

        var currentBounds = GetWindowBounds(hwnd);
        if (currentBounds is not null && AreBoundsClose(currentBounds.Value, shellWorkArea))
        {
            return false;
        }

        _ = User32.ShowWindow(hwnd, User32.SwRestore);
        return User32.SetWindowPos(
            hwnd,
            User32.HwndTop,
            shellWorkArea.Left,
            shellWorkArea.Top,
            shellWorkArea.Width,
            shellWorkArea.Height,
            User32.SwpNoZorder | User32.SwpNoActivate | User32.SwpShowwindow);
    }

    private bool LooksLikeNativeFullscreenWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd) || User32.IsIconic(hwnd))
        {
            return false;
        }

        var currentBounds = GetWindowBounds(hwnd);
        if (currentBounds is null || currentBounds.Value.IsEmpty)
        {
            return false;
        }

        var monitorBounds = GetMonitorBounds(hwnd);
        if (monitorBounds.IsEmpty || !AreBoundsClose(currentBounds.Value, monitorBounds, tolerance: 4))
        {
            return false;
        }

        var style = User32.GetWindowLongPtr(hwnd, User32.GwlStyle).ToInt64();
        var hasStandardFrame = (style & (User32.WsCaption | User32.WsThickframe)) != 0;
        var isPopup = (style & User32.WsPopup) != 0;

        return !User32.IsZoomed(hwnd) || (isPopup && !hasStandardFrame);
    }

    private static WindowBounds GetMonitorBounds(nint hwnd)
    {
        var monitor = User32.MonitorFromWindow(hwnd, User32.MonitorDefaulttonearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != nint.Zero && User32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return new WindowBounds(
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                Math.Max(0, monitorInfo.Monitor.Right - monitorInfo.Monitor.Left),
                Math.Max(0, monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top));
        }

        return default;
    }

    private WindowBounds GetShellWorkArea(nint hwnd, int topReservedSpace)
    {
        var workArea = GetMonitorWorkArea(hwnd);
        var reservedTop = Math.Max(MinimumTopReservedSpace, topReservedSpace);
        var safeWidth = Math.Max(100, workArea.Width - (MaximizedShellEdgePadding * 2));
        var safeHeight = Math.Max(100, workArea.Height - reservedTop - (MaximizedShellEdgePadding * 2));
        return new WindowBounds(
            workArea.Left + MaximizedShellEdgePadding,
            workArea.Top + reservedTop + MaximizedShellEdgePadding,
            safeWidth,
            safeHeight);
    }

    private bool RestoreWindowPlacement(nint hwnd, WindowPlacement placement)
    {
        placement.Length = Marshal.SizeOf<WindowPlacement>();
        var restored = User32.SetWindowPlacement(hwnd, ref placement);
        _ = User32.SetWindowPos(
            hwnd,
            User32.HwndNotopmost,
            0,
            0,
            0,
            0,
            User32.SwpNomove | User32.SwpNosize | User32.SwpNoActivate | User32.SwpShowwindow);
        return restored;
    }

    private static bool AreBoundsClose(WindowBounds left, WindowBounds right, int tolerance = 2)
    {
        return Math.Abs(left.Left - right.Left) <= tolerance
               && Math.Abs(left.Top - right.Top) <= tolerance
               && Math.Abs(left.Width - right.Width) <= tolerance
               && Math.Abs(left.Height - right.Height) <= tolerance;
    }
}
