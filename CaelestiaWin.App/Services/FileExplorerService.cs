using System.Diagnostics;
using System.IO;
using System.Windows;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.UI.ViewModels;
using CaelestiaWin.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CaelestiaWin.App.Services;

public sealed class FileExplorerService(
    IServiceProvider serviceProvider,
    IAppStateService appStateService,
    IDiagnosticLogService logService) : IFileExplorerService
{
    public Task OpenAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        return appStateService.Config.Launcher.DefaultFileExplorer == FileExplorerProfileKind.Nebula
            ? OpenNebulaExplorerAsync(path, cancellationToken)
            : OpenWindowsExplorerAsync(path, cancellationToken);
    }

    public Task OpenNebulaExplorerAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        return OpenNebulaExplorerCoreAsync(path);
    }

    public Task OpenWindowsExplorerAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedPath = ResolvePath(path);
            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = File.Exists(explorerPath) ? explorerPath : "explorer.exe",
                Arguments = string.IsNullOrWhiteSpace(resolvedPath) ? string.Empty : $"\"{resolvedPath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception exception)
        {
            logService.Error("Failed to open Windows File Explorer.", exception, new Dictionary<string, object?>
            {
                ["path"] = path
            });
            throw;
        }

        return Task.CompletedTask;
    }

    private async Task OpenNebulaExplorerCoreAsync(string? path)
    {
        var window = serviceProvider.GetRequiredService<NebulaFileExplorerWindow>();
        var resolvedPath = ResolvePath(path);
        var viewModel = (NebulaFileExplorerViewModel)window.DataContext;

        if (window.IsVisible && !string.IsNullOrWhiteSpace(path) && !string.Equals(viewModel.CurrentPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            await viewModel.OpenInNewTabAsync(resolvedPath);
        }
        else
        {
            await window.OpenPathAsync(resolvedPath);
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }

    private static string ResolvePath(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : path.Trim();

        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.GetFullPath(candidate);
        }

        return Directory.Exists(candidate)
            ? candidate
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
