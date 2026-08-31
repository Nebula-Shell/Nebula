using CaelestiaWin.Core.Enums;

namespace CaelestiaWin.Core.Models;

public sealed class LauncherSearchResult
{
    public required string Key { get; init; }

    public required LauncherResultKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string SourceLabel { get; init; }

    public bool IsRecent { get; init; }

    public bool IsFavorite { get; set; }

    public int Score { get; init; }

    public string MatchPrefix { get; init; } = string.Empty;

    public string MatchText { get; init; } = string.Empty;

    public string MatchSuffix { get; init; } = string.Empty;

    public AppLaunchItem? App { get; init; }

    public SystemCommandKind? Command { get; init; }
}
