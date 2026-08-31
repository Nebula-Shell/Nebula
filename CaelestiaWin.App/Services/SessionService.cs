using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CaelestiaWin.Config.Helpers;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.App.Services;

public sealed class SessionService(
    IAppStateService appStateService,
    IVisibleWindowService visibleWindowService,
    IWorkspaceService workspaceService,
    IExplorerIntegrationService explorerIntegrationService,
    IDiagnosticLogService logService) : ISessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string SessionPath => ConfigurationPaths.SessionPath;

    public async Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(SessionPath);
            return await JsonSerializer.DeserializeAsync<SessionSnapshot>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception)
        {
            logService.Error("Failed to load the previous shell session.", exception);
            return null;
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!appStateService.Config.Session.SessionRestoreEnabled)
        {
            return;
        }

        var snapshot = await LoadAsync(cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.ActiveWorkspaceIndex is >= 1 and <= 8)
        {
            workspaceService.SwitchTo(snapshot.ActiveWorkspaceIndex);
        }

        if (!appStateService.Config.Session.RelaunchAppsOnRestore)
        {
            return;
        }

        foreach (var executablePath in snapshot.Windows
                     .Select(window => window.ExecutablePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Cast<string>())
        {
            try
            {
                Process.Start(explorerIntegrationService.CreateExecutableLaunchStartInfo(executablePath));
            }
            catch (Exception exception)
            {
                logService.Warn("Session restore could not relaunch an app.", new Dictionary<string, object?>
                {
                    ["path"] = executablePath,
                    ["error"] = exception.Message
                });
            }
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(ConfigurationPaths.RootDirectory);
            var snapshot = new SessionSnapshot
            {
                SavedAt = DateTimeOffset.Now,
                ActiveWorkspaceIndex = appStateService.ActiveWorkspaceIndex,
                Windows = visibleWindowService.GetVisibleWindows()
                    .Select(window => new SessionWindowEntry
                    {
                        Title = window.Title,
                        ProcessName = window.ProcessName,
                        ExecutablePath = window.ExecutablePath
                    })
                    .ToList()
            };

            await using var stream = File.Create(SessionPath);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
        }
        catch (Exception exception)
        {
            logService.Error("Failed to persist the shell session snapshot.", exception);
        }
    }
}
