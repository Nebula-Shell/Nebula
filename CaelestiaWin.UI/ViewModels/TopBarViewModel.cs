using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class TopBarViewModel : ObservableObjectBase
{
    private const int VisibleWorkspaceCount = 4;
    private const int NormalWorkspaceCount = 8;
    private const int QuickAccessWorkspaceStart = 9;
    private const int DiscordWorkspaceIndex = 9;
    private const int SpotifyWorkspaceIndex = 10;
    private const int GitHubDesktopWorkspaceIndex = 11;

    private readonly IAppStateService _appStateService;
    private readonly INotificationService _notificationService;
    private readonly ISystemStatusService _systemStatusService;
    private readonly ISystemTrayService _systemTrayService;
    private readonly IActiveWindowService _activeWindowService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWindowActionService _windowActionService;
    private readonly IWindowLayoutService _windowLayoutService;
    private readonly IDiagnosticLogService _logService;
    private readonly IShellCommandService _shellCommandService;
    private readonly IPomodoroService _pomodoroService;
    private readonly SystemStatusModel _systemStatus;
    private readonly MediaSessionModel _mediaSession;
    private readonly Action<int> _cycleWorkspace;
    private readonly List<WorkspaceItemViewModel> _normalWorkspaces;
    private string _activeWindowTitle = "Desktop";
    private string _clockText = DateTime.Now.ToString("HH:mm");
    private string _dateText = DateTime.Now.ToString("ddd, dd MMM");
    private int _notificationCount;
    private bool _isVisible = true;
    private int _workspaceViewportStart = 1;
    private int _workspaceTransitionDirection;
    private int _workspaceTransitionVersion;
    private int _activeDesktopStripIndex;
    private int _lastActiveWorkspaceIndex;
    private bool _isMediaPopoverOpen;
    private bool _isPomodoroPopoverOpen;
    private bool _isTrayExpanded;
    private bool _isShowingQuickAccessApps;

    public TopBarViewModel(
        IAppStateService appStateService,
        IShellCommandService shellCommandService,
        INotificationService notificationService,
        ISystemStatusService systemStatusService,
        IMediaService mediaService,
        ISystemTrayService systemTrayService,
        IActiveWindowService activeWindowService,
        IWorkspaceService workspaceService,
        IWindowActionService windowActionService,
        IWindowLayoutService windowLayoutService,
        IDiagnosticLogService logService,
        IPomodoroService pomodoroService)
    {
        _appStateService = appStateService;
        _notificationService = notificationService;
        _systemStatusService = systemStatusService;
        _systemTrayService = systemTrayService;
        _activeWindowService = activeWindowService;
        _workspaceService = workspaceService;
        _windowActionService = windowActionService;
        _windowLayoutService = windowLayoutService;
        _logService = logService;
        _shellCommandService = shellCommandService;
        _pomodoroService = pomodoroService;
        _systemStatus = systemStatusService.CurrentStatus;
        _mediaSession = mediaService.CurrentSession;
        _cycleWorkspace = shellCommandService.CycleWorkspace;
        _normalWorkspaces = _appStateService.Workspaces
            .Where(workspace => workspace.Index <= NormalWorkspaceCount)
            .Select(workspace => new WorkspaceItemViewModel(workspace, ActivateWorkspaceItem))
            .ToList();
        DesktopStripItems = [];
        QuickAccessApps =
        [
            new QuickAccessAppViewModel("discord", "Discord", "\uE8BD", "Segoe MDL2 Assets", () => _shellCommandService.ToggleDiscordDesktopAsync()),
            new QuickAccessAppViewModel("spotify", "Spotify", "\u266A", "JetBrains Mono, Cascadia Mono, Segoe UI Symbol, Segoe UI", () => _shellCommandService.ToggleSpotifyDesktopAsync()),
            new QuickAccessAppViewModel("github-desktop", "GitHub Desktop", "GH", "JetBrains Mono, Cascadia Mono, Segoe UI", () => _shellCommandService.ToggleGitHubDesktopAsync())
        ];
        RefreshDesktopStripItems();
        _lastActiveWorkspaceIndex = _appStateService.ActiveWorkspaceIndex;
        ToggleLauncherCommand = new RelayCommand(shellCommandService.ToggleLauncher);
        ToggleControlCenterCommand = new RelayCommand(shellCommandService.ToggleControlCenter);
        ToggleNotificationCenterCommand = new RelayCommand(shellCommandService.ToggleNotificationCenter);
        ToggleTrayCommand = new RelayCommand(() => IsTrayExpanded = !IsTrayExpanded);
        ActivateTrayItemCommand = new RelayCommand<SystemTrayItem>(ActivateTrayItem, item => item is not null);
        OpenTrayItemContextMenuCommand = new RelayCommand<SystemTrayItem>(OpenTrayItemContextMenu, item => item is not null);
        TerminateTrayItemCommand = new RelayCommand<SystemTrayItem>(TerminateTrayItem, item => item is not null);
        RestoreActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.RestoreWindow(hwnd)));
        MinimizeActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.MinimizeWindow(hwnd)));
        MaximizeActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.MaximizeWindow(hwnd)));
        FocusActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.FocusWindow(hwnd)));
        CloseActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.CloseWindow(hwnd)));
        ForceKillActiveWindowCommand = new RelayCommand(() => WithActiveWindow(hwnd => _windowActionService.KillWindowProcess(hwnd)));
        MoveActiveWindowToWorkspaceCommand = new RelayCommand<int>(MoveActiveWindowToWorkspace, workspace => workspace is >= 1 and <= NormalWorkspaceCount);
        OpenTaskManagerCommand = new RelayCommand(OpenTaskManager);
        PlayPauseCommand = new AsyncRelayCommand(() => mediaService.PlayPauseAsync());
        PreviousTrackCommand = new AsyncRelayCommand(() => mediaService.PreviousAsync());
        NextTrackCommand = new AsyncRelayCommand(() => mediaService.NextAsync());
        TogglePomodoroPauseCommand = new RelayCommand(TogglePomodoroPause);
        RestartPomodoroCommand = new RelayCommand(_pomodoroService.Restart);
        StopPomodoroCommand = new RelayCommand(_pomodoroService.Stop);
        ActiveWindowTitle = _appStateService.ActiveWindowTitle;
        NotificationCount = _notificationService.Notifications.Count;
        IsVisible = !_appStateService.IsForegroundFullscreen;

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        _systemStatus.PropertyChanged += OnSystemStatusPropertyChanged;
        _mediaSession.PropertyChanged += OnMediaSessionPropertyChanged;
        _pomodoroService.PropertyChanged += OnPomodoroServicePropertyChanged;
        if (_notificationService.Notifications is INotifyCollectionChanged notifications)
        {
            notifications.CollectionChanged += (_, _) => NotificationCount = _notificationService.Notifications.Count;
        }

        if (_systemTrayService.Items is INotifyCollectionChanged trayItems)
        {
            trayItems.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTrayItems));
                OnPropertyChanged(nameof(TrayToggleGlyph));
                OnPropertyChanged(nameof(TraySummary));
            };
        }

        var clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
    }

    public ObservableCollection<IDesktopStripItemViewModel> DesktopStripItems { get; }

    public ObservableCollection<QuickAccessAppViewModel> QuickAccessApps { get; }

    public MediaSessionModel MediaSession => _mediaSession;

    public ReadOnlyObservableCollection<SystemTrayItem> TrayItems => _systemTrayService.Items;

    public ICommand ToggleControlCenterCommand { get; }

    public ICommand ToggleLauncherCommand { get; }

    public ICommand ToggleNotificationCenterCommand { get; }

    public ICommand ToggleTrayCommand { get; }

    public ICommand ActivateTrayItemCommand { get; }

    public ICommand OpenTrayItemContextMenuCommand { get; }

    public ICommand TerminateTrayItemCommand { get; }

    public ICommand RestoreActiveWindowCommand { get; }

    public ICommand MinimizeActiveWindowCommand { get; }

    public ICommand MaximizeActiveWindowCommand { get; }

    public ICommand FocusActiveWindowCommand { get; }

    public ICommand CloseActiveWindowCommand { get; }

    public ICommand ForceKillActiveWindowCommand { get; }

    public ICommand MoveActiveWindowToWorkspaceCommand { get; }

    public ICommand OpenTaskManagerCommand { get; }

    public ICommand PlayPauseCommand { get; }

    public ICommand PreviousTrackCommand { get; }

    public ICommand NextTrackCommand { get; }

    public ICommand TogglePomodoroPauseCommand { get; }

    public ICommand RestartPomodoroCommand { get; }

    public ICommand StopPomodoroCommand { get; }

    public int WorkspaceViewportStart
    {
        get => _workspaceViewportStart;
        private set => SetProperty(ref _workspaceViewportStart, value);
    }

    public int WorkspaceTransitionDirection
    {
        get => _workspaceTransitionDirection;
        private set => SetProperty(ref _workspaceTransitionDirection, value);
    }

    public int WorkspaceTransitionVersion
    {
        get => _workspaceTransitionVersion;
        private set => SetProperty(ref _workspaceTransitionVersion, value);
    }

    public int ActiveDesktopStripIndex
    {
        get => _activeDesktopStripIndex;
        private set => SetProperty(ref _activeDesktopStripIndex, value);
    }

    public bool IsShowingQuickAccessApps
    {
        get => _isShowingQuickAccessApps;
        private set => SetProperty(ref _isShowingQuickAccessApps, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        private set => SetProperty(ref _activeWindowTitle, value);
    }

    public string ClockText
    {
        get => _clockText;
        private set => SetProperty(ref _clockText, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public int NotificationCount
    {
        get => _notificationCount;
        private set => SetProperty(ref _notificationCount, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsMediaPopoverOpen
    {
        get => _isMediaPopoverOpen;
        set => SetProperty(ref _isMediaPopoverOpen, value);
    }

    public bool IsPomodoroPopoverOpen
    {
        get => _isPomodoroPopoverOpen;
        set => SetProperty(ref _isPomodoroPopoverOpen, value);
    }

    public bool IsTrayExpanded
    {
        get => _isTrayExpanded;
        set
        {
            if (SetProperty(ref _isTrayExpanded, value))
            {
                OnPropertyChanged(nameof(TrayToggleGlyph));
                OnPropertyChanged(nameof(TraySummary));
            }
        }
    }

    public bool ShowMediaPill => _appStateService.Config.ControlCenter.ShowMediaPill
                                 && MediaSession.IsAvailable
                                 && MediaSession.HasSession;

    public bool ShowPomodoroPill => _appStateService.Config.ControlCenter.ShowPomodoroPill
                                    && _pomodoroService.IsVisible;

    public string PomodoroGlyph => _pomodoroService.IsPaused ? "\uE769" : "\uE823";

    public string PomodoroRemainingText => FormatPomodoroDuration(_pomodoroService.RemainingSeconds);

    public string PomodoroStatusText => _pomodoroService.IsPaused
        ? _pomodoroService.Phase == CaelestiaWin.Core.Enums.PomodoroPhaseKind.Break ? "Break paused" : "Focus paused"
        : _pomodoroService.Phase == CaelestiaWin.Core.Enums.PomodoroPhaseKind.Break ? "Break" : "Focus";

    public string PomodoroPauseResumeLabel => _pomodoroService.IsPaused ? "Resume" : "Pause";

    public bool HasTrayItems => TrayItems.Count > 0;

    public string TrayToggleGlyph => IsTrayExpanded ? "\uE70E" : "\uE70D";

    public string TraySummary => HasTrayItems
        ? IsTrayExpanded ? "Hide background apps" : $"Show background apps ({TrayItems.Count})"
        : "No background apps";

    public string VolumeGlyph => _systemStatus.IsMuted || _systemStatus.VolumePercent <= 0 ? "\uE74F" : "\uE767";

    public string VolumeSummary => _systemStatus.IsMuted ? "Muted" : $"{_systemStatus.VolumePercent:0}%";

    public double VolumeLevel
    {
        get => _systemStatus.VolumePercent;
        set
        {
            if (Math.Abs(_systemStatus.VolumePercent - value) > 0.1d)
            {
                _systemStatusService.SetVolume(value);
                OnPropertyChanged();
            }
        }
    }

    public string MediaPlayPauseGlyph => MediaSession.IsPlaying ? "\uE769" : "\uE768";

    public string NetworkGlyph => _systemStatus.WifiEnabled ? "\uE701" : "\uE704";

    public string BluetoothGlyph => _systemStatus.BluetoothEnabled ? "\uE702" : "\uE701";

    public bool ShowBluetoothIndicator => _systemStatus.BluetoothEnabled;

    public void CycleWorkspace(int delta)
    {
        if (IsShowingQuickAccessApps)
        {
            SetQuickAccessMode(false);
            RefreshDesktopStripItems();
            WorkspaceTransitionVersion++;
            return;
        }

        _cycleWorkspace(delta);
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.ActiveWindowTitle))
        {
            ActiveWindowTitle = _appStateService.ActiveWindowTitle;
            return;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.IsForegroundFullscreen))
        {
            IsVisible = !_appStateService.IsForegroundFullscreen;
            return;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.Config))
        {
            OnPropertyChanged(nameof(ShowMediaPill));
            OnPropertyChanged(nameof(ShowPomodoroPill));
            if (!ShowMediaPill)
            {
                IsMediaPopoverOpen = false;
            }

            if (!ShowPomodoroPill)
            {
                IsPomodoroPopoverOpen = false;
            }

            return;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.ActiveWorkspaceIndex))
        {
            var current = _appStateService.ActiveWorkspaceIndex;
            WorkspaceTransitionDirection = Math.Sign(current - _lastActiveWorkspaceIndex);
            _lastActiveWorkspaceIndex = current;
            SetQuickAccessMode(current >= QuickAccessWorkspaceStart);
            RefreshDesktopStripItems();
            WorkspaceTransitionVersion++;
        }
    }

    private void ActivateWorkspaceItem(WorkspaceItemViewModel item)
    {
        if (item.IsActive)
        {
            SetQuickAccessMode(!IsShowingQuickAccessApps);
            RefreshDesktopStripItems();
            WorkspaceTransitionVersion++;
            return;
        }

        SetQuickAccessMode(false);
        _shellCommandService.SwitchWorkspace(item.Index);
    }

    private void SetQuickAccessMode(bool isShowing)
    {
        if (SetProperty(ref _isShowingQuickAccessApps, isShowing, nameof(IsShowingQuickAccessApps)))
        {
            WorkspaceTransitionDirection = 0;
        }
    }

    private void RefreshDesktopStripItems()
    {
        UpdateQuickAccessState();

        if (IsShowingQuickAccessApps)
        {
            ReplaceDesktopStripItems(QuickAccessApps);
            UpdateActiveDesktopStripIndex();
            return;
        }

        RefreshVisibleWorkspaces();
        UpdateActiveDesktopStripIndex();
    }

    private void RefreshVisibleWorkspaces()
    {
        var activeWorkspace = _appStateService.ActiveWorkspaceIndex;
        var normalizedActiveWorkspace = Math.Clamp(activeWorkspace, 1, NormalWorkspaceCount);
        var desiredViewportStart = (((normalizedActiveWorkspace - 1) / VisibleWorkspaceCount) * VisibleWorkspaceCount) + 1;
        var maxViewportStart = Math.Max(1, ((NormalWorkspaceCount - 1) / VisibleWorkspaceCount * VisibleWorkspaceCount) + 1);
        desiredViewportStart = Math.Min(desiredViewportStart, maxViewportStart);

        WorkspaceViewportStart = desiredViewportStart;

        var visibleItems = _normalWorkspaces
            .Where(workspace => workspace.Index >= WorkspaceViewportStart
                                && workspace.Index < WorkspaceViewportStart + VisibleWorkspaceCount)
            .Cast<IDesktopStripItemViewModel>()
            .ToArray();

        if (DesktopStripItems.Count == visibleItems.Length
            && DesktopStripItems.SequenceEqual(visibleItems))
        {
            return;
        }

        ReplaceDesktopStripItems(visibleItems);
    }

    private void ReplaceDesktopStripItems(IEnumerable<IDesktopStripItemViewModel> items)
    {
        DesktopStripItems.Clear();
        foreach (var item in items)
        {
            DesktopStripItems.Add(item);
        }

        UpdateActiveDesktopStripIndex();
    }

    private void UpdateQuickAccessState()
    {
        foreach (var app in QuickAccessApps)
        {
            app.IsActive = app.Key switch
            {
                "discord" => _appStateService.ActiveWorkspaceIndex == DiscordWorkspaceIndex,
                "spotify" => _appStateService.ActiveWorkspaceIndex == SpotifyWorkspaceIndex,
                "github-desktop" => _appStateService.ActiveWorkspaceIndex == GitHubDesktopWorkspaceIndex,
                _ => false
            };
        }
    }

    private void UpdateActiveDesktopStripIndex()
    {
        var activeIndex = -1;
        for (var i = 0; i < DesktopStripItems.Count; i++)
        {
            var item = DesktopStripItems[i];
            var isActiveItem = IsShowingQuickAccessApps
                ? item.IsActive
                : item is WorkspaceItemViewModel workspaceItem
                  && workspaceItem.Index == _appStateService.ActiveWorkspaceIndex;

            if (isActiveItem)
            {
                activeIndex = i;
                break;
            }
        }

        ActiveDesktopStripIndex = activeIndex;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm");
        DateText = now.ToString("ddd, dd MMM");
    }

    private void TogglePomodoroPause()
    {
        if (_pomodoroService.IsRunning)
        {
            _pomodoroService.Pause();
        }
        else if (_pomodoroService.IsPaused)
        {
            _pomodoroService.Resume();
        }
    }

    private void ActivateTrayItem(SystemTrayItem? item)
    {
        if (item is null)
        {
            return;
        }

        _ = _systemTrayService.Activate(item.Id);
    }

    private void OpenTrayItemContextMenu(SystemTrayItem? item)
    {
        if (item is null)
        {
            return;
        }

        _ = _systemTrayService.Activate(item.Id);
    }

    private void TerminateTrayItem(SystemTrayItem? item)
    {
        if (item is null)
        {
            return;
        }

        _ = _systemTrayService.Terminate(item.Id);
    }

    private void MoveActiveWindowToWorkspace(int workspaceIndex)
    {
        if (_activeWindowService.CurrentWindow is null)
        {
            return;
        }

        _shellCommandService.MoveFocusedWindowToWorkspace(workspaceIndex);
    }

    private void WithActiveWindow(Func<nint, bool> action)
    {
        var hwnd = _activeWindowService.CurrentWindow?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero)
        {
            return;
        }

        _ = action(hwnd);
    }

    private void OpenTaskManager()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = false
            });
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to open Task Manager from the top-bar menu.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void OnMediaSessionPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MediaSessionModel.HasSession)
            or nameof(MediaSessionModel.IsAvailable)
            or nameof(MediaSessionModel.TrackTitle)
            or nameof(MediaSessionModel.IsPlaying)
            or nameof(MediaSessionModel.ArtworkPath))
        {
            OnPropertyChanged(nameof(ShowMediaPill));
            OnPropertyChanged(nameof(MediaPlayPauseGlyph));
            if (!ShowMediaPill)
            {
                IsMediaPopoverOpen = false;
            }
        }
    }

    private void OnPomodoroServicePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IPomodoroService.State)
            or nameof(IPomodoroService.Phase))
        {
            OnPropertyChanged(nameof(ShowPomodoroPill));
            OnPropertyChanged(nameof(PomodoroGlyph));
            OnPropertyChanged(nameof(PomodoroRemainingText));
            OnPropertyChanged(nameof(PomodoroStatusText));
            OnPropertyChanged(nameof(PomodoroPauseResumeLabel));
            if (!ShowPomodoroPill)
            {
                IsPomodoroPopoverOpen = false;
            }
        }

        if (eventArgs.PropertyName is nameof(IPomodoroService.RemainingSeconds)
            or nameof(IPomodoroService.ElapsedSeconds))
        {
            OnPropertyChanged(nameof(PomodoroRemainingText));
        }
    }

    private void OnSystemStatusPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemStatusModel.VolumePercent) or nameof(SystemStatusModel.IsMuted))
        {
            OnPropertyChanged(nameof(VolumeGlyph));
            OnPropertyChanged(nameof(VolumeSummary));
            OnPropertyChanged(nameof(VolumeLevel));
        }

        if (eventArgs.PropertyName == nameof(SystemStatusModel.WifiEnabled))
        {
            OnPropertyChanged(nameof(NetworkGlyph));
        }

        if (eventArgs.PropertyName == nameof(SystemStatusModel.BluetoothEnabled))
        {
            OnPropertyChanged(nameof(BluetoothGlyph));
            OnPropertyChanged(nameof(ShowBluetoothIndicator));
        }
    }

    private static string FormatPomodoroDuration(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.TotalHours >= 1d
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
