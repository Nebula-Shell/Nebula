using System.Text.Json;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Config.Services;

public sealed class FileExplorerSidebarStateService(IDiagnosticLogService logService) : IFileExplorerSidebarStateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IDiagnosticLogService _logService = logService;

    private static string SidebarStatePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "explorer-sidebar.json");

    public IReadOnlyList<FileExplorerSidebarEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(SidebarStatePath))
            {
                return [];
            }

            using var stream = File.OpenRead(SidebarStatePath);
            var entries = JsonSerializer.Deserialize<List<FileExplorerSidebarEntry>>(stream, SerializerOptions);
            return entries?
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Path))
                .ToList() ?? [];
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to load Nebula file explorer sidebar state.", new Dictionary<string, object?>
            {
                ["path"] = SidebarStatePath,
                ["error"] = exception.Message
            });

            return [];
        }
    }

    public void SaveEntries(IReadOnlyList<FileExplorerSidebarEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(SidebarStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(SidebarStatePath);
            JsonSerializer.Serialize(stream, entries, SerializerOptions);
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to save Nebula file explorer sidebar state.", new Dictionary<string, object?>
            {
                ["path"] = SidebarStatePath,
                ["error"] = exception.Message
            });
        }
    }
}
