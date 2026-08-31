using System.ComponentModel;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.UI.Views;

public partial class DesktopHostWindow : Window
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> WallpaperCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAppStateService _appStateService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IMonitorService _monitorService;
    private readonly IShellDesktopSurfaceService _desktopSurfaceService;
    private readonly IShellLifetimeService _shellLifetimeService;
    private readonly IDiagnosticLogService _logService;
    private string? _loadedWallpaperCacheKey;

    public DesktopHostWindow(
        IAppStateService appStateService,
        IWallpaperService wallpaperService,
        IMonitorService monitorService,
        IShellDesktopSurfaceService desktopSurfaceService,
        IShellLifetimeService shellLifetimeService,
        IDiagnosticLogService logService)
    {
        _appStateService = appStateService;
        _wallpaperService = wallpaperService;
        _monitorService = monitorService;
        _desktopSurfaceService = desktopSurfaceService;
        _shellLifetimeService = shellLifetimeService;
        _logService = logService;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _desktopSurfaceService.PrepareHostWindow(handle);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        ApplyMonitorBounds();
        ApplyWallpaper();
        ApplyDecorationVisibility();
        KeepHostInBack();
    }

    private void OnActivated(object? sender, EventArgs eventArgs)
    {
        KeepHostInBack();
    }

    private void ApplyMonitorBounds()
    {
        if (!_appStateService.Config.Startup.ShowOnPrimaryMonitor)
        {
            return;
        }

        var primaryMonitor = _monitorService.GetPrimaryMonitor();
        if (primaryMonitor is null)
        {
            return;
        }

        WindowState = WindowState.Normal;
        Left = primaryMonitor.Bounds.Left;
        Top = primaryMonitor.Bounds.Top;
        Width = Math.Max(800, primaryMonitor.Bounds.Width);
        Height = Math.Max(600, primaryMonitor.Bounds.Height);
    }

    private void ApplyWallpaper()
    {
        var configuredWallpaper = _appStateService.Config.Theme.WallpaperPath;
        var wallpaperPath = !string.IsNullOrWhiteSpace(configuredWallpaper) && File.Exists(configuredWallpaper)
            ? configuredWallpaper
            : _wallpaperService.TryGetCurrentWallpaperPath();
        if (string.IsNullOrWhiteSpace(wallpaperPath))
        {
            _logService.Warn("No current Windows wallpaper could be resolved. Falling back to the shell gradient.");
            _loadedWallpaperCacheKey = null;
            WallpaperImage.Source = null;
            return;
        }

        try
        {
            var decodeSize = GetWallpaperDecodeSize();
            var cacheKey = $"{wallpaperPath}|{decodeSize.Width}x{decodeSize.Height}";
            if (string.Equals(_loadedWallpaperCacheKey, cacheKey, StringComparison.OrdinalIgnoreCase)
                && WallpaperImage.Source is not null)
            {
                return;
            }

            var image = WallpaperCache.GetOrAdd(cacheKey, _ => LoadWallpaperBitmap(wallpaperPath, decodeSize.Width, decodeSize.Height));
            WallpaperImage.Source = image;
            _loadedWallpaperCacheKey = cacheKey;
        }
        catch (Exception exception)
        {
            _logService.Error("Failed to apply the current Windows wallpaper to the shell host.", exception, new Dictionary<string, object?>
            {
                ["wallpaperPath"] = wallpaperPath
            });
            _loadedWallpaperCacheKey = null;
            WallpaperImage.Source = null;
        }
    }

    private (int Width, int Height) GetWallpaperDecodeSize()
    {
        var primaryMonitor = _monitorService.GetPrimaryMonitor();
        if (primaryMonitor is not null)
        {
            return (Math.Max(primaryMonitor.Bounds.Width, 1), Math.Max(primaryMonitor.Bounds.Height, 1));
        }

        return ((int)Math.Max(Width, 1), (int)Math.Max(Height, 1));
    }

    private static BitmapSource? LoadWallpaperBitmap(string wallpaperPath, int decodeWidth, int decodeHeight)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(wallpaperPath, UriKind.Absolute);
        if (decodeWidth > 0)
        {
            image.DecodePixelWidth = decodeWidth;
        }

        if (decodeHeight > 0)
        {
            image.DecodePixelHeight = decodeHeight;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ApplyDecorationVisibility()
    {
        var visibility = _appStateService.Config.Theme.ShowDesktopDecorations ? Visibility.Visible : Visibility.Collapsed;
        AccentDecoration.Visibility = visibility;
        SecondaryDecoration.Visibility = visibility;
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IAppStateService.Config))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyWallpaper();
                ApplyDecorationVisibility();
            }));
            return;
        }

        ApplyWallpaper();
        ApplyDecorationVisibility();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (_shellLifetimeService.CanExit)
        {
            return;
        }

        // Future Shell Launcher integration depends on a persistent, non-blocking shell host process.
        eventArgs.Cancel = true;
        _logService.Warn("Prevented the shell host window from closing unexpectedly.");
    }

    private void KeepHostInBack()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _desktopSurfaceService.KeepHostInBack(handle)),
            DispatcherPriority.ApplicationIdle);

        _ = Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(45);
            _desktopSurfaceService.KeepHostInBack(handle);
        }, DispatcherPriority.Background);
    }
}
