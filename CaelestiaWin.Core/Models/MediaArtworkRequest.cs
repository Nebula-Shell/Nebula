namespace CaelestiaWin.Core.Models;

public sealed record MediaArtworkRequest(
    string SourceApp,
    string TrackTitle,
    string Artist,
    string? ExecutablePath,
    bool AllowAppIconFallback = true);
