using System.Runtime.InteropServices;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class Kernel32
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys;
    public ulong AvailPhys;
    public ulong TotalPageFile;
    public ulong AvailPageFile;
    public ulong TotalVirtual;
    public ulong AvailVirtual;
    public ulong AvailExtendedVirtual;
}
