using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaelestiaWin.UI.ViewModels;

internal static class FileExplorerVisualResolver
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ThumbnailExtensions =
    [
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v"
    ];

    public static ImageSource? GetVisual(string path, bool preferThumbnail, int preferredSize = 64)
    {
        var normalizedSize = Math.Clamp(preferredSize, 16, 256);
        var cacheKey = $"{path}|thumb:{preferThumbnail}|size:{normalizedSize}";
        return Cache.GetOrAdd(cacheKey, _ => LoadVisual(path, preferThumbnail, normalizedSize));
    }

    public static ImageSource? GetDriveVisual(string path)
    {
        var cacheKey = $"{path}|drive";
        return Cache.GetOrAdd(cacheKey, _ => TryGetFileIcon(path, isLargeIcon: true, preferredSize: 96));
    }

    private static ImageSource? LoadVisual(string path, bool preferThumbnail, int preferredSize)
    {
        if (preferThumbnail && ThumbnailExtensions.Contains(Path.GetExtension(path)))
        {
            var thumbnail = TryGetShellBitmap(path, preferThumbnail: true, preferredSize);
            if (thumbnail is not null)
            {
                return thumbnail;
            }
        }

        var icon = TryGetFileIcon(path, isLargeIcon: true, preferredSize);
        if (icon is not null)
        {
            return icon;
        }

        return preferThumbnail ? TryGetShellBitmap(path, preferThumbnail: false, preferredSize) : null;
    }

    private static ImageSource? TryGetShellBitmap(string path, bool preferThumbnail, int preferredSize)
    {
        try
        {
            var factoryGuid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, nint.Zero, ref factoryGuid, out var nativeFactory);
            if (hr != 0 || nativeFactory is null)
            {
                return null;
            }

            var size = new NativeSize { Width = preferredSize, Height = preferredSize };
            try
            {
                var flags = preferThumbnail
                    ? ShellItemImageFlags.BigGIcon | ShellItemImageFlags.ThumbnailOnly | ShellItemImageFlags.ResizeToFit
                    : ShellItemImageFlags.BigGIcon | ShellItemImageFlags.IconOnly | ShellItemImageFlags.ResizeToFit;

                var imageHr = nativeFactory.GetImage(size, flags, out var hBitmap);
                if (imageHr != 0 || hBitmap == nint.Zero)
                {
                    return null;
                }

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        nint.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(preferredSize, preferredSize));
                    source.Freeze();
                    return source;
                }
                finally
                {
                    _ = DeleteObject(hBitmap);
                }
            }
            finally
            {
                if (Marshal.IsComObject(nativeFactory))
                {
                    _ = Marshal.ReleaseComObject(nativeFactory);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryGetFileIcon(string path, bool isLargeIcon, int preferredSize)
    {
        try
        {
            var fileInfo = new SHFileInfo();
            var flags = SHGetFileInfoFlags.Icon | SHGetFileInfoFlags.UseFileAttributes;
            flags |= isLargeIcon ? SHGetFileInfoFlags.LargeIcon : SHGetFileInfoFlags.SmallIcon;

            var attributes = Directory.Exists(path) ? FileAttributes.Directory : FileAttributes.Normal;
            var result = SHGetFileInfo(path, attributes, ref fileInfo, (uint)Marshal.SizeOf<SHFileInfo>(), flags);
            if (result == nint.Zero || fileInfo.IconHandle == nint.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    fileInfo.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(preferredSize, preferredSize));
                source.Freeze();
                return source;
            }
            finally
            {
                _ = DestroyIcon(fileInfo.IconHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        nint bindingContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        FileAttributes fileAttributes,
        ref SHFileInfo fileInfo,
        uint fileInfoSize,
        SHGetFileInfoFlags flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [Flags]
    private enum SHGetFileInfoFlags : uint
    {
        Icon = 0x000000100,
        LargeIcon = 0x000000000,
        SmallIcon = 0x000000001,
        UseFileAttributes = 0x000000010
    }

    [Flags]
    private enum ShellItemImageFlags
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        CropToSquare = 0x20,
        WideThumbnails = 0x40,
        IconBackground = 0x80,
        ScaleUp = 0x100,
        BigGIcon = 0x4000
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellItemImageFlags flags, out nint phbm);
    }
}
