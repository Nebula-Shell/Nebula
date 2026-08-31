using System.Runtime.InteropServices;
using System.Text;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class User32
{
    public const uint SpiGetDeskWallpaper = 0x0073;
    public const uint SpiSetDeskWallpaper = 0x0014;
    public const uint SpifUpdateIniFile = 0x0001;
    public const uint SpifSendWinIniChange = 0x0002;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectShow = 0x8002;
    public const uint EventObjectHide = 0x8003;
    public const uint EventObjectDestroy = 0x8001;
    public const uint EventObjectCreate = 0x8000;
    public const uint EventObjectNameChange = 0x800C;
    public const uint EventObjectStateChange = 0x800A;
    public const uint EventObjectLocationChange = 0x800B;
    public const long ObjidWindow = 0x00000000;
    public const uint WineventOutofcontext = 0x0000;
    public const uint WmClose = 0x0010;
    public const uint WmContextMenu = 0x007B;
    public const int SwHide = 0;
    public const int SwMaximize = 3;
    public const int SwMinimize = 6;
    public const int SwShow = 5;
    public const int SwShowNa = 8;
    public const int SwRestore = 9;
    public const int GwlStyle = -16;
    public const int GwlExstyle = -20;
    public const long WsCaption = 0x00C00000L;
    public const long WsThickframe = 0x00040000L;
    public const long WsPopup = unchecked((long)0x80000000);
    public const int WsExToolwindow = 0x00000080;
    public const uint MonitorDefaulttonearest = 0x00000002;
    public static readonly nint HwndTop = nint.Zero;
    public static readonly nint HwndBottom = new(1);
    public static readonly nint HwndTopmost = new(-1);
    public static readonly nint HwndNotopmost = new(-2);
    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoZorder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowwindow = 0x0040;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect monitorRect, nint data);

    public delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowEx(nint parentHandle, nint childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPlacement(nint hwnd, [In] ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

    public static string GetWindowText(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString().Trim();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfo
{
    public int Size;
    public Rect Monitor;
    public Rect WorkArea;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public int Length;
    public int Flags;
    public int ShowCmd;
    public Point MinPosition;
    public Point MaxPosition;
    public Rect NormalPosition;

    public static WindowPlacement CreateDefault()
    {
        return new WindowPlacement
        {
            Length = Marshal.SizeOf<WindowPlacement>()
        };
    }
}
