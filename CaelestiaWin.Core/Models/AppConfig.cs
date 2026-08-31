using System.Text.Json.Serialization;
using CaelestiaWin.Core.Enums;

namespace CaelestiaWin.Core.Models;

public sealed class AppConfig
{
    public ThemeConfig Theme { get; init; } = new();

    public LoggingConfig Logging { get; init; } = new();

    public AnimationConfig Animations { get; init; } = new();

    public PerformanceConfig Performance { get; init; } = new();

    public ControlCenterConfig ControlCenter { get; init; } = new();

    public WindowingConfig Windowing { get; init; } = new();

    public LauncherConfig Launcher { get; init; } = new();

    public TerminalConfig Terminal { get; init; } = new();

    public NotificationConfig Notifications { get; init; } = new();

    public SessionConfig Session { get; init; } = new();

    public StartupConfig Startup { get; init; } = new();

    public HotkeyConfig Hotkeys { get; init; } = new();

    public GameModeConfig GameMode { get; init; } = new();

    public static AppConfig CreateDefault() => new();
}

public sealed class ThemeConfig
{
    public bool KeepWindowsAccentSeparate { get; init; } = true;

    public bool TintShellSurfacesWithAccent { get; init; } = true;

    public string AccentColor { get; init; } = "#79E6F5";

    public string SecondaryAccentColor { get; init; } = "#F8FDFF";

    public string ForegroundColor { get; init; } = "#F5FBFF";

    public string MutedForegroundColor { get; init; } = "#8FA7B7";

    public string PanelColor { get; init; } = "#141B23";

    public string BackgroundColor { get; init; } = "#0A1118";

    public string? WallpaperPath { get; init; }

    public bool ShowDesktopDecorations { get; init; } = true;

    public List<string> RecentWallpapers { get; init; } = [];

    public List<string> AccentPalette { get; init; } =
    [
        "#5AB7FF",
        "#79E6F5",
        "#59D2B1",
        "#93F27E",
        "#B6E16B",
        "#F5C16C",
        "#FF9A62",
        "#F58CC6",
        "#FF6FAE",
        "#B8A3FF",
        "#8DA2FF",
        "#C4AEFF"
    ];

    public double BackgroundOpacity { get; init; } = 0.92d;

    public double PanelOpacity { get; init; } = 0.78d;

    public bool EnableBackdropBlur { get; init; }

    public bool EnableShadows { get; init; } = true;

    public bool EnableTransparency { get; init; } = true;

    public double CornerRadius { get; init; } = 22d;
}

public sealed class AnimationConfig
{
    public int FastMs { get; init; } = 140;

    public int NormalMs { get; init; } = 220;

    public int SlowMs { get; init; } = 320;

    public string OverlayEasing { get; init; } = "CubicOut";

    public double LauncherScaleFrom { get; init; } = 0.96d;

    public double SidePanelOffset { get; init; } = 36d;

    public int DesiredFrameRate { get; init; } = 45;
}

public sealed class LoggingConfig
{
    public LogLevelKind Level { get; init; } = LogLevelKind.Info;

    public int MaxFileSizeMb { get; init; } = 4;
}

public sealed class PerformanceConfig
{
    public int ActiveWindowDebounceMs { get; init; } = 80;

    public int AppDiscoveryCacheMinutes { get; init; } = 10;

    public int WorkspaceSyncThrottleMs { get; init; } = 180;
}

public sealed class ControlCenterConfig
{
    public ControlCenterInputPlacementKind InputWidgetPlacement { get; init; } = ControlCenterInputPlacementKind.Auto;

    public ShellBarLayoutKind BarLayout { get; init; } = ShellBarLayoutKind.Top;

    public bool ShowMediaPill { get; init; } = true;

    public bool ShowPomodoroPill { get; init; } = true;

    // When true, the shell top bar shows a minimal title (app name) instead of the full window title
    public bool MinimalModeTitles { get; init; }

    public List<ControlCenterWidgetKind> WidgetOrder { get; init; } = [];

    public List<ControlCenterWidgetKind> HiddenWidgets { get; init; } = [];
}

public sealed class LauncherConfig
{
    public LauncherDisplayModeKind DisplayMode { get; init; } = LauncherDisplayModeKind.Grid;

    public int MaxResults { get; init; } = 8;

    public int SearchDebounceMs { get; init; } = 90;

    public bool PreloadOnStartup { get; init; } = true;

