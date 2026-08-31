using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Windowing.Services;

public sealed class ActiveWindowService(
    IForegroundWindowTracker foregroundWindowTracker,
    IAppStateService appStateService,
    IWindowActionService windowActionService,
    IUiDispatcher uiDispatcher,
    IDiagnosticLogService logService) : IActiveWindowService
{
    private CancellationTokenSource? _debounceCts;

    public event EventHandler<ForegroundWindowChangedEventArgs>? CurrentWindowChanged;

    public event EventHandler? WindowsChanged;

    public WindowDescriptor? CurrentWindow { get; private set; }

    public void Start()
    {
        foregroundWindowTracker.ForegroundWindowChanged += OnForegroundWindowChanged;
        foregroundWindowTracker.WindowsChanged += OnWindowsChanged;
        foregroundWindowTracker.Start();

        var current = foregroundWindowTracker.GetForegroundWindow();
        if (current is not null)
        {
            CurrentWindow = current;
            appStateService.ActiveWindowTitle = FormatTitleForConfig(current);
            CurrentWindowChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(current));
        }
    }

    public void Stop()
    {
        foregroundWindowTracker.ForegroundWindowChanged -= OnForegroundWindowChanged;
        foregroundWindowTracker.WindowsChanged -= OnWindowsChanged;
        foregroundWindowTracker.Stop();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    private async void OnForegroundWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.Window is null)
            {
                return;
            }

            var delay = Math.Max(0, appStateService.Config.Performance.ActiveWindowDebounceMs);
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var debounceToken = _debounceCts.Token;

            if (delay > 0)
            {
                await Task.Delay(delay, debounceToken);
            }

            CurrentWindow = eventArgs.Window;
            await uiDispatcher.InvokeAsync(() =>
            {
                appStateService.ActiveWindowTitle = FormatTitleForConfig(eventArgs.Window);
                appStateService.IsForegroundFullscreen = windowActionService.IsWindowFullscreen(eventArgs.Window.Handle);
            });
            CurrentWindowChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(eventArgs.Window));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logService.Error("Failed to update active window state.", exception);
        }
    }

    private string FormatTitleForConfig(WindowDescriptor window)
    {
        try
        {
            if (window is null)
            {
                return "Desktop";
            }

            if (!appStateService.Config.ControlCenter.MinimalModeTitles)
            {
                return window.Title;
            }

            // Prefer process/executable name as a minimal app name
            var proc = window.ProcessName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(proc))
            {
                var name = Path.GetFileNameWithoutExtension(proc);
                // Normalize separators
                name = name.Replace('-', ' ').Replace('_', ' ');
                // Remove common build suffixes like win64-shipping
                name = Regex.Replace(name, @"win64[-_]?shipping", string.Empty, RegexOptions.IgnoreCase);
                name = Regex.Replace(name, @"clientux|client|services", string.Empty, RegexOptions.IgnoreCase);
                name = name.Trim();
                if (name.Length > 0)
                {
                    return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
                }
            }

            // Fallback: take the part of the title before common separators
            var parts = window.Title?.Split(new[] { '-', '—', '|', ':' }, 2);
            return (parts is not null && parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                ? parts[0].Trim()
                : window.Title;
        }
        catch
        {
            return window?.Title ?? "Desktop";
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs eventArgs)
    {
        if (CurrentWindow is not null)
        {
            appStateService.IsForegroundFullscreen = windowActionService.IsWindowFullscreen(CurrentWindow.Handle);
        }

        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }
}
