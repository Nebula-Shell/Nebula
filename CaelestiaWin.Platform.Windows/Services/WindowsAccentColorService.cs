using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsAccentColorService(IDiagnosticLogService logService) : IWindowsAccentColorService
{
    private const string DwmRegistryPath = @"Software\Microsoft\Windows\DWM";
    private const string ExplorerAccentRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string ColorizationColorValue = "ColorizationColor";
    private const string ColorizationAfterglowValue = "ColorizationAfterglow";
    private const string AccentColorMenuValue = "AccentColorMenu";
    private const string StartColorMenuValue = "StartColorMenu";
    private const uint WmSettingChange = 0x001A;
    private const uint WmDwmColorizationColorChanged = 0x0320;
    private static readonly nint HwndBroadcast = new(0xFFFF);

    private readonly IDiagnosticLogService _logService = logService;

    public string? TryGetCurrentAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmRegistryPath, writable: false);
            var raw = key?.GetValue(ColorizationColorValue);
            if (raw is null)
            {
                return null;
            }

            var value = Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
            var r = (byte)((value >> 16) & 0xFF);
            var g = (byte)((value >> 8) & 0xFF);
            var b = (byte)(value & 0xFF);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to read current Windows accent color.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });

            return null;
        }
    }

    public bool TrySetAccentColor(string accentColor)
    {
        if (!TryParseHexColor(accentColor, out var r, out var g, out var b))
        {
            return false;
        }

        try
        {
            var argb = (uint)(0xFF000000 | (r << 16) | (g << 8) | b);
            using (var dwmKey = Registry.CurrentUser.CreateSubKey(DwmRegistryPath, writable: true))
            {
                dwmKey?.SetValue(ColorizationColorValue, unchecked((int)argb), RegistryValueKind.DWord);
                dwmKey?.SetValue(ColorizationAfterglowValue, unchecked((int)argb), RegistryValueKind.DWord);
            }

            using (var accentKey = Registry.CurrentUser.CreateSubKey(ExplorerAccentRegistryPath, writable: true))
            {
                accentKey?.SetValue(AccentColorMenuValue, unchecked((int)argb), RegistryValueKind.DWord);
                accentKey?.SetValue(StartColorMenuValue, unchecked((int)argb), RegistryValueKind.DWord);
            }

            SendNotifyMessage(HwndBroadcast, WmDwmColorizationColorChanged, (nuint)argb, 0);
            SendNotifyMessage(HwndBroadcast, WmSettingChange, 0, 0);
            return true;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to update Windows accent color.", new Dictionary<string, object?>
            {
                ["accentColor"] = accentColor,
                ["error"] = exception.Message
            });

            return false;
        }
    }

    private static bool TryParseHexColor(string? value, out uint r, out uint g, out uint b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length != 6 || !uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        r = (rgb >> 16) & 0xFF;
        g = (rgb >> 8) & 0xFF;
        b = rgb & 0xFF;
        return true;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool SendNotifyMessage(nint hWnd, uint msg, nuint wParam, nint lParam);
}
