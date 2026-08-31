using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsVisibleWindowService(WindowsWindowIntrospection introspection) : IVisibleWindowService
{
    public IReadOnlyList<WindowDescriptor> GetVisibleWindows()
    {
        var windows = new List<WindowDescriptor>();
        _ = User32.EnumWindows((hwnd, _) =>
        {
            var descriptor = introspection.CreateDescriptor(hwnd);
            if (descriptor is not null)
            {
                windows.Add(descriptor);
            }

            return true;
        }, nint.Zero);

        return windows;
    }
}
