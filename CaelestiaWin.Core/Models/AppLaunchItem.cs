namespace CaelestiaWin.Core.Models;

public sealed record AppLaunchItem(
    string Id,
    string DisplayName,
    string LaunchPath,
    string? Arguments,
    string? Description,
    string Source,
    string? ResolvedTargetPath,
    string? IconPath = null);
