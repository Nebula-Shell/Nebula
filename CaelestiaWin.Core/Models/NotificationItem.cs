using CaelestiaWin.Core.Enums;

namespace CaelestiaWin.Core.Models;

public sealed record NotificationItem(
    Guid Id,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    NotificationKind Kind = NotificationKind.Info,
    string Source = "Nebula",
    bool ShowToast = true,
    string? PrimaryActionLabel = null,
    string? PrimaryActionId = null,
    double? ProgressFraction = null,
    bool IsCompleted = false,
    bool HasError = false)
{
    public bool HasProgress => ProgressFraction.HasValue;

    public double NormalizedProgressFraction => Math.Clamp(ProgressFraction ?? 0d, 0d, 1d);

    public int ProgressPercentage => (int)Math.Round(NormalizedProgressFraction * 100d);
}
