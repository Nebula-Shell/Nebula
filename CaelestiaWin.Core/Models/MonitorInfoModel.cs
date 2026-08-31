namespace CaelestiaWin.Core.Models;

public sealed record MonitorInfoModel(
    string DeviceName,
    bool IsPrimary,
    WindowBounds Bounds,
    WindowBounds WorkArea);
