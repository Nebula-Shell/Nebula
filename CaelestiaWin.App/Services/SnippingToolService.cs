using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Views;
using Microsoft.Win32;

namespace CaelestiaWin.App.Services;

public sealed class SnippingToolService : ISnippingToolService
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly INotificationService _notificationService;
    private readonly IDiagnosticLogService _logService;
    private readonly ConcurrentDictionary<string, ScreenCaptureResult> _captures = [];
    private int _isCapturing;

    public SnippingToolService(
        IScreenCaptureService screenCaptureService,
        INotificationService notificationService,
        IDiagnosticLogService logService)
    {
        _screenCaptureService = screenCaptureService;
        _notificationService = notificationService;
        _logService = logService;
        _notificationService.ActionRequested += OnNotificationActionRequested;
    }

    public async Task CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _isCapturing, 1) == 1)
        {
            return;
        }

        try
        {
            var region = await SelectRegionAsync();
            if (region is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            var capture = await Task.Run(() => _screenCaptureService.CaptureRegion(region), cancellationToken).ConfigureAwait(false);
            await SetClipboardImageAsync(capture);

            var actionId = $"snip.save.{Guid.NewGuid():N}";
            _captures[actionId] = capture;

            _notificationService.Push(
                "Screenshot copied",
                $"Captured {capture.Width} x {capture.Height}px and copied it to the clipboard.",
                kind: NotificationKind.Success,
                source: "Snipping Tool",
                primaryActionLabel: "Save",
                primaryActionId: actionId);

            _logService.Info("Screen region captured.", new Dictionary<string, object?>
            {
                ["x"] = region.X,
                ["y"] = region.Y,
                ["width"] = region.Width,
                ["height"] = region.Height
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logService.Error("Screen region capture failed.", exception);
            _notificationService.Push(
                "Screenshot failed",
                "Nebula could not capture the selected region. Check the logs for details.",
                kind: NotificationKind.Error,
                source: "Snipping Tool");
        }
        finally
        {
            Interlocked.Exchange(ref _isCapturing, 0);
        }
    }

    private static async Task<ScreenCaptureRegion?> SelectRegionAsync()
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            var overlay = new ScreenSnipOverlayWindow();
            return await overlay.CaptureAsync();
        }

        return await await dispatcher.InvokeAsync(async () =>
        {
            var overlay = new ScreenSnipOverlayWindow();
            return await overlay.CaptureAsync();
        });
    }

    private static async Task SetClipboardImageAsync(ScreenCaptureResult capture)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            SetClipboardImage(capture);
            return;
        }

        await dispatcher.InvokeAsync(() => SetClipboardImage(capture));
    }

    private static void SetClipboardImage(ScreenCaptureResult capture)
    {
        using var stream = new MemoryStream(capture.PngBytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        Clipboard.SetImage(frame);
    }

    private void OnNotificationActionRequested(object? sender, NotificationActionRequestedEventArgs eventArgs)
    {
        if (!eventArgs.ActionId.StartsWith("snip.save.", StringComparison.Ordinal)
            || !_captures.TryGetValue(eventArgs.ActionId, out var capture))
        {
            return;
        }

        _ = SaveCaptureAsync(eventArgs.ActionId, capture);
    }

    private async Task SaveCaptureAsync(string actionId, ScreenCaptureResult capture)
    {
        try
        {
            var saved = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var dialog = new SaveFileDialog
                {
                    Title = "Save screenshot",
                    InitialDirectory = string.IsNullOrWhiteSpace(pictures) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : pictures,
                    FileName = $"Nebula Screenshot {capture.CapturedAt:yyyy-MM-dd HH-mm-ss}.png",
                    Filter = "PNG image (*.png)|*.png",
                    AddExtension = true,
                    DefaultExt = ".png",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return null;
                }

                File.WriteAllBytes(dialog.FileName, capture.PngBytes);
                return dialog.FileName;
            });

            if (saved is null)
            {
                return;
            }

            _captures.TryRemove(actionId, out _);
            _notificationService.Push(
                "Screenshot saved",
                saved,
                kind: NotificationKind.Success,
                source: "Snipping Tool");
        }
        catch (Exception exception)
        {
            _logService.Error("Saving screenshot failed.", exception);
            _notificationService.Push(
                "Save failed",
                "Nebula could not save the screenshot. Check the logs for details.",
                kind: NotificationKind.Error,
                source: "Snipping Tool");
        }
    }
}
