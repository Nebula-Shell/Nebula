namespace CaelestiaWin.Core.Models;

public sealed record SystemInformationSnapshot(
    string CpuName,
    string MemorySummary,
    string GpuName,
    string VideoMemorySummary,
    string StorageSummary,
    string WindowsVersion,
    string Architecture,
    string DeviceSummary);
