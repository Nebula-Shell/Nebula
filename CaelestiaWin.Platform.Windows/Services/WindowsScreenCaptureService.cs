using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;

    public ScreenCaptureResult CaptureRegion(ScreenCaptureRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Capture region must have a positive size.");
        }

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new InvalidOperationException("Could not acquire the desktop device context.");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw new InvalidOperationException("Could not create a compatible capture device context.");
            }

            bitmap = CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            if (bitmap == nint.Zero)
            {
                throw new InvalidOperationException("Could not create a compatible capture bitmap.");
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, Srccopy | Captureblt))
            {
                throw new InvalidOperationException("The selected screen region could not be copied.");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);

            return new ScreenCaptureResult(stream.ToArray(), region.Width, region.Height, DateTimeOffset.Now);
        }
        finally
        {
            if (previousBitmap != nint.Zero && memoryDc != nint.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destinationDc, int x, int y, int width, int height, nint sourceDc, int sourceX, int sourceY, int rasterOperation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);
}
