using CaelestiaWin.App.Services;
using CaelestiaWin.Config.Services;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Services;
using CaelestiaWin.Hotkeys.Services;
using CaelestiaWin.Platform.Windows.Services;
using CaelestiaWin.UI.Services;
using CaelestiaWin.UI.ViewModels;
using CaelestiaWin.UI.Views;
using CaelestiaWin.Windowing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CaelestiaWin.App.Startup;

public static class AppBootstrapper
{
    public static ServiceProvider Build(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RuntimeOptions(args));
        services.AddSingleton<ICurrentProcessService>(provider => provider.GetRequiredService<RuntimeOptions>());

        services.AddSingleton<IDiagnosticLogService, DiagnosticLogService>();
        services.AddSingleton<ILoggerService>(provider => provider.GetRequiredService<IDiagnosticLogService>());
        services.AddSingleton<IConfigurationService, JsonConfigurationService>();
        services.AddSingleton<IAppStateService, AppStateService>();
        services.AddSingleton<IToastNotificationService, WindowsToastNotificationService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IRecentAppsService, RecentAppsService>();
        services.AddSingleton<IFavoriteAppsService, FavoriteAppsService>();
        services.AddSingleton<IExternalNotificationListenerService, WindowsExternalNotificationListenerService>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<IShellLifetimeService, ShellLifetimeService>();
        services.AddSingleton<CrashRecoveryService>();
        services.AddSingleton<WindowsWindowIntrospection>();
        services.AddSingleton<IVisibleWindowService, WindowsVisibleWindowService>();
        services.AddSingleton<IForegroundWindowTracker, WindowsForegroundWindowTracker>();
        services.AddSingleton<IWindowActionService, WindowsWindowActionService>();
        services.AddSingleton<IAppDiscoveryService, WindowsAppDiscoveryService>();
        services.AddSingleton<IWallpaperService, WindowsWallpaperService>();
        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
        services.AddSingleton<IMonitorService, WindowsMonitorService>();
        services.AddSingleton<IFileExplorerIndexService, WindowsFileExplorerIndexService>();
        services.AddSingleton<IFileShellContextMenuService, WindowsFileShellContextMenuService>();
        services.AddSingleton<IFileExplorerSidebarStateService, FileExplorerSidebarStateService>();
        services.AddSingleton<IShellDesktopSurfaceService, WindowsShellDesktopSurfaceService>();
        services.AddSingleton<IShellOverlayWindowService, ShellOverlayWindowService>();
        services.AddSingleton<ISystemStatusService, WindowsSystemStatusService>();
        services.AddSingleton<ISystemTrayService, WindowsSystemTrayService>();
        services.AddSingleton<IMediaArtworkResolver, WindowsMediaArtworkResolver>();
        services.AddSingleton<IMediaService, WindowsMediaService>();
        services.AddSingleton<IExplorerIntegrationService, WindowsExplorerIntegrationService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IStartupRegistrationService, WindowsStartupRegistrationService>();
        services.AddSingleton<IStartupService>(provider => (IStartupService)provider.GetRequiredService<IStartupRegistrationService>());
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IActiveWindowService, ActiveWindowService>();
        services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
        services.AddSingleton<IWindowLayoutService, WindowLayoutService>();
        services.AddSingleton<IOverviewService, OverviewService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<ILauncherSearchService, LauncherSearchService>();
        services.AddSingleton<ILauncherCommandService, LauncherCommandService>();
        services.AddSingleton<IThemeManager, ThemeManager>();
        services.AddSingleton<IWindowsAccentColorService, WindowsAccentColorService>();
        services.AddSingleton<IShellSettingsService, ShellSettingsService>();
        services.AddSingleton<ISnippingToolService, SnippingToolService>();
        services.AddSingleton<IShellCommandService, ShellCommandService>();
        services.AddSingleton<IGameModeService, GameModeService>();
        services.AddSingleton<IPomodoroService, PomodoroService>();

        services.AddSingleton<StartupOrchestrator>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<TopBarViewModel>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<ControlCenterViewModel>();
        services.AddSingleton<NotificationCenterViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<FocusOutlineViewModel>();
        services.AddSingleton<DesktopSwitchIndicatorViewModel>();
        services.AddSingleton<ShellToastViewModel>();
        services.AddSingleton<ClipboardHistoryViewModel>();
        services.AddSingleton<ShellSettingsViewModel>();
        services.AddSingleton<NebulaFileExplorerViewModel>();
        services.AddSingleton<DesktopHostWindow>();
        services.AddSingleton<ShellOverlayWindow>();
        services.AddSingleton<FocusOutlineWindow>();
        services.AddSingleton<ShellSettingsWindow>();
        services.AddSingleton<NebulaFileExplorerWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
