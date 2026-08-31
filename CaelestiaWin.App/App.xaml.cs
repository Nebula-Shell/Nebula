using System.Windows;
using System.Windows.Threading;
using CaelestiaWin.App.Services;
using CaelestiaWin.App.Startup;
using CaelestiaWin.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace CaelestiaWin.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _serviceProvider = AppBootstrapper.Build(e.Args);
        var logService = _serviceProvider.GetRequiredService<IDiagnosticLogService>();
        var crashRecoveryService = _serviceProvider.GetRequiredService<CrashRecoveryService>();
        RegisterExceptionHandlers(crashRecoveryService);
        TryElevateProcessPriority(logService);

        try
        {
            var startupOrchestrator = _serviceProvider.GetRequiredService<StartupOrchestrator>();
            await startupOrchestrator.InitializeAsync();

            var appStateService = _serviceProvider.GetRequiredService<IAppStateService>();
            var shellOverlayWindow = _serviceProvider.GetRequiredService<CaelestiaWin.UI.Views.ShellOverlayWindow>();
            var focusOutlineWindow = _serviceProvider.GetRequiredService<CaelestiaWin.UI.Views.FocusOutlineWindow>();
            var useNativeDesktopWallpaper =
                string.IsNullOrWhiteSpace(appStateService.Config.Theme.WallpaperPath)
                && appStateService.IsExplorerRunning;

            if (useNativeDesktopWallpaper)
            {
                MainWindow = shellOverlayWindow;
            }
            else
            {
                var desktopHostWindow = _serviceProvider.GetRequiredService<CaelestiaWin.UI.Views.DesktopHostWindow>();
                MainWindow = desktopHostWindow;
                desktopHostWindow.Show();
            }

            focusOutlineWindow.Show();
            shellOverlayWindow.Show();
        }
        catch (Exception exception)
        {
            crashRecoveryService.HandleCriticalFailure("Nebula Shell failed during startup.", exception, preferSafeMode: true);
            TryRestoreExplorerAfterStartupFailure(logService);
            MessageBox.Show(
                "Nebula Shell could not start. Check the log file in %LocalAppData%\\NebulaShell\\logs for details.",
                "Startup Failure",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            _serviceProvider.GetRequiredService<IShellLifetimeService>().AllowExit();
            _serviceProvider.GetRequiredService<StartupOrchestrator>().Shutdown();
            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }

    private void RegisterExceptionHandlers(CrashRecoveryService crashRecoveryService)
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            crashRecoveryService.HandleCriticalFailure("Unhandled dispatcher exception.", eventArgs.Exception, preferSafeMode: true);
            eventArgs.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                crashRecoveryService.HandleCriticalFailure("Unhandled AppDomain exception.", exception, preferSafeMode: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            crashRecoveryService.HandleCriticalFailure("Unobserved task exception.", eventArgs.Exception, preferSafeMode: true);
            eventArgs.SetObserved();
        };
    }

    private void TryRestoreExplorerAfterStartupFailure(IDiagnosticLogService logService)
    {
        if (_serviceProvider is null)
        {
            return;
        }

        try
        {
            var explorerIntegrationService = _serviceProvider.GetRequiredService<IExplorerIntegrationService>();
            if (!explorerIntegrationService.IsExplorerRunning)
            {
                _ = explorerIntegrationService.StartExplorerShell();
            }
        }
        catch (Exception explorerException)
        {
            logService.Error("Failed to restore Explorer after Nebula startup failure.", explorerException);
        }
    }

    private static void TryElevateProcessPriority(IDiagnosticLogService logService)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityBoostEnabled = true;
            if (process.PriorityClass != ProcessPriorityClass.High)
            {
                process.PriorityClass = ProcessPriorityClass.High;
            }

            logService.Info("Nebula Shell process priority elevated.", new Dictionary<string, object?>
            {
                ["priorityClass"] = process.PriorityClass.ToString(),
                ["priorityBoostEnabled"] = process.PriorityBoostEnabled
            });
        }
        catch (Exception exception)
        {
            logService.Warn("Nebula could not elevate its process priority.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }
}
