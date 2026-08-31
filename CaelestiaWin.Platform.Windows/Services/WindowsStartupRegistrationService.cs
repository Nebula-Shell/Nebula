using Microsoft.Win32;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService, IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NebulaShell";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled, string executablePath, string arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var quotedPath = $"\"{executablePath}\"";
        var commandLine = string.IsNullOrWhiteSpace(arguments)
            ? quotedPath
            : $"{quotedPath} {arguments}".Trim();
        key.SetValue(ValueName, commandLine);
    }
}
