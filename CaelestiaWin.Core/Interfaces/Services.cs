using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Core.Interfaces;

public interface ILoggerService
{
    LogLevelKind MinimumLevel { get; }

    void Configure(LogLevelKind minimumLevel, int maxFileSizeMb);

    void Info(string message, IReadOnlyDictionary<string, object?>? data = null);

    void Warn(string message, IReadOnlyDictionary<string, object?>? data = null);

    void Error(string message, Exception exception, IReadOnlyDictionary<string, object?>? data = null);
}

public interface IDiagnosticLogService : ILoggerService
{
}

public interface IConfigurationService
{
    Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

public interface IAppStateService : INotifyPropertyChanged
{
    AppConfig Config { get; set; }

    string ActiveWindowTitle { get; set; }

    int ActiveWorkspaceIndex { get; set; }

    bool IsLauncherOpen { get; set; }

    bool IsControlCenterOpen { get; set; }

    bool IsNotificationCenterOpen { get; set; }

    bool IsOverviewOpen { get; set; }

    bool IsClipboardHistoryOpen { get; set; }

    bool IsSafeMode { get; set; }

    bool IsExplorerRunning { get; set; }

    bool IsForegroundFullscreen { get; set; }

    bool IsShortcutGuideVisible { get; set; }

    ReadOnlyObservableCollection<WorkspaceModel> Workspaces { get; }
}

public interface IForegroundWindowTracker
{
    event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

    event EventHandler? WindowsChanged;

    WindowDescriptor? GetForegroundWindow();

    void Start();

    void Stop();
}

public interface IVisibleWindowService
{
    IReadOnlyList<WindowDescriptor> GetVisibleWindows();
}

public interface IActiveWindowService
{
    event EventHandler<ForegroundWindowChangedEventArgs>? CurrentWindowChanged;

    event EventHandler? WindowsChanged;

    WindowDescriptor? CurrentWindow { get; }

    void Start();

    void Stop();
}

public interface IAppDiscoveryService
{
    Task<IReadOnlyList<AppLaunchItem>> GetAppsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

public interface ILauncherSearchService
{
    IReadOnlyList<LauncherSearchResult> SearchApps(
        IReadOnlyList<AppLaunchItem> apps,
        IReadOnlyList<AppLaunchItem> recentApps,
        IReadOnlyList<AppLaunchItem> favoriteApps,
        string query,
        LauncherConfig config);
}

public interface ILauncherCommandService
{
    IReadOnlyList<LauncherSearchResult> Search(string query);

    Task ExecuteAsync(SystemCommandKind command, CancellationToken cancellationToken = default);
}

public interface IRecentAppsService
{
    IReadOnlyList<AppLaunchItem> GetRecentApps(int maxResults);

    void RecordLaunch(AppLaunchItem app);
}

public interface IFavoriteAppsService
{
    IReadOnlyList<AppLaunchItem> GetFavorites();

    bool IsFavorite(string appId);

    void ToggleFavorite(AppLaunchItem app);
}

public interface IWorkspaceService
{
    ReadOnlyObservableCollection<WorkspaceModel> Workspaces { get; }

    int ActiveWorkspaceIndex { get; }

    bool SwitchTo(int workspaceIndex);

    bool MoveFocusedWindowToWorkspace(int workspaceIndex);

    bool MoveWindowToWorkspace(nint hwnd, int workspaceIndex);

    IReadOnlyList<WindowDescriptor> GetWindowsForWorkspace(int workspaceIndex);

    IReadOnlyList<WindowDescriptor> GetAllTrackedWindows();

    int GetWorkspaceForWindow(nint hwnd);

    void Synchronize();
}

public interface IWindowActionService
{
    bool CloseFocusedWindow();

    bool CloseWindow(nint hwnd);

    Task<string> OpenTerminalAsync(CancellationToken cancellationToken = default);

    void Lock();

    void SignOut();

    void Restart();

    void Shutdown();

    void RebootToFirmware();

    WindowDescriptor? GetForegroundWindow();

    bool FocusWindow(nint hwnd);

    bool ShowWindow(nint hwnd);

    bool RestoreWindow(nint hwnd);

    bool MinimizeWindow(nint hwnd);

    bool MaximizeWindow(nint hwnd);

    bool HideWindow(nint hwnd);

    bool MoveWindow(nint hwnd, WindowBounds bounds);

    bool IsWindowAlive(nint hwnd);

    WindowBounds? GetWindowBounds(nint hwnd);

    WindowBounds GetMonitorWorkArea(nint hwnd);

    bool EnsureForegroundWindowUsesShellWorkArea(int topReservedSpace);

    bool ToggleFocusedWindowFullscreen();

    bool ToggleFocusedWindowFloat();

    bool IsWindowFloating(nint hwnd);

    bool IsForegroundWindowFullscreen();

    bool IsWindowFullscreen(nint hwnd);

    bool KillWindowProcess(nint hwnd);
}

public interface IFileExplorerService
{
    Task OpenAsync(string? path = null, CancellationToken cancellationToken = default);

