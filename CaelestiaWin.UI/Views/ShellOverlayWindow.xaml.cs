using System.Windows;
using System.Windows.Interop;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class ShellOverlayWindow : Window
{
    private readonly IAppStateService _appStateService;
    private readonly IMonitorService _monitorService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IShellLifetimeService _shellLifetimeService;
    private readonly IDiagnosticLogService _logService;

    public ShellOverlayWindow(
        ShellViewModel viewModel,
        IAppStateService appStateService,
        IMonitorService monitorService,
        IGlobalHotkeyService globalHotkeyService,
        IShellLifetimeService shellLifetimeService,
        IDiagnosticLogService logService)
    {
        _appStateService = appStateService;
        _monitorService = monitorService;
        _globalHotkeyService = globalHotkeyService;
        _shellLifetimeService = shellLifetimeService;
        _logService = logService;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _globalHotkeyService.AttachWindow(handle);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        ApplyMonitorBounds();
    }

    private void ApplyMonitorBounds()
    {
        if (!_appStateService.Config.Startup.ShowOnPrimaryMonitor)
        {
            return;
        }

        var primaryMonitor = _monitorService.GetPrimaryMonitor();
        if (primaryMonitor is null)
        {
            return;
        }

        WindowState = WindowState.Normal;
        Left = primaryMonitor.Bounds.Left;
        Top = primaryMonitor.Bounds.Top;
        Width = Math.Max(800, primaryMonitor.Bounds.Width);
        Height = Math.Max(600, primaryMonitor.Bounds.Height);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (_shellLifetimeService.CanExit)
        {
            return;
        }

        eventArgs.Cancel = true;
        _logService.Warn("Prevented the shell overlay window from closing unexpectedly.");
    }
}
