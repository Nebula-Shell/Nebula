namespace CaelestiaWin.Core.Models;

public sealed class SessionSnapshot
{
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;

    public int ActiveWorkspaceIndex { get; init; } = 1;

    public List<SessionWindowEntry> Windows { get; init; } = [];
}

public sealed class SessionWindowEntry
{
    public string Title { get; init; } = string.Empty;

    public string? ProcessName { get; init; }

    public string? ExecutablePath { get; init; }
}
