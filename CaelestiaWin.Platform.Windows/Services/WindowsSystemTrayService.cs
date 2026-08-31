using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsSystemTrayService : ISystemTrayService, IDisposable
{
    private static readonly HashSet<string> KnownTrayProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord",
        "DiscordCanary",
        "DiscordPTB",
        "Steam",
        "steamwebhelper",
        "OneDrive",
        "Teams",
        "ms-teams",
        "Slack",
        "Spotify",
        "Telegram",
        "Signal",
        "WhatsApp",
        "WhatsApp.Root",
        "EpicGamesLauncher",
        "Battle.net",
        "RiotClientServices",
        "NVIDIA App",
        "NVIDIA Share",
        "NVDisplay.Container",
        "GeForceNOWContainer",
        "Dropbox",
        "GoogleDriveFS",
        "ShareX"
    };

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost",
        "audiodg",
        "CaelestiaWin.App",
        "Code",
        "cmd",
        "conhost",
        "csrss",
        "ctfmon",
        "dwm",
        "dotnet",
        "explorer",
        "fontdrvhost",
        "Idle",
        "lsass",
        "Memory Compression",
        "msedgewebview2",
        "OpenConsole",
        "powershell",
        "pwsh",
        "Registry",
        "RuntimeBroker",
        "SearchHost",
        "services",
        "ShellExperienceHost",
        "sihost",
        "smss",
        "StartMenuExperienceHost",
        "svchost",
        "System",
        "Taskmgr",
        "TextInputHost",
        "wininit",
        "winlogon",
        "WmiPrvSE"
    };

    private readonly ObservableCollection<SystemTrayItem> _items = [];
    private readonly ReadOnlyObservableCollection<SystemTrayItem> _readonlyItems;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDiagnosticLogService _logService;
    private readonly Dictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _lifetimeCts;
    private bool _started;

    public WindowsSystemTrayService(
        IUiDispatcher uiDispatcher,
        IDiagnosticLogService logService)
    {
        _uiDispatcher = uiDispatcher;
        _logService = logService;
        _readonlyItems = new ReadOnlyObservableCollection<SystemTrayItem>(_items);
    }

    public ReadOnlyObservableCollection<SystemTrayItem> Items => _readonlyItems;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _lifetimeCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        _ = RefreshAsync(_lifetimeCts.Token);
        _ = PollAsync(_lifetimeCts.Token);
        _logService.Info("Nebula shell-owned tray service started without Explorer tray dependencies.");
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _ = _uiDispatcher.InvokeAsync(_items.Clear);
    }

    public bool Activate(string itemId)
    {
        if (!TryResolveProcessId(itemId, out var processId))
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            if (TryActivateProcessWindow(process.Id))
            {
                return true;
            }

            var executablePath = TryGetExecutablePath(process);
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
            });
            return true;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to activate a shell-owned tray item.", new Dictionary<string, object?>
            {
                ["itemId"] = itemId,
                ["error"] = exception.Message
            });
            return false;
        }
    }

    // Explorer-dependent fallback removed to keep tray handling independent of Explorer.

    public bool Terminate(string itemId)
    {
        if (!TryResolveProcessId(itemId, out var processId) || processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to terminate a shell-owned tray item.", new Dictionary<string, object?>
            {
                ["itemId"] = itemId,
                ["processId"] = processId,
                ["error"] = exception.Message
            });
            return false;
        }
    }


    public void Dispose()
    {
        Stop();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_refreshTimer is not null && await _refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var items = await Task.Run(DiscoverTrayItems, cancellationToken).ConfigureAwait(false);
        await _uiDispatcher.InvokeAsync(() =>
        {
            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
            }
        }).ConfigureAwait(false);
    }

    private IReadOnlyList<SystemTrayItem> DiscoverTrayItems()
    {
        var currentProcessId = Environment.ProcessId;
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var candidates = new List<(SystemTrayItem item, int score)>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcessId
                    || process.SessionId != currentSessionId
                    || IgnoredProcessNames.Contains(process.ProcessName))
                {
                    continue;
                }

                var executablePath = TryGetExecutablePath(process);
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    continue;
                }

                var score = ScoreTrayCandidate(process, executablePath);
                if (score <= 0)
                {
                    continue;
                }

                var displayName = ToDisplayName(process.ProcessName);
                candidates.Add((new SystemTrayItem
                {
                    Id = $"process:{process.Id}",
                    Glyph = "\uECAA",
                    IconPath = TryImportProcessIcon(process.ProcessName, executablePath) ?? string.Empty,
                    Label = displayName,
                    ToolTip = $"{displayName} background app",
                    FontFamilyName = "Segoe MDL2 Assets"
                }, score));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return candidates
            .GroupBy(candidate => candidate.item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.score).First())
            .OrderByDescending(candidate => candidate.score)
            .ThenBy(candidate => candidate.item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .Select(candidate => candidate.item)
            .ToArray();
    }

    private static int ScoreTrayCandidate(Process process, string executablePath)
    {
        var traySignal = HasTrayWindowSignal(process.Id);
        var knownTrayProcess = KnownTrayProcessNames.Contains(process.ProcessName);

        // Require either a known tray process name or an actual tray-like window signal.
        // This keeps generic background processes with hidden windows out of the tray list.
        if (!knownTrayProcess && !traySignal)
        {
            return 0;
        }

        var score = 0;

        if (knownTrayProcess)
        {
            score += 100;
        }

        if (traySignal)
        {
            score += 75;
        }

        if (process.MainWindowHandle == nint.Zero)
        {
            score += 10;
        }
        else if (IsIconic(process.MainWindowHandle) || !IsWindowVisible(process.MainWindowHandle))
        {
            score += 5;
        }

        if (executablePath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (executablePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
        {
            score -= 80;
        }

        return score >= 60 ? score : 0;
    }

    private static string? TryGetExecutablePath(Process process)
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

    private string? TryImportProcessIcon(string processName, string executablePath)
    {
        try
        {
            if (_iconCache.TryGetValue(executablePath, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var iconDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "tray-icons");
            Directory.CreateDirectory(iconDirectory);

            var safeName = string.Concat(processName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var iconHandle = TryExtractIconHandle(executablePath);
            if (iconHandle == nint.Zero)
            {
                return null;
            }

            string iconPath;
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));
                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }

                iconPath = ShellAssetCache.SaveBitmapSourceAsPng(iconDirectory, safeName, bitmapSource);
            }
            finally
            {
                _ = DestroyIcon(iconHandle);
            }

            _iconCache[executablePath] = iconPath;
            return iconPath;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to import a shell-owned tray icon.", new Dictionary<string, object?>
            {
                ["process"] = processName,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private static nint TryExtractIconHandle(string executablePath)
    {
        var result = SHGetFileInfo(
            executablePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiLargeIcon);
        return result == nint.Zero ? nint.Zero : fileInfo.hIcon;
    }

    private static bool TryActivateProcessWindow(int processId)
    {
        var bestWindow = FindBestProcessWindow(processId, includeHidden: false);

        if (bestWindow == nint.Zero)
        {
            return false;
        }

        if (IsIconic(bestWindow))
        {
            _ = ShowWindow(bestWindow, SwRestore);
        }

        if (!IsWindowVisible(bestWindow))
        {
            _ = ShowWindow(bestWindow, SwRestore);
        }

        _ = SetForegroundWindow(bestWindow);
        return true;
    }

    private bool TryResolveProcessId(string itemId, out int processId)
    {
        processId = 0;
        var item = _items.FirstOrDefault(entry => entry.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        return item is not null
               && item.Id.StartsWith("process:", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(item.Id["process:".Length..], CultureInfo.InvariantCulture, out processId);
    }

    private static nint FindBestProcessWindow(int processId, bool includeHidden)
    {
        nint bestWindow = nint.Zero;

        _ = EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            if (!includeHidden && !IsWindowVisible(hwnd))
            {
                return true;
            }

            bestWindow = hwnd;
            return false;
        }, nint.Zero);

        return bestWindow;
    }

    private static bool HasTrayWindowSignal(int processId)
    {
        var found = false;
        _ = EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            // Look for a real tray-ish helper window class, not just any hidden tool window.
            var className = GetClassNameSafe(hwnd).ToLowerInvariant();
            if (!string.IsNullOrEmpty(className))
            {
                if (className == "tooltips_class32"
                    || className.Contains("notifyicon")
                    || className.Contains("tray")
                    || className.Contains("systray"))
                {
                    found = true;
                    return false;
                }
            }

            return true;
        }, nint.Zero);

        return found;
    }

    private static string GetClassNameSafe(nint hwnd)
    {
        try
        {
            var buffer = new char[256];
            var len = GetClassNameW(hwnd, buffer, buffer.Length);
            return len > 0 ? new string(buffer, 0, len) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTextSafe(nint hwnd)
    {
        try
        {
            var length = GetWindowTextLengthW(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new char[length + 1];
            var read = GetWindowTextW(hwnd, buffer, buffer.Length);
            return read > 0 ? new string(buffer, 0, read) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IntPtr GetWindowLongPtr(nint hWnd, int nIndex)
    {
        try
        {
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex);
            }

            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string ToDisplayName(string processName)
    {
        return processName
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", " ", StringComparison.Ordinal)
            .Trim();
    }

    private const int SwRestore = 9;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint iconHandle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int commandShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint hWnd);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
