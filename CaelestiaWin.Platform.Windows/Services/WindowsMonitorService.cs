using System.Runtime.InteropServices;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsMonitorService : IMonitorService
{
    public IReadOnlyList<MonitorInfoModel> GetMonitors()
    {
        var monitors = new List<MonitorInfoModel>();

        _ = User32.EnumDisplayMonitors(nint.Zero, nint.Zero, (nint monitor, nint hdc, ref Rect monitorRect, nint data) =>
        {
            var info = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            if (!User32.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            monitors.Add(new MonitorInfoModel(
                $"Display {monitors.Count + 1}",
                (info.Flags & 1u) != 0,
                ToBounds(info.Monitor),
                ToBounds(info.WorkArea)));

            return true;
        }, nint.Zero);

        return monitors;
    }

    public MonitorInfoModel? GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(monitor => monitor.IsPrimary)
               ?? monitors.FirstOrDefault();
    }

    private static WindowBounds ToBounds(Rect rect)
    {
        return new WindowBounds(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }
}
