using System.ComponentModel;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.App.Services;

public sealed class GameModeService(
    IAppStateService appStateService,
    IActiveWindowService activeWindowService,
    IVisibleWindowService visibleWindowService,
    IWindowActionService windowActionService,
    IThemeManager themeManager,
    IDiagnosticLogService logService,
    IWorkspaceService workspaceService) : ObservableObjectBase, IGameModeService
{
    private static readonly string[] DefaultGameTokens =
    [
        "league of legends",
        "valorant-win64-shipping",
        "fortniteclient-win64-shipping",
        "rocketleague",
        "cs2",
        "dota2",
        "eldenring",
        "genshinimpact",
        "overwatch"
    ];

    private static readonly string[] DefaultLauncherTokens =
    [
        "leagueclient",
        "leagueclientux",
        "leagueclientuxrender",
        "riotclientservices",
        "riotclientux"
    ];

    private static readonly string[] TilingIncompatibleGames =
    [
        "league of legends",
        "leagueclient",
        "leagueclientux"
    ];

    private static readonly string[] FullscreenGamePathHints =
    [
        "\\steamapps\\common\\",
        "\\riot games\\",
        "\\epic games\\",
        "\\battle.net\\",
        "\\games\\"
    ];

    private bool _started;
    private bool _autoEnable = true;
    private bool _isGameRunning;
    private bool _isFullscreenGameRunning;
    private bool _isEffective;
    private string _activeGameName = "No active game";
    private bool? _lastAppliedReducedVisualMode;
    private AppConfig? _lastAppliedConfig;

    public bool AutoEnable
    {
        get => _autoEnable;
        private set => SetProperty(ref _autoEnable, value);
    }

    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set => SetProperty(ref _isGameRunning, value);
    }

    public bool IsFullscreenGameRunning
    {
        get => _isFullscreenGameRunning;
        private set => SetProperty(ref _isFullscreenGameRunning, value);
    }

    public bool IsEffective
    {
        get => _isEffective;
        private set => SetProperty(ref _isEffective, value);
    }

    public string ActiveGameName
    {
        get => _activeGameName;
        private set => SetProperty(ref _activeGameName, value);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        AutoEnable = appStateService.Config.GameMode.AutoEnable;
        activeWindowService.CurrentWindowChanged += OnCurrentWindowChanged;
        activeWindowService.WindowsChanged += OnWindowsChanged;
        appStateService.PropertyChanged += OnAppStatePropertyChanged;
        Evaluate();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        activeWindowService.CurrentWindowChanged -= OnCurrentWindowChanged;
        activeWindowService.WindowsChanged -= OnWindowsChanged;
        appStateService.PropertyChanged -= OnAppStatePropertyChanged;
        themeManager.ApplyTheme(appStateService.Config);
    }

    public void SetAutoEnabled(bool enabled)
    {
        if (AutoEnable == enabled)
        {
            return;
        }

        AutoEnable = enabled;
        Evaluate();
    }

    public bool IsGameWindow(WindowDescriptor window)
    {
        return MatchesConfiguredGame(window) || LooksLikeFullscreenGame(window);
    }

    public bool ShouldCenterWindow(WindowDescriptor window)
    {
        var configTokens = appStateService.Config.GameMode.CenteredLauncherProcesses;
        return (MatchesWindow(window, configTokens) || MatchesWindow(window, DefaultLauncherTokens))
               && !windowActionService.IsWindowFullscreen(window.Handle);
    }

    private void OnCurrentWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        Evaluate();
    }

    private void OnWindowsChanged(object? sender, EventArgs eventArgs)
    {
        Evaluate();
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.Config))
        {
            AutoEnable = appStateService.Config.GameMode.AutoEnable;
            Evaluate();
        }
    }

    private void Evaluate()
    {
        var current = activeWindowService.CurrentWindow;
        IReadOnlyList<WindowDescriptor> allTrackedWindows;
        try
        {
            // Check all windows across all workspaces, not just visible ones
            allTrackedWindows = workspaceService.GetAllTrackedWindows();
        }
        catch (Exception exception)
        {
            logService.Warn("Game mode could not enumerate all windows.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
            // Fallback to visible windows if workspace service fails
            try
            {
                allTrackedWindows = visibleWindowService.GetVisibleWindows();
            }
            catch
            {
                allTrackedWindows = current is null ? [] : [current];
            }
        }

        var detectedGame = current is not null && IsGameWindow(current)
            ? current
            : allTrackedWindows.FirstOrDefault(IsGameWindow);
        var isGameRunning = detectedGame is not null;
        var isFullscreenGame = current is not null
            && IsGameWindow(current)
            && windowActionService.IsWindowFullscreen(current.Handle);
        var isEffective = AutoEnable && isGameRunning;
        var activeGameName = detectedGame is null
            ? "No active game"
            : !string.IsNullOrWhiteSpace(detectedGame.Title)
                ? detectedGame.Title
                : detectedGame.ProcessName ?? "Detected game";

        var shouldLogChange = isEffective != IsEffective
                              || isFullscreenGame != IsFullscreenGameRunning
                              || !string.Equals(activeGameName, ActiveGameName, StringComparison.Ordinal);

        IsGameRunning = isGameRunning;
        IsFullscreenGameRunning = isFullscreenGame;
        ActiveGameName = activeGameName;

        if (isFullscreenGame)
        {
            appStateService.IsLauncherOpen = false;
            appStateService.IsControlCenterOpen = false;
            appStateService.IsNotificationCenterOpen = false;
            appStateService.IsOverviewOpen = false;
            appStateService.IsClipboardHistoryOpen = false;
        }

        if (IsEffective != isEffective)
        {
            IsEffective = isEffective;
        }

        if (shouldLogChange)
        {
            logService.Info("Game mode state updated.", new Dictionary<string, object?>
            {
                ["autoEnable"] = AutoEnable,
                ["gameRunning"] = isGameRunning,
                ["fullscreenGame"] = isFullscreenGame,
                ["effective"] = isEffective,
                ["activeGame"] = activeGameName
            });
        }

        ApplyVisualMode();
    }

    private void ApplyVisualMode()
    {
        var config = appStateService.Config;
        var shouldReduceEffects = IsEffective && config.GameMode.ReduceEffects;
        if (_lastAppliedReducedVisualMode == shouldReduceEffects && ReferenceEquals(_lastAppliedConfig, config))
        {
            return;
        }

        _lastAppliedReducedVisualMode = shouldReduceEffects;
        _lastAppliedConfig = config;

        if (!shouldReduceEffects)
        {
            themeManager.ApplyTheme(config);
            return;
        }

        themeManager.ApplyTheme(new AppConfig
        {
            Theme = new ThemeConfig
            {
                AccentColor = config.Theme.AccentColor,
                SecondaryAccentColor = config.Theme.SecondaryAccentColor,
                ForegroundColor = config.Theme.ForegroundColor,
                MutedForegroundColor = config.Theme.MutedForegroundColor,
                PanelColor = config.Theme.PanelColor,
                BackgroundColor = config.Theme.BackgroundColor,
                WallpaperPath = config.Theme.WallpaperPath,
                ShowDesktopDecorations = config.Theme.ShowDesktopDecorations,
                AccentPalette = config.Theme.AccentPalette,
                BackgroundOpacity = 1d,
                PanelOpacity = 1d,
                EnableBackdropBlur = false,
                EnableShadows = false,
                EnableTransparency = false,
                CornerRadius = 0d
            },
            Logging = config.Logging,
            Animations = new AnimationConfig
            {
                FastMs = config.Animations.FastMs,
                NormalMs = config.Animations.NormalMs,
                SlowMs = config.Animations.SlowMs,
                OverlayEasing = config.Animations.OverlayEasing,
                LauncherScaleFrom = config.Animations.LauncherScaleFrom,
                SidePanelOffset = config.Animations.SidePanelOffset,
                DesiredFrameRate = Math.Min(config.Animations.DesiredFrameRate, 24)
            },
            Performance = config.Performance,
            ControlCenter = config.ControlCenter,
            Windowing = new WindowingConfig
            {
                EnableSoftTiling = config.Windowing.EnableSoftTiling,
                TilingStrategy = config.Windowing.TilingStrategy,
                UseRoundedFocusOutline = false,
                FocusOutlineOffset = config.Windowing.FocusOutlineOffset,
                FocusOutlineThickness = config.Windowing.FocusOutlineThickness,
                LayoutGap = config.Windowing.LayoutGap,
                OuterMargin = config.Windowing.OuterMargin,
                TopReservedSpace = config.Windowing.TopReservedSpace,
                OverviewColumns = config.Windowing.OverviewColumns
            },
            Launcher = config.Launcher,
            Terminal = config.Terminal,
            Notifications = config.Notifications,
            Session = config.Session,
            Startup = config.Startup,
            Hotkeys = config.Hotkeys,
            GameMode = config.GameMode
        });
    }

    private bool MatchesConfiguredGame(WindowDescriptor window)
    {
        var configTokens = appStateService.Config.GameMode.KnownGameProcesses;
        return MatchesWindow(window, configTokens) || MatchesWindow(window, DefaultGameTokens);
    }

    private bool LooksLikeFullscreenGame(WindowDescriptor window)
    {
        if (!windowActionService.IsWindowFullscreen(window.Handle))
        {
            return false;
        }

        var executablePath = window.ExecutablePath ?? string.Empty;
        for (var index = 0; index < FullscreenGamePathHints.Length; index++)
        {
            if (executablePath.Contains(FullscreenGamePathHints[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesWindow(WindowDescriptor window, IEnumerable<string> tokens)
    {
        foreach (var rawToken in tokens)
        {
            var token = rawToken?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (window.ProcessName?.Contains(token, StringComparison.OrdinalIgnoreCase) == true
                || window.ExecutablePath?.Contains(token, StringComparison.OrdinalIgnoreCase) == true
                || window.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool ShouldExcludeFromTiling(WindowDescriptor window)
    {
        return MatchesWindow(window, TilingIncompatibleGames);
    }
}