    public bool ClearQueryOnClose { get; init; } = true;

    public bool ShowRecentAppsOnEmptyQuery { get; init; } = true;

    public int RecentAppLimit { get; init; } = 5;

    public bool EnableCommandMode { get; init; } = true;

    public TerminalProfileKind DefaultTerminal { get; init; } = TerminalProfileKind.Nebula;

    public FileExplorerProfileKind DefaultFileExplorer { get; init; } = FileExplorerProfileKind.WindowsExplorer;

    public string CustomTerminalPath { get; init; } = string.Empty;
}

public sealed class TerminalConfig
{
    public bool FollowShellTransparency { get; init; } = true;

    public double Opacity { get; init; } = 0.86d;
}

public sealed class WindowingConfig
{
    public bool EnableSoftTiling { get; init; } = true;

    public WindowTilingStrategyKind TilingStrategy { get; init; } = WindowTilingStrategyKind.Grid;

    public bool UseRoundedFocusOutline { get; init; }

    public int FocusOutlineOffset { get; init; } = 2;

    public int FocusOutlineThickness { get; init; } = 3;

    public int LayoutGap { get; init; } = 14;

    public int OuterMargin { get; init; } = 18;

    public int TopReservedSpace { get; init; } = 82;

    public int OverviewColumns { get; init; } = 3;
}

public sealed class NotificationConfig
{
    public int MaxItems { get; init; } = 20;

    public bool EnableWindowsToasts { get; init; } = true;

    public bool EnableShellToasts { get; init; } = true;

    public bool EnableNotificationSounds { get; init; } = true;

    public NotificationToastPositionKind ShellToastPosition { get; init; } = NotificationToastPositionKind.TopRight;

    public bool ShowStartupStatus { get; init; } = true;

    // Path to a custom notification sound file (WAV). If present and valid, it will be used
    // for all Nebula notification sounds. Optional.
    public string? CustomSoundPath { get; init; }

    // Legacy configs may still contain this field; it is intentionally ignored.
    public bool SimulateOnStartup { get; init; }
}

public sealed class SessionConfig
{
    public bool SessionRestoreEnabled { get; init; } = true;

    public bool RelaunchAppsOnRestore { get; init; }
}

public sealed class StartupConfig
{
    public bool ShowOnPrimaryMonitor { get; init; } = true;

    public bool StartLauncherOpen { get; init; }

    public bool StartControlCenterOpen { get; init; }

    public bool TrackForegroundWindow { get; init; } = true;

    public bool StartOnLogin { get; init; }

    public bool EnableAutoStart { get; init; }

    public bool RestartOnCrash { get; init; } = true;

    public bool StopExplorerOnLaunch { get; init; } = true;
}

public sealed class HotkeyConfig
{
    public List<HotkeyBindingConfig> Bindings { get; init; } = CreateDefaultBindings();

