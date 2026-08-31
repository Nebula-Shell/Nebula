using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class ShellViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IWindowLayoutService _windowLayoutService;
    private readonly SystemStatusModel _systemStatus;
    private readonly DispatcherTimer _volumeHudHideTimer;
    private ShellBarLayoutKind _lastBarLayout;
    private bool _isShortcutGuideVisible;
    private bool _isVolumeHudVisible;
    private double _lastVolumePercent;
    private bool _lastIsMuted;

    public ShellViewModel(
        IAppStateService appStateService,
        IUiDispatcher uiDispatcher,
        ISystemStatusService systemStatusService,
        IWindowLayoutService windowLayoutService,
        TopBarViewModel topBar,
        LauncherViewModel launcher,
        ControlCenterViewModel controlCenter,
        NotificationCenterViewModel notificationCenter,
        OverviewViewModel overview,
        FocusOutlineViewModel focusOutline,
        DesktopSwitchIndicatorViewModel desktopSwitchIndicator,
        ShellToastViewModel shellToast,
        ClipboardHistoryViewModel clipboardHistory)
    {
        _appStateService = appStateService;
        _uiDispatcher = uiDispatcher;
        _windowLayoutService = windowLayoutService;
        _systemStatus = systemStatusService.CurrentStatus;
        _lastVolumePercent = _systemStatus.VolumePercent;
        _lastIsMuted = _systemStatus.IsMuted;
        _lastBarLayout = _appStateService.Config.ControlCenter.BarLayout;
        TopBar = topBar;
        Launcher = launcher;
        ControlCenter = controlCenter;
        NotificationCenter = notificationCenter;
        Overview = overview;
        FocusOutline = focusOutline;
        DesktopSwitchIndicator = desktopSwitchIndicator;
        ShellToast = shellToast;
        ClipboardHistory = clipboardHistory;
        DismissOverlaysCommand = new RelayCommand(DismissOverlays);
        ShortcutGuideItems = [];
        IsShortcutGuideVisible = _appStateService.IsShortcutGuideVisible;
        _volumeHudHideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1350)
        };
        _volumeHudHideTimer.Tick += (_, _) =>
        {
            _volumeHudHideTimer.Stop();
            IsVolumeHudVisible = false;
        };
        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        _systemStatus.PropertyChanged += OnSystemStatusPropertyChanged;
        ReloadShortcutGuide();
    }

    public TopBarViewModel TopBar { get; }

    public LauncherViewModel Launcher { get; }

    public ControlCenterViewModel ControlCenter { get; }

    public NotificationCenterViewModel NotificationCenter { get; }

    public OverviewViewModel Overview { get; }

    public FocusOutlineViewModel FocusOutline { get; }

    public DesktopSwitchIndicatorViewModel DesktopSwitchIndicator { get; }

    public ShellToastViewModel ShellToast { get; }

    public ClipboardHistoryViewModel ClipboardHistory { get; }

    public ObservableCollection<ShortcutGuideItemViewModel> ShortcutGuideItems { get; }

    public ICommand DismissOverlaysCommand { get; }

    public bool IsShortcutGuideVisible
    {
        get => _isShortcutGuideVisible;
        private set => SetProperty(ref _isShortcutGuideVisible, value);
    }

    public bool IsVolumeHudVisible
    {
        get => _isVolumeHudVisible;
        private set => SetProperty(ref _isVolumeHudVisible, value);
    }

    public bool UseLeftBarLayout => _appStateService.Config.ControlCenter.BarLayout == ShellBarLayoutKind.Left;

    public bool UseNativeDesktopWallpaper =>
        string.IsNullOrWhiteSpace(_appStateService.Config.Theme.WallpaperPath)
        && _appStateService.IsExplorerRunning;

    public bool ShowDesktopDecorationsOverlay => UseNativeDesktopWallpaper && _appStateService.Config.Theme.ShowDesktopDecorations;

    public string VolumeHudGlyph => _systemStatus.IsMuted || _systemStatus.VolumePercent <= 0 ? "\uE74F" : "\uE767";

    public string VolumeHudSummary => _systemStatus.IsMuted ? "Muted" : $"{_systemStatus.VolumePercent:0}%";

    public double VolumeHudLevel => _systemStatus.VolumePercent;

    private void DismissOverlays()
    {
        _appStateService.IsLauncherOpen = false;
        _appStateService.IsControlCenterOpen = false;
        _appStateService.IsNotificationCenterOpen = false;
        _appStateService.IsOverviewOpen = false;
        _appStateService.IsClipboardHistoryOpen = false;
    }

    private void OnAppStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.IsShortcutGuideVisible))
        {
            IsShortcutGuideVisible = _appStateService.IsShortcutGuideVisible;
            return;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.Config))
        {
            var currentBarLayout = _appStateService.Config.ControlCenter.BarLayout;
            OnPropertyChanged(nameof(UseLeftBarLayout));
            OnPropertyChanged(nameof(UseNativeDesktopWallpaper));
            OnPropertyChanged(nameof(ShowDesktopDecorationsOverlay));
            ReloadShortcutGuide();
            if (currentBarLayout != _lastBarLayout)
            {
                _lastBarLayout = currentBarLayout;
                _windowLayoutService.RefreshActiveWorkspaceLayout();
            }

            return;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.IsExplorerRunning))
        {
            OnPropertyChanged(nameof(UseNativeDesktopWallpaper));
            OnPropertyChanged(nameof(ShowDesktopDecorationsOverlay));
        }
    }

    private void OnSystemStatusPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not (nameof(SystemStatusModel.VolumePercent) or nameof(SystemStatusModel.IsMuted)))
        {
            return;
        }

        var volumeChanged = Math.Abs(_systemStatus.VolumePercent - _lastVolumePercent) > 0.1d;
        var muteChanged = _systemStatus.IsMuted != _lastIsMuted;
        _lastVolumePercent = _systemStatus.VolumePercent;
        _lastIsMuted = _systemStatus.IsMuted;

        if (volumeChanged || muteChanged)
        {
            _ = _uiDispatcher.InvokeAsync(ShowVolumeHud);
        }
    }

    private void ShowVolumeHud()
    {
        OnPropertyChanged(nameof(VolumeHudGlyph));
        OnPropertyChanged(nameof(VolumeHudSummary));
        OnPropertyChanged(nameof(VolumeHudLevel));
        IsVolumeHudVisible = true;
        _volumeHudHideTimer.Stop();
        _volumeHudHideTimer.Start();
    }

    private void ReloadShortcutGuide()
    {
        ShortcutGuideItems.Clear();
        var addedWorkspaceSwitchGroup = false;
        var addedWorkspaceMoveGroup = false;

        foreach (var binding in _appStateService.Config.Hotkeys.Bindings)
        {
            if (IsNormalWorkspaceBinding(binding, HotkeyActionKind.SwitchWorkspace))
            {
                if (!addedWorkspaceSwitchGroup)
                {
                    ShortcutGuideItems.Add(new ShortcutGuideItemViewModel(
                        "Switch desktops",
                        CollapseWorkspaceGestureRange(binding.Gesture),
                        "Jump directly to desktop 1-8."));
                    addedWorkspaceSwitchGroup = true;
                }

                continue;
            }

            if (IsNormalWorkspaceBinding(binding, HotkeyActionKind.MoveWindowToWorkspace))
            {
                if (!addedWorkspaceMoveGroup)
                {
                    ShortcutGuideItems.Add(new ShortcutGuideItemViewModel(
                        "Move window to desktop",
                        CollapseWorkspaceGestureRange(binding.Gesture),
                        "Move the focused window to desktop 1-8 and follow it."));
                    addedWorkspaceMoveGroup = true;
                }

                continue;
            }

            if (IsFunctionKeyBinding(binding))
            {
                continue;
            }

            var editor = new HotkeyBindingEditorViewModel(binding);
            ShortcutGuideItems.Add(new ShortcutGuideItemViewModel(editor.DisplayName, editor.Gesture, editor.Description));
        }
    }

    private static bool IsNormalWorkspaceBinding(HotkeyBindingConfig binding, HotkeyActionKind action)
    {
        return binding.Action == action && binding.Workspace is >= 1 and <= 8;
    }

    private static bool IsFunctionKeyBinding(HotkeyBindingConfig binding)
    {
        return binding.Action is HotkeyActionKind.VolumeUp
            or HotkeyActionKind.VolumeDown
            or HotkeyActionKind.ToggleMute
            or HotkeyActionKind.MediaPlayPause
            or HotkeyActionKind.MediaNext
            or HotkeyActionKind.MediaPrevious
            or HotkeyActionKind.BrightnessUp
            or HotkeyActionKind.BrightnessDown;
    }

    private static string CollapseWorkspaceGestureRange(string gesture)
    {
        for (var workspace = 1; workspace <= 8; workspace++)
        {
            var suffix = workspace.ToString();
            if (gesture.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(gesture.AsSpan(0, gesture.Length - suffix.Length), "1-8");
            }
        }

        return gesture;
    }
}
