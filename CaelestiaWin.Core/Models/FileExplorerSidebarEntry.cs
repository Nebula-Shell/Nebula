namespace CaelestiaWin.Core.Models;

public sealed class FileExplorerSidebarEntry
{
    public string Id { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool IsBuiltIn { get; init; }

    public string? CustomDisplayName { get; init; }
}
