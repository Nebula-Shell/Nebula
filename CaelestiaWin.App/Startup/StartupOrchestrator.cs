using CaelestiaWin.App.Services;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Platform.Windows.Services;
using System.Runtime;

namespace CaelestiaWin.App.Startup;

public sealed class StartupOrchestrator(
    IConfigurationService configurationService,
    IAppStateService appStateService,
    IThemeManager themeManager,
    IGlobalHotkeyService globalHotkeyService,
    IShellCommandService shellCommandService,
    IActiveWindowService activeWindowService,
    IAppDiscoveryService appDiscoveryService,
    ISystemStatusService systemStatusService,
    ISystemTrayService systemTrayService,
    IMediaService mediaService,
    IExternalNotificationListenerService externalNotificationListenerService,
    IToastNotificationService toastNotificationService,
    INotificationService notificationService,
    ISessionService sessionService,
    IWorkspaceService workspaceService,
    IWindowActionService windowActionService,
    IWindowLayoutService windowLayoutService,
    IGameModeService gameModeService,
    IMonitorService monitorService,
    IExplorerIntegrationService explorerIntegrationService,
    IStartupService startupService,
    RuntimeOptions runtimeOptions,
    ILoggerService logService)
{
    private CancellationTokenSource? _windowReflowCts;
    private int _isReflowing;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var configLoadResult = await configurationService.LoadAsync(cancellationToken);
        logService.Configure(configLoadResult.Config.Logging.Level, configLoadResult.Config.Logging.MaxFileSizeMb);
        runtimeOptions.RestartOnCrash = configLoadResult.Config.Startup.RestartOnCrash;

        appStateService.Config = configLoadResult.Config;
        appStateService.IsSafeMode = runtimeOptions.IsSafeMode;
        appStateService.IsLauncherOpen = !runtimeOptions.IsSafeMode && configLoadResult.Config.Startup.StartLauncherOpen;
        appStateService.IsControlCenterOpen = !runtimeOptions.IsSafeMode && configLoadResult.Config.Startup.StartControlCenterOpen;
        appStateService.IsNotificationCenterOpen = false;
        appStateService.IsOverviewOpen = false;
        appStateService.IsExplorerRunning = explorerIntegrationService.IsExplorerRunning;

        logService.Info("Nebula Shell startup configuration loaded.", new Dictionary<string, object?>
        {
            ["safeMode"] = runtimeOptions.IsSafeMode,
            ["explorerRunning"] = appStateService.IsExplorerRunning,
            ["monitorCount"] = monitorService.GetMonitors().Count
        });

        TryStopExplorerShell(configLoadResult.Config);
        ScheduleExplorerOwnershipChecks(configLoadResult.Config);
        themeManager.ApplyTheme(configLoadResult.Config);
        TryInitializeToasts(configLoadResult.Config);
        ApplyStartupRegistration(configLoadResult.Config);
        _ = Task.Run(() => ShellAssetCache.RunSafeCleanup(logService), CancellationToken.None);

        foreach (var warning in configLoadResult.Warnings)
        {
            logService.Warn(warning);
            notificationService.Push(
                "Config warning",
                warning,
                kind: NotificationKind.Warning,
                source: "Config");
        }

        if (!appStateService.IsExplorerRunning)
        {
            logService.Warn("Explorer is not running. Nebula will use reduced shell assumptions for app launches and paths.");
            notificationService.Push(
                "Explorer is not running",
                "Nebula is operating without Explorer shell services. Recovery actions remain available in the control center.",
                kind: NotificationKind.Warning,
                source: "Shell");
        }

        if (runtimeOptions.IsSafeMode)
        {
            logService.Warn("Nebula Shell started in safe mode. Hotkeys and advanced window orchestration are disabled.");
            notificationService.Push(
                "Safe mode active",
                "Hotkeys and advanced window management are disabled so the shell can recover safely.",
                kind: NotificationKind.Warning,
                source: "Recovery");
            return;
        }

        TryRegisterHotkeys(configLoadResult.Config);
        TryStartActiveWindowTracking(configLoadResult.Config);
        activeWindowService.CurrentWindowChanged -= OnCurrentWindowChanged;
        activeWindowService.CurrentWindowChanged += OnCurrentWindowChanged;
        activeWindowService.WindowsChanged -= OnWindowsChanged;
        activeWindowService.WindowsChanged += OnWindowsChanged;

        workspaceService.Synchronize();
        await sessionService.RestoreAsync(cancellationToken);
        _ = windowActionService.EnsureForegroundWindowUsesShellWorkArea(configLoadResult.Config.Windowing.TopReservedSpace);
        windowLayoutService.RefreshActiveWorkspaceLayout();
        ScheduleInitialLayoutRefresh();
        TryStartOptionalModule("Game mode", "Game Center", gameModeService.Start);
        TryStartOptionalModule("System status", "System telemetry", systemStatusService.Start);
        TryStartOptionalModule("System tray", "Tray bridge", systemTrayService.Start);
        TryStartOptionalModule("Media service", "Media sessions", mediaService.Start);
        TryStartOptionalModule("App notifications", "Windows notification listener", externalNotificationListenerService.Start);

        if (configLoadResult.Config.Notifications.ShowStartupStatus)
        {
            notificationService.Push(
                "Nebula Shell is ready",
                "Core shell services initialized. Real subsystem events will appear here.",
                kind: NotificationKind.Success,
                source: "Startup");
        }

        if (configLoadResult.Config.Launcher.PreloadOnStartup)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await appDiscoveryService.GetAppsAsync(false, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logService.Error("Launcher preloading failed.", exception);
                    notificationService.Push(
                        "Launcher cache failed",
                        "App discovery could not be preloaded. The launcher will retry when opened.",
                        kind: NotificationKind.Warning,
                        source: "Launcher");
                }
            }, CancellationToken.None);
        }
    }

    public void Shutdown()
    {
        try
        {
            sessionService.SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logService.Error("Session persistence failed during shutdown.", exception);
        }

        globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
        activeWindowService.CurrentWindowChanged -= OnCurrentWindowChanged;
        activeWindowService.WindowsChanged -= OnWindowsChanged;
        globalHotkeyService.UnregisterAll();
        activeWindowService.Stop();
        gameModeService.Stop();
        systemTrayService.Stop();
        systemStatusService.Stop();
        mediaService.Stop();
    }

    private void TryRegisterHotkeys(Core.Models.AppConfig config)
    {
        try
        {
            globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
            globalHotkeyService.HotkeyPressed += OnHotkeyPressed;
            globalHotkeyService.RegisterBindings(config.Hotkeys.Bindings);

            foreach (var registrationFailure in globalHotkeyService.FailedRegistrations)
            {
                logService.Warn(registrationFailure);
            }

            if (globalHotkeyService.FailedRegistrations.Count > 0)
            {
                notificationService.Push(
                    "Some hotkeys did not register",
                    $"{globalHotkeyService.FailedRegistrations.Count} shortcut(s) are unavailable, usually because Windows or another app reserved them.",
                    kind: NotificationKind.Warning,
                    source: "Hotkeys");
            }
        }
        catch (Exception exception)
        {
            logService.Error("Global hotkey registration failed.", exception);
            notificationService.Push(
                "Hotkeys disabled",
                "Global hotkey registration failed. You can still interact with visible shell UI.",
                kind: NotificationKind.Error,
                source: "Hotkeys");
        }
    }

    private void TryInitializeToasts(Core.Models.AppConfig config)
    {
        if (!config.Notifications.EnableWindowsToasts)
        {
            logService.Info("Windows toast notifications are disabled by config.");
            return;
        }

        try
        {
            toastNotificationService.Initialize();
        }
        catch (Exception exception)
        {
            logService.Warn("Windows toast notification bridge failed to initialize.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
            notificationService.Push(
                "Windows toasts unavailable",
                "Nebula will continue showing in-shell notification toasts.",
                kind: NotificationKind.Warning,
                source: "Notifications",
                showToast: false);
        }
    }

    private void TryStartActiveWindowTracking(Core.Models.AppConfig config)
    {
        if (!config.Startup.TrackForegroundWindow)
        {
            return;
        }

        try
        {
            activeWindowService.Start();
        }
        catch (Exception exception)
        {
            logService.Error("Foreground window tracking failed to initialize.", exception);
            notificationService.Push(
                "Window tracking unavailable",
                "Active window title and automatic layout updates may be degraded.",
                kind: NotificationKind.Error,
                source: "Windowing");
        }
    }

    private void ApplyStartupRegistration(Core.Models.AppConfig config)
    {
        try
        {
            var startOnLogin = config.Startup.StartOnLogin || config.Startup.EnableAutoStart;
            startupService.SetEnabled(startOnLogin, runtimeOptions.ExecutablePath, string.Empty);
        }
        catch (Exception exception)
        {
            logService.Error("Auto-start registration failed.", exception);
            notificationService.Push(
                "Startup registration failed",
                "Nebula could not update the login startup entry. The shell will continue running.",
                kind: NotificationKind.Warning,
                source: "Startup");
        }
    }

    private void TryStartOptionalModule(string name, string source, Action start)
    {
        try
        {
            start();
            logService.Info($"{name} module started.");
        }
        catch (Exception exception)
        {
            logService.Error($"{name} module failed to start.", exception);
            notificationService.Push(
                $"{name} unavailable",
                "This subsystem failed to start and has been isolated so the shell can keep running.",
                kind: NotificationKind.Error,
                source: source);
        }
    }

    private void TryStopExplorerShell(Core.Models.AppConfig config)
    {
        if (runtimeOptions.IsSafeMode)
        {
            logService.Info("Safe mode is active; Explorer will not be stopped on launch.");
            return;
        }

        if (!config.Startup.StopExplorerOnLaunch)
        {
            logService.Info("Explorer stop-on-launch is disabled by config.");
            return;
        }

        try
        {
            var wasRunning = explorerIntegrationService.IsExplorerRunning;
            var stopped = explorerIntegrationService.StopExplorerShell();
            appStateService.IsExplorerRunning = explorerIntegrationService.IsExplorerRunning;

            logService.Info("Explorer stop-on-launch completed.", new Dictionary<string, object?>
            {
                ["wasRunning"] = wasRunning,
                ["stopped"] = stopped,
                ["explorerRunning"] = appStateService.IsExplorerRunning
            });
        }
        catch (Exception exception)
        {
            appStateService.IsExplorerRunning = explorerIntegrationService.IsExplorerRunning;
            logService.Error("Explorer stop-on-launch failed. Continuing Nebula startup.", exception);
        }
    }

    private void ScheduleExplorerOwnershipChecks(Core.Models.AppConfig config)
    {
        if (runtimeOptions.IsSafeMode || !config.Startup.StopExplorerOnLaunch)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var delays = new[]
            {
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            };

            foreach (var delay in delays)
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (!explorerIntegrationService.IsExplorerRunning)
                {
                    appStateService.IsExplorerRunning = false;
                    continue;
                }

                logService.Warn("Explorer respawn detected after startup; stopping it again for Nebula shell ownership.");
                var stopped = explorerIntegrationService.StopExplorerShell();
                appStateService.IsExplorerRunning = explorerIntegrationService.IsExplorerRunning;
                logService.Info("Delayed Explorer ownership check completed.", new Dictionary<string, object?>
                {
                    ["stopped"] = stopped,
                    ["explorerRunning"] = appStateService.IsExplorerRunning
                });
            }
        }, CancellationToken.None);
    }

    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs eventArgs)
    {
        try
        {
            await shellCommandService.ExecuteHotkeyAsync(eventArgs.Binding);
        }
        catch (Exception exception)
        {
            logService.Error("Hotkey dispatch failed.", exception, new Dictionary<string, object?>
            {
                ["gesture"] = eventArgs.Binding.Gesture,
                ["action"] = eventArgs.Binding.Action
            });
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs eventArgs)
    {
        ScheduleWindowReflow(immediate: true);
    }

    private void OnCurrentWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        if (eventArgs.Window is null)
        {
            return;
        }

        ScheduleWindowReflow(immediate: true);
    }

    private void ScheduleInitialLayoutRefresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var delay in GetInitialRefreshDelays())
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    ReflowActiveWorkspace();
                }
            }
            catch (Exception exception)
            {
                logService.Warn("Initial delayed layout refresh failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }, CancellationToken.None);
    }

    private void ScheduleWindowReflow(bool immediate)
    {
        _windowReflowCts?.Cancel();
        _windowReflowCts?.Dispose();
        _windowReflowCts = new CancellationTokenSource();
        var token = _windowReflowCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (immediate)
                {
                    ReflowActiveWorkspace();
                }

                foreach (var delay in GetReflowDelays())
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    ReflowActiveWorkspace();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logService.Warn("Delayed window lifecycle reflow failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }, CancellationToken.None);
    }

    private void ReflowActiveWorkspace()
    {
        if (Interlocked.Exchange(ref _isReflowing, 1) == 1)
        {
            return;
        }

        try
        {
            if (gameModeService.IsFullscreenGameRunning)
            {
                return;
            }

            _ = windowActionService.EnsureForegroundWindowUsesShellWorkArea(appStateService.Config.Windowing.TopReservedSpace);
            workspaceService.Synchronize();
            windowLayoutService.RefreshActiveWorkspaceLayout();
        }
        catch (Exception exception)
        {
            logService.Error("Window reflow failed.", exception);
        }
        finally
        {
            Volatile.Write(ref _isReflowing, 0);
        }
    }

    private static IReadOnlyList<int> GetInitialRefreshDelays()
    {
        return IsRuntimeUnderPressure()
            ? [500, 1400]
            : [250, 700, 1300];
    }

    private static IReadOnlyList<int> GetReflowDelays()
    {
        return IsRuntimeUnderPressure()
            ? [220, 900]
            : [90, 260, 700];
    }

    private static bool IsRuntimeUnderPressure()
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            if (memoryInfo.HighMemoryLoadThresholdBytes > 0
                && memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * 9 / 10)
            {
                return true;
            }

            if (memoryInfo.TotalAvailableMemoryBytes > 0
                && memoryInfo.TotalCommittedBytes >= memoryInfo.TotalAvailableMemoryBytes * 8 / 10)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
