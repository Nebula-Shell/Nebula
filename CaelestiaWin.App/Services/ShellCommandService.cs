using System.Windows;
using System.Diagnostics;
using System.IO;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.App.Services;

public sealed class ShellCommandService(
    IAppStateService appStateService,
    IShellOverlayWindowService shellOverlayWindowService,
    IShellSettingsService shellSettingsService,
    IFileExplorerService fileExplorerService,
    IWorkspaceService workspaceService,
    IWindowActionService windowActionService,
    IWindowNavigationService windowNavigationService,
    IWindowLayoutService windowLayoutService,
    IOverviewService overviewService,
    ISystemStatusService systemStatusService,
    IMediaService mediaService,
    ISnippingToolService snippingToolService,
    IAppDiscoveryService appDiscoveryService,
    IExplorerIntegrationService explorerIntegrationService,
    IShellLifetimeService shellLifetimeService,
    IDiagnosticLogService logService) : IShellCommandService
{
    private const int WorkspaceCount = 11;
    private const int NormalWorkspaceCount = 8;
    private const int DiscordWorkspaceIndex = 9;
    private const int SpotifyWorkspaceIndex = 10;
    private const int GitHubDesktopWorkspaceIndex = 11;
    private int _lastWorkspaceBeforeQuickAccess = 1;
    private nint _lastWindowBeforeQuickAccess = nint.Zero;
    private readonly Dictionary<int, CancellationTokenSource> _quickAccessSyncTokens = [];

    public async Task ExecuteHotkeyAsync(HotkeyBindingConfig binding, CancellationToken cancellationToken = default)
    {
        switch (binding.Action)
        {
            case HotkeyActionKind.ToggleLauncher:
                ToggleLauncher();
                break;
            case HotkeyActionKind.OpenTerminal:
                await OpenTerminalAsync(cancellationToken);
                break;
            case HotkeyActionKind.OpenFileExplorer:
                await OpenFileExplorerAsync(null, cancellationToken);
                break;
            case HotkeyActionKind.ToggleControlCenter:
                ToggleControlCenter();
                break;
            case HotkeyActionKind.ToggleNotificationCenter:
                ToggleNotificationCenter();
                break;
            case HotkeyActionKind.ToggleClipboardHistory:
                ToggleClipboardHistory();
                break;
            case HotkeyActionKind.ToggleSettingsPanel:
                ToggleSettingsPanel();
                break;
            case HotkeyActionKind.ToggleFocusedWindowFullscreen:
                ToggleFocusedWindowFullscreen();
                break;
            case HotkeyActionKind.ToggleFocusedWindowFloat:
                _ = windowActionService.ToggleFocusedWindowFloat();
                break;
            case HotkeyActionKind.CloseFocusedWindow:
                CloseFocusedWindow();
                break;
            case HotkeyActionKind.SwitchWorkspace when binding.Workspace is int workspaceIndex:
                SwitchWorkspace(workspaceIndex);
                break;
            case HotkeyActionKind.CycleWorkspacePrevious:
                CycleWorkspace(-1);
                break;
            case HotkeyActionKind.CycleWorkspaceNext:
                CycleWorkspace(1);
                break;
            case HotkeyActionKind.MoveWindowToWorkspacePrevious:
                MoveFocusedWindowToAdjacentWorkspace(-1);
                break;
            case HotkeyActionKind.MoveWindowToWorkspaceNext:
                MoveFocusedWindowToAdjacentWorkspace(1);
                break;
            case HotkeyActionKind.MoveWindowToWorkspace when binding.Workspace is int targetWorkspace:
                MoveFocusedWindowToWorkspace(targetWorkspace);
                break;
            case HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection direction:
                FocusWindow(direction);
                break;
            case HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection moveDirection:
                MoveWindow(moveDirection);
                break;
            case HotkeyActionKind.ToggleOverview:
                ToggleOverview();
                break;
            case HotkeyActionKind.ToggleDiscordDesktop:
                await ToggleDiscordDesktopAsync(cancellationToken);
                break;
            case HotkeyActionKind.ToggleSpotifyDesktop:
                await ToggleSpotifyDesktopAsync(cancellationToken);
                break;
            case HotkeyActionKind.ToggleGitHubDesktop:
                await ToggleGitHubDesktopAsync(cancellationToken);
                break;
            case HotkeyActionKind.CaptureRegion:
                await CaptureRegionAsync(cancellationToken);
                break;
            case HotkeyActionKind.VolumeUp:
                systemStatusService.AdjustVolume(5);
                break;
            case HotkeyActionKind.VolumeDown:
                systemStatusService.AdjustVolume(-5);
                break;
            case HotkeyActionKind.ToggleMute:
                systemStatusService.ToggleMute();
                break;
            case HotkeyActionKind.MediaPlayPause:
                await mediaService.PlayPauseAsync(cancellationToken);
                break;
            case HotkeyActionKind.MediaNext:
                await mediaService.NextAsync(cancellationToken);
                break;
            case HotkeyActionKind.MediaPrevious:
                await mediaService.PreviousAsync(cancellationToken);
                break;
            case HotkeyActionKind.BrightnessUp:
                systemStatusService.AdjustBrightness(10);
                break;
            case HotkeyActionKind.BrightnessDown:
                systemStatusService.AdjustBrightness(-10);
                break;
        }
    }

    public void ToggleLauncher()
    {
        var isOpening = !appStateService.IsLauncherOpen;
        appStateService.IsLauncherOpen = isOpening;

        if (isOpening)
        {
            shellOverlayWindowService.ActivateForInput();
        }
    }

    public void ToggleControlCenter()
    {
        var isOpening = !appStateService.IsControlCenterOpen;
        appStateService.IsControlCenterOpen = isOpening;

        if (isOpening)
        {
            shellOverlayWindowService.ActivateForInput();
        }
    }

    public void ToggleNotificationCenter()
    {
        var isOpening = !appStateService.IsNotificationCenterOpen;
        appStateService.IsNotificationCenterOpen = isOpening;

        if (isOpening)
        {
            shellOverlayWindowService.ActivateForInput();
        }
    }

    public void ToggleSettingsPanel()
    {
        shellSettingsService.Toggle();
    }

    public void ToggleClipboardHistory()
    {
        var isOpening = !appStateService.IsClipboardHistoryOpen;
        appStateService.IsClipboardHistoryOpen = isOpening;

        if (isOpening)
        {
            shellOverlayWindowService.ActivateForInput();
        }
    }

    public void ToggleOverview()
    {
        if (!overviewService.IsOpen)
        {
            shellOverlayWindowService.ActivateForInput();
        }

        overviewService.Toggle();
    }

    public async Task OpenTerminalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var terminal = await windowActionService.OpenTerminalAsync(cancellationToken);
            logService.Info("Opened terminal.", new Dictionary<string, object?> { ["terminal"] = terminal });
        }
        catch (Exception exception)
        {
            logService.Error("Failed to open a terminal.", exception);
        }
    }

    public async Task OpenFileExplorerAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await fileExplorerService.OpenAsync(path, cancellationToken);
            logService.Info("Opened file explorer.", new Dictionary<string, object?>
            {
                ["path"] = path
            });
        }
        catch (Exception exception)
        {
            logService.Error("Failed to open file explorer.", exception, new Dictionary<string, object?>
            {
                ["path"] = path
            });
        }
    }

    public void CloseFocusedWindow()
    {
        if (!windowActionService.CloseFocusedWindow())
        {
            logService.Warn("Close focused window was requested but no eligible foreground window was found.");
        }
    }

    public void SwitchWorkspace(int workspaceIndex)
    {
        var currentWorkspace = workspaceService.ActiveWorkspaceIndex;
        var isQuickAccessTransition = currentWorkspace > NormalWorkspaceCount || workspaceIndex > NormalWorkspaceCount;
        var shouldRefreshLayout = !isQuickAccessTransition && !appStateService.IsForegroundFullscreen;

        if (!workspaceService.SwitchTo(workspaceIndex))
        {
            logService.Warn("Ignoring invalid workspace switch request.", new Dictionary<string, object?>
            {
                ["workspace"] = workspaceIndex
            });
            return;
        }

        if (shouldRefreshLayout)
        {
            windowLayoutService.RefreshActiveWorkspaceLayout();
        }

        overviewService.Refresh();
    }

    public void CycleWorkspace(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var current = workspaceService.ActiveWorkspaceIndex;
        if (current > NormalWorkspaceCount)
        {
            current = _lastWorkspaceBeforeQuickAccess is >= 1 and <= NormalWorkspaceCount
                ? _lastWorkspaceBeforeQuickAccess
                : 1;
        }

        var next = current + delta;
        if (next < 1)
        {
            next = NormalWorkspaceCount;
        }
        else if (next > NormalWorkspaceCount)
        {
            next = 1;
        }

        SwitchWorkspace(next);
    }

    public void MoveFocusedWindowToWorkspace(int workspaceIndex)
    {
        if (!workspaceService.MoveFocusedWindowToWorkspace(workspaceIndex))
        {
            logService.Warn("Ignoring invalid move-to-workspace request.", new Dictionary<string, object?>
            {
                ["workspace"] = workspaceIndex
            });
            return;
        }

        SwitchWorkspace(workspaceIndex);
    }

    private void MoveFocusedWindowToAdjacentWorkspace(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var current = workspaceService.ActiveWorkspaceIndex;
        if (current > NormalWorkspaceCount)
        {
            current = _lastWorkspaceBeforeQuickAccess is >= 1 and <= NormalWorkspaceCount
                ? _lastWorkspaceBeforeQuickAccess
                : 1;
        }

        var target = current + delta;
        if (target < 1)
        {
            target = NormalWorkspaceCount;
        }
        else if (target > NormalWorkspaceCount)
        {
            target = 1;
        }

        MoveFocusedWindowToWorkspace(target);
    }

    public void FocusWindow(WindowDirection direction)
    {
        if (!windowNavigationService.Focus(direction))
        {
            logService.Warn("No focus candidate was found in the requested direction.", new Dictionary<string, object?>
            {
                ["direction"] = direction
            });
        }
    }

    public void MoveWindow(WindowDirection direction)
    {
        if (!windowLayoutService.MoveFocusedWindow(direction))
        {
            logService.Warn("No movable window was found for the requested direction.", new Dictionary<string, object?>
            {
                ["direction"] = direction
            });
        }
    }

    public void ToggleFocusedWindowFullscreen()
    {
        if (!windowActionService.ToggleFocusedWindowFullscreen())
        {
            logService.Warn("No eligible foreground window was found for fullscreen toggle.");
            appStateService.IsForegroundFullscreen = false;
            return;
        }

        var isFullscreen = windowActionService.IsForegroundWindowFullscreen();
        appStateService.IsForegroundFullscreen = isFullscreen;

        if (!isFullscreen)
        {
            windowLayoutService.RefreshActiveWorkspaceLayout();
        }
    }

    public Task ToggleDiscordDesktopAsync(CancellationToken cancellationToken = default)
    {
        return ToggleQuickAccessDesktopAsync(CreateDiscordQuickAccessDefinition(), cancellationToken);
    }

    public Task CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        return snippingToolService.CaptureRegionAsync(cancellationToken);
    }

    public Task ToggleSpotifyDesktopAsync(CancellationToken cancellationToken = default)
    {
        return ToggleQuickAccessDesktopAsync(CreateSpotifyQuickAccessDefinition(), cancellationToken);
    }

    public Task ToggleGitHubDesktopAsync(CancellationToken cancellationToken = default)
    {
        return ToggleQuickAccessDesktopAsync(CreateGitHubDesktopQuickAccessDefinition(), cancellationToken);
    }

    public void ReturnToExplorerAndExit()
    {
        try
        {
            var explorerStarted = explorerIntegrationService.StartExplorerShell();
            appStateService.IsExplorerRunning = explorerIntegrationService.IsExplorerRunning || explorerStarted;
            logService.Info("Return to Explorer requested from Nebula.", new Dictionary<string, object?>
            {
                ["explorerStarted"] = explorerStarted,
                ["explorerRunning"] = appStateService.IsExplorerRunning
            });
        }
        catch (Exception exception)
        {
            logService.Error("Return to Explorer failed before shell exit.", exception);
        }

        shellLifetimeService.AllowExit();

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            application.Shutdown();
            return;
        }

        application.Dispatcher.BeginInvoke(() => application.Shutdown());
    }

    private async Task ToggleQuickAccessDesktopAsync(QuickAccessAppDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            CancelPendingQuickAccessSync(definition.WorkspaceIndex);
            var appWindows = GetQuickAccessWindows(definition);

            if (workspaceService.ActiveWorkspaceIndex == definition.WorkspaceIndex)
            {
                var targetWorkspace = Math.Clamp(_lastWorkspaceBeforeQuickAccess, 1, NormalWorkspaceCount);
                SwitchWorkspace(targetWorkspace);

                if (_lastWindowBeforeQuickAccess != nint.Zero)
                {
                    _ = windowActionService.FocusWindow(_lastWindowBeforeQuickAccess);
                }

                return;
            }

            _lastWorkspaceBeforeQuickAccess = workspaceService.ActiveWorkspaceIndex is >= 1 and <= NormalWorkspaceCount
                ? workspaceService.ActiveWorkspaceIndex
                : 1;
            _lastWindowBeforeQuickAccess = windowActionService.GetForegroundWindow()?.Handle ?? nint.Zero;

            if (appWindows.Count == 0)
            {
                await LaunchQuickAccessAppAsync(definition, cancellationToken);
                ScheduleQuickAccessSync(definition);
            }

            SwitchWorkspace(definition.WorkspaceIndex);

            if (appWindows.Count > 0)
            {
                _ = windowActionService.FocusWindow(appWindows[0].Handle);
            }
        }
        catch (Exception exception)
        {
            logService.Error($"Failed to toggle {definition.DisplayName} quick access.", exception);
        }
    }

    private IReadOnlyList<WindowDescriptor> GetQuickAccessWindows(QuickAccessAppDefinition definition)
    {
        return workspaceService.GetAllTrackedWindows()
            .Where(window => IsQuickAccessWindow(window, definition))
            .ToArray();
    }

    private async Task LaunchQuickAccessAppAsync(QuickAccessAppDefinition definition, CancellationToken cancellationToken)
    {
        var apps = await appDiscoveryService.GetAppsAsync(false, cancellationToken);
        var discoveredApp = apps
            .Where(app => MatchesQuickAccessLaunchItem(app, definition))
            .OrderByDescending(app => app.DisplayName.Equals(definition.DisplayName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (discoveredApp is not null)
        {
            _ = explorerIntegrationService.LaunchApp(discoveredApp);
            logService.Info($"Launched {definition.DisplayName} for quick access.", new Dictionary<string, object?>
            {
                ["source"] = discoveredApp.Source,
                ["path"] = discoveredApp.ResolvedTargetPath ?? discoveredApp.LaunchPath
            });
            return;
        }

        var executable = FindQuickAccessExecutable(definition);
        if (executable is not null)
        {
            _ = Process.Start(explorerIntegrationService.CreateExecutableLaunchStartInfo(executable.Value.Path, executable.Value.Arguments));
            logService.Info($"Launched {definition.DisplayName} from a known installation path.", new Dictionary<string, object?>
            {
                ["path"] = executable.Value.Path,
                ["arguments"] = executable.Value.Arguments
            });
            return;
        }

        logService.Warn($"{definition.DisplayName} quick access was requested, but the app could not be found.");
    }

    private static bool MatchesQuickAccessLaunchItem(AppLaunchItem app, QuickAccessAppDefinition definition)
    {
        return definition.SearchTerms.Any(term =>
            app.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || app.LaunchPath.Contains(term, StringComparison.OrdinalIgnoreCase)
            || app.ResolvedTargetPath?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static (string Path, string? Arguments)? FindQuickAccessExecutable(QuickAccessAppDefinition definition)
    {
        foreach (var candidate in definition.KnownLaunchCandidates)
        {
            if (File.Exists(candidate.Path))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsQuickAccessWindow(WindowDescriptor window, QuickAccessAppDefinition definition)
    {
        return definition.ProcessNames.Any(processName => string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
               || definition.SearchTerms.Any(term => window.ExecutablePath?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static QuickAccessAppDefinition CreateDiscordQuickAccessDefinition()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new QuickAccessAppDefinition(
            "discord",
            "Discord",
            DiscordWorkspaceIndex,
            ["Discord"],
            ["Discord", "DiscordCanary", "DiscordPTB"],
            [
                (Path.Combine(localAppData, "Discord", "Update.exe"), "--processStart Discord.exe"),
                (Path.Combine(localAppData, "DiscordCanary", "Update.exe"), "--processStart DiscordCanary.exe"),
                (Path.Combine(localAppData, "DiscordPTB", "Update.exe"), "--processStart DiscordPTB.exe")
            ]);
    }

    private static QuickAccessAppDefinition CreateSpotifyQuickAccessDefinition()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new QuickAccessAppDefinition(
            "spotify",
            "Spotify",
            SpotifyWorkspaceIndex,
            ["Spotify"],
            ["Spotify"],
            [
                (Path.Combine(appData, "Spotify", "Spotify.exe"), null),
                (Path.Combine(localAppData, "Microsoft", "WindowsApps", "Spotify.exe"), null)
            ]);
    }

    private static QuickAccessAppDefinition CreateGitHubDesktopQuickAccessDefinition()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new QuickAccessAppDefinition(
            "github-desktop",
            "GitHub Desktop",
            GitHubDesktopWorkspaceIndex,
            ["GitHub Desktop", "GitHubDesktop"],
            ["GitHubDesktop"],
            [
                (Path.Combine(localAppData, "GitHubDesktop", "GitHubDesktop.exe"), null),
                (Path.Combine(localAppData, "GitHubDesktop", "Update.exe"), "--processStart GitHubDesktop.exe")
            ]);
    }

    private void ScheduleQuickAccessSync(QuickAccessAppDefinition definition)
    {
        CancelPendingQuickAccessSync(definition.WorkspaceIndex);
        var cancellationTokenSource = new CancellationTokenSource();
        _quickAccessSyncTokens[definition.WorkspaceIndex] = cancellationTokenSource;
        var token = cancellationTokenSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var delays = new[]
                {
                    TimeSpan.FromMilliseconds(350),
                    TimeSpan.FromMilliseconds(900),
                    TimeSpan.FromMilliseconds(1800)
                };

                foreach (var delay in delays)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    var appWindows = GetQuickAccessWindows(definition);
                    if (appWindows.Count == 0)
                    {
                        continue;
                    }

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (token.IsCancellationRequested || workspaceService.ActiveWorkspaceIndex != definition.WorkspaceIndex)
                        {
                            return;
                        }

                        SwitchWorkspace(definition.WorkspaceIndex);
                        _ = windowActionService.FocusWindow(appWindows[0].Handle);
                    });

                    break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logService.Warn($"Delayed {definition.DisplayName} quick access synchronization failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }, CancellationToken.None);
    }

    private void CancelPendingQuickAccessSync(int workspaceIndex)
    {
        if (!_quickAccessSyncTokens.Remove(workspaceIndex, out var tokenSource))
        {
            return;
        }

        tokenSource.Cancel();
        tokenSource.Dispose();
    }

    private sealed record QuickAccessAppDefinition(
        string Key,
        string DisplayName,
        int WorkspaceIndex,
        IReadOnlyList<string> SearchTerms,
        IReadOnlyList<string> ProcessNames,
        IReadOnlyList<(string Path, string? Arguments)> KnownLaunchCandidates);
}
