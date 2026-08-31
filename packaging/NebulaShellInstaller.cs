using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

internal static class NebulaShellInstaller
{
    private const string PayloadResourceName = "NebulaShell.zip";
    private const string UninstallResourceName = "uninstall.ps1";
    private const string DisplayVersion = "0.1.0";
    private static readonly string[] KnownNebulaProcessNames =
    {
        "CaelestiaWin.App",
        "CaelestiaWin.Terminal"
    };

    private static int Main(string[] args)
    {
        var quiet = Array.Exists(args, arg =>
            string.Equals(arg, "/Q", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-Quiet", StringComparison.OrdinalIgnoreCase));

        try
        {
            Install();
            if (!quiet)
            {
                Console.WriteLine("Nebula Shell installed successfully.");
                Console.WriteLine(@"Start Menu > Nebula Shell > Nebula Shell");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                Console.Error.WriteLine("Nebula Shell installation failed.");
                Console.Error.WriteLine(ex);
            }

            return 1;
        }
    }

    private static void Install()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var installRoot = Path.Combine(localAppData, "Programs", "NebulaShell");
        var startMenuRoot = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs", "Nebula Shell");
        var appExe = Path.Combine(installRoot, "CaelestiaWin.App.exe");
        var uninstallScript = Path.Combine(installRoot, "uninstall.ps1");

        StopRunningShell(installRoot);
        ResetInstallDirectory(installRoot);
        ExtractPayload(installRoot);
        WriteResourceToFile(UninstallResourceName, uninstallScript);

        Directory.CreateDirectory(startMenuRoot);
        CreateShortcut(Path.Combine(startMenuRoot, "Nebula Shell.lnk"), appExe, string.Empty, "Start Nebula Shell");
        CreateShortcut(Path.Combine(startMenuRoot, "Nebula Shell Safe Mode.lnk"), appExe, "--safe-mode", "Start Nebula Shell in safe mode");

        RegisterUninstallEntry(installRoot, appExe, uninstallScript);
    }

    private static void StopRunningShell(string installRoot)
    {
        var installRootFullPath = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited || process.Id == Process.GetCurrentProcess().Id)
                {
                    continue;
                }

                if (!ShouldStopProcess(process, installRootFullPath))
                {
                    continue;
                }

                TryStopProcess(process);
            }
            catch
            {
                // Installation should continue even if an older shell process has already exited.
            }
        }
    }

    private static void ResetInstallDirectory(string installRoot)
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        var installFullPath = Path.GetFullPath(installRoot);
        var localProgramsFullPath = Path.GetFullPath(localPrograms);

        if (!installFullPath.StartsWith(localProgramsFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Install path is outside the expected per-user Programs directory.");
        }

        if (Directory.Exists(installRoot))
        {
            DeleteDirectoryWithRetries(installRoot);
        }

        Directory.CreateDirectory(installRoot);
    }

    private static bool ShouldStopProcess(Process process, string installRootFullPath)
    {
        foreach (var processName in KnownNebulaProcessNames)
        {
            if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        try
        {
            var mainModule = process.MainModule;
            var mainModulePath = mainModule != null ? mainModule.FileName : null;
            if (string.IsNullOrWhiteSpace(mainModulePath))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(mainModulePath);
            return fullPath.StartsWith(installRootFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (process.CloseMainWindow())
            {
                if (process.WaitForExit(2500))
                {
                    return;
                }
            }
        }
        catch
        {
            // Fall back to forced termination below.
        }

        try
        {
            process.Kill();
            process.WaitForExit(5000);
        }
        catch
        {
            // A later delete retry may still succeed if the process exits meanwhile.
        }
    }

    private static void DeleteDirectoryWithRetries(string installRoot)
    {
        Exception lastError = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(installRoot);
                Directory.Delete(installRoot, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            Thread.Sleep(500 + (attempt * 250));
        }

        throw new IOException(
            "Nebula Shell could not update because files in the install directory are still in use. Close any running Nebula windows or terminals and run the installer again.",
            lastError);
    }

    private static void ClearReadOnlyAttributes(string installRoot)
    {
        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                // Best-effort attribute cleanup only.
            }
        }
    }

    private static void ExtractPayload(string installRoot)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(PayloadResourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded NebulaShell.zip payload was not found.");
            }

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var destinationPath = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
                    if (!destinationPath.StartsWith(Path.GetFullPath(installRoot), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Installer payload contains an invalid path.");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }
        }
    }

    private static void WriteResourceToFile(string resourceName, string destinationPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded resource was not found: " + resourceName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            using (var file = File.Create(destinationPath))
            {
                stream.CopyTo(file);
            }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        var shell = Activator.CreateInstance(shellType);
        var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
        SetComProperty(shortcut, "TargetPath", targetPath);
        SetComProperty(shortcut, "Arguments", arguments);
        SetComProperty(shortcut, "WorkingDirectory", Path.GetDirectoryName(targetPath));
        SetComProperty(shortcut, "Description", description);
        SetComProperty(shortcut, "IconLocation", targetPath + ",0");
        shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    private static void SetComProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new[] { value });
    }

    private static void RegisterUninstallEntry(string installRoot, string appExe, string uninstallScript)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NebulaShell"))
        {
            key.SetValue("DisplayName", "Nebula Shell", RegistryValueKind.String);
            key.SetValue("DisplayVersion", DisplayVersion, RegistryValueKind.String);
            key.SetValue("Publisher", "Nebula Shell", RegistryValueKind.String);
            key.SetValue("InstallLocation", installRoot, RegistryValueKind.String);
            key.SetValue("DisplayIcon", appExe, RegistryValueKind.String);
            key.SetValue("UninstallString", @"powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ + uninstallScript + @"""", RegistryValueKind.String);
            key.SetValue("QuietUninstallString", @"powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ + uninstallScript + @""" -Quiet", RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }
}