    public static List<HotkeyBindingConfig> CreateDefaultBindings() =>
    [
        new() { Action = HotkeyActionKind.ToggleLauncher, Gesture = "Win" },
        new() { Action = HotkeyActionKind.OpenTerminal, Gesture = "Win+Enter" },
        new() { Action = HotkeyActionKind.OpenFileExplorer, Gesture = "Win+E" },
        new() { Action = HotkeyActionKind.ToggleControlCenter, Gesture = "Win+B" },
        new() { Action = HotkeyActionKind.ToggleNotificationCenter, Gesture = "Win+N" },
        new() { Action = HotkeyActionKind.ToggleClipboardHistory, Gesture = "Win+V" },
        new() { Action = HotkeyActionKind.CaptureRegion, Gesture = "Win+Shift+S" },
        new() { Action = HotkeyActionKind.ToggleSettingsPanel, Gesture = "Win+C" },
        new() { Action = HotkeyActionKind.ToggleDiscordDesktop, Gesture = "Win+D" },
        new() { Action = HotkeyActionKind.ToggleSpotifyDesktop, Gesture = "Win+M" },
        new() { Action = HotkeyActionKind.ToggleGitHubDesktop, Gesture = "Win+G" },
        new() { Action = HotkeyActionKind.ToggleFocusedWindowFullscreen, Gesture = "Win+F" },
        new() { Action = HotkeyActionKind.ToggleFocusedWindowFloat, Gesture = "Win+Shift+F" },
        new() { Action = HotkeyActionKind.CloseFocusedWindow, Gesture = "Win+Q" },
        new() { Action = HotkeyActionKind.FocusWindow, Gesture = "Win+H", Direction = WindowDirection.Left },
        new() { Action = HotkeyActionKind.FocusWindow, Gesture = "Win+L", Direction = WindowDirection.Right },
        new() { Action = HotkeyActionKind.FocusWindow, Gesture = "Win+K", Direction = WindowDirection.Up },
        new() { Action = HotkeyActionKind.FocusWindow, Gesture = "Win+J", Direction = WindowDirection.Down },
        new() { Action = HotkeyActionKind.MoveWindow, Gesture = "Win+Shift+H", Direction = WindowDirection.Left },
        new() { Action = HotkeyActionKind.MoveWindow, Gesture = "Win+Shift+L", Direction = WindowDirection.Right },
        new() { Action = HotkeyActionKind.MoveWindow, Gesture = "Win+Shift+K", Direction = WindowDirection.Up },
        new() { Action = HotkeyActionKind.MoveWindow, Gesture = "Win+Shift+J", Direction = WindowDirection.Down },
        new() { Action = HotkeyActionKind.CycleWorkspacePrevious, Gesture = "Win+Left" },
        new() { Action = HotkeyActionKind.CycleWorkspaceNext, Gesture = "Win+Right" },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspacePrevious, Gesture = "Win+Ctrl+Left" },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspaceNext, Gesture = "Win+Ctrl+Right" },
        new() { Action = HotkeyActionKind.VolumeUp, Gesture = "VolumeUp" },
        new() { Action = HotkeyActionKind.VolumeDown, Gesture = "VolumeDown" },
        new() { Action = HotkeyActionKind.ToggleMute, Gesture = "VolumeMute" },
        new() { Action = HotkeyActionKind.MediaPlayPause, Gesture = "MediaPlayPause" },
        new() { Action = HotkeyActionKind.MediaNext, Gesture = "MediaNext" },
        new() { Action = HotkeyActionKind.MediaPrevious, Gesture = "MediaPrevious" },
        new() { Action = HotkeyActionKind.BrightnessUp, Gesture = "BrightnessUp" },
        new() { Action = HotkeyActionKind.BrightnessDown, Gesture = "BrightnessDown" },
        new() { Action = HotkeyActionKind.ToggleOverview, Gesture = "Win+Tab" },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+1", Workspace = 1 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+2", Workspace = 2 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+3", Workspace = 3 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+4", Workspace = 4 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+5", Workspace = 5 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+6", Workspace = 6 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+7", Workspace = 7 },
        new() { Action = HotkeyActionKind.SwitchWorkspace, Gesture = "Win+8", Workspace = 8 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+1", Workspace = 1 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+2", Workspace = 2 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+3", Workspace = 3 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+4", Workspace = 4 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+5", Workspace = 5 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+6", Workspace = 6 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+7", Workspace = 7 },
        new() { Action = HotkeyActionKind.MoveWindowToWorkspace, Gesture = "Win+Shift+8", Workspace = 8 }
    ];
}

public sealed class HotkeyBindingConfig
{
    public HotkeyActionKind Action { get; init; }

    public string Gesture { get; init; } = string.Empty;

    public int? Workspace { get; init; }

    public WindowDirection? Direction { get; init; }

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            if (Workspace is int workspace)
            {
                return $"{Action}:{workspace} ({Gesture})";
            }

            if (Direction is WindowDirection direction)
            {
                return $"{Action}:{direction} ({Gesture})";
            }

            return $"{Action} ({Gesture})";
        }
    }
}

public sealed class GameModeConfig
{
    public bool AutoEnable { get; init; } = true;

    public bool ReduceEffects { get; init; } = true;

    public List<string> KnownGameProcesses { get; init; } =
    [
        "League of Legends",
        "VALORANT-Win64-Shipping",
        "FortniteClient-Win64-Shipping",
        "RocketLeague",
        "cs2",
        "dota2"
    ];

    public List<string> CenteredLauncherProcesses { get; init; } =
    [
        "LeagueClient",
        "LeagueClientUx",
        "LeagueClientUxRender",
        "RiotClientServices",
        "RiotClientUx"
    ];
}

public sealed class ConfigLoadResult
{
    public required AppConfig Config { get; init; }

    public required string ConfigPath { get; init; }

    public bool UsedDefaults { get; init; }

    public List<string> Warnings { get; init; } = [];
}