    Task OpenNebulaExplorerAsync(string? path = null, CancellationToken cancellationToken = default);

    Task OpenWindowsExplorerAsync(string? path = null, CancellationToken cancellationToken = default);
}

public interface IFileExplorerIndexService
{
    string? TryGetPathSuggestion(string input, string currentBase);
}

public interface IFileShellContextMenuService
{
    IReadOnlyList<ShellMenuItem> GetMenuItems(string path);

    bool TryInvoke(string path, string invokeToken);
}

public interface IFileExplorerSidebarStateService
{
    IReadOnlyList<FileExplorerSidebarEntry> LoadEntries();

    void SaveEntries(IReadOnlyList<FileExplorerSidebarEntry> entries);
}

public interface IWindowNavigationService
{
    bool Focus(WindowDirection direction);
}

public interface IWindowLayoutService
{
    bool MoveFocusedWindow(WindowDirection direction);

    void RefreshActiveWorkspaceLayout();

    WindowLayout GetLayoutForWorkspace(int workspaceIndex);
}

public interface IGameModeService : INotifyPropertyChanged
{
    bool AutoEnable { get; }

    bool IsGameRunning { get; }

    bool IsFullscreenGameRunning { get; }

    bool IsEffective { get; }

    string ActiveGameName { get; }

    void Start();

    void Stop();

    void SetAutoEnabled(bool enabled);

    bool IsGameWindow(WindowDescriptor window);

    bool ShouldCenterWindow(WindowDescriptor window);

    bool ShouldExcludeFromTiling(WindowDescriptor window);
}

public interface IOverviewService : INotifyPropertyChanged
{
    bool IsOpen { get; }

    ReadOnlyObservableCollection<OverviewWindowItem> Windows { get; }

    void Toggle();

    void Close();

    void Refresh();

    bool ActivateWindow(nint hwnd);
}

public interface IGlobalHotkeyService
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    IReadOnlyList<string> FailedRegistrations { get; }

    void AttachWindow(nint hwnd);

    void RegisterBindings(IEnumerable<HotkeyBindingConfig> bindings);

    void UnregisterAll();
}

public interface IThemeManager
{
    void ApplyTheme(AppConfig config);

    void ApplyAccentColor(string accentColor);
}

public interface IWindowsAccentColorService
{
    string? TryGetCurrentAccentColor();

    bool TrySetAccentColor(string accentColor);
}

public interface IWallpaperService
{
    string? TryGetCurrentWallpaperPath();

    bool TrySetWallpaper(string wallpaperPath);

    IReadOnlyList<string> GetDefaultWallpaperPaths();
}

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public interface INotificationService
{
    ReadOnlyObservableCollection<NotificationItem> Notifications { get; }

    event EventHandler<NotificationActionRequestedEventArgs>? ActionRequested;

    void Push(
        string title,
        string message,
        DateTimeOffset? timestamp = null,
        NotificationKind kind = NotificationKind.Info,
        string source = "Nebula",
        bool showToast = true,
        string? primaryActionLabel = null,
        string? primaryActionId = null,
        Guid? notificationId = null,
        double? progressFraction = null,
        bool isCompleted = false,
        bool hasError = false);

    void Update(
        Guid notificationId,
        string? title = null,
        string? message = null,
        double? progressFraction = null,
        bool? isCompleted = null,
        bool? hasError = null,
        DateTimeOffset? timestamp = null,
        bool? showToast = null);

    void InvokeAction(Guid notificationId);

    void Dismiss(Guid notificationId);

    void ClearAll();
}

public interface IPomodoroService : INotifyPropertyChanged
{
    PomodoroStateKind State { get; }

    PomodoroPhaseKind Phase { get; }

    int SessionLengthMinutes { get; }

    int BreakLengthMinutes { get; }

    int RemainingSeconds { get; }

    int ElapsedSeconds { get; }

    bool AutoCycleEnabled { get; }

    bool IsVisible { get; }

    bool IsRunning { get; }

    bool IsPaused { get; }

    void SetSessionLength(int minutes);

    void SetBreakLength(int minutes);

    void SetAutoCycleEnabled(bool enabled);

    void Start();

    void StartBreak();

    void Pause();

    void Resume();

    void Restart();

    void Stop();

    IReadOnlyList<PomodoroFocusBucket> GetFocusBuckets(PomodoroHistoryRangeKind range);
}

public interface IScreenCaptureService
{
    ScreenCaptureResult CaptureRegion(ScreenCaptureRegion region);
}

public interface ISnippingToolService
{
    Task CaptureRegionAsync(CancellationToken cancellationToken = default);
}

public interface IToastNotificationService
{
    bool IsAvailable { get; }

    void Initialize();

    void Show(NotificationItem notification);
}

public interface IExternalNotificationListenerService
{
    void Start();

    void Stop();
}

public interface IMediaService
{
    MediaSessionModel CurrentSession { get; }

