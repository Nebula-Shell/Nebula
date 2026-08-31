using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsShellDesktopSurfaceService(IDiagnosticLogService logService) : IShellDesktopSurfaceService
{
    public void PrepareHostWindow(nint hwnd)
    {
        KeepHostInBack(hwnd);
    }

    public void KeepHostInBack(nint hwnd)
    {
        if (hwnd == nint.Zero || !User32.IsWindow(hwnd))
        {
            return;
        }

        if (!User32.SetWindowPos(
                hwnd,
                User32.HwndBottom,
                0,
                0,
                0,
                0,
                User32.SwpNomove | User32.SwpNosize | User32.SwpNoActivate))
        {
            logService.Warn("Failed to keep the shell host window in the background.");
        }
    }
}
