using System.Runtime.InteropServices;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class DwmApi
{
    private const int DwmwaCloaked = 14;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int attributeValue, int attributeSize);

    private const int DwmwaExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int attributeSize);

    public static bool TryGetExtendedFrameBounds(nint hwnd, out Rect rect)
    {
        rect = default;
        return DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<Rect>()) == 0;
    }

    public static bool IsWindowCloaked(nint hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }
}
