using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsForegroundWindowTracker : IForegroundWindowTracker
{
    private readonly WindowsWindowIntrospection _introspection;
    private readonly User32.WinEventDelegate _callback;
    private nint _hookHandle;
    private nint _showHookHandle;
    private nint _hideHookHandle;
    private nint _destroyHookHandle;
    private nint _createHookHandle;
    private nint _nameChangeHookHandle;
    private nint _stateChangeHookHandle;
    private readonly object _windowsChangedSync = new();
    private CancellationTokenSource? _windowsChangedCts;

    public WindowsForegroundWindowTracker(WindowsWindowIntrospection introspection)
    {
        _introspection = introspection;
        _callback = OnWinEvent;
    }

    public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

    public event EventHandler? WindowsChanged;

    public WindowDescriptor? GetForegroundWindow()
    {
        return _introspection.CreateDescriptor(User32.GetForegroundWindow());
    }

    public void Start()
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        _hookHandle = User32.SetWinEventHook(
            User32.EventSystemForeground,
            User32.EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            User32.WineventOutofcontext);
        _showHookHandle = RegisterWindowEventHook(User32.EventObjectShow);
        _hideHookHandle = RegisterWindowEventHook(User32.EventObjectHide);
        _destroyHookHandle = RegisterWindowEventHook(User32.EventObjectDestroy);
        _createHookHandle = RegisterWindowEventHook(User32.EventObjectCreate);
        _nameChangeHookHandle = RegisterWindowEventHook(User32.EventObjectNameChange);
        _stateChangeHookHandle = RegisterWindowEventHook(User32.EventObjectStateChange);

        var current = GetForegroundWindow();
        if (current is not null)
        {
            ForegroundWindowChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(current));
        }
    }

    public void Stop()
    {
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        _ = User32.UnhookWinEvent(_hookHandle);
        Unhook(ref _showHookHandle);
        Unhook(ref _hideHookHandle);
        Unhook(ref _destroyHookHandle);
        Unhook(ref _createHookHandle);
        Unhook(ref _nameChangeHookHandle);
        Unhook(ref _stateChangeHookHandle);
        lock (_windowsChangedSync)
        {
            _windowsChangedCts?.Cancel();
            _windowsChangedCts = null;
        }

        _hookHandle = nint.Zero;
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != User32.ObjidWindow || idChild != 0)
        {
            return;
        }

        if (eventType == User32.EventSystemForeground)
        {
            var descriptor = _introspection.CreateDescriptor(hwnd);
            ForegroundWindowChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(descriptor));
            return;
        }

        if (eventType == User32.EventObjectLocationChange)
        {
            return;
        }

        ScheduleWindowsChanged();
    }

    private void ScheduleWindowsChanged()
    {
        CancellationTokenSource cancellationTokenSource;
        lock (_windowsChangedSync)
        {
            _windowsChangedCts?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
            _windowsChangedCts = cancellationTokenSource;
        }

        var token = cancellationTokenSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(35, token).ConfigureAwait(false);
                WindowsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_windowsChangedSync)
                {
                    if (ReferenceEquals(_windowsChangedCts, cancellationTokenSource))
                    {
                        _windowsChangedCts = null;
                    }
                }

                cancellationTokenSource.Dispose();
            }
        }, CancellationToken.None);
    }

    private nint RegisterWindowEventHook(uint eventType)
    {
        return User32.SetWinEventHook(
            eventType,
            eventType,
            nint.Zero,
            _callback,
            0,
            0,
            User32.WineventOutofcontext);
    }

    private static void Unhook(ref nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        _ = User32.UnhookWinEvent(handle);
        handle = nint.Zero;
    }
}
