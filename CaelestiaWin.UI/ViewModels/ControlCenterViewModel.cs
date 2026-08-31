using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;
using System.Collections.ObjectModel;
using System.Threading;

namespace CaelestiaWin.UI.ViewModels;

public sealed class ControlCenterViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly ISystemStatusService _systemStatusService;
    private readonly IWindowActionService _windowActionService;
    private readonly IShellSettingsService _shellSettingsService;
    private readonly IGameModeService _gameModeService;
    private readonly IWindowLayoutService _windowLayoutService;
    private readonly IPomodoroService _pomodoroService;
    private readonly IConfigurationService _configurationService;
    private bool _isOpen;
    private bool _isEditMode;
    private bool _isPowerMenuOpen;
    private bool _isWifiMenuOpen;
    private bool _isSoundMenuOpen;
    private bool _isInputMenuOpen;
    private bool _isPomodoroMenuOpen;
    private bool _isWifiPasswordPromptVisible;
    private string _wifiStatusText = string.Empty;
    private string _soundStatusText = string.Empty;
    private string _inputStatusText = string.Empty;
    private string _wifiPassword = string.Empty;
    private string _clockText = DateTime.Now.ToString("HH:mm");
    private string _dateSummary = DateTime.Now.ToString("dddd, dd MMMM yyyy");
    private double _brightnessLevel = 58;
    private WifiNetworkModel? _selectedWifiNetwork;
    private PomodoroHistoryRangeKind _selectedPomodoroHistoryRange = PomodoroHistoryRangeKind.Week;
    private string _pomodoroTotalFocusText = "0h 00m";
    private int _pomodoroHeatmapColumnCount = 5;
    private int _lastPomodoroHistoryRefreshMinute = -1;

    public ControlCenterViewModel(
        IAppStateService appStateService,
        IWindowActionService windowActionService,
        ISystemStatusService systemStatusService,
        IMediaService mediaService,
        IShellSettingsService shellSettingsService,
        IShellCommandService shellCommandService,
        IGameModeService gameModeService,
        IWindowLayoutService windowLayoutService,
        IPomodoroService pomodoroService,
        IConfigurationService configurationService)
    {
        _appStateService = appStateService;
        _windowActionService = windowActionService;
        _systemStatusService = systemStatusService;
        _shellSettingsService = shellSettingsService;
        _gameModeService = gameModeService;
        _windowLayoutService = windowLayoutService;
        _pomodoroService = pomodoroService;
        _configurationService = configurationService;
        IsOpen = _appStateService.IsControlCenterOpen;
        SystemStatus = systemStatusService.CurrentStatus;
        MediaSession = mediaService.CurrentSession;
        AvailableWifiNetworks = [];
        OutputDevices = [];
        InputDevices = [];
        AppVolumeSessions = [];
        PomodoroFocusBars = [];
        PomodoroHeatmapCells = [];
        ActiveWidgets = [];
        HiddenWidgets = [];
        SystemStatus.PropertyChanged += OnSystemStatusPropertyChanged;
        _pomodoroService.PropertyChanged += OnPomodoroServicePropertyChanged;

        CloseCommand = new RelayCommand(CloseAllPanels);
        OpenSettingsCommand = new RelayCommand(() => _shellSettingsService.Show(ShellSettingsSection.Appearance));
        OpenGameCenterCommand = new RelayCommand(() => _shellSettingsService.Show(ShellSettingsSection.GameCenter));
        ToggleGameModeCommand = new RelayCommand(ToggleGameMode);
        LockCommand = new RelayCommand(windowActionService.Lock);
        SignOutCommand = new RelayCommand(windowActionService.SignOut);
        OpenPowerMenuCommand = new RelayCommand(() => IsPowerMenuOpen = true);
        ClosePowerMenuCommand = new RelayCommand(() => IsPowerMenuOpen = false);
        ShutdownCommand = new RelayCommand(() => ExecutePowerAction(windowActionService.Shutdown));
        RestartCommand = new RelayCommand(() => ExecutePowerAction(windowActionService.Restart));
        RebootToFirmwareCommand = new RelayCommand(() => ExecutePowerAction(windowActionService.RebootToFirmware));
        ReturnToExplorerCommand = new RelayCommand(() =>
        {
            CloseAllPanels();
            shellCommandService.ReturnToExplorerAndExit();
        });
        OpenWifiMenuCommand = new AsyncRelayCommand(OpenWifiMenuAsync);
        CloseWifiMenuCommand = new RelayCommand(() => IsWifiMenuOpen = false);
        ConnectToWifiCommand = new AsyncRelayCommand<WifiNetworkModel>(ConnectToWifiAsync);
        ToggleWifiCommand = new RelayCommand(systemStatusService.ToggleWifi);
        OpenSoundMenuCommand = new AsyncRelayCommand(OpenSoundMenuAsync);
        CloseSoundMenuCommand = new RelayCommand(() => IsSoundMenuOpen = false);
        OpenInputMenuCommand = new AsyncRelayCommand(OpenInputMenuAsync);
        CloseInputMenuCommand = new RelayCommand(() => IsInputMenuOpen = false);
        OpenPomodoroMenuCommand = new RelayCommand(OpenPomodoroMenu);
        ClosePomodoroMenuCommand = new RelayCommand(() => IsPomodoroMenuOpen = false);
        StartPomodoroCommand = new RelayCommand(StartPomodoro);
        TogglePomodoroPauseCommand = new RelayCommand(TogglePomodoroPause);
        RestartPomodoroCommand = new RelayCommand(_pomodoroService.Restart);
        StopPomodoroCommand = new RelayCommand(_pomodoroService.Stop);
        StartPomodoroBreakCommand = new RelayCommand(_pomodoroService.StartBreak);
        IncreasePomodoroLengthCommand = new RelayCommand(() => AdjustPomodoroLength(5));
        DecreasePomodoroLengthCommand = new RelayCommand(() => AdjustPomodoroLength(-5));
        IncreasePomodoroBreakLengthCommand = new RelayCommand(() => AdjustPomodoroBreakLength(1));
        DecreasePomodoroBreakLengthCommand = new RelayCommand(() => AdjustPomodoroBreakLength(-1));
        ShowWeeklyPomodoroHistoryCommand = new RelayCommand(() => SetPomodoroHistoryRange(PomodoroHistoryRangeKind.Week));
        ShowMonthlyPomodoroHistoryCommand = new RelayCommand(() => SetPomodoroHistoryRange(PomodoroHistoryRangeKind.Month));
        SelectAudioDeviceCommand = new AsyncRelayCommand<AudioDeviceModel>(SelectAudioDeviceAsync);
        SubmitWifiPasswordCommand = new AsyncRelayCommand(SubmitWifiPasswordAsync, () => SelectedWifiNetwork is not null);
        CancelWifiPasswordCommand = new RelayCommand(CancelWifiPasswordPrompt);
        OpenWifiSettingsAppCommand = new RelayCommand(OpenWifiSettingsApp);
        ToggleBluetoothCommand = new RelayCommand(systemStatusService.ToggleBluetooth);
        PlayPauseCommand = new AsyncRelayCommand(() => mediaService.PlayPauseAsync());
        PreviousTrackCommand = new AsyncRelayCommand(() => mediaService.PreviousAsync());
        NextTrackCommand = new AsyncRelayCommand(() => mediaService.NextAsync());
        ToggleEditModeCommand = new RelayCommand(() => IsEditMode = !IsEditMode);
        MoveWidgetCommand = new AsyncRelayCommand<Tuple<int, int>>(MoveWidgetAsync, tuple => tuple != null);
        ToggleWidgetVisibilityCommand = new AsyncRelayCommand<ControlCenterWidgetKind>(ToggleWidgetVisibilityAsync);

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        _gameModeService.PropertyChanged += OnGameModePropertyChanged;

        var clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        RefreshPomodoroHistory();
        LoadWidgetOrder();
    }

    public ICommand CloseCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenGameCenterCommand { get; }

    public ICommand ToggleGameModeCommand { get; }

    public ICommand LockCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand OpenPowerMenuCommand { get; }

    public ICommand ClosePowerMenuCommand { get; }

    public ICommand RestartCommand { get; }

    public ICommand ShutdownCommand { get; }

    public ICommand RebootToFirmwareCommand { get; }

    public ICommand ReturnToExplorerCommand { get; }

    public ICommand OpenWifiMenuCommand { get; }

    public ICommand CloseWifiMenuCommand { get; }

    public ICommand ConnectToWifiCommand { get; }

    public ICommand ToggleWifiCommand { get; }

    public ICommand OpenSoundMenuCommand { get; }

    public ICommand CloseSoundMenuCommand { get; }

    public ICommand OpenInputMenuCommand { get; }

    public ICommand CloseInputMenuCommand { get; }

    public ICommand OpenPomodoroMenuCommand { get; }

    public ICommand ClosePomodoroMenuCommand { get; }

    public ICommand StartPomodoroCommand { get; }

    public ICommand TogglePomodoroPauseCommand { get; }

    public ICommand RestartPomodoroCommand { get; }

    public ICommand StopPomodoroCommand { get; }

    public ICommand StartPomodoroBreakCommand { get; }

    public ICommand IncreasePomodoroLengthCommand { get; }

    public ICommand DecreasePomodoroLengthCommand { get; }

    public ICommand IncreasePomodoroBreakLengthCommand { get; }

    public ICommand DecreasePomodoroBreakLengthCommand { get; }

    public ICommand ShowWeeklyPomodoroHistoryCommand { get; }

    public ICommand ShowMonthlyPomodoroHistoryCommand { get; }

    public ICommand SelectAudioDeviceCommand { get; }

    public ICommand SubmitWifiPasswordCommand { get; }

    public ICommand CancelWifiPasswordCommand { get; }

    public ICommand OpenWifiSettingsAppCommand { get; }

    public ICommand ToggleBluetoothCommand { get; }

    public ICommand PlayPauseCommand { get; }

    public ICommand PreviousTrackCommand { get; }

    public ICommand NextTrackCommand { get; }

    public ICommand ToggleEditModeCommand { get; }

    public ICommand MoveWidgetCommand { get; }

    public ICommand ToggleWidgetVisibilityCommand { get; }

    public SystemStatusModel SystemStatus { get; }

    public MediaSessionModel MediaSession { get; }

    public ObservableCollection<WifiNetworkModel> AvailableWifiNetworks { get; }

    public ObservableCollection<AudioDeviceModel> OutputDevices { get; }

    public ObservableCollection<AudioDeviceModel> InputDevices { get; }

    public ObservableCollection<AppVolumeSessionViewModel> AppVolumeSessions { get; }

    public ObservableCollection<PomodoroFocusBarViewModel> PomodoroFocusBars { get; }

    public ObservableCollection<PomodoroHeatCellViewModel> PomodoroHeatmapCells { get; }

    public ObservableCollection<ControlCenterWidgetKind> ActiveWidgets { get; }

    public ObservableCollection<ControlCenterWidgetKind> HiddenWidgets { get; }

    public WifiNetworkModel? SelectedWifiNetwork
    {
        get => _selectedWifiNetwork;
        private set
        {
            if (SetProperty(ref _selectedWifiNetwork, value))
            {
                if (SubmitWifiPasswordCommand is AsyncRelayCommand command)
                {
                    command.NotifyCanExecuteChanged();
                }
            }
        }
    }

    public string BatterySummary => SystemStatus.IsBatteryPresent && SystemStatus.BatteryPercent.HasValue
        ? $"{SystemStatus.BatteryPercent.Value}% battery"
        : "Battery unavailable";

    public string VolumeSummary => SystemStatus.IsMuted
        ? "Muted"
        : $"{SystemStatus.VolumePercent:0}%";

    public string BrightnessSummary => $"{BrightnessLevel:0}%";

    public string WifiSummary => SystemStatus.WifiEnabled
        ? SystemStatus.ActiveNetworkName
        : "Wi-Fi off";

    public string BluetoothSummary => SystemStatus.BluetoothEnabled
        ? "Bluetooth on"
        : "Bluetooth off";

    public bool IsGameModeAutoEnabled => _gameModeService.AutoEnable;

    public bool IsGameModeEffective => _gameModeService.IsEffective;

    public string GameModeSummary => _gameModeService.AutoEnable
        ? "Game mode auto"
        : "Game mode paused";

    public string GameModeDetail => _gameModeService.IsGameRunning
        ? _gameModeService.ActiveGameName
        : "Reduce shell effects while gaming";

    public bool IsBluetoothAvailable => SystemStatus.BluetoothAvailable;

    public bool ShowBluetoothRow => IsBluetoothAvailable
                                    && _appStateService.Config.ControlCenter.InputWidgetPlacement != ControlCenterInputPlacementKind.ConnectivityCard;

    public bool ShowInputInConnectivityCard => _appStateService.Config.ControlCenter.InputWidgetPlacement == ControlCenterInputPlacementKind.ConnectivityCard
                                               || _appStateService.Config.ControlCenter.InputWidgetPlacement == ControlCenterInputPlacementKind.Auto && !IsBluetoothAvailable;

    public bool ShowInputInBottomRow => _appStateService.Config.ControlCenter.InputWidgetPlacement == ControlCenterInputPlacementKind.BottomRow
                                        || _appStateService.Config.ControlCenter.InputWidgetPlacement == ControlCenterInputPlacementKind.Auto && IsBluetoothAvailable;

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        set => SetProperty(ref _isEditMode, value);
    }

    public bool IsPowerMenuOpen
    {
        get => _isPowerMenuOpen;
        set => SetProperty(ref _isPowerMenuOpen, value);
    }

    public bool IsWifiMenuOpen
    {
        get => _isWifiMenuOpen;
        set
        {
            if (SetProperty(ref _isWifiMenuOpen, value) && !value)
            {
                WifiStatusText = string.Empty;
                CancelWifiPasswordPrompt();
            }
        }
    }

    public bool IsSoundMenuOpen
    {
        get => _isSoundMenuOpen;
        set => SetProperty(ref _isSoundMenuOpen, value);
    }

    public bool IsInputMenuOpen
    {
        get => _isInputMenuOpen;
        set => SetProperty(ref _isInputMenuOpen, value);
    }

    public bool IsPomodoroMenuOpen
    {
        get => _isPomodoroMenuOpen;
        set => SetProperty(ref _isPomodoroMenuOpen, value);
    }

    public bool IsWifiPasswordPromptVisible
    {
        get => _isWifiPasswordPromptVisible;
        private set => SetProperty(ref _isWifiPasswordPromptVisible, value);
    }

    public string ClockText
    {
        get => _clockText;
        private set => SetProperty(ref _clockText, value);
    }

    public string DateSummary
    {
        get => _dateSummary;
        private set => SetProperty(ref _dateSummary, value);
    }

    public double VolumeLevel
    {
        get => SystemStatus.VolumePercent;
        set
        {
            if (Math.Abs(SystemStatus.VolumePercent - value) > 0.1d)
            {
                _systemStatusService.SetVolume(value);
                OnPropertyChanged();
            }
        }
    }

    public double BrightnessLevel
    {
        get => _brightnessLevel;
        set => SetProperty(ref _brightnessLevel, value);
    }

    public string WifiStatusText
    {
        get => _wifiStatusText;
        private set => SetProperty(ref _wifiStatusText, value);
    }

    public string SoundStatusText
    {
        get => _soundStatusText;
        private set => SetProperty(ref _soundStatusText, value);
    }

    public string InputStatusText
    {
        get => _inputStatusText;
        private set => SetProperty(ref _inputStatusText, value);
    }

    public string WifiPassword
    {
        get => _wifiPassword;
        set => SetProperty(ref _wifiPassword, value);
    }

    public int PomodoroSessionLengthMinutes => _pomodoroService.SessionLengthMinutes;

    public int PomodoroBreakLengthMinutes => _pomodoroService.BreakLengthMinutes;

    public bool PomodoroAutoCycleEnabled
    {
        get => _pomodoroService.AutoCycleEnabled;
        set
        {
            if (_pomodoroService.AutoCycleEnabled == value)
            {
                return;
            }

            _pomodoroService.SetAutoCycleEnabled(value);
            OnPropertyChanged();
        }
    }

    public string PomodoroRemainingText => FormatDuration(_pomodoroService.RemainingSeconds);

    public string PomodoroElapsedText => FormatDuration(_pomodoroService.ElapsedSeconds);

    public string PomodoroPhaseLabel => _pomodoroService.Phase == PomodoroPhaseKind.Break ? "Break" : "Focus";

    public bool IsPomodoroRunning => _pomodoroService.IsRunning;

    public bool IsPomodoroPaused => _pomodoroService.IsPaused;

    public bool HasActivePomodoro => _pomodoroService.IsVisible;

    public bool ShowPomodoroStartActions => !HasActivePomodoro;

    public bool ShowPomodoroActiveActions => HasActivePomodoro;

    public string PomodoroPrimaryActionLabel => _pomodoroService.IsPaused ? "Resume" : "Pause";

    public string PomodoroStatusLabel => _pomodoroService.State switch
    {
        PomodoroStateKind.Running => _pomodoroService.Phase == PomodoroPhaseKind.Break ? "Break in progress" : "Focus in progress",
        PomodoroStateKind.Paused => _pomodoroService.Phase == PomodoroPhaseKind.Break ? "Break paused" : "Focus paused",
        _ => "Ready to focus"
    };

    public string PomodoroSummaryText => _selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Week
        ? "Weekly focus"
        : "Monthly focus";

    public string PomodoroHistoryRangeWeekBackground => _selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Week ? "#24FFFFFF" : "#12000000";

    public string PomodoroHistoryRangeMonthBackground => _selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Month ? "#24FFFFFF" : "#12000000";

    public bool IsPomodoroWeekView => _selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Week;

    public bool IsPomodoroMonthView => _selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Month;

    public int PomodoroHeatmapColumnCount
    {
        get => _pomodoroHeatmapColumnCount;
        private set => SetProperty(ref _pomodoroHeatmapColumnCount, value);
    }

    public string PomodoroTotalFocusText
    {
        get => _pomodoroTotalFocusText;
        private set => SetProperty(ref _pomodoroTotalFocusText, value);
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.IsControlCenterOpen))
        {
            IsOpen = _appStateService.IsControlCenterOpen;
            if (!IsOpen)
            {
                IsPowerMenuOpen = false;
                IsWifiMenuOpen = false;
                IsSoundMenuOpen = false;
                IsInputMenuOpen = false;
                IsPomodoroMenuOpen = false;
                IsEditMode = false;
            }
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.Config))
        {
            OnPropertyChanged(nameof(ShowBluetoothRow));
            OnPropertyChanged(nameof(ShowInputInConnectivityCard));
            OnPropertyChanged(nameof(ShowInputInBottomRow));
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm");
        DateSummary = now.ToString("dddd, dd MMMM yyyy");
    }

    private void OnPomodoroServicePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IPomodoroService.State)
            or nameof(IPomodoroService.Phase)
            or nameof(IPomodoroService.RemainingSeconds)
            or nameof(IPomodoroService.ElapsedSeconds)
            or nameof(IPomodoroService.SessionLengthMinutes)
            or nameof(IPomodoroService.BreakLengthMinutes)
            or nameof(IPomodoroService.AutoCycleEnabled))
        {
            OnPropertyChanged(nameof(PomodoroSessionLengthMinutes));
            OnPropertyChanged(nameof(PomodoroBreakLengthMinutes));
            OnPropertyChanged(nameof(PomodoroAutoCycleEnabled));
            OnPropertyChanged(nameof(PomodoroRemainingText));
            OnPropertyChanged(nameof(PomodoroElapsedText));
            OnPropertyChanged(nameof(PomodoroPhaseLabel));
            OnPropertyChanged(nameof(IsPomodoroRunning));
            OnPropertyChanged(nameof(IsPomodoroPaused));
            OnPropertyChanged(nameof(HasActivePomodoro));
            OnPropertyChanged(nameof(ShowPomodoroStartActions));
            OnPropertyChanged(nameof(ShowPomodoroActiveActions));
            OnPropertyChanged(nameof(PomodoroPrimaryActionLabel));
            OnPropertyChanged(nameof(PomodoroStatusLabel));
            var shouldRefreshHistory = eventArgs.PropertyName is nameof(IPomodoroService.State)
                or nameof(IPomodoroService.Phase)
                or nameof(IPomodoroService.SessionLengthMinutes)
                or nameof(IPomodoroService.BreakLengthMinutes)
                or nameof(IPomodoroService.AutoCycleEnabled);

            if (!shouldRefreshHistory
                && eventArgs.PropertyName == nameof(IPomodoroService.ElapsedSeconds)
                && _pomodoroService.ElapsedSeconds / 60 != _lastPomodoroHistoryRefreshMinute)
            {
                shouldRefreshHistory = true;
            }

            if (shouldRefreshHistory)
            {
                RefreshPomodoroHistory();
            }
        }
    }

    private void OnGameModePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(IGameModeService.AutoEnable)
            or nameof(IGameModeService.IsEffective)
            or nameof(IGameModeService.IsGameRunning)
            or nameof(IGameModeService.ActiveGameName))
        {
            OnPropertyChanged(nameof(IsGameModeAutoEnabled));
            OnPropertyChanged(nameof(IsGameModeEffective));
            OnPropertyChanged(nameof(GameModeSummary));
            OnPropertyChanged(nameof(GameModeDetail));
        }
    }

    private void OnSystemStatusPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemStatusModel.VolumePercent) or nameof(SystemStatusModel.IsMuted))
        {
            OnPropertyChanged(nameof(VolumeLevel));
            OnPropertyChanged(nameof(VolumeSummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.BatteryPercent) or nameof(SystemStatusModel.IsBatteryPresent))
        {
            OnPropertyChanged(nameof(BatterySummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.NetworkSummary) or nameof(SystemStatusModel.WifiEnabled) or nameof(SystemStatusModel.ActiveNetworkName))
        {
            OnPropertyChanged(nameof(WifiSummary));
        }

        if (eventArgs.PropertyName is nameof(SystemStatusModel.BluetoothEnabled) or nameof(SystemStatusModel.BluetoothAvailable))
        {
            OnPropertyChanged(nameof(BluetoothSummary));
            OnPropertyChanged(nameof(IsBluetoothAvailable));
            OnPropertyChanged(nameof(ShowBluetoothRow));
            OnPropertyChanged(nameof(ShowInputInConnectivityCard));
            OnPropertyChanged(nameof(ShowInputInBottomRow));
        }
    }

    private void ExecutePowerAction(Action powerAction)
    {
        CloseAllPanels();
        powerAction();
    }

    private async Task OpenWifiMenuAsync()
    {
        IsWifiMenuOpen = true;
        IsPowerMenuOpen = false;
        IsSoundMenuOpen = false;
        IsInputMenuOpen = false;
        IsPomodoroMenuOpen = false;
        CancelWifiPasswordPrompt();
        WifiStatusText = "Scanning networks...";
        await RefreshWifiNetworksAsync();
    }

    private async Task OpenSoundMenuAsync()
    {
        IsSoundMenuOpen = true;
        IsInputMenuOpen = false;
        IsPowerMenuOpen = false;
        IsWifiMenuOpen = false;
        IsPomodoroMenuOpen = false;
        SoundStatusText = "Loading output devices...";
        await RefreshAudioControlsAsync();
    }

    private async Task OpenInputMenuAsync()
    {
        IsInputMenuOpen = true;
        IsSoundMenuOpen = false;
        IsPowerMenuOpen = false;
        IsWifiMenuOpen = false;
        IsPomodoroMenuOpen = false;
        InputStatusText = "Loading input devices...";
        await RefreshAudioControlsAsync();
    }

    private void OpenPomodoroMenu()
    {
        IsPomodoroMenuOpen = true;
        IsInputMenuOpen = false;
        IsSoundMenuOpen = false;
        IsPowerMenuOpen = false;
        IsWifiMenuOpen = false;
        RefreshPomodoroHistory();
    }

    private async Task SelectAudioDeviceAsync(AudioDeviceModel? device)
    {
        if (device is null)
        {
            return;
        }

        SoundStatusText = $"Switching to {device.Name}...";
        var changed = await _systemStatusService.SetDefaultAudioDeviceAsync(device);
        SoundStatusText = changed
            ? $"{device.Name} is now the default {device.Kind.ToString().ToLowerInvariant()} device."
            : $"Couldn't switch to {device.Name}. Windows may have rejected the device change.";
        InputStatusText = SoundStatusText;

        await RefreshAudioControlsAsync();
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
            IsWifiPasswordPromptVisible = true;
            WifiStatusText = $"Enter the password for {network.Ssid}.";
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

    private async Task RefreshWifiNetworksAsync()
    {
        var networks = await _systemStatusService.GetAvailableWifiNetworksAsync();
        AvailableWifiNetworks.Clear();
        foreach (var network in networks)
        {
            AvailableWifiNetworks.Add(network);
        }

        WifiStatusText = AvailableWifiNetworks.Count == 0
            ? "No Wi-Fi networks available right now."
            : string.IsNullOrWhiteSpace(WifiStatusText) || WifiStatusText.StartsWith("Scanning", StringComparison.OrdinalIgnoreCase)
                ? "Choose a network to connect."
                : WifiStatusText;
    }

    private async Task RefreshAudioControlsAsync()
    {
        var outputDevicesTask = _systemStatusService.GetAudioDevicesAsync(AudioDeviceKind.Output);
        var inputDevicesTask = _systemStatusService.GetAudioDevicesAsync(AudioDeviceKind.Input);
        var sessionsTask = _systemStatusService.GetAppVolumeSessionsAsync();

        await Task.WhenAll(outputDevicesTask, inputDevicesTask, sessionsTask);

        OutputDevices.Clear();
        foreach (var device in outputDevicesTask.Result)
        {
            OutputDevices.Add(device);
        }

        InputDevices.Clear();
        foreach (var device in inputDevicesTask.Result)
        {
            InputDevices.Add(device);
        }

        AppVolumeSessions.Clear();
        foreach (var session in sessionsTask.Result)
        {
            AppVolumeSessions.Add(new AppVolumeSessionViewModel(session, _systemStatusService.SetAppVolume));
        }

        SoundStatusText = OutputDevices.Count == 0 && InputDevices.Count == 0
            ? "No active audio devices were reported by Windows."
            : "Choose an output device or tune active app volumes.";
        InputStatusText = InputDevices.Count == 0
            ? "No active input devices were reported by Windows."
            : "Choose the default microphone for recording and calls.";
    }

    private void CancelWifiPasswordPrompt()
    {
        SelectedWifiNetwork = null;
        WifiPassword = string.Empty;
        IsWifiPasswordPromptVisible = false;
    }

    private void OpenWifiSettingsApp()
    {
        IsWifiMenuOpen = false;
        _shellSettingsService.Show(ShellSettingsSection.Wifi);
    }

    private void ToggleGameMode()
    {
        _gameModeService.SetAutoEnabled(!_gameModeService.AutoEnable);
        _windowLayoutService.RefreshActiveWorkspaceLayout();
        OnPropertyChanged(nameof(IsGameModeAutoEnabled));
        OnPropertyChanged(nameof(IsGameModeEffective));
        OnPropertyChanged(nameof(GameModeSummary));
        OnPropertyChanged(nameof(GameModeDetail));
    }

    private void StartPomodoro()
    {
        _pomodoroService.Start();
        RefreshPomodoroHistory();
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
        else
        {
            _pomodoroService.Start();
        }
    }

    private void AdjustPomodoroLength(int deltaMinutes)
    {
        _pomodoroService.SetSessionLength(_pomodoroService.SessionLengthMinutes + deltaMinutes);
        OnPropertyChanged(nameof(PomodoroSessionLengthMinutes));
        OnPropertyChanged(nameof(PomodoroRemainingText));
    }

    private void AdjustPomodoroBreakLength(int deltaMinutes)
    {
        _pomodoroService.SetBreakLength(_pomodoroService.BreakLengthMinutes + deltaMinutes);
        OnPropertyChanged(nameof(PomodoroBreakLengthMinutes));
        if (_pomodoroService.Phase == PomodoroPhaseKind.Break && !_pomodoroService.IsVisible)
        {
            OnPropertyChanged(nameof(PomodoroRemainingText));
        }
    }

    private void SetPomodoroHistoryRange(PomodoroHistoryRangeKind range)
    {
        if (_selectedPomodoroHistoryRange == range)
        {
            return;
        }

        _selectedPomodoroHistoryRange = range;
        OnPropertyChanged(nameof(PomodoroSummaryText));
        OnPropertyChanged(nameof(PomodoroHistoryRangeWeekBackground));
        OnPropertyChanged(nameof(PomodoroHistoryRangeMonthBackground));
        OnPropertyChanged(nameof(IsPomodoroWeekView));
        OnPropertyChanged(nameof(IsPomodoroMonthView));
        RefreshPomodoroHistory();
    }

    private void RefreshPomodoroHistory()
    {
        var buckets = _pomodoroService.GetFocusBuckets(_selectedPomodoroHistoryRange);
        var maxSeconds = Math.Max(1, buckets.Max(bucket => bucket.FocusedSeconds));
        _lastPomodoroHistoryRefreshMinute = _pomodoroService.ElapsedSeconds / 60;
        PomodoroFocusBars.Clear();
        PomodoroHeatmapCells.Clear();

        if (_selectedPomodoroHistoryRange == PomodoroHistoryRangeKind.Week)
        {
            foreach (var bucket in buckets)
            {
                var height = bucket.FocusedSeconds <= 0
                    ? 8d
                    : 12d + (76d * bucket.FocusedSeconds / maxSeconds);

                PomodoroFocusBars.Add(new PomodoroFocusBarViewModel
                {
                    Label = bucket.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd"),
                    Summary = bucket.FocusedSeconds <= 0 ? "0m" : $"{Math.Round(bucket.FocusedSeconds / 60d):0}m",
                    Height = Math.Round(height, 1),
                    Width = 46,
                    IsToday = bucket.Date == DateOnly.FromDateTime(DateTime.Today),
                    FillBrush = bucket.Date == DateOnly.FromDateTime(DateTime.Today) ? "#D8B4FE" : "#A78BFA",
                    ToolTip = BuildPomodoroTooltip(bucket)
                });
            }
        }
        else
        {
            var firstDate = buckets.Min(bucket => bucket.Date);
            var lastDate = buckets.Max(bucket => bucket.Date);
            var alignedStart = firstDate.AddDays(-(int)firstDate.DayOfWeek);
            var alignedEnd = lastDate.AddDays(6 - (int)lastDate.DayOfWeek);
            var totalDays = alignedEnd.DayNumber - alignedStart.DayNumber + 1;
            PomodoroHeatmapColumnCount = Math.Max(1, totalDays / 7);
            var bucketMap = buckets.ToDictionary(bucket => bucket.Date, bucket => bucket);

            for (var offset = 0; offset < totalDays; offset++)
            {
                var date = alignedStart.AddDays(offset);
                var isPlaceholder = date < firstDate || date > lastDate;
                var focusedSeconds = !isPlaceholder && bucketMap.TryGetValue(date, out var bucket)
                    ? bucket.FocusedSeconds
                    : 0;

                PomodoroHeatmapCells.Add(new PomodoroHeatCellViewModel
                {
                    IsPlaceholder = isPlaceholder,
                    IsToday = date == DateOnly.FromDateTime(DateTime.Today),
                    FillBrush = isPlaceholder ? "#00000000" : GetPomodoroHeatmapBrush(focusedSeconds, maxSeconds),
                    ToolTip = isPlaceholder
                        ? string.Empty
                        : BuildPomodoroTooltip(new PomodoroFocusBucket(date, focusedSeconds))
                });
            }
        }

        PomodoroTotalFocusText = FormatFocusSummary(buckets.Sum(bucket => bucket.FocusedSeconds));
    }

    private static string BuildPomodoroTooltip(PomodoroFocusBucket bucket)
    {
        var summary = bucket.FocusedSeconds <= 0
            ? "No focus logged"
            : FormatFocusSummary(bucket.FocusedSeconds);
        return $"{bucket.Date:ddd, dd MMM}: {summary}";
    }

    private static string GetPomodoroHeatmapBrush(int focusedSeconds, int maxSeconds)
    {
        if (focusedSeconds <= 0 || maxSeconds <= 0)
        {
            return "#14000000";
        }

        var ratio = focusedSeconds / (double)maxSeconds;
        if (ratio >= 0.85d)
        {
            return "#D8B4FE";
        }

        if (ratio >= 0.6d)
        {
            return "#C084FC";
        }

        if (ratio >= 0.35d)
        {
            return "#A78BFA";
        }

        return "#7C3AED";
    }

    private void CloseAllPanels()
    {
        IsPowerMenuOpen = false;
        IsWifiMenuOpen = false;
        IsSoundMenuOpen = false;
        IsInputMenuOpen = false;
        IsPomodoroMenuOpen = false;
        IsEditMode = false;
        _appStateService.IsControlCenterOpen = false;
    }

    private static readonly ControlCenterWidgetKind[] DefaultWidgetOrder =
    [
        ControlCenterWidgetKind.GameMode,
        ControlCenterWidgetKind.Connectivity,
        ControlCenterWidgetKind.Power,
        ControlCenterWidgetKind.Media,
        ControlCenterWidgetKind.Display,
        ControlCenterWidgetKind.Sound,
        ControlCenterWidgetKind.Actions,
        ControlCenterWidgetKind.Pomodoro,
        ControlCenterWidgetKind.Explorer
    ];

    private void LoadWidgetOrder()
    {
        ActiveWidgets.Clear();
        HiddenWidgets.Clear();
        var order = _appStateService.Config.ControlCenter.WidgetOrder;
        var hidden = _appStateService.Config.ControlCenter.HiddenWidgets ?? [];

        if (order == null || order.Count == 0)
        {
            foreach (var w in DefaultWidgetOrder)
            {
                if (hidden.Contains(w))
                    HiddenWidgets.Add(w);
                else
                    ActiveWidgets.Add(w);
            }
        }
        else
        {
            // Ensure any new widget kinds that were added after the user saved are included
            var allKnown = new HashSet<ControlCenterWidgetKind>(order);
            foreach (var w in DefaultWidgetOrder)
            {
                if (!allKnown.Contains(w) && !hidden.Contains(w))
                    order.Add(w);
            }

            foreach (var widget in order)
            {
                if (hidden.Contains(widget))
                    HiddenWidgets.Add(widget);
                else
                    ActiveWidgets.Add(widget);
            }
        }
    }

    private async Task MoveWidgetAsync(Tuple<int, int>? indices)
    {
        if (indices is null)
        {
            return;
        }

        var oldIndex = indices.Item1;
        var newIndex = indices.Item2;
        if (oldIndex < 0 || oldIndex >= ActiveWidgets.Count || newIndex < 0 || newIndex >= ActiveWidgets.Count || oldIndex == newIndex)
        {
            return;
        }

        var widget = ActiveWidgets[oldIndex];
        ActiveWidgets.RemoveAt(oldIndex);
        ActiveWidgets.Insert(newIndex, widget);

        await SaveWidgetLayoutAsync();
    }

    private async Task ToggleWidgetVisibilityAsync(ControlCenterWidgetKind widget)
    {
        if (ActiveWidgets.Contains(widget))
        {
            ActiveWidgets.Remove(widget);
            HiddenWidgets.Add(widget);
        }
        else if (HiddenWidgets.Contains(widget))
        {
            HiddenWidgets.Remove(widget);
            ActiveWidgets.Add(widget);
        }

        await SaveWidgetLayoutAsync();
    }

    private Task SaveWidgetLayoutAsync()
    {
        // Build full order: active widgets first, then hidden ones (to preserve ordering)
        var fullOrder = ActiveWidgets.Concat(HiddenWidgets).ToList();
        return SaveControlCenterConfigAsync(config => new ControlCenterConfig
        {
            InputWidgetPlacement = config.InputWidgetPlacement,
            BarLayout = config.BarLayout,
            ShowMediaPill = config.ShowMediaPill,
            ShowPomodoroPill = config.ShowPomodoroPill,
            MinimalModeTitles = config.MinimalModeTitles,
            WidgetOrder = fullOrder,
            HiddenWidgets = HiddenWidgets.ToList()
        });
    }

    private async Task SaveControlCenterConfigAsync(Func<ControlCenterConfig, ControlCenterConfig> update, CancellationToken cancellationToken = default)
    {
        var updatedConfig = new AppConfig
        {
            Theme = _appStateService.Config.Theme,
            Logging = _appStateService.Config.Logging,
            Animations = _appStateService.Config.Animations,
            Performance = _appStateService.Config.Performance,
            ControlCenter = update(_appStateService.Config.ControlCenter),
            Windowing = _appStateService.Config.Windowing,
            Launcher = _appStateService.Config.Launcher,
            Terminal = _appStateService.Config.Terminal,
            Notifications = _appStateService.Config.Notifications,
            Session = _appStateService.Config.Session,
            Startup = _appStateService.Config.Startup,
            Hotkeys = _appStateService.Config.Hotkeys,
            GameMode = _appStateService.Config.GameMode
        };

        _appStateService.Config = updatedConfig;
        await _configurationService.SaveAsync(updatedConfig, cancellationToken);
    }

    private static string FormatDuration(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.TotalHours >= 1d
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static string FormatFocusSummary(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var time = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)time.TotalHours}h {time.Minutes:00}m";
    }
}

public sealed class AppVolumeSessionViewModel : ObservableObjectBase
{
    private readonly Action<string, double> _setVolume;
    private double _volumePercent;

    public AppVolumeSessionViewModel(AppVolumeSessionModel model, Action<string, double> setVolume)
    {
        _setVolume = setVolume;
        Id = model.Id;
        DisplayName = model.DisplayName;
        ProcessName = model.ProcessName;
        IconPath = model.IconPath;
        IsMuted = model.IsMuted;
        _volumePercent = model.VolumePercent;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string? ProcessName { get; }

    public string? IconPath { get; }

    public bool IsMuted { get; }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (SetProperty(ref _volumePercent, value))
            {
                _setVolume(Id, value);
            }
        }
    }
}
