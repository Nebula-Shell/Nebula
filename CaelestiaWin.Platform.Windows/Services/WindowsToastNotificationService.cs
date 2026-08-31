using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsToastNotificationService(IDiagnosticLogService logService) : IToastNotificationService
{
    private const string AppId = "NebulaShell.Desktop";
    private const string ShortcutName = "Nebula Shell.lnk";
    private bool _isInitialized;
    private bool _isDisabled;

    public bool IsAvailable => OperatingSystem.IsWindows() && !_isDisabled;

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            EnsureStartMenuShortcut();
        }
        catch (Exception exception)
        {
            logService.Warn("Windows toast app identity setup failed. Shell toasts remain available.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }

        _isInitialized = true;
        logService.Info("Windows toast notification bridge initialized.", new Dictionary<string, object?>
        {
            ["appId"] = AppId
        });
    }

    public void Show(NotificationItem notification)
    {
        if (!IsAvailable)
        {
            return;
        }

        if (!_isInitialized)
        {
            Initialize();
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = BuildToastCommand(notification),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardError = true
                });

                if (process is null)
                {
                    _isDisabled = true;
                    logService.Warn("Windows toast notification bridge could not start PowerShell and was disabled for this session.");
                    return;
                }

                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    logService.Warn("Windows toast notification bridge timed out.");
                    return;
                }

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    _isDisabled = true;
                    logService.Warn("Windows toast notification bridge failed and was disabled for this session.", new Dictionary<string, object?>
                    {
                        ["exitCode"] = process.ExitCode,
                        ["error"] = error
                    });
                }
            }
            catch (Exception exception)
            {
                _isDisabled = true;
                logService.Warn("Windows toast notification bridge failed and was disabled for this session.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        });
    }

    private static string BuildToastCommand(NotificationItem notification)
    {
        var title = SecurityElement.Escape(notification.Title) ?? string.Empty;
        var message = SecurityElement.Escape(notification.Message) ?? string.Empty;
        var source = SecurityElement.Escape(notification.Source) ?? string.Empty;
        var xml = "<toast><visual><binding template='ToastGeneric'><text>" +
                  title +
                  "</text><text>" +
                  message +
                  "</text><text>" +
                  source +
                  "</text></binding></visual></toast>";

        var script =
            "$ErrorActionPreference='Stop';" +
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;" +
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null;" +
            "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument;" +
            "$xml.LoadXml('" + EscapePowerShellSingleQuoted(xml) + "');" +
            "$toast = [Windows.UI.Notifications.ToastNotification]::new($xml);" +
            "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('" + AppId + "').Show($toast);";

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}";
    }

    private static void EnsureStartMenuShortcut()
    {
        var shortcutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs");
        Directory.CreateDirectory(shortcutDirectory);

        var shortcutPath = Path.Combine(shortcutDirectory, ShortcutName);
        var targetPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        // Desktop toast activation on Windows 10 expects a Start Menu shortcut that carries the same AUMID.
        var shellLink = (IShellLinkW)(object)new CShellLink();
        try
        {
            shellLink.SetPath(targetPath);
            shellLink.SetArguments(string.Empty);
            shellLink.SetDescription("Nebula Shell");
            shellLink.SetIconLocation(targetPath, 0);

            var propertyStore = (IPropertyStore)shellLink;
            var appIdKey = PropertyKeys.AppUserModelId;
            using var appId = PropVariant.FromString(AppId);
            propertyStore.SetValue(ref appIdKey, ref appId.Value);
            propertyStore.Commit();

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class CShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, nint pfd, uint fFlags);

        void GetIDList(out nint ppidl);

        void SetIDList(nint pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(nint hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000138-0000-0000-C000-000000000046")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);

        void GetAt(uint iProp, out PropertyKey pkey);

        void GetValue(ref PropertyKey key, out PropVariant pv);

        void SetValue(ref PropertyKey key, ref PropVariant propvar);

        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    private static class PropertyKeys
    {
        public static PropertyKey AppUserModelId => new()
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = 5
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort _variantType;
        private readonly ushort _reserved1;
        private readonly ushort _reserved2;
        private readonly ushort _reserved3;
        private nint _value;

        public static DisposablePropVariant FromString(string value)
        {
            return new DisposablePropVariant(new PropVariant
            {
                _variantType = 31,
                _value = Marshal.StringToCoTaskMemUni(value)
            });
        }

        public sealed class DisposablePropVariant(PropVariant value) : IDisposable
        {
            public PropVariant Value = value;

            public void Dispose()
            {
                if (Value._value == nint.Zero)
                {
                    return;
                }

                Marshal.FreeCoTaskMem(Value._value);
                Value._value = nint.Zero;
            }
        }
    }
}
