namespace CaelestiaWin.Core.Models;

public enum AudioDeviceKind
{
    Output,
    Input
}

public sealed record AudioDeviceModel(string Id, string Name, bool IsDefault, AudioDeviceKind Kind);

public sealed record AppVolumeSessionModel(string Id, string DisplayName, double VolumePercent, bool IsMuted, string? ProcessName, string? IconPath);
