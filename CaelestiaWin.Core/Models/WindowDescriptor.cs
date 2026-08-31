namespace CaelestiaWin.Core.Models;

public sealed record WindowDescriptor(
    nint Handle,
    string Title,
    string? ProcessName,
    string? ExecutablePath,
    WindowBounds Bounds,
    bool IsMinimized);
