using System.Collections.ObjectModel;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Windowing.Services;

public sealed class WorkspaceService : IWorkspaceService
{
    private const int WorkspaceCount = 11;
    private const int DiscordWorkspaceIndex = 9;
    private const int SpotifyWorkspaceIndex = 10;
    private const int GitHubDesktopWorkspaceIndex = 11;
    private readonly IAppStateService _appStateService;
    private readonly IVisibleWindowService _visibleWindowService;
    private readonly IActiveWindowService _activeWindowService;
    private readonly IWindowActionService _windowActionService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDiagnosticLogService _logService;
    private readonly object _sync = new();
    private readonly Dictionary<nint, int> _workspaceAssignments = [];
    private readonly Dictionary<nint, WindowDescriptor> _windowCache = [];
    private CancellationTokenSource? _synchronizationCts;
    private DateTimeOffset _lastSynchronization = DateTimeOffset.MinValue;

    public WorkspaceService(
        IAppStateService appStateService,
        IVisibleWindowService visibleWindowService,
        IActiveWindowService activeWindowService,
        IWindowActionService windowActionService,
        IUiDispatcher uiDispatcher,
        IDiagnosticLogService logService)
    {
        _appStateService = appStateService;
        _visibleWindowService = visibleWindowService;
        _activeWindowService = activeWindowService;
        _windowActionService = windowActionService;
        _uiDispatcher = uiDispatcher;
        _logService = logService;
        _activeWindowService.CurrentWindowChanged += OnCurrentWindowChanged;
        _activeWindowService.WindowsChanged += OnWindowsChanged;
    }

    public ReadOnlyObservableCollection<WorkspaceModel> Workspaces => _appStateService.Workspaces;

    public int ActiveWorkspaceIndex => _appStateService.ActiveWorkspaceIndex;

    public bool SwitchTo(int workspaceIndex)
    {
        lock (_sync)
        {
            if (workspaceIndex is < 1 or > WorkspaceCount)
            {
                return false;
            }

            SynchronizeCore();
            _appStateService.ActiveWorkspaceIndex = workspaceIndex;
            ApplyWorkspaceVisibility();
            FocusFirstWindowInWorkspace(workspaceIndex);
            UpdateWorkspaceCounts();
            return true;
        }
    }

    public bool MoveFocusedWindowToWorkspace(int workspaceIndex)
    {
        var currentWindow = _activeWindowService.CurrentWindow;
        return currentWindow is not null && MoveWindowToWorkspace(currentWindow.Handle, workspaceIndex);
    }

    public bool MoveWindowToWorkspace(nint hwnd, int workspaceIndex)
    {
        lock (_sync)
        {
            if (workspaceIndex is < 1 or > WorkspaceCount || hwnd == nint.Zero)
            {
                return false;
            }

            SynchronizeCore();
            if (!_windowActionService.IsWindowAlive(hwnd))
            {
                return false;
            }

            var currentWindow = _windowCache.TryGetValue(hwnd, out var cached)
                ? cached
                : _visibleWindowService.GetVisibleWindows().FirstOrDefault(window => window.Handle == hwnd);

            if (currentWindow is null)
            {
                return false;
            }

            _workspaceAssignments[currentWindow.Handle] = workspaceIndex;
            _windowCache[currentWindow.Handle] = currentWindow;

            if (workspaceIndex != ActiveWorkspaceIndex)
            {
                _ = _windowActionService.HideWindow(currentWindow.Handle);
                FocusFirstWindowInWorkspace(ActiveWorkspaceIndex, currentWindow.Handle);
            }
            else
            {
                _ = _windowActionService.ShowWindow(currentWindow.Handle);
            }

            UpdateWorkspaceCounts();
            return true;
        }
    }

