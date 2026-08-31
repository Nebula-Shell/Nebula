namespace CaelestiaWin.Core.Models;

public sealed record OverviewWindowItem(
    nint Handle,
    string Title,
    string Subtitle,
    int WorkspaceIndex);