    void Start();

    void Stop();

    Task PlayPauseAsync(CancellationToken cancellationToken = default);

    Task NextAsync(CancellationToken cancellationToken = default);

    Task PreviousAsync(CancellationToken cancellationToken = default);
}

public interface IMediaArtworkResolver
{
    Task<string?> ResolveAsync(MediaArtworkRequest request, CancellationToken cancellationToken = default);
}

public interface ISystemStatusService
{
    SystemStatusModel CurrentStatus { get; }

    void Start();

    void Stop();

    void SetVolume(double volumePercent);

    void AdjustVolume(double deltaPercent);

    void ToggleMute();

    void AdjustBrightness(int deltaPercent);

    void ToggleWifi();

    void ToggleBluetooth();

    Task<IReadOnlyList<AudioDeviceModel>> GetAudioDevicesAsync(AudioDeviceKind kind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppVolumeSessionModel>> GetAppVolumeSessionsAsync(CancellationToken cancellationToken = default);

    Task<SystemInformationSnapshot> GetSystemInformationAsync(CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAudioDeviceAsync(AudioDeviceModel device, CancellationToken cancellationToken = default);

    void SetAppVolume(string sessionId, double volumePercent);

    Task<IReadOnlyList<WifiNetworkModel>> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken = default);

    Task<bool> ConnectToWifiAsync(WifiConnectionRequest request, CancellationToken cancellationToken = default);
}

public interface ISystemTrayService
{
    ReadOnlyObservableCollection<SystemTrayItem> Items { get; }

    void Start();

    void Stop();

    bool Activate(string itemId);

    bool Terminate(string itemId);
}

public interface IShellCommandService
{
    Task ExecuteHotkeyAsync(HotkeyBindingConfig binding, CancellationToken cancellationToken = default);

    void ToggleLauncher();

    void ToggleControlCenter();

    void ToggleNotificationCenter();

    void ToggleSettingsPanel();

    void ToggleClipboardHistory();

    Task OpenTerminalAsync(CancellationToken cancellationToken = default);

    Task OpenFileExplorerAsync(string? path = null, CancellationToken cancellationToken = default);

    void CloseFocusedWindow();

    void SwitchWorkspace(int workspaceIndex);

    void CycleWorkspace(int delta);

    void MoveFocusedWindowToWorkspace(int workspaceIndex);

    void FocusWindow(WindowDirection direction);

    void MoveWindow(WindowDirection direction);

    void ToggleOverview();

    void ToggleFocusedWindowFullscreen();

    Task ToggleDiscordDesktopAsync(CancellationToken cancellationToken = default);

    Task ToggleSpotifyDesktopAsync(CancellationToken cancellationToken = default);

    Task ToggleGitHubDesktopAsync(CancellationToken cancellationToken = default);

    Task CaptureRegionAsync(CancellationToken cancellationToken = default);

    void ReturnToExplorerAndExit();
}

public interface IShellSettingsService
{
    void Toggle();

    void Show(ShellSettingsSection? section = null);
}

public interface ICurrentProcessService
{
    string ExecutablePath { get; }
}

public interface IShellOverlayWindowService
{
    void ActivateForInput();
}

public interface IShellDesktopSurfaceService
{
    void PrepareHostWindow(nint hwnd);

    void KeepHostInBack(nint hwnd);
}

public interface IStartupRegistrationService
{
    bool IsEnabled();

    void SetEnabled(bool enabled, string executablePath, string arguments);
}

public interface IStartupService
{
    bool IsEnabled();

    void SetEnabled(bool enabled, string executablePath, string arguments);
}

public interface ISessionService
{
    string SessionPath { get; }

    Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IMonitorService
{
    IReadOnlyList<MonitorInfoModel> GetMonitors();

    MonitorInfoModel? GetPrimaryMonitor();
}

public interface IExplorerIntegrationService
{
    bool IsExplorerRunning { get; }

    bool IsTrayAvailable { get; }

    bool IsShellServicesAvailable { get; }

    ProcessStartInfo CreateAppLaunchStartInfo(AppLaunchItem app);

    Process? LaunchApp(AppLaunchItem app);

    ProcessStartInfo CreateExecutableLaunchStartInfo(string executablePath, string? arguments = null);

    bool StopExplorerShell();

    bool StartExplorerShell();
}

public interface IShellLifetimeService
{
    bool CanExit { get; }

    void AllowExit();
}

public sealed class ForegroundWindowChangedEventArgs(WindowDescriptor? window) : EventArgs
{
    public WindowDescriptor? Window { get; } = window;
}

public sealed class HotkeyPressedEventArgs(HotkeyBindingConfig binding) : EventArgs
{
    public HotkeyBindingConfig Binding { get; } = binding;
}

public sealed class NotificationActionRequestedEventArgs(NotificationItem notification, string actionId) : EventArgs
{
    public NotificationItem Notification { get; } = notification;

    public string ActionId { get; } = actionId;
}
