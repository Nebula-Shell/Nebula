using System.Runtime.InteropServices;

namespace CaelestiaWin.Platform.Windows.Interop;

internal static class BluetoothInterop
{
    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    public static extern nint BluetoothFindFirstRadio(ref BluetoothFindRadioParams parameters, out nint radioHandle);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindRadioClose(nint findHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct BluetoothFindRadioParams
{
    public int Size;
}
