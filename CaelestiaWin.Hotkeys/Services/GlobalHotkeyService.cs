using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Hotkeys.Services;

public sealed class GlobalHotkeyService(
    IDiagnosticLogService logService,
    IAppStateService appStateService) : IGlobalHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmAppCommand = 0x0319;
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint VkLwin = 0x5B;
    private const uint VkRwin = 0x5C;
    private const uint VkControl = 0x11;
    private const uint VkLcontrol = 0xA2;
    private const uint VkRcontrol = 0xA3;
    private const uint VkD = 0x44;
    private const uint VkE = 0x45;
    private const uint VkG = 0x47;
    private const uint VkM = 0x4D;
    private const uint VkS = 0x53;
    private const uint VkV = 0x56;
    private const uint VkLeft = 0x25;
    private const uint VkRight = 0x27;
    private const uint VkShift = 0x10;
    private const uint VkLshift = 0xA0;
    private const uint VkRshift = 0xA1;
    private const uint VkVolumeMute = 0xAD;
    private const uint VkVolumeDown = 0xAE;
    private const uint VkVolumeUp = 0xAF;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaPlayPause = 0xB3;
    private const uint VkBrightnessDown = 0xD8;
    private const uint VkBrightnessUp = 0xD9;
    private const int AppCommandMask = unchecked((int)0xF000);
    private const int AppCommandVolumeMute = 8;
    private const int AppCommandVolumeDown = 9;
    private const int AppCommandVolumeUp = 10;
    private const int AppCommandMediaNextTrack = 11;
    private const int AppCommandMediaPreviousTrack = 12;
    private const int AppCommandMediaPlayPause = 14;
    private const uint LlkhfInjected = 0x00000010;
    private const int WinTapMaxDurationMs = 700;
    private const int WinGuideHoldDelayMs = 4000;

    private readonly Dictionary<int, HotkeyBindingConfig> _bindingsById = [];
    private readonly Dictionary<uint, HotkeyBindingConfig> _reservedWinCtrlArrowBindings = [];
    private readonly Dictionary<uint, HotkeyBindingConfig> _reservedWinQuickAccessBindings = [];
    private HotkeyBindingConfig? _reservedWinShiftSnipBinding;
    private readonly Dictionary<uint, HotkeyBindingConfig> _specialFunctionKeyBindings = [];
    private readonly Dictionary<int, HotkeyBindingConfig> _appCommandBindings = [];
    private readonly List<string> _failedRegistrations = [];
    private IReadOnlyList<HotkeyBindingConfig> _pendingBindings = [];
    private HwndSource? _source;
    private nint _windowHandle;
    private nint _keyboardHookHandle;
    private HookProc? _keyboardHookProc;
    private HotkeyBindingConfig? _standaloneWinBinding;
    private bool _shouldTrackWinKeyState;
    private bool _isWinKeyDown;
    private bool _winKeyUsedInChord;
    private uint _activeWinVirtualKey;
    private long _winKeyDownAt;
    private readonly Dictionary<uint, long> _lastReservedWinQuickAccessDispatchAt = [];
    private Timer? _shortcutGuideTimer;
    private int _nextId = 0x5000;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public IReadOnlyList<string> FailedRegistrations => new ReadOnlyCollection<string>(_failedRegistrations);

    public void AttachWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || _windowHandle == hwnd)
        {
            return;
        }

        _windowHandle = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        if (_pendingBindings.Count > 0)
        {
            RegisterBindings(_pendingBindings);
        }
    }

    public void RegisterBindings(IEnumerable<HotkeyBindingConfig> bindings)
    {
        var bindingList = bindings.ToArray();
        _pendingBindings = bindingList;
        _standaloneWinBinding = bindingList.FirstOrDefault(ShouldHandleAsStandaloneWinTap);
        _reservedWinCtrlArrowBindings.Clear();
        _reservedWinQuickAccessBindings.Clear();
        _lastReservedWinQuickAccessDispatchAt.Clear();
        _reservedWinShiftSnipBinding = null;
        _specialFunctionKeyBindings.Clear();
        _appCommandBindings.Clear();
        _shouldTrackWinKeyState = bindingList.Any(ContainsWinModifier);
        EnsureKeyboardHook();
        UnregisterAll();
        _failedRegistrations.Clear();

        if (_windowHandle == nint.Zero)
        {
            logService.Info("Deferring hotkey registration until the shell window handle is ready.");
            return;
        }

        foreach (var binding in bindingList)
        {
            if (IsStandaloneWinGesture(binding.Gesture))
            {
                continue;
            }

            if (TryRegisterReservedWinCtrlArrowBinding(binding))
            {
                continue;
            }

            if (TryRegisterReservedWinQuickAccessBinding(binding))
            {
                continue;
            }

            if (TryRegisterReservedWinShiftSnipBinding(binding))
            {
                continue;
            }

            if (TryRegisterSpecialFunctionKeyBinding(binding))
            {
                continue;
            }

            if (!TryParseGesture(binding.Gesture, out var modifiers, out var virtualKey))
            {
                _failedRegistrations.Add($"Invalid gesture: {binding.DisplayLabel}");
                continue;
            }

            var hotkeyId = _nextId++;
            if (!NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, modifiers, virtualKey))
            {
                var win32Error = Marshal.GetLastWin32Error();
                var failureData = new Dictionary<string, object?>
                {
                    ["gesture"] = binding.Gesture,
                    ["action"] = binding.Action,
                    ["workspace"] = binding.Workspace,
                    ["win32Error"] = win32Error
                };

                if (TryRegisterFallback(binding, hotkeyId, failureData))
                {
                    continue;
                }

                var failure = $"Failed to register {binding.DisplayLabel} (Win32: {win32Error})";
                _failedRegistrations.Add(failure);
                logService.Warn(failure, failureData);
                continue;
            }

            _bindingsById[hotkeyId] = binding;
        }
    }

    public void UnregisterAll()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        foreach (var bindingId in _bindingsById.Keys)
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, bindingId);
        }

        _bindingsById.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        ReleaseKeyboardHook();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && _bindingsById.TryGetValue(wParam.ToInt32(), out var binding))
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(binding));
            handled = true;
        }

        if (message == WmAppCommand && TryGetAppCommand(lParam, out var appCommand)
                                    && _appCommandBindings.TryGetValue(appCommand, out var appCommandBinding))
        {
            QueueConfiguredHotkey(appCommandBinding);
            handled = true;
        }

        return nint.Zero;
    }

    private void EnsureKeyboardHook()
    {
        if (_keyboardHookHandle != nint.Zero)
        {
            return;
        }

        _keyboardHookProc = KeyboardHookCallback;
        var moduleHandle = NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);

        if (_keyboardHookHandle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            logService.Warn("Failed to install shell keyboard hook.", new Dictionary<string, object?>
            {
                ["win32Error"] = error
            });
        }
    }

    private void ReleaseKeyboardHook()
    {
        if (_keyboardHookHandle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = nint.Zero;
        _keyboardHookProc = null;
        _isWinKeyDown = false;
        _winKeyUsedInChord = false;
        _activeWinVirtualKey = 0;
        _winKeyDownAt = 0;
        CancelShortcutGuide();
        SetShortcutGuideVisibility(false);
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code != HcAction)
        {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }

        var message = unchecked((int)wParam.ToInt64());
        var keyboardData = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
        var isWinKey = keyboardData.VirtualKeyCode is VkLwin or VkRwin;
        var isInjected = (keyboardData.Flags & LlkhfInjected) == LlkhfInjected;

        if (isInjected)
        {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }

        if (_specialFunctionKeyBindings.TryGetValue(keyboardData.VirtualKeyCode, out var specialFunctionBinding))
        {
            if (_source is null)
            {
                return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
            }

            if (message is WmKeyDown or WmSysKeyDown)
            {
                QueueConfiguredHotkey(specialFunctionBinding);
            }

            return (nint)1;
        }

        if (message is WmKeyDown or WmSysKeyDown
            && _reservedWinQuickAccessBindings.TryGetValue(keyboardData.VirtualKeyCode, out var quickAccessBinding)
            && IsWinModifierDown()
            && !IsControlModifierDown()
            && !IsShiftModifierDown())
        {
            _winKeyUsedInChord = true;
            CancelShortcutGuide();
            SetShortcutGuideVisibility(false);
            var lastDispatch = _lastReservedWinQuickAccessDispatchAt.GetValueOrDefault(keyboardData.VirtualKeyCode);
            if (Environment.TickCount64 - lastDispatch > 350)
            {
                _lastReservedWinQuickAccessDispatchAt[keyboardData.VirtualKeyCode] = Environment.TickCount64;
                QueueConfiguredHotkey(quickAccessBinding);
            }

            return (nint)1;
        }

        if (message is WmKeyDown or WmSysKeyDown
            && keyboardData.VirtualKeyCode == VkS
            && _reservedWinShiftSnipBinding is not null
            && IsWinModifierDown()
            && IsShiftModifierDown())
        {
            _winKeyUsedInChord = true;
            CancelShortcutGuide();
            SetShortcutGuideVisibility(false);
            QueueConfiguredHotkey(_reservedWinShiftSnipBinding);
            return (nint)1;
        }

        if (message is WmKeyDown or WmSysKeyDown
            && _reservedWinCtrlArrowBindings.TryGetValue(keyboardData.VirtualKeyCode, out var reservedBinding)
            && IsWinModifierDown()
            && IsControlModifierDown())
        {
            _winKeyUsedInChord = true;
            CancelShortcutGuide();
            SetShortcutGuideVisibility(false);
            QueueConfiguredHotkey(reservedBinding);
            return (nint)1;
        }

        if (!_shouldTrackWinKeyState)
        {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }

        if (message is WmKeyDown or WmSysKeyDown)
        {
            if (isWinKey)
            {
                if (_isWinKeyDown && _activeWinVirtualKey == keyboardData.VirtualKeyCode)
                {
                    return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
                }

                _isWinKeyDown = true;
                _winKeyUsedInChord = false;
                _activeWinVirtualKey = keyboardData.VirtualKeyCode;
                _winKeyDownAt = Environment.TickCount64;
                ScheduleShortcutGuide();
            }
            else if (_isWinKeyDown)
            {
                _winKeyUsedInChord = true;
                CancelShortcutGuide();
                SetShortcutGuideVisibility(false);
            }
        }
        else if (message is WmKeyUp or WmSysKeyUp)
        {
            if (isWinKey && _isWinKeyDown && keyboardData.VirtualKeyCode == _activeWinVirtualKey)
            {
                var isTap = !_winKeyUsedInChord && Environment.TickCount64 - _winKeyDownAt <= WinTapMaxDurationMs;

                _isWinKeyDown = false;
                _winKeyUsedInChord = false;
                _activeWinVirtualKey = 0;
                _winKeyDownAt = 0;
                CancelShortcutGuide();
                SetShortcutGuideVisibility(false);

                if (isTap && _standaloneWinBinding is not null)
                {
                    QueueStandaloneWinTap(_standaloneWinBinding);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
    }

    private void QueueConfiguredHotkey(HotkeyBindingConfig binding)
    {
        if (_source is null)
        {
            return;
        }

        _ = _source.Dispatcher.BeginInvoke(new Action(() =>
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(binding));
        }));
    }

    private void QueueStandaloneWinTap(HotkeyBindingConfig binding)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(15).ConfigureAwait(false);

                if (!appStateService.IsLauncherOpen)
                {
                    NativeMethods.SendEscapeKeyPress();
                    await Task.Delay(45).ConfigureAwait(false);
                }

                if (_source is null)
                {
                    return;
                }

                _ = _source.Dispatcher.BeginInvoke(new Action(() =>
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(binding));
                }));
            }
            catch (Exception exception)
            {
                logService.Warn("Failed to process a standalone Win-key launcher tap.", new Dictionary<string, object?>
                {
                    ["exception"] = exception.Message
                });
            }
        });
    }

    private void ScheduleShortcutGuide()
    {
        CancelShortcutGuide();
        _shortcutGuideTimer = new Timer(_ =>
        {
            if (_isWinKeyDown && !_winKeyUsedInChord)
            {
                SetShortcutGuideVisibility(true);
            }
        }, null, WinGuideHoldDelayMs, Timeout.Infinite);
    }

    private void CancelShortcutGuide()
    {
        _shortcutGuideTimer?.Dispose();
        _shortcutGuideTimer = null;
    }

    private void SetShortcutGuideVisibility(bool isVisible)
    {
        if (_source is not null)
        {
            _ = _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                appStateService.IsShortcutGuideVisible = isVisible;
            }));
            return;
        }

        appStateService.IsShortcutGuideVisible = isVisible;
    }

    private static bool TryParseGesture(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var tokens = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? keyToken = null;

        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "alt":
                    modifiers |= NativeMethods.ModAlt;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.ModControl;
                    break;
                case "shift":
                    modifiers |= NativeMethods.ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.ModWin;
                    break;
                default:
                    keyToken = token;
                    break;
            }
        }

        if (keyToken is null)
        {
            return false;
        }

        var converted = new KeyConverter().ConvertFromInvariantString(keyToken);
        if (converted is not Key key)
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private static bool ShouldHandleAsStandaloneWinTap(HotkeyBindingConfig binding)
    {
        if (IsStandaloneWinGesture(binding.Gesture))
        {
            return true;
        }

        return binding.Action == HotkeyActionKind.ToggleLauncher
               && string.Equals(binding.Gesture, "Win+Space", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStandaloneWinGesture(string gesture)
    {
        return string.Equals(gesture, "Win", StringComparison.OrdinalIgnoreCase)
               || string.Equals(gesture, "Windows", StringComparison.OrdinalIgnoreCase)
               || string.Equals(gesture, "LWin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(gesture, "RWin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsWinModifier(HotkeyBindingConfig binding)
    {
        return binding.Gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals("Win", StringComparison.OrdinalIgnoreCase)
                          || token.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                          || token.Equals("LWin", StringComparison.OrdinalIgnoreCase)
                          || token.Equals("RWin", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryRegisterSpecialFunctionKeyBinding(HotkeyBindingConfig binding)
    {
        if (!TryGetSpecialFunctionVirtualKey(binding, out var virtualKey))
        {
            return false;
        }

        _specialFunctionKeyBindings[virtualKey] = binding;
        var hotkeyRegistered = TryRegisterSpecialFunctionHotkey(binding, virtualKey);
        if (TryGetAppCommand(binding.Action, out var appCommand))
        {
            _appCommandBindings[appCommand] = binding;
        }

        logService.Info("Registered special function key through shell keyboard/app-command handling.", new Dictionary<string, object?>
        {
            ["gesture"] = binding.Gesture,
            ["action"] = binding.Action,
            ["virtualKey"] = $"0x{virtualKey:X2}",
            ["appCommand"] = appCommand == 0 ? null : appCommand,
            ["registeredHotkey"] = hotkeyRegistered
        });

        return true;
    }

    private bool TryRegisterSpecialFunctionHotkey(HotkeyBindingConfig binding, uint virtualKey)
    {
        if (_windowHandle == nint.Zero)
        {
            return false;
        }

        var hotkeyId = _nextId++;
        if (NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, 0, virtualKey))
        {
            _bindingsById[hotkeyId] = binding;
            return true;
        }

        logService.Warn("Special function key RegisterHotKey fallback was unavailable; hook/app-command paths remain active.", new Dictionary<string, object?>
        {
            ["gesture"] = binding.Gesture,
            ["action"] = binding.Action,
            ["virtualKey"] = $"0x{virtualKey:X2}",
            ["win32Error"] = Marshal.GetLastWin32Error()
        });
        return false;
    }

    private static bool TryGetSpecialFunctionVirtualKey(HotkeyBindingConfig binding, out uint virtualKey)
    {
        virtualKey = binding.Action switch
        {
            HotkeyActionKind.VolumeUp => VkVolumeUp,
            HotkeyActionKind.VolumeDown => VkVolumeDown,
            HotkeyActionKind.ToggleMute => VkVolumeMute,
            HotkeyActionKind.MediaPlayPause => VkMediaPlayPause,
            HotkeyActionKind.MediaNext => VkMediaNextTrack,
            HotkeyActionKind.MediaPrevious => VkMediaPrevTrack,
            HotkeyActionKind.BrightnessUp => VkBrightnessUp,
            HotkeyActionKind.BrightnessDown => VkBrightnessDown,
            _ => 0
        };

        if (virtualKey == 0)
        {
            return false;
        }

        return binding.Gesture.Equals(GetCanonicalSpecialFunctionGesture(binding.Action), StringComparison.OrdinalIgnoreCase)
               || IsAliasForSpecialFunctionGesture(binding.Gesture, binding.Action);
    }

    private static bool TryGetAppCommand(HotkeyActionKind action, out int appCommand)
    {
        appCommand = action switch
        {
            HotkeyActionKind.VolumeUp => AppCommandVolumeUp,
            HotkeyActionKind.VolumeDown => AppCommandVolumeDown,
            HotkeyActionKind.ToggleMute => AppCommandVolumeMute,
            HotkeyActionKind.MediaPlayPause => AppCommandMediaPlayPause,
            HotkeyActionKind.MediaNext => AppCommandMediaNextTrack,
            HotkeyActionKind.MediaPrevious => AppCommandMediaPreviousTrack,
            _ => 0
        };

        return appCommand != 0;
    }

    private static bool TryGetAppCommand(nint lParam, out int appCommand)
    {
        appCommand = (int)((lParam.ToInt64() >> 16) & 0xFFFF) & ~AppCommandMask;
        return appCommand is AppCommandVolumeMute
            or AppCommandVolumeDown
            or AppCommandVolumeUp
            or AppCommandMediaNextTrack
            or AppCommandMediaPreviousTrack
            or AppCommandMediaPlayPause;
    }

    private static string GetCanonicalSpecialFunctionGesture(HotkeyActionKind action)
    {
        return action switch
        {
            HotkeyActionKind.VolumeUp => "VolumeUp",
            HotkeyActionKind.VolumeDown => "VolumeDown",
            HotkeyActionKind.ToggleMute => "VolumeMute",
            HotkeyActionKind.MediaPlayPause => "MediaPlayPause",
            HotkeyActionKind.MediaNext => "MediaNext",
            HotkeyActionKind.MediaPrevious => "MediaPrevious",
            HotkeyActionKind.BrightnessUp => "BrightnessUp",
            HotkeyActionKind.BrightnessDown => "BrightnessDown",
            _ => string.Empty
        };
    }

    private static bool IsAliasForSpecialFunctionGesture(string gesture, HotkeyActionKind action)
    {
        return action switch
        {
            HotkeyActionKind.ToggleMute => gesture.Equals("Mute", StringComparison.OrdinalIgnoreCase),
            HotkeyActionKind.MediaNext => gesture.Equals("MediaNextTrack", StringComparison.OrdinalIgnoreCase),
            HotkeyActionKind.MediaPrevious => gesture.Equals("MediaPreviousTrack", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool TryRegisterReservedWinCtrlArrowBinding(HotkeyBindingConfig binding)
    {
        var virtualKey = binding.Action switch
        {
            HotkeyActionKind.MoveWindowToWorkspacePrevious
                when string.Equals(binding.Gesture, "Win+Ctrl+Left", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(binding.Gesture, "Ctrl+Win+Left", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(binding.Gesture, "Windows+Ctrl+Left", StringComparison.OrdinalIgnoreCase) => VkLeft,
            HotkeyActionKind.MoveWindowToWorkspaceNext
                when string.Equals(binding.Gesture, "Win+Ctrl+Right", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(binding.Gesture, "Ctrl+Win+Right", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(binding.Gesture, "Windows+Ctrl+Right", StringComparison.OrdinalIgnoreCase) => VkRight,
            _ => 0u
        };

        if (virtualKey == 0)
        {
            return false;
        }

        _reservedWinCtrlArrowBindings[virtualKey] = binding;
        logService.Info("Registered reserved Win+Ctrl+Arrow hotkey through the keyboard hook.", new Dictionary<string, object?>
        {
            ["gesture"] = binding.Gesture,
            ["action"] = binding.Action
        });
        return true;
    }

    private bool TryRegisterReservedWinQuickAccessBinding(HotkeyBindingConfig binding)
    {
        var virtualKey = binding.Action switch
        {
            HotkeyActionKind.ToggleDiscordDesktop when IsWinGesture(binding.Gesture, "D") => VkD,
            HotkeyActionKind.OpenFileExplorer when IsWinGesture(binding.Gesture, "E") => VkE,
            HotkeyActionKind.ToggleSpotifyDesktop when IsWinGesture(binding.Gesture, "M") => VkM,
            HotkeyActionKind.ToggleGitHubDesktop when IsWinGesture(binding.Gesture, "G") => VkG,
            HotkeyActionKind.ToggleClipboardHistory when IsWinGesture(binding.Gesture, "V") => VkV,
            _ => 0u
        };

        if (virtualKey == 0)
        {
            return false;
        }

        _reservedWinQuickAccessBindings[virtualKey] = binding;
        logService.Info("Registered reserved quick access hotkey through the keyboard hook.", new Dictionary<string, object?>
        {
            ["gesture"] = binding.Gesture,
            ["action"] = binding.Action
        });
        return true;
    }

    private static bool IsWinGesture(string gesture, string key)
    {
        return string.Equals(gesture, $"Win+{key}", StringComparison.OrdinalIgnoreCase)
               || string.Equals(gesture, $"Windows+{key}", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryRegisterReservedWinShiftSnipBinding(HotkeyBindingConfig binding)
    {
        if (binding.Action != HotkeyActionKind.CaptureRegion
            || !string.Equals(binding.Gesture, "Win+Shift+S", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _reservedWinShiftSnipBinding = binding;
        logService.Info("Registered reserved Win+Shift+S snipping hotkey through the keyboard hook.");
        return true;
    }

    private static bool IsWinModifierDown()
    {
        return IsKeyDown(VkLwin) || IsKeyDown(VkRwin);
    }

    private static bool IsControlModifierDown()
    {
        return IsKeyDown(VkControl) || IsKeyDown(VkLcontrol) || IsKeyDown(VkRcontrol);
    }

    private static bool IsShiftModifierDown()
    {
        return IsKeyDown(VkShift) || IsKeyDown(VkLshift) || IsKeyDown(VkRshift);
    }

    private static bool IsKeyDown(uint virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;
    }

    private bool TryRegisterFallback(HotkeyBindingConfig binding, int hotkeyId, IReadOnlyDictionary<string, object?> failureData)
    {
        var fallbackGesture = GetFallbackGesture(binding);
        if (fallbackGesture is null || !TryParseGesture(fallbackGesture, out var fallbackModifiers, out var fallbackVirtualKey))
        {
            return false;
        }

        if (!NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, fallbackModifiers, fallbackVirtualKey))
        {
            return false;
        }

        _bindingsById[hotkeyId] = binding;
        logService.Warn(
            $"Primary hotkey {binding.Gesture} was unavailable. Registered fallback {fallbackGesture} instead.",
            new Dictionary<string, object?>(failureData)
            {
                ["fallbackGesture"] = fallbackGesture
            });
        return true;
    }

    private static string? GetFallbackGesture(HotkeyBindingConfig binding)
    {
        return binding.Action switch
        {
            HotkeyActionKind.ToggleLauncher => "Ctrl+Space",
            HotkeyActionKind.OpenTerminal => "Ctrl+Alt+Enter",
            HotkeyActionKind.OpenFileExplorer => "Ctrl+Alt+E",
            HotkeyActionKind.ToggleControlCenter => "Ctrl+Alt+B",
            HotkeyActionKind.ToggleNotificationCenter => "Ctrl+Alt+N",
            HotkeyActionKind.ToggleClipboardHistory => "Ctrl+Alt+V",
            HotkeyActionKind.ToggleSettingsPanel => "Ctrl+Alt+C",
            HotkeyActionKind.CaptureRegion => "Ctrl+Shift+S",
            HotkeyActionKind.ToggleFocusedWindowFullscreen => "Ctrl+Alt+F",
            HotkeyActionKind.CloseFocusedWindow => "Ctrl+Alt+Q",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Left => "Ctrl+Alt+H",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Right => "Ctrl+Alt+L",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Up => "Ctrl+Alt+K",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Down => "Ctrl+Alt+J",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Left => "Ctrl+Alt+Shift+H",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Right => "Ctrl+Alt+Shift+L",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Up => "Ctrl+Alt+Shift+K",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Down => "Ctrl+Alt+Shift+J",
            HotkeyActionKind.CycleWorkspacePrevious => "Ctrl+Alt+Left",
            HotkeyActionKind.CycleWorkspaceNext => "Ctrl+Alt+Right",
            HotkeyActionKind.MoveWindowToWorkspacePrevious => "Ctrl+Alt+Shift+Left",
            HotkeyActionKind.MoveWindowToWorkspaceNext => "Ctrl+Alt+Shift+Right",
            HotkeyActionKind.SwitchWorkspace when binding.Workspace is int workspace && workspace >= 1 && workspace <= 8
                => $"Ctrl+Alt+{workspace}",
            HotkeyActionKind.MoveWindowToWorkspace when binding.Workspace is int workspace && workspace >= 1 && workspace <= 8
                => $"Ctrl+Alt+Shift+{workspace}",
            HotkeyActionKind.ToggleOverview => "Ctrl+Alt+Tab",
            _ => null
        };
    }
}

internal static class NativeMethods
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    private const byte VkEscape = 0x1B;
    private const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int hookType, HookProc callback, nint moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    public static void SendEscapeKeyPress()
    {
        keybd_event(VkEscape, 0, 0, 0);
        keybd_event(VkEscape, 0, KeyeventfKeyup, 0);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Kbdllhookstruct
{
    public uint VirtualKeyCode;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

internal delegate nint HookProc(int code, nint wParam, nint lParam);