    public IReadOnlyList<WindowDescriptor> GetWindowsForWorkspace(int workspaceIndex)
    {
        lock (_sync)
        {
            SynchronizeCore();
            var windows = new List<WindowDescriptor>();
            foreach (var assignment in _workspaceAssignments)
            {
                if (assignment.Value != workspaceIndex || !_windowActionService.IsWindowAlive(assignment.Key))
                {
                    continue;
                }

                if (_windowCache.TryGetValue(assignment.Key, out var descriptor))
                {
                    windows.Add(descriptor);
                }
            }

            return windows;
        }
    }

    public IReadOnlyList<WindowDescriptor> GetAllTrackedWindows()
    {
        lock (_sync)
        {
            SynchronizeCore();
            var windows = new List<WindowDescriptor>(_workspaceAssignments.Count);
            foreach (var handle in _workspaceAssignments.Keys)
            {
                if (!_windowActionService.IsWindowAlive(handle))
                {
                    continue;
                }

                if (_windowCache.TryGetValue(handle, out var descriptor))
                {
                    windows.Add(descriptor);
                }
            }

            return windows;
        }
    }

    public int GetWorkspaceForWindow(nint hwnd)
    {
        lock (_sync)
        {
            return _workspaceAssignments.TryGetValue(hwnd, out var workspaceIndex)
                ? workspaceIndex
                : ActiveWorkspaceIndex;
        }
    }

    public void Synchronize()
    {
        lock (_sync)
        {
            SynchronizeCore();
        }
    }

    private void SynchronizeCore()
    {
        var visibleWindows = _visibleWindowService.GetVisibleWindows();
        var visibleHandles = visibleWindows.Select(window => window.Handle).ToHashSet();
        foreach (var window in visibleWindows)
        {
            _windowCache[window.Handle] = window;
            var targetWorkspace = GetDefaultWorkspaceForWindow(window, ActiveWorkspaceIndex);
            var isQuickAccessWindow = targetWorkspace > 8;
            if (!_workspaceAssignments.TryAdd(window.Handle, targetWorkspace) && isQuickAccessWindow)
            {
                _workspaceAssignments[window.Handle] = targetWorkspace;
            }

            if (targetWorkspace != ActiveWorkspaceIndex && isQuickAccessWindow)
            {
                _ = _windowActionService.HideWindow(window.Handle);
            }
        }

        var staleHandles = _workspaceAssignments.Keys
            .Where(handle => !_windowActionService.IsWindowAlive(handle)
                             || (_workspaceAssignments.TryGetValue(handle, out var workspaceIndex)
                                 && workspaceIndex == ActiveWorkspaceIndex
                                 && !visibleHandles.Contains(handle)))
            .ToArray();

        foreach (var staleHandle in staleHandles)
        {
            _workspaceAssignments.Remove(staleHandle);
            _windowCache.Remove(staleHandle);
        }

        UpdateWorkspaceCounts();
    }

    private void OnCurrentWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        lock (_sync)
        {
            if (eventArgs.Window is null)
            {
                return;
            }

            _windowCache[eventArgs.Window.Handle] = eventArgs.Window;
            var targetWorkspace = GetDefaultWorkspaceForWindow(eventArgs.Window, ActiveWorkspaceIndex);
            var isQuickAccessWindow = targetWorkspace > 8;
            if (!_workspaceAssignments.TryAdd(eventArgs.Window.Handle, targetWorkspace) && isQuickAccessWindow)
            {
                _workspaceAssignments[eventArgs.Window.Handle] = targetWorkspace;
            }

            if (targetWorkspace != ActiveWorkspaceIndex && isQuickAccessWindow)
            {
                _ = _windowActionService.HideWindow(eventArgs.Window.Handle);
                FocusFirstWindowInWorkspace(ActiveWorkspaceIndex, eventArgs.Window.Handle);
            }

            UpdateWorkspaceCounts();
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs eventArgs)
    {
        var throttleMs = Math.Max(0, _appStateService.Config.Performance.WorkspaceSyncThrottleMs);
        if (throttleMs > 0 && DateTimeOffset.UtcNow - _lastSynchronization < TimeSpan.FromMilliseconds(throttleMs))
        {
            ScheduleSynchronization(throttleMs);
            return;
        }

        _lastSynchronization = DateTimeOffset.UtcNow;
        Synchronize();
    }

