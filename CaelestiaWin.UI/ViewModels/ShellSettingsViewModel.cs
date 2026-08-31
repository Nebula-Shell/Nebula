using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;
using CaelestiaWin.UI.Views;
using Microsoft.Win32;

namespace CaelestiaWin.UI.ViewModels;

public sealed class ShellSettingsViewModel : ObservableObjectBase
{
    private const int AutoSaveDelayMs = 500;
    private const int AutoRefreshDelayMs = 45000;
    private static readonly Regex WallpaperResolutionSuffixPattern = new(@"_(\d{3,5})x(\d{3,5})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IAppStateService _appStateService;
    private readonly IConfigurationService _configurationService;
    private readonly IThemeManager _themeManager;
    private readonly IWindowsAccentColorService _windowsAccentColorService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IStartupService _startupService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IWindowLayoutService _windowLayoutService;
    private readonly ICurrentProcessService _currentProcessService;
    private readonly ISystemStatusService _systemStatusService;
    private readonly IMonitorService _monitorService;
    private readonly IGameModeService _gameModeService;
    private readonly IDiagnosticLogService _logService;
    private string _selectedAccentColor = string.Empty;
    private AccentPaletteOptionViewModel? _selectedAccentPaletteOption;
    private string _secondaryAccentColor = string.Empty;
    private string _foregroundColor = string.Empty;
    private string _mutedForegroundColor = string.Empty;
    private string _panelColor = string.Empty;
    private string _backgroundColor = string.Empty;
    private string _wallpaperPath = string.Empty;
    private bool _showDesktopDecorations = true;
    private bool _keepWindowsAccentSeparate = true;
    private bool _tintShellSurfacesWithAccent = true;
    private string _windowsAccentColor = string.Empty;
    private bool _startOnLogin;
    private bool _sessionRestoreEnabled;
    private bool _relaunchAppsOnRestore;
    private WindowTilingStrategyKind _tilingStrategy = WindowTilingStrategyKind.Grid;
    private bool _useRoundedFocusOutline;
    private int _focusOutlineOffset;
    private int _focusOutlineThickness;
    private int _layoutGap;
    private int _outerMargin;
    private int _topReservedSpace;
    private double _panelOpacity;
    private double _backgroundOpacity;
    private bool _enableShadows = true;
    private bool _enableTransparency = true;
    private int _animationDesiredFrameRate = 45;
    private TerminalProfileKind _defaultTerminal = TerminalProfileKind.Nebula;
    private FileExplorerProfileKind _defaultFileExplorer = FileExplorerProfileKind.WindowsExplorer;
    private string _customTerminalPath = string.Empty;
    private bool _terminalFollowShellTransparency = true;
    private double _terminalOpacity = 0.86d;
    private LauncherDisplayModeKind _launcherDisplayMode = LauncherDisplayModeKind.Grid;
    private ControlCenterInputPlacementKind _controlCenterInputPlacement = ControlCenterInputPlacementKind.Auto;
    private ShellBarLayoutKind _shellBarLayout = ShellBarLayoutKind.Top;
    private bool _showMediaPill = true;
    private bool _showPomodoroPill = true;
    private bool _minimalModeTitles;
    private bool _enableNotificationSounds = true;
    private string _customNotificationSoundPath = string.Empty;
    private NotificationToastPositionKind _shellToastPosition = NotificationToastPositionKind.TopRight;
    private bool _gameModeAutoEnable = true;
    private bool _gameModeReduceEffects = true;
    private string _newKnownGameProcess = string.Empty;
    private string _newCenteredLauncherProcess = string.Empty;
    private string _wifiStatusText = string.Empty;
    private string _soundStatusText = string.Empty;
    private string _wifiPassword = string.Empty;
    private WifiNetworkModel? _selectedWifiNetwork;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isReloading;
    private bool _isPersisting;
    private bool _persistAgainAfterCurrentSave;
    private bool _isRefreshingWifi;
    private bool _isRefreshingAudio;
    private string _cpuSummary = "Loading CPU details...";
    private string _memoryDetails = "Loading memory details...";
    private string _gpuSummary = "Loading GPU details...";
    private string _vramDetails = "Loading VRAM details...";
    private string _storageDetails = "Loading storage details...";
    private string _windowsVersionDetails = "Loading Windows details...";
    private string _systemArchitectureDetails = "Loading architecture details...";
    private string _deviceIdentitySummary = "Loading device details...";

    public ShellSettingsViewModel(
        IAppStateService appStateService,
        IConfigurationService configurationService,
        IThemeManager themeManager,
        IWindowsAccentColorService windowsAccentColorService,
        IWallpaperService wallpaperService,
        IStartupService startupService,
        IGlobalHotkeyService globalHotkeyService,
        IWindowLayoutService windowLayoutService,
        ICurrentProcessService currentProcessService,
        ISystemStatusService systemStatusService,
        IMonitorService monitorService,
        IGameModeService gameModeService,
        IDiagnosticLogService logService)
    {
        _appStateService = appStateService;
        _configurationService = configurationService;
        _themeManager = themeManager;
        _windowsAccentColorService = windowsAccentColorService;
        _wallpaperService = wallpaperService;
        _startupService = startupService;
        _globalHotkeyService = globalHotkeyService;
        _windowLayoutService = windowLayoutService;
        _currentProcessService = currentProcessService;
        _systemStatusService = systemStatusService;
        _monitorService = monitorService;
        _gameModeService = gameModeService;
        _logService = logService;

        AccentPaletteOptions = new ObservableCollection<AccentPaletteOptionViewModel>();
        RecentWallpaperOptions = new ObservableCollection<WallpaperOptionViewModel>();
        DefaultWallpaperOptions = new ObservableCollection<WallpaperOptionViewModel>();
        SaveCommand = new AsyncRelayCommand(PersistSettingsAsync);
        CloseCommand = new AsyncRelayCommand(CloseAsync);
        PickColorCommand = new RelayCommand<string>(PickColor, target => !string.IsNullOrWhiteSpace(target));
        UseWindowsAccentCommand = new RelayCommand(UseWindowsAccent);
        SelectWallpaperCommand = new RelayCommand(SelectWallpaper);
        UseWindowsWallpaperCommand = new RelayCommand(UseWindowsWallpaper);
        ApplyWallpaperOptionCommand = new RelayCommand<WallpaperOptionViewModel>(ApplyWallpaperOption, option => option is not null);
        SelectCustomTerminalCommand = new RelayCommand(SelectCustomTerminal);
        SelectCustomNotificationSoundCommand = new RelayCommand(SelectCustomNotificationSound);
        PreviewNotificationSoundCommand = new RelayCommand(PreviewNotificationSound, () => !string.IsNullOrWhiteSpace(CustomNotificationSoundPath));
        RefreshWifiCommand = new AsyncRelayCommand(RefreshWifiNetworksAsync);
        ConnectToWifiCommand = new AsyncRelayCommand<WifiNetworkModel>(ConnectToWifiAsync);
        SubmitWifiPasswordCommand = new AsyncRelayCommand(SubmitWifiPasswordAsync, () => SelectedWifiNetwork is not null);
        CancelWifiPasswordCommand = new RelayCommand(CancelWifiPasswordPrompt);
        ToggleWifiCommand = new RelayCommand(_systemStatusService.ToggleWifi);
        RefreshAudioDevicesCommand = new AsyncRelayCommand(RefreshAudioDevicesAsync);
        SelectOutputDeviceCommand = new AsyncRelayCommand<AudioDeviceModel>(device => SelectAudioDeviceAsync(device, AudioDeviceKind.Output));
        SelectInputDeviceCommand = new AsyncRelayCommand<AudioDeviceModel>(device => SelectAudioDeviceAsync(device, AudioDeviceKind.Input));
        ToggleMuteCommand = new RelayCommand(_systemStatusService.ToggleMute);
        AddKnownGameProcessCommand = new RelayCommand(AddKnownGameProcess);
        RemoveKnownGameProcessCommand = new RelayCommand<string>(RemoveKnownGameProcess, value => !string.IsNullOrWhiteSpace(value));
        AddCenteredLauncherProcessCommand = new RelayCommand(AddCenteredLauncherProcess);
        RemoveCenteredLauncherProcessCommand = new RelayCommand<string>(RemoveCenteredLauncherProcess, value => !string.IsNullOrWhiteSpace(value));
        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMs)
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _ = PersistSettingsAsync();
        };
        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(AutoRefreshDelayMs)
        };
        _autoRefreshTimer.Tick += (_, _) => _ = RefreshLiveSettingsAsync();

        _systemStatusService.CurrentStatus.PropertyChanged += OnSystemStatusPropertyChanged;
        _gameModeService.PropertyChanged += OnGameModePropertyChanged;
        ReloadFromConfig();
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<AccentPaletteOptionViewModel> AccentPaletteOptions { get; }

    public ObservableCollection<WallpaperOptionViewModel> RecentWallpaperOptions { get; }

    public ObservableCollection<WallpaperOptionViewModel> DefaultWallpaperOptions { get; }

    public ObservableCollection<HotkeyBindingEditorViewModel> ShortcutBindings { get; } = [];

    public ObservableCollection<HotkeyBindingEditorViewModel> FunctionKeyBindings { get; } = [];

    public ObservableCollection<WifiNetworkModel> AvailableWifiNetworks { get; } = [];

    public ObservableCollection<AudioDeviceModel> OutputAudioDevices { get; } = [];

    public ObservableCollection<AudioDeviceModel> InputAudioDevices { get; } = [];

    public ObservableCollection<string> KnownGameProcesses { get; } = [];

    public ObservableCollection<string> CenteredLauncherProcesses { get; } = [];

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand PickColorCommand { get; }

    public ICommand UseWindowsAccentCommand { get; }

    public ICommand SelectWallpaperCommand { get; }

    public ICommand UseWindowsWallpaperCommand { get; }

    public ICommand ApplyWallpaperOptionCommand { get; }

    public ICommand SelectCustomTerminalCommand { get; }
    public ICommand SelectCustomNotificationSoundCommand { get; }
    public ICommand PreviewNotificationSoundCommand { get; }

    public ICommand RefreshWifiCommand { get; }

    public ICommand ConnectToWifiCommand { get; }

    public ICommand SubmitWifiPasswordCommand { get; }

    public ICommand CancelWifiPasswordCommand { get; }

    public ICommand ToggleWifiCommand { get; }

    public ICommand RefreshAudioDevicesCommand { get; }

    public ICommand SelectOutputDeviceCommand { get; }

    public ICommand SelectInputDeviceCommand { get; }

    public ICommand ToggleMuteCommand { get; }

    public ICommand AddKnownGameProcessCommand { get; }

    public ICommand RemoveKnownGameProcessCommand { get; }

    public ICommand AddCenteredLauncherProcessCommand { get; }

    public ICommand RemoveCenteredLauncherProcessCommand { get; }

    public string SelectedAccentColor
    {
        get => _selectedAccentColor;
        set
        {
            if (SetProperty(ref _selectedAccentColor, value))
            {
                var matchingPalette = AccentPaletteOptions.FirstOrDefault(option =>
                    string.Equals(option.AccentColor, value, StringComparison.OrdinalIgnoreCase));
                if (!ReferenceEquals(_selectedAccentPaletteOption, matchingPalette))
                {
                    _selectedAccentPaletteOption = matchingPalette;
                    OnPropertyChanged(nameof(SelectedAccentPaletteOption));
                }

                PreviewThemeAccent();
                QueueAutoSave();
            }
        }
    }

    public AccentPaletteOptionViewModel? SelectedAccentPaletteOption
    {
        get => _selectedAccentPaletteOption;
        set
        {
            if (!SetProperty(ref _selectedAccentPaletteOption, value) || value is null)
            {
                return;
            }

            SelectedAccentColor = value.AccentColor;
            SecondaryAccentColor = value.SecondaryAccentColor;
        }
    }

    public string SecondaryAccentColor
    {
        get => _secondaryAccentColor;
        set
        {
            if (SetProperty(ref _secondaryAccentColor, value))
            {
                PreviewThemeAccent();
                QueueAutoSave();
            }
        }
    }

    public string ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            if (SetProperty(ref _foregroundColor, value))
            {
                QueueAutoSave();
            }
        }
    }

    public string MutedForegroundColor
    {
        get => _mutedForegroundColor;
        set
        {
            if (SetProperty(ref _mutedForegroundColor, value))
            {
                QueueAutoSave();
            }
        }
    }

    public string PanelColor
    {
        get => _panelColor;
        set
        {
            if (SetProperty(ref _panelColor, value))
            {
                QueueAutoSave();
            }
        }
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value))
            {
                QueueAutoSave();
            }
        }
    }

    public string WallpaperPath
    {
        get => _wallpaperPath;
        set
        {
            if (!SetProperty(ref _wallpaperPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(WallpaperSummary));
            QueueAutoSave();
        }
    }

    public string WallpaperSummary => string.IsNullOrWhiteSpace(WallpaperPath)
        ? "Using current Windows wallpaper"
        : File.Exists(WallpaperPath)
            ? WallpaperPath
            : "Selected wallpaper file is missing";

    public bool ShowDesktopDecorations
    {
        get => _showDesktopDecorations;
        set
        {
            if (SetProperty(ref _showDesktopDecorations, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool KeepWindowsAccentSeparate
    {
        get => _keepWindowsAccentSeparate;
        set
        {
            if (!SetProperty(ref _keepWindowsAccentSeparate, value))
            {
                return;
            }

            if (!value)
            {
                PreviewThemeAccent();
            }

            QueueAutoSave();
        }
    }

    public bool TintShellSurfacesWithAccent
    {
        get => _tintShellSurfacesWithAccent;
        set
        {
            if (!SetProperty(ref _tintShellSurfacesWithAccent, value))
            {
                return;
            }

            PreviewThemeAccent();
            QueueAutoSave();
        }
    }

    public string WindowsAccentColor
    {
        get => _windowsAccentColor;
        private set
        {
            if (SetProperty(ref _windowsAccentColor, value))
            {
                OnPropertyChanged(nameof(WindowsAccentSummary));
            }
        }
    }

    public string WindowsAccentSummary => string.IsNullOrWhiteSpace(WindowsAccentColor)
        ? "Windows accent unavailable"
        : $"Windows accent {WindowsAccentColor}";

    public bool StartOnLogin
    {
        get => _startOnLogin;
        set
        {
            if (SetProperty(ref _startOnLogin, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool SessionRestoreEnabled
    {
        get => _sessionRestoreEnabled;
        set
        {
            if (SetProperty(ref _sessionRestoreEnabled, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool RelaunchAppsOnRestore
    {
        get => _relaunchAppsOnRestore;
        set
        {
            if (SetProperty(ref _relaunchAppsOnRestore, value))
            {
                QueueAutoSave();
            }
        }
    }

    public WindowTilingStrategyKind TilingStrategy
    {
        get => _tilingStrategy;
        set
        {
            if (!SetProperty(ref _tilingStrategy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseGridTiling));
            OnPropertyChanged(nameof(UseGoldenRatioTiling));
            QueueAutoSave();
        }
    }

    public bool UseGridTiling
    {
        get => TilingStrategy == WindowTilingStrategyKind.Grid;
        set
        {
            if (value)
            {
                TilingStrategy = WindowTilingStrategyKind.Grid;
            }
        }
    }

    public bool UseGoldenRatioTiling
    {
        get => TilingStrategy == WindowTilingStrategyKind.GoldenRatio;
        set
        {
            if (value)
            {
                TilingStrategy = WindowTilingStrategyKind.GoldenRatio;
            }
        }
    }

    public bool UseRoundedFocusOutline
    {
        get => _useRoundedFocusOutline;
        set
        {
            if (SetProperty(ref _useRoundedFocusOutline, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int FocusOutlineOffset
    {
        get => _focusOutlineOffset;
        set
        {
            if (SetProperty(ref _focusOutlineOffset, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int FocusOutlineThickness
    {
        get => _focusOutlineThickness;
        set
        {
            if (SetProperty(ref _focusOutlineThickness, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int LayoutGap
    {
        get => _layoutGap;
        set
        {
            if (SetProperty(ref _layoutGap, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int OuterMargin
    {
        get => _outerMargin;
        set
        {
            if (SetProperty(ref _outerMargin, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int TopReservedSpace
    {
        get => _topReservedSpace;
        set
        {
            if (SetProperty(ref _topReservedSpace, value))
            {
                QueueAutoSave();
            }
        }
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set
        {
            if (SetProperty(ref _panelOpacity, value))
            {
                QueueAutoSave();
            }
        }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            if (SetProperty(ref _backgroundOpacity, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool EnableShadows
    {
        get => _enableShadows;
        set
        {
            if (SetProperty(ref _enableShadows, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool EnableTransparency
    {
        get => _enableTransparency;
        set
        {
            if (SetProperty(ref _enableTransparency, value))
            {
                QueueAutoSave();
            }
        }
    }

    public int AnimationDesiredFrameRate
    {
        get => _animationDesiredFrameRate;
        set
        {
            if (SetProperty(ref _animationDesiredFrameRate, value))
            {
                QueueAutoSave();
            }
        }
    }

    public TerminalProfileKind DefaultTerminal
    {
        get => _defaultTerminal;
        set
        {
            if (!SetProperty(ref _defaultTerminal, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseNebulaTerminal));
            OnPropertyChanged(nameof(UseWindowsTerminal));
            OnPropertyChanged(nameof(UsePowerShellTerminal));
            OnPropertyChanged(nameof(UseCommandPromptTerminal));
            OnPropertyChanged(nameof(UseCustomTerminal));
            QueueAutoSave();
        }
    }

    public bool UseNebulaTerminal
    {
        get => DefaultTerminal == TerminalProfileKind.Nebula;
        set
        {
            if (value)
            {
                DefaultTerminal = TerminalProfileKind.Nebula;
            }
        }
    }

    public bool UseWindowsTerminal
    {
        get => DefaultTerminal == TerminalProfileKind.WindowsTerminal;
        set
        {
            if (value)
            {
                DefaultTerminal = TerminalProfileKind.WindowsTerminal;
            }
        }
    }

    public bool UsePowerShellTerminal
    {
        get => DefaultTerminal == TerminalProfileKind.PowerShell;
        set
        {
            if (value)
            {
                DefaultTerminal = TerminalProfileKind.PowerShell;
            }
        }
    }

    public bool UseCommandPromptTerminal
    {
        get => DefaultTerminal == TerminalProfileKind.CommandPrompt;
        set
        {
            if (value)
            {
                DefaultTerminal = TerminalProfileKind.CommandPrompt;
            }
        }
    }

    public bool UseCustomTerminal
    {
        get => DefaultTerminal == TerminalProfileKind.Custom;
        set
        {
            if (value)
            {
                DefaultTerminal = TerminalProfileKind.Custom;
            }
        }
    }

    public string CustomTerminalPath
    {
        get => _customTerminalPath;
        set
        {
            if (SetProperty(ref _customTerminalPath, value))
            {
                QueueAutoSave();
            }
        }
    }

    public FileExplorerProfileKind DefaultFileExplorer
    {
        get => _defaultFileExplorer;
        set
        {
            if (!SetProperty(ref _defaultFileExplorer, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseWindowsFileExplorer));
            OnPropertyChanged(nameof(UseNebulaFileExplorer));
            QueueAutoSave();
        }
    }

    public bool UseWindowsFileExplorer
    {
        get => DefaultFileExplorer == FileExplorerProfileKind.WindowsExplorer;
        set
        {
            if (value)
            {
                DefaultFileExplorer = FileExplorerProfileKind.WindowsExplorer;
            }
        }
    }

    public bool UseNebulaFileExplorer
    {
        get => DefaultFileExplorer == FileExplorerProfileKind.Nebula;
        set
        {
            if (value)
            {
                DefaultFileExplorer = FileExplorerProfileKind.Nebula;
            }
        }
    }

    public bool TerminalFollowShellTransparency
    {
        get => _terminalFollowShellTransparency;
        set
        {
            if (SetProperty(ref _terminalFollowShellTransparency, value))
            {
                OnPropertyChanged(nameof(IsTerminalOpacityCustom));
                QueueAutoSave();
            }
        }
    }

    public bool IsTerminalOpacityCustom => !TerminalFollowShellTransparency;

    public double TerminalOpacity
    {
        get => _terminalOpacity;
        set
        {
            if (SetProperty(ref _terminalOpacity, value))
            {
                QueueAutoSave();
            }
        }
    }

    public ControlCenterInputPlacementKind ControlCenterInputPlacement
    {
        get => _controlCenterInputPlacement;
        set
        {
            if (!SetProperty(ref _controlCenterInputPlacement, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseInputAutoPlacement));
            OnPropertyChanged(nameof(UseInputConnectivityPlacement));
            OnPropertyChanged(nameof(UseInputBottomPlacement));
            QueueAutoSave();
        }
    }

    public ShellBarLayoutKind ShellBarLayout
    {
        get => _shellBarLayout;
        set
        {
            if (!SetProperty(ref _shellBarLayout, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseTopBarLayout));
            OnPropertyChanged(nameof(UseLeftBarLayout));
            QueueAutoSave();
        }
    }

    public bool ShowMediaPill
    {
        get => _showMediaPill;
        set
        {
            if (SetProperty(ref _showMediaPill, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool MinimalModeTitles
    {
        get => _minimalModeTitles;
        set
        {
            if (SetProperty(ref _minimalModeTitles, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool ShowPomodoroPill
    {
        get => _showPomodoroPill;
        set
        {
            if (SetProperty(ref _showPomodoroPill, value))
            {
                QueueAutoSave();
            }
        }
    }

    public bool UseTopBarLayout
    {
        get => ShellBarLayout == ShellBarLayoutKind.Top;
        set
        {
            if (value)
            {
                ShellBarLayout = ShellBarLayoutKind.Top;
            }
        }
    }

    public bool UseLeftBarLayout
    {
        get => ShellBarLayout == ShellBarLayoutKind.Left;
        set
        {
            if (value)
            {
                ShellBarLayout = ShellBarLayoutKind.Left;
            }
        }
    }

    public bool UseInputAutoPlacement
    {
        get => ControlCenterInputPlacement == ControlCenterInputPlacementKind.Auto;
        set
        {
            if (value)
            {
                ControlCenterInputPlacement = ControlCenterInputPlacementKind.Auto;
            }
        }
    }

    public bool UseInputConnectivityPlacement
    {
        get => ControlCenterInputPlacement == ControlCenterInputPlacementKind.ConnectivityCard;
        set
        {
            if (value)
            {
                ControlCenterInputPlacement = ControlCenterInputPlacementKind.ConnectivityCard;
            }
        }
    }

    public bool UseInputBottomPlacement
    {
        get => ControlCenterInputPlacement == ControlCenterInputPlacementKind.BottomRow;
        set
        {
            if (value)
            {
                ControlCenterInputPlacement = ControlCenterInputPlacementKind.BottomRow;
            }
        }
    }

    public LauncherDisplayModeKind LauncherDisplayMode
    {
        get => _launcherDisplayMode;
        set
        {
            if (!SetProperty(ref _launcherDisplayMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseLauncherGridDisplay));
            OnPropertyChanged(nameof(UseLauncherListDisplay));
            ApplyLauncherDisplayModeLive(value);
            QueueAutoSave();
        }
    }

    public bool UseLauncherGridDisplay
    {
        get => LauncherDisplayMode == LauncherDisplayModeKind.Grid;
        set
        {
            if (value)
            {
                LauncherDisplayMode = LauncherDisplayModeKind.Grid;
            }
        }
    }

    public bool UseLauncherListDisplay
    {
        get => LauncherDisplayMode == LauncherDisplayModeKind.List;
        set
        {
            if (value)
            {
                LauncherDisplayMode = LauncherDisplayModeKind.List;
            }
        }
    }

    public bool EnableNotificationSounds
    {
        get => _enableNotificationSounds;
        set
        {
            if (!SetProperty(ref _enableNotificationSounds, value))
            {
                return;
            }

            QueueAutoSave();
        }
    }

    public string CustomNotificationSoundPath
    {
        get => _customNotificationSoundPath;
        set
        {
            if (!SetProperty(ref _customNotificationSoundPath, value))
            {
                return;
            }

            if (PreviewNotificationSoundCommand is RelayCommand cmd)
            {
                cmd.NotifyCanExecuteChanged();
            }

            QueueAutoSave();
        }
    }

    public NotificationToastPositionKind ShellToastPosition
    {
        get => _shellToastPosition;
        set
        {
            if (!SetProperty(ref _shellToastPosition, value))
            {
                return;
            }

            OnPropertyChanged(nameof(UseToastTopRight));
            OnPropertyChanged(nameof(UseToastTopLeft));
            OnPropertyChanged(nameof(UseToastBottomRight));
            OnPropertyChanged(nameof(UseToastBottomLeft));
            QueueAutoSave();
        }
    }

    public bool GameModeAutoEnable
    {
        get => _gameModeAutoEnable;
        set
        {
            if (!SetProperty(ref _gameModeAutoEnable, value))
            {
                return;
            }

            _gameModeService.SetAutoEnabled(value);
            QueueAutoSave();
            OnPropertyChanged(nameof(GameModeStateSummary));
        }
    }

    public bool GameModeReduceEffects
    {
        get => _gameModeReduceEffects;
        set
        {
            if (SetProperty(ref _gameModeReduceEffects, value))
            {
                QueueAutoSave();
            }
        }
    }

    public string NewKnownGameProcess
    {
        get => _newKnownGameProcess;
        set => SetProperty(ref _newKnownGameProcess, value);
    }

    public string NewCenteredLauncherProcess
    {
        get => _newCenteredLauncherProcess;
        set => SetProperty(ref _newCenteredLauncherProcess, value);
    }

    public bool IsGameModeEffective => _gameModeService.IsEffective;

    public bool IsGameRunning => _gameModeService.IsGameRunning;

    public bool IsFullscreenGameRunning => _gameModeService.IsFullscreenGameRunning;

    public string GameModeStatusText => _gameModeService.ActiveGameName;

    public string GameModeStateSummary => _gameModeService.IsEffective
        ? "Reduced shell effects are active for the current game."
        : GameModeAutoEnable
            ? "Game mode will engage automatically when Nebula detects a game."
            : "Game mode is paused until you enable it again.";

    public bool UseToastTopRight
    {
        get => ShellToastPosition == NotificationToastPositionKind.TopRight;
        set
        {
            if (value)
            {
                ShellToastPosition = NotificationToastPositionKind.TopRight;
            }
        }
    }

    public bool UseToastTopLeft
    {
        get => ShellToastPosition == NotificationToastPositionKind.TopLeft;
        set
        {
            if (value)
            {
                ShellToastPosition = NotificationToastPositionKind.TopLeft;
            }
        }
    }

    public bool UseToastBottomRight
    {
        get => ShellToastPosition == NotificationToastPositionKind.BottomRight;
        set
        {
            if (value)
            {
                ShellToastPosition = NotificationToastPositionKind.BottomRight;
            }
        }
    }

    public bool UseToastBottomLeft
    {
        get => ShellToastPosition == NotificationToastPositionKind.BottomLeft;
        set
        {
            if (value)
            {
                ShellToastPosition = NotificationToastPositionKind.BottomLeft;
            }
        }
    }

    public bool WifiEnabled => _systemStatusService.CurrentStatus.WifiEnabled;

    public string ActiveNetworkName => _systemStatusService.CurrentStatus.ActiveNetworkName;

    public string NetworkSummary => _systemStatusService.CurrentStatus.NetworkSummary;

    public double VolumePercent
    {
        get => _systemStatusService.CurrentStatus.VolumePercent;
        set
        {
            if (Math.Abs(_systemStatusService.CurrentStatus.VolumePercent - value) < 0.5d)
            {
                return;
            }

            _systemStatusService.SetVolume(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterVolumeSummary));
        }
    }

    public bool IsMuted => _systemStatusService.CurrentStatus.IsMuted;

    public string MuteSummary => IsMuted ? "Muted" : "Audible";

    public string MasterVolumeSummary => $"{Math.Round(VolumePercent, 0)}%";

    public string SoundStatusText
    {
        get => _soundStatusText;
        private set => SetProperty(ref _soundStatusText, value);
    }

    public string BatterySummary => _systemStatusService.CurrentStatus.IsBatteryPresent
        ? _systemStatusService.CurrentStatus.BatteryPercent is int percent
            ? $"{percent}%"
            : "Battery present"
        : "No battery detected";

    public string BluetoothSummary => _systemStatusService.CurrentStatus.BluetoothAvailable
        ? _systemStatusService.CurrentStatus.BluetoothEnabled ? "Available and enabled" : "Available"
        : "No Bluetooth radio detected";

    public string MachineSummary => $"{Environment.MachineName}\\{Environment.UserName}";

    public string OperatingSystemSummary => RuntimeInformation.OSDescription.Trim();

    public string FrameworkSummary => RuntimeInformation.FrameworkDescription;

    public string ArchitectureSummary => $"{RuntimeInformation.ProcessArchitecture} process";

    public string ProcessorSummary => $"{Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)} logical processors";

    public string WorkingSetSummary => $"{(Environment.WorkingSet / 1024d / 1024d).ToString("F0", CultureInfo.InvariantCulture)} MB";

    public string MonitorSummary
    {
        get
        {
            try
            {
                var monitors = _monitorService.GetMonitors();
                var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
                if (primary is null)
                {
                    return monitors.Count == 0
                        ? "No monitors reported"
                        : $"{monitors.Count.ToString(CultureInfo.InvariantCulture)} monitor(s)";
                }

                return $"{monitors.Count.ToString(CultureInfo.InvariantCulture)} monitor(s), primary {primary.Bounds.Width}x{primary.Bounds.Height}";
            }
            catch (Exception exception)
            {
                _logService.Warn("Failed to read monitor summary for settings.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });

                return "Monitor details unavailable";
            }
        }
    }

    public string WifiStatusText
    {
        get => _wifiStatusText;
        private set => SetProperty(ref _wifiStatusText, value);
    }

    public string CpuSummary
    {
        get => _cpuSummary;
        private set => SetProperty(ref _cpuSummary, value);
    }

    public string MemoryDetails
    {
        get => _memoryDetails;
        private set => SetProperty(ref _memoryDetails, value);
    }

    public string GpuSummary
    {
        get => _gpuSummary;
        private set => SetProperty(ref _gpuSummary, value);
    }

    public string VramDetails
    {
        get => _vramDetails;
        private set => SetProperty(ref _vramDetails, value);
    }

    public string StorageDetails
    {
        get => _storageDetails;
        private set => SetProperty(ref _storageDetails, value);
    }

    public string WindowsVersionDetails
    {
        get => _windowsVersionDetails;
        private set => SetProperty(ref _windowsVersionDetails, value);
    }

    public string SystemArchitectureDetails
    {
        get => _systemArchitectureDetails;
        private set => SetProperty(ref _systemArchitectureDetails, value);
    }

    public string DeviceIdentitySummary
    {
        get => _deviceIdentitySummary;
        private set => SetProperty(ref _deviceIdentitySummary, value);
    }

    public string WifiPassword
    {
        get => _wifiPassword;
        set => SetProperty(ref _wifiPassword, value);
    }

    public WifiNetworkModel? SelectedWifiNetwork
    {
        get => _selectedWifiNetwork;
        private set
        {
            if (SetProperty(ref _selectedWifiNetwork, value) && SubmitWifiPasswordCommand is AsyncRelayCommand command)
            {
                command.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsWifiPasswordPromptVisible => SelectedWifiNetwork is not null;

    public void BeginAutoRefresh()
    {
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    public void EndAutoRefresh()
    {
        _autoRefreshTimer.Stop();
    }

    public void ReloadFromConfig()
    {
        _isReloading = true;
        try
        {
            CancelPendingAutoSave();
            var config = _appStateService.Config;
            WindowsAccentColor = _windowsAccentColorService.TryGetCurrentAccentColor() ?? string.Empty;
            RebuildAccentPaletteOptions(config);
            KeepWindowsAccentSeparate = config.Theme.KeepWindowsAccentSeparate;
            TintShellSurfacesWithAccent = config.Theme.TintShellSurfacesWithAccent;
            SelectedAccentColor = config.Theme.AccentColor;
            SecondaryAccentColor = config.Theme.SecondaryAccentColor;
            ForegroundColor = config.Theme.ForegroundColor;
            MutedForegroundColor = config.Theme.MutedForegroundColor;
            PanelColor = config.Theme.PanelColor;
            BackgroundColor = config.Theme.BackgroundColor;
            WallpaperPath = config.Theme.WallpaperPath ?? string.Empty;
            RebuildWallpaperOptions(config);
            ShowDesktopDecorations = config.Theme.ShowDesktopDecorations;
            StartOnLogin = config.Startup.StartOnLogin || config.Startup.EnableAutoStart;
            SessionRestoreEnabled = config.Session.SessionRestoreEnabled;
            RelaunchAppsOnRestore = config.Session.RelaunchAppsOnRestore;
            TilingStrategy = config.Windowing.TilingStrategy;
            UseRoundedFocusOutline = config.Windowing.UseRoundedFocusOutline;
            FocusOutlineOffset = config.Windowing.FocusOutlineOffset;
            FocusOutlineThickness = config.Windowing.FocusOutlineThickness;
            LayoutGap = config.Windowing.LayoutGap;
            OuterMargin = config.Windowing.OuterMargin;
            TopReservedSpace = config.Windowing.TopReservedSpace;
            PanelOpacity = config.Theme.PanelOpacity;
            BackgroundOpacity = config.Theme.BackgroundOpacity;
            EnableShadows = config.Theme.EnableShadows;
            EnableTransparency = config.Theme.EnableTransparency;
            AnimationDesiredFrameRate = config.Animations.DesiredFrameRate;
            DefaultTerminal = config.Launcher.DefaultTerminal;
            DefaultFileExplorer = config.Launcher.DefaultFileExplorer;
            CustomTerminalPath = config.Launcher.CustomTerminalPath;
            TerminalFollowShellTransparency = config.Terminal.FollowShellTransparency;
            TerminalOpacity = config.Terminal.Opacity;
            LauncherDisplayMode = config.Launcher.DisplayMode;
            ControlCenterInputPlacement = config.ControlCenter.InputWidgetPlacement;
            ShellBarLayout = config.ControlCenter.BarLayout;
            ShowMediaPill = config.ControlCenter.ShowMediaPill;
            ShowPomodoroPill = config.ControlCenter.ShowPomodoroPill;
            MinimalModeTitles = config.ControlCenter.MinimalModeTitles;
            EnableNotificationSounds = config.Notifications.EnableNotificationSounds;
            CustomNotificationSoundPath = config.Notifications.CustomSoundPath ?? string.Empty;
            ShellToastPosition = config.Notifications.ShellToastPosition;
            GameModeAutoEnable = config.GameMode.AutoEnable;
            GameModeReduceEffects = config.GameMode.ReduceEffects;
            KnownGameProcesses.Clear();
            foreach (var process in config.GameMode.KnownGameProcesses.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                KnownGameProcesses.Add(process.Trim());
            }

            CenteredLauncherProcesses.Clear();
            foreach (var process in config.GameMode.CenteredLauncherProcesses.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                CenteredLauncherProcesses.Add(process.Trim());
            }

            foreach (var binding in ShortcutBindings)
            {
                binding.PropertyChanged -= OnShortcutBindingPropertyChanged;
            }

            foreach (var binding in FunctionKeyBindings)
            {
                binding.PropertyChanged -= OnShortcutBindingPropertyChanged;
            }

            ShortcutBindings.Clear();
            FunctionKeyBindings.Clear();
            foreach (var binding in config.Hotkeys.Bindings.Select(binding => new HotkeyBindingEditorViewModel(binding)))
            {
                binding.PropertyChanged += OnShortcutBindingPropertyChanged;
                if (IsFunctionKeyBinding(binding.Action))
                {
                    FunctionKeyBindings.Add(binding);
                }
                else
                {
                    ShortcutBindings.Add(binding);
                }
            }

            SelectedAccentPaletteOption = AccentPaletteOptions.FirstOrDefault(option =>
                string.Equals(option.AccentColor, SelectedAccentColor, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isReloading = false;
        }

        _ = RefreshLiveSettingsAsync();
    }

    private async Task CloseAsync()
    {
        CancelPendingAutoSave();
        await PersistSettingsAsync();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task PersistSettingsAsync()
    {
        if (_isReloading)
        {
            return;
        }

        if (_isPersisting)
        {
            _persistAgainAfterCurrentSave = true;
            return;
        }

        _isPersisting = true;
        try
        {
            var current = _appStateService.Config;
            var updatedConfig = new AppConfig
            {
                Theme = new ThemeConfig
                {
                    KeepWindowsAccentSeparate = KeepWindowsAccentSeparate,
                    TintShellSurfacesWithAccent = TintShellSurfacesWithAccent,
                    AccentColor = string.IsNullOrWhiteSpace(SelectedAccentColor) ? current.Theme.AccentColor : SelectedAccentColor,
                    SecondaryAccentColor = string.IsNullOrWhiteSpace(SecondaryAccentColor) ? current.Theme.SecondaryAccentColor : SecondaryAccentColor,
                    ForegroundColor = string.IsNullOrWhiteSpace(ForegroundColor) ? current.Theme.ForegroundColor : ForegroundColor,
                    MutedForegroundColor = string.IsNullOrWhiteSpace(MutedForegroundColor) ? current.Theme.MutedForegroundColor : MutedForegroundColor,
                    PanelColor = string.IsNullOrWhiteSpace(PanelColor) ? current.Theme.PanelColor : PanelColor,
                    BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? current.Theme.BackgroundColor : BackgroundColor,
                    WallpaperPath = string.IsNullOrWhiteSpace(WallpaperPath) ? null : WallpaperPath.Trim(),
                    ShowDesktopDecorations = ShowDesktopDecorations,
                    RecentWallpapers = RecentWallpaperOptions.Select(option => option.Path).Take(3).ToList(),
                    AccentPalette = AccentPaletteOptions.Select(option => option.AccentColor).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.4d, 1d),
                    PanelOpacity = Math.Clamp(PanelOpacity, 0.4d, 1d),
                    EnableBackdropBlur = current.Theme.EnableBackdropBlur,
                    EnableShadows = EnableShadows,
                    EnableTransparency = EnableTransparency,
                    CornerRadius = current.Theme.CornerRadius
                },
                Logging = current.Logging,
                Animations = new AnimationConfig
                {
                    FastMs = current.Animations.FastMs,
                    NormalMs = current.Animations.NormalMs,
                    SlowMs = current.Animations.SlowMs,
                    OverlayEasing = current.Animations.OverlayEasing,
                    LauncherScaleFrom = current.Animations.LauncherScaleFrom,
                    SidePanelOffset = current.Animations.SidePanelOffset,
                    DesiredFrameRate = Math.Clamp(AnimationDesiredFrameRate, 15, 60)
                },
                Performance = current.Performance,
                ControlCenter = new ControlCenterConfig
                {
                    InputWidgetPlacement = ControlCenterInputPlacement,
                    BarLayout = ShellBarLayout,
                    ShowMediaPill = ShowMediaPill,
                    ShowPomodoroPill = ShowPomodoroPill
                    ,
                    MinimalModeTitles = MinimalModeTitles
                },
                Windowing = new WindowingConfig
                {
                    EnableSoftTiling = current.Windowing.EnableSoftTiling,
                    TilingStrategy = TilingStrategy,
                    UseRoundedFocusOutline = UseRoundedFocusOutline,
                    FocusOutlineOffset = Math.Clamp(FocusOutlineOffset, 0, 24),
                    FocusOutlineThickness = Math.Clamp(FocusOutlineThickness, 1, 12),
                    LayoutGap = Math.Clamp(LayoutGap, 0, 64),
                    OuterMargin = Math.Clamp(OuterMargin, 0, 120),
                    TopReservedSpace = Math.Clamp(TopReservedSpace, 0, 240),
                    OverviewColumns = current.Windowing.OverviewColumns
                },
                Launcher = new LauncherConfig
                {
                    DisplayMode = LauncherDisplayMode,
                    MaxResults = current.Launcher.MaxResults,
                    SearchDebounceMs = current.Launcher.SearchDebounceMs,
                    PreloadOnStartup = current.Launcher.PreloadOnStartup,
                    ClearQueryOnClose = current.Launcher.ClearQueryOnClose,
                    ShowRecentAppsOnEmptyQuery = current.Launcher.ShowRecentAppsOnEmptyQuery,
                    RecentAppLimit = current.Launcher.RecentAppLimit,
                    EnableCommandMode = current.Launcher.EnableCommandMode,
                    DefaultTerminal = DefaultTerminal,
                    DefaultFileExplorer = DefaultFileExplorer,
                    CustomTerminalPath = string.IsNullOrWhiteSpace(CustomTerminalPath) ? string.Empty : CustomTerminalPath.Trim()
                },
                Terminal = new TerminalConfig
                {
                    FollowShellTransparency = TerminalFollowShellTransparency,
                    Opacity = Math.Clamp(TerminalOpacity, 0.35d, 1d)
                },
                Notifications = new NotificationConfig
                {
                    MaxItems = current.Notifications.MaxItems,
                    EnableWindowsToasts = current.Notifications.EnableWindowsToasts,
                    EnableShellToasts = current.Notifications.EnableShellToasts,
                    EnableNotificationSounds = EnableNotificationSounds,
                    CustomSoundPath = string.IsNullOrWhiteSpace(CustomNotificationSoundPath) ? current.Notifications.CustomSoundPath : CustomNotificationSoundPath.Trim(),
                    ShellToastPosition = ShellToastPosition,
                    ShowStartupStatus = current.Notifications.ShowStartupStatus,
                    SimulateOnStartup = current.Notifications.SimulateOnStartup
                },
                Session = new SessionConfig
                {
                    SessionRestoreEnabled = SessionRestoreEnabled,
                    RelaunchAppsOnRestore = RelaunchAppsOnRestore
                },
                Startup = new StartupConfig
                {
                    ShowOnPrimaryMonitor = current.Startup.ShowOnPrimaryMonitor,
                    StartLauncherOpen = current.Startup.StartLauncherOpen,
                    StartControlCenterOpen = current.Startup.StartControlCenterOpen,
                    TrackForegroundWindow = current.Startup.TrackForegroundWindow,
                    StartOnLogin = StartOnLogin,
                    EnableAutoStart = false,
                    RestartOnCrash = current.Startup.RestartOnCrash,
                    StopExplorerOnLaunch = current.Startup.StopExplorerOnLaunch
                },
                Hotkeys = new HotkeyConfig
                {
                    Bindings = ShortcutBindings.Concat(FunctionKeyBindings).Select(binding => binding.ToConfig()).ToList()
                },
                GameMode = new GameModeConfig
                {
                    AutoEnable = GameModeAutoEnable,
                    ReduceEffects = GameModeReduceEffects,
                    KnownGameProcesses = KnownGameProcesses.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    CenteredLauncherProcesses = CenteredLauncherProcesses.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                }
            };

            _appStateService.Config = updatedConfig;
            _themeManager.ApplyTheme(updatedConfig);
            if (!KeepWindowsAccentSeparate)
            {
                _windowsAccentColorService.TrySetAccentColor(SelectedAccentColor);
                WindowsAccentColor = _windowsAccentColorService.TryGetCurrentAccentColor() ?? SelectedAccentColor;
            }
            _startupService.SetEnabled(StartOnLogin, _currentProcessService.ExecutablePath, string.Empty);
            await _configurationService.SaveAsync(updatedConfig);
            _globalHotkeyService.RegisterBindings(updatedConfig.Hotkeys.Bindings);
            _windowLayoutService.RefreshActiveWorkspaceLayout();
        }
        catch (Exception exception)
        {
            _logService.Warn("Shell settings auto-save failed.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
        finally
        {
            _isPersisting = false;
            if (_persistAgainAfterCurrentSave)
            {
                _persistAgainAfterCurrentSave = false;
                QueueAutoSave();
            }
        }
    }

    private void QueueAutoSave()
    {
        if (_isReloading)
        {
            return;
        }

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void ApplyLauncherDisplayModeLive(LauncherDisplayModeKind displayMode)
    {
        var current = _appStateService.Config;
        if (current.Launcher.DisplayMode == displayMode)
        {
            return;
        }

        _appStateService.Config = new AppConfig
        {
            Theme = current.Theme,
            Logging = current.Logging,
            Animations = current.Animations,
            Performance = current.Performance,
            ControlCenter = current.ControlCenter,
            Windowing = current.Windowing,
            Launcher = new LauncherConfig
            {
                DisplayMode = displayMode,
                MaxResults = current.Launcher.MaxResults,
                SearchDebounceMs = current.Launcher.SearchDebounceMs,
                PreloadOnStartup = current.Launcher.PreloadOnStartup,
                ClearQueryOnClose = current.Launcher.ClearQueryOnClose,
                ShowRecentAppsOnEmptyQuery = current.Launcher.ShowRecentAppsOnEmptyQuery,
                RecentAppLimit = current.Launcher.RecentAppLimit,
                EnableCommandMode = current.Launcher.EnableCommandMode,
                DefaultTerminal = current.Launcher.DefaultTerminal,
                DefaultFileExplorer = current.Launcher.DefaultFileExplorer,
                CustomTerminalPath = current.Launcher.CustomTerminalPath
            },
            Terminal = current.Terminal,
            Notifications = current.Notifications,
            Session = current.Session,
            Startup = current.Startup,
            Hotkeys = current.Hotkeys,
            GameMode = current.GameMode
        };
    }

    private void CancelPendingAutoSave()
    {
        _autoSaveTimer.Stop();
    }

    private void OnShortcutBindingPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(HotkeyBindingEditorViewModel.Gesture))
        {
            QueueAutoSave();
        }
    }

    private void OnGameModePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IGameModeService.AutoEnable)
            or nameof(IGameModeService.IsEffective)
            or nameof(IGameModeService.IsGameRunning)
            or nameof(IGameModeService.IsFullscreenGameRunning)
            or nameof(IGameModeService.ActiveGameName))
        {
            if (eventArgs.PropertyName == nameof(IGameModeService.AutoEnable))
            {
                _gameModeAutoEnable = _gameModeService.AutoEnable;
            }

            OnPropertyChanged(nameof(GameModeAutoEnable));
            OnPropertyChanged(nameof(IsGameModeEffective));
            OnPropertyChanged(nameof(IsGameRunning));
            OnPropertyChanged(nameof(IsFullscreenGameRunning));
            OnPropertyChanged(nameof(GameModeStatusText));
            OnPropertyChanged(nameof(GameModeStateSummary));
        }
    }

    private void AddKnownGameProcess()
    {
        if (TryAddCollectionValue(KnownGameProcesses, NewKnownGameProcess))
        {
            NewKnownGameProcess = string.Empty;
            QueueAutoSave();
        }
    }

    private void RemoveKnownGameProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        if (KnownGameProcesses.Remove(processName))
        {
            QueueAutoSave();
        }
    }

    private void AddCenteredLauncherProcess()
    {
        if (TryAddCollectionValue(CenteredLauncherProcesses, NewCenteredLauncherProcess))
        {
            NewCenteredLauncherProcess = string.Empty;
            QueueAutoSave();
        }
    }

    private void RemoveCenteredLauncherProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        if (CenteredLauncherProcesses.Remove(processName))
        {
            QueueAutoSave();
        }
    }

    private static bool TryAddCollectionValue(ObservableCollection<string> values, string? rawValue)
    {
        var value = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (values.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        values.Add(value);
        return true;
    }

    private void RebuildAccentPaletteOptions(AppConfig config)
    {
        AccentPaletteOptions.Clear();

        var palette = config.Theme.AccentPalette
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var windowsAccent = _windowsAccentColorService.TryGetCurrentAccentColor();
        if (!string.IsNullOrWhiteSpace(windowsAccent))
        {
            palette.Insert(0, windowsAccent);
        }

        foreach (var color in palette.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AccentPaletteOptions.Add(new AccentPaletteOptionViewModel(
                GetPaletteName(color, windowsAccent),
                color,
                ToHex(Lighten(ParseColor(color), 0.44d))));
        }
    }

    private void PreviewThemeAccent()
    {
        if (_isReloading || string.IsNullOrWhiteSpace(SelectedAccentColor))
        {
            return;
        }

        _themeManager.ApplyTheme(BuildPreviewConfig(_appStateService.Config));
        if (!KeepWindowsAccentSeparate)
        {
            _windowsAccentColorService.TrySetAccentColor(SelectedAccentColor);
            WindowsAccentColor = _windowsAccentColorService.TryGetCurrentAccentColor() ?? SelectedAccentColor;
        }
    }

    private AppConfig BuildPreviewConfig(AppConfig current)
    {
        return new AppConfig
        {
            Theme = new ThemeConfig
            {
                KeepWindowsAccentSeparate = KeepWindowsAccentSeparate,
                TintShellSurfacesWithAccent = TintShellSurfacesWithAccent,
                AccentColor = string.IsNullOrWhiteSpace(SelectedAccentColor) ? current.Theme.AccentColor : SelectedAccentColor,
                SecondaryAccentColor = string.IsNullOrWhiteSpace(SecondaryAccentColor) ? current.Theme.SecondaryAccentColor : SecondaryAccentColor,
                ForegroundColor = string.IsNullOrWhiteSpace(ForegroundColor) ? current.Theme.ForegroundColor : ForegroundColor,
                MutedForegroundColor = string.IsNullOrWhiteSpace(MutedForegroundColor) ? current.Theme.MutedForegroundColor : MutedForegroundColor,
                PanelColor = string.IsNullOrWhiteSpace(PanelColor) ? current.Theme.PanelColor : PanelColor,
                BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? current.Theme.BackgroundColor : BackgroundColor,
                WallpaperPath = string.IsNullOrWhiteSpace(WallpaperPath) ? null : WallpaperPath.Trim(),
                ShowDesktopDecorations = ShowDesktopDecorations,
                RecentWallpapers = current.Theme.RecentWallpapers,
                AccentPalette = AccentPaletteOptions.Select(option => option.AccentColor).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.4d, 1d),
                PanelOpacity = Math.Clamp(PanelOpacity, 0.4d, 1d),
                EnableBackdropBlur = current.Theme.EnableBackdropBlur,
                EnableShadows = EnableShadows,
                EnableTransparency = EnableTransparency,
                CornerRadius = current.Theme.CornerRadius
            },
            Logging = current.Logging,
            Animations = current.Animations,
            Performance = current.Performance,
            ControlCenter = current.ControlCenter,
            Windowing = current.Windowing,
            Launcher = current.Launcher,
            Terminal = current.Terminal,
            Notifications = current.Notifications,
            Session = current.Session,
            Startup = current.Startup,
            Hotkeys = current.Hotkeys,
            GameMode = current.GameMode
        };
    }

    private void PickColor(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var dialog = new ColorPickerWindow(GetColorValue(target))
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetColorValue(target, dialog.SelectedColorHex);
    }

    private void UseWindowsAccent()
    {
        var windowsAccent = _windowsAccentColorService.TryGetCurrentAccentColor();
        if (string.IsNullOrWhiteSpace(windowsAccent))
        {
            return;
        }

        WindowsAccentColor = windowsAccent;
        SelectedAccentColor = windowsAccent;
        SecondaryAccentColor = ToHex(Lighten(ParseColor(windowsAccent), 0.44d));
        SelectedAccentPaletteOption = AccentPaletteOptions.FirstOrDefault(option =>
            string.Equals(option.AccentColor, windowsAccent, StringComparison.OrdinalIgnoreCase));
    }

    private string GetColorValue(string target)
    {
        return target switch
        {
            nameof(SelectedAccentColor) => SelectedAccentColor,
            nameof(SecondaryAccentColor) => SecondaryAccentColor,
            nameof(ForegroundColor) => ForegroundColor,
            nameof(MutedForegroundColor) => MutedForegroundColor,
            nameof(PanelColor) => PanelColor,
            nameof(BackgroundColor) => BackgroundColor,
            _ => SelectedAccentColor
        };
    }

    private void SetColorValue(string target, string value)
    {
        switch (target)
        {
            case nameof(SelectedAccentColor):
                SelectedAccentColor = value;
                break;
            case nameof(SecondaryAccentColor):
                SecondaryAccentColor = value;
                break;
            case nameof(ForegroundColor):
                ForegroundColor = value;
                break;
            case nameof(MutedForegroundColor):
                MutedForegroundColor = value;
                break;
            case nameof(PanelColor):
                PanelColor = value;
                break;
            case nameof(BackgroundColor):
                BackgroundColor = value;
                break;
        }
    }

    private static Color ParseColor(string value)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color color)
            {
                return color;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return Color.FromRgb(0x79, 0xE6, 0xF5);
    }

    private static string GetPaletteName(string color, string? windowsAccent)
    {
        if (!string.IsNullOrWhiteSpace(windowsAccent)
            && string.Equals(color, windowsAccent, StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        return color.ToUpperInvariant() switch
        {
            "#5AB7FF" => "Sky",
            "#79E6F5" => "Aqua",
            "#59D2B1" => "Seafoam",
            "#93F27E" => "Mint",
            "#B6E16B" => "Lime",
            "#F5C16C" => "Amber",
            "#FF9A62" => "Tangerine",
            "#F58CC6" => "Bloom",
            "#FF6FAE" => "Rose",
            "#B8A3FF" => "Iris",
            "#8DA2FF" => "Periwinkle",
            "#C4AEFF" => "Lilac",
            _ => "Custom"
        };
    }

    private static Color Lighten(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(color.R + ((255 - color.R) * amount)),
            (byte)(color.G + ((255 - color.G) * amount)),
            (byte)(color.B + ((255 - color.B) * amount)));
    }

    private static string ToHex(Color color)
    {
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
    }

    private static bool IsFunctionKeyBinding(HotkeyActionKind action)
    {
        return action is HotkeyActionKind.VolumeUp
            or HotkeyActionKind.VolumeDown
            or HotkeyActionKind.ToggleMute
            or HotkeyActionKind.MediaPlayPause
            or HotkeyActionKind.MediaNext
            or HotkeyActionKind.MediaPrevious
            or HotkeyActionKind.BrightnessUp
            or HotkeyActionKind.BrightnessDown;
    }

    private void SelectWallpaper()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Nebula wallpaper",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyWallpaper(dialog.FileName);
        }
    }

    private void SelectCustomNotificationSound()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*",
            Title = "Select custom notification sound"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CustomNotificationSoundPath = dialog.FileName;
    }

    private void PreviewNotificationSound()
    {
        if (string.IsNullOrWhiteSpace(CustomNotificationSoundPath))
        {
            return;
        }

        try
        {
            SoundStatusText = "Playing...";
            var player = new SoundPlayer(CustomNotificationSoundPath);
            player.Play();
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to preview notification sound.", new Dictionary<string, object?> { ["error"] = exception.Message });
            SoundStatusText = "Unable to play file";
            _ = Task.Delay(2000).ContinueWith(_ => SoundStatusText = string.Empty);
        }
    }

    private void UseWindowsWallpaper()
    {
        WallpaperPath = string.Empty;
        UpdateWallpaperSelectionStates();
    }

    private void ApplyWallpaperOption(WallpaperOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        ApplyWallpaper(option.Path);
    }

    private void ApplyWallpaper(string? wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
        {
            return;
        }

        WallpaperPath = wallpaperPath;
        PushRecentWallpaper(wallpaperPath);
        UpdateWallpaperSelectionStates();

        if (!_wallpaperService.TrySetWallpaper(wallpaperPath))
        {
            _logService.Warn("Failed to update Windows wallpaper from Nebula settings.", new Dictionary<string, object?>
            {
                ["wallpaperPath"] = wallpaperPath
            });
        }
    }

    private void RebuildWallpaperOptions(AppConfig config)
    {
        RecentWallpaperOptions.Clear();
        foreach (var wallpaperPath in config.Theme.RecentWallpapers
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(3))
        {
            RecentWallpaperOptions.Add(CreateWallpaperOption(wallpaperPath));
        }

        DefaultWallpaperOptions.Clear();
        foreach (var wallpaperPath in _wallpaperService.GetDefaultWallpaperPaths()
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(9))
        {
            DefaultWallpaperOptions.Add(CreateWallpaperOption(wallpaperPath));
        }

        UpdateWallpaperSelectionStates();
    }

    private void PushRecentWallpaper(string wallpaperPath)
    {
        var updated = RecentWallpaperOptions
            .Where(option => !string.Equals(option.Path, wallpaperPath, StringComparison.OrdinalIgnoreCase))
            .Select(option => option.Path)
            .Prepend(wallpaperPath)
            .Where(File.Exists)
            .Take(3)
            .ToList();

        RecentWallpaperOptions.Clear();
        foreach (var path in updated)
        {
            RecentWallpaperOptions.Add(CreateWallpaperOption(path));
        }

        UpdateWallpaperSelectionStates();
    }

    private static WallpaperOptionViewModel CreateWallpaperOption(string wallpaperPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(wallpaperPath);
        var normalizedName = WallpaperResolutionSuffixPattern.Replace(fileName, string.Empty);
        var parent = Directory.GetParent(wallpaperPath)?.Name ?? "Wallpaper";
        return new WallpaperOptionViewModel(wallpaperPath, normalizedName, parent);
    }

    private void UpdateWallpaperSelectionStates()
    {
        var effectiveWallpaperPath = string.IsNullOrWhiteSpace(WallpaperPath)
            ? _wallpaperService.TryGetCurrentWallpaperPath()
            : WallpaperPath;

        foreach (var option in RecentWallpaperOptions.Concat(DefaultWallpaperOptions))
        {
            option.IsCurrent = !string.IsNullOrWhiteSpace(effectiveWallpaperPath)
                && string.Equals(option.Path, effectiveWallpaperPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SelectCustomTerminal()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose terminal executable",
            Filter = "Executable files|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            CustomTerminalPath = dialog.FileName;
            DefaultTerminal = TerminalProfileKind.Custom;
        }
    }

    private async Task RefreshLiveSettingsAsync()
    {
        await Task.WhenAll(
            RefreshWifiNetworksAsync(),
            RefreshAudioDevicesAsync(),
            RefreshSystemInformationAsync());
    }

    private async Task RefreshSystemInformationAsync()
    {
        try
        {
            var snapshot = await _systemStatusService.GetSystemInformationAsync();
            CpuSummary = snapshot.CpuName;
            MemoryDetails = snapshot.MemorySummary;
            GpuSummary = snapshot.GpuName;
            VramDetails = snapshot.VideoMemorySummary;
            StorageDetails = snapshot.StorageSummary;
            WindowsVersionDetails = snapshot.WindowsVersion;
            SystemArchitectureDetails = snapshot.Architecture;
            DeviceIdentitySummary = snapshot.DeviceSummary;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to refresh detailed system information for settings.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });

            CpuSummary = "CPU details unavailable";
            MemoryDetails = "Memory details unavailable";
            GpuSummary = "GPU details unavailable";
            VramDetails = "VRAM details unavailable";
            StorageDetails = "Storage details unavailable";
            WindowsVersionDetails = "Windows version unavailable";
            SystemArchitectureDetails = "Architecture details unavailable";
            DeviceIdentitySummary = "Device details unavailable";
        }
    }

    public async Task RefreshWifiNetworksAsync()
    {
        if (_isRefreshingWifi)
        {
            return;
        }

        _isRefreshingWifi = true;
        WifiStatusText = "Scanning networks...";
        try
        {
            var networks = await _systemStatusService.GetAvailableWifiNetworksAsync();
            AvailableWifiNetworks.Clear();
            foreach (var network in networks)
            {
                AvailableWifiNetworks.Add(network);
            }

            OnPropertyChanged(nameof(WifiEnabled));
            OnPropertyChanged(nameof(ActiveNetworkName));
            WifiStatusText = AvailableWifiNetworks.Count == 0 ? "No Wi-Fi networks found." : "Nearby networks update automatically.";
        }
        catch (Exception exception)
        {
            WifiStatusText = "Wi-Fi details are unavailable.";
            _logService.Warn("Failed to refresh Wi-Fi networks from settings.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
        finally
        {
            _isRefreshingWifi = false;
        }
    }

    public async Task RefreshAudioDevicesAsync()
    {
        if (_isRefreshingAudio)
        {
            return;
        }

        _isRefreshingAudio = true;
        SoundStatusText = "Refreshing audio devices...";

        try
        {
            var outputDevices = await _systemStatusService.GetAudioDevicesAsync(AudioDeviceKind.Output);
            var inputDevices = await _systemStatusService.GetAudioDevicesAsync(AudioDeviceKind.Input);

            OutputAudioDevices.Clear();
            foreach (var device in outputDevices.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                OutputAudioDevices.Add(device);
            }

            InputAudioDevices.Clear();
            foreach (var device in inputDevices.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                InputAudioDevices.Add(device);
            }

            SoundStatusText = OutputAudioDevices.Count == 0 && InputAudioDevices.Count == 0
                ? "No audio devices reported by Windows."
                : "Audio devices update automatically.";
        }
        catch (Exception exception)
        {
            SoundStatusText = "Audio device details are unavailable.";
            _logService.Warn("Failed to refresh audio devices from settings.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
        finally
        {
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(MuteSummary));
            OnPropertyChanged(nameof(MasterVolumeSummary));
            _isRefreshingAudio = false;
        }
    }

    private async Task SelectAudioDeviceAsync(AudioDeviceModel? device, AudioDeviceKind expectedKind)
    {
        if (device is null || device.Kind != expectedKind)
        {
            return;
        }

        SoundStatusText = $"Switching default {expectedKind.ToString().ToLowerInvariant()} device...";
        var changed = await _systemStatusService.SetDefaultAudioDeviceAsync(device);
        SoundStatusText = changed
            ? $"Default {expectedKind.ToString().ToLowerInvariant()} device set to {device.Name}."
            : $"Couldn't switch to {device.Name}.";

        await RefreshAudioDevicesAsync();
    }

    private async Task ConnectToWifiAsync(WifiNetworkModel? network)
    {
        if (network is null)
        {
            return;
        }

        if (network.IsSecure && !network.IsSavedProfile)
        {
            SelectedWifiNetwork = network;
            WifiPassword = string.Empty;
            WifiStatusText = $"Enter the password for {network.Ssid}.";
            OnPropertyChanged(nameof(IsWifiPasswordPromptVisible));
            return;
        }

        await ConnectToWifiCoreAsync(network, null);
    }

    private async Task SubmitWifiPasswordAsync()
    {
        if (SelectedWifiNetwork is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WifiPassword))
        {
            WifiStatusText = "Enter a password before connecting.";
            return;
        }

        await ConnectToWifiCoreAsync(SelectedWifiNetwork, WifiPassword);
    }

    private async Task ConnectToWifiCoreAsync(WifiNetworkModel network, string? password)
    {
        WifiStatusText = $"Connecting to {network.Ssid}...";
        var connected = await _systemStatusService.ConnectToWifiAsync(new WifiConnectionRequest(
            network.Ssid,
            password,
            network.Authentication,
            network.Encryption,
            network.IsSavedProfile));

        WifiStatusText = connected
            ? $"Connected to {network.Ssid}."
            : password is not null
                ? $"Couldn't connect to {network.Ssid}. Check the password and try again."
                : $"Couldn't connect to {network.Ssid}.";

        if (connected)
        {
            CancelWifiPasswordPrompt();
        }

        await RefreshWifiNetworksAsync();
    }

    private void CancelWifiPasswordPrompt()
    {
        SelectedWifiNetwork = null;
        WifiPassword = string.Empty;
        OnPropertyChanged(nameof(IsWifiPasswordPromptVisible));
    }

    private void OnSystemStatusPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemStatusModel.WifiEnabled) or nameof(SystemStatusModel.ActiveNetworkName))
        {
            OnPropertyChanged(nameof(WifiEnabled));
            OnPropertyChanged(nameof(ActiveNetworkName));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.NetworkSummary) or nameof(SystemStatusModel.ActiveNetworkName))
        {
            OnPropertyChanged(nameof(NetworkSummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.VolumePercent) or nameof(SystemStatusModel.IsMuted))
        {
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(MuteSummary));
            OnPropertyChanged(nameof(MasterVolumeSummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.IsBatteryPresent) or nameof(SystemStatusModel.BatteryPercent))
        {
            OnPropertyChanged(nameof(BatterySummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.BluetoothAvailable) or nameof(SystemStatusModel.BluetoothEnabled))
        {
            OnPropertyChanged(nameof(BluetoothSummary));
        }
    }
}
