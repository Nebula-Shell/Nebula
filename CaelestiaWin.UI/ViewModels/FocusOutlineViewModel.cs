using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.UI.ViewModels;

public sealed class FocusOutlineViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly IActiveWindowService _activeWindowService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWindowActionService _windowActionService;
    private readonly IGameModeService _gameModeService;
    private readonly DispatcherTimer _followTimer;
    private nint _trackedWindow;
    private bool _isVisible;
    private double _left;
    private double _top;
    private double _width;
    private double _height;
    private nint _targetWindowHandle;

    public FocusOutlineViewModel(
        IAppStateService appStateService,
        IActiveWindowService activeWindowService,
        IWorkspaceService workspaceService,
        IWindowActionService windowActionService,
        IGameModeService gameModeService)
    {
        _appStateService = appStateService;
        _activeWindowService = activeWindowService;
        _workspaceService = workspaceService;
        _windowActionService = windowActionService;
        _gameModeService = gameModeService;
        _followTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _followTimer.Tick += (_, _) => RefreshOutline();

        _activeWindowService.CurrentWindowChanged += OnCurrentWindowChanged;
        _activeWindowService.WindowsChanged += OnWindowsChanged;
        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        _gameModeService.PropertyChanged += OnGameModePropertyChanged;
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public double Left
    {
        get => _left;
        private set => SetProperty(ref _left, value);
    }

    public double Top
    {
        get => _top;
        private set => SetProperty(ref _top, value);
    }

    public double Width
    {
        get => _width;
        private set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        private set => SetProperty(ref _height, value);
    }

    public nint TargetWindowHandle
    {
        get => _targetWindowHandle;
        private set => SetProperty(ref _targetWindowHandle, value);
    }

    public CornerRadius CornerRadius => _appStateService.Config.Windowing.UseRoundedFocusOutline
        ? new CornerRadius(16)
        : new CornerRadius(0);

    public Thickness BorderThickness => new(GetOutlineThickness());

    private void OnCurrentWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        _trackedWindow = eventArgs.Window?.Handle ?? nint.Zero;
        RefreshOutline();
    }

    private void OnWindowsChanged(object? sender, EventArgs eventArgs)
    {
        RefreshOutline();
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IAppStateService.IsOverviewOpen)
            or nameof(IAppStateService.IsForegroundFullscreen)
            or nameof(IAppStateService.ActiveWorkspaceIndex)
            or nameof(IAppStateService.Config))
        {
            if (eventArgs.PropertyName == nameof(IAppStateService.ActiveWorkspaceIndex))
            {
                IsVisible = false;
            }

            if (eventArgs.PropertyName == nameof(IAppStateService.Config))
            {
                OnPropertyChanged(nameof(CornerRadius));
                OnPropertyChanged(nameof(BorderThickness));
            }

            RefreshOutline();
        }
    }

    private void OnGameModePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IGameModeService.IsEffective) or nameof(IGameModeService.IsFullscreenGameRunning))
        {
            RefreshOutline();
        }
    }

    private void RefreshOutline()
    {
        if (!ShouldShowOutline())
        {
            StopFollowing();
            return;
        }

        var bounds = _windowActionService.GetWindowBounds(_trackedWindow);
        if (bounds is null || bounds.Value.IsEmpty)
        {
            StopFollowing();
            return;
        }

        var outlineOffset = GetOutlineOffset();
        Left = bounds.Value.Left - outlineOffset;
        Top = bounds.Value.Top - outlineOffset;
        Width = bounds.Value.Width + (outlineOffset * 2);
        Height = bounds.Value.Height + (outlineOffset * 2);
        TargetWindowHandle = _trackedWindow;
        IsVisible = true;

        if (!_followTimer.IsEnabled)
        {
            _followTimer.Start();
        }
    }

    private bool ShouldShowOutline()
    {
        return _trackedWindow != nint.Zero
               && !_appStateService.IsOverviewOpen
               && !_appStateService.IsForegroundFullscreen
               && !_gameModeService.IsEffective
               && _workspaceService.GetWorkspaceForWindow(_trackedWindow) == _appStateService.ActiveWorkspaceIndex
               && _windowActionService.IsWindowAlive(_trackedWindow)
               && !_windowActionService.IsWindowFullscreen(_trackedWindow);
    }

    private void StopFollowing()
    {
        IsVisible = false;
        TargetWindowHandle = nint.Zero;
        if (_followTimer.IsEnabled)
        {
            _followTimer.Stop();
        }
    }

    private int GetOutlineOffset() => Math.Clamp(_appStateService.Config.Windowing.FocusOutlineOffset, 0, 24);

    private int GetOutlineThickness() => Math.Clamp(_appStateService.Config.Windowing.FocusOutlineThickness, 1, 12);
}