    private void ScheduleSynchronization(int delayMs)
    {
        _synchronizationCts?.Cancel();
        _synchronizationCts?.Dispose();
        _synchronizationCts = new CancellationTokenSource();
        var token = _synchronizationCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token).ConfigureAwait(false);
                _lastSynchronization = DateTimeOffset.UtcNow;
                Synchronize();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logService.Warn("Delayed workspace synchronization failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }, CancellationToken.None);
    }

    private void ApplyWorkspaceVisibility()
    {
        foreach (var assignment in _workspaceAssignments.ToArray())
        {
            if (!_windowActionService.IsWindowAlive(assignment.Key))
            {
                _workspaceAssignments.Remove(assignment.Key);
                _windowCache.Remove(assignment.Key);
                continue;
            }

            if (assignment.Value == ActiveWorkspaceIndex)
            {
                _ = _windowActionService.ShowWindow(assignment.Key);
            }
            else
            {
                _ = _windowActionService.HideWindow(assignment.Key);
            }
        }
    }

    private void FocusFirstWindowInWorkspace(int workspaceIndex, nint excludeHandle = default)
    {
        var candidate = _workspaceAssignments
            .Where(pair => pair.Value == workspaceIndex && pair.Key != excludeHandle)
            .Select(pair => pair.Key)
            .FirstOrDefault(_windowActionService.IsWindowAlive);

        if (candidate != nint.Zero)
        {
            _ = _windowActionService.FocusWindow(candidate);
        }
    }

    private void UpdateWorkspaceCounts()
    {
        var counts = new Dictionary<int, int>(WorkspaceCount);
        for (var workspaceIndex = 1; workspaceIndex <= WorkspaceCount; workspaceIndex++)
        {
            counts[workspaceIndex] = 0;
        }

        foreach (var assignment in _workspaceAssignments)
        {
            if (counts.ContainsKey(assignment.Value) && _windowActionService.IsWindowAlive(assignment.Key))
            {
                counts[assignment.Value]++;
            }
        }

        _ = _uiDispatcher.InvokeAsync(() =>
        {
            foreach (var workspace in Workspaces)
            {
                workspace.WindowCount = counts.TryGetValue(workspace.Index, out var count) ? count : 0;
            }
        });
    }

    private static int GetDefaultWorkspaceForWindow(WindowDescriptor window, int fallbackWorkspace)
    {
        if (IsDiscordWindow(window))
        {
            return DiscordWorkspaceIndex;
        }

        if (IsSpotifyWindow(window))
        {
            return SpotifyWorkspaceIndex;
        }

        if (IsGitHubDesktopWindow(window))
        {
            return GitHubDesktopWorkspaceIndex;
        }

        return fallbackWorkspace;
    }

    private static bool IsDiscordWindow(WindowDescriptor window)
    {
        return string.Equals(window.ProcessName, "Discord", StringComparison.OrdinalIgnoreCase)
               || window.ExecutablePath?.Contains("Discord", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSpotifyWindow(WindowDescriptor window)
    {
        return string.Equals(window.ProcessName, "Spotify", StringComparison.OrdinalIgnoreCase)
               || window.ExecutablePath?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsGitHubDesktopWindow(WindowDescriptor window)
    {
        return string.Equals(window.ProcessName, "GitHubDesktop", StringComparison.OrdinalIgnoreCase)
               || window.ExecutablePath?.Contains("GitHubDesktop", StringComparison.OrdinalIgnoreCase) == true
               || window.ExecutablePath?.Contains("GitHub Desktop", StringComparison.OrdinalIgnoreCase) == true;
    }
}
