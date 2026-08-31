using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsExplorerIntegrationService(IDiagnosticLogService logService) : IExplorerIntegrationService
{
    private static readonly string[] DirectLaunchExtensions =
    [
        ".exe",
        ".com",
        ".bat",
        ".cmd"
    ];

    public bool IsExplorerRunning => Process.GetProcessesByName("explorer").Length > 0;

    public bool IsTrayAvailable => false;

    public bool IsShellServicesAvailable => false;

    public ProcessStartInfo CreateAppLaunchStartInfo(AppLaunchItem app)
    {
        if (IsPackagedApp(app))
        {
            return CreatePackagedAppFallbackStartInfo(app);
        }

        if (!string.IsNullOrWhiteSpace(app.ResolvedTargetPath)
            && IsDirectLaunchCandidate(app.ResolvedTargetPath))
        {
            return CreateExecutableLaunchStartInfo(app.ResolvedTargetPath, app.Arguments);
        }

        if (IsDirectLaunchCandidate(app.LaunchPath))
        {
            return CreateExecutableLaunchStartInfo(app.LaunchPath, app.Arguments);
        }

        return CreateShellIndependentLaunchStartInfo(app.LaunchPath, app.Arguments);
    }

    public Process? LaunchApp(AppLaunchItem app)
    {
        if (IsPackagedApp(app))
        {
            try
            {
                var appUserModelId = app.ResolvedTargetPath ?? app.LaunchPath["shell:AppsFolder\\".Length..];
                var activationManager = (IApplicationActivationManager)Activator.CreateInstance(typeof(ApplicationActivationManager))!;
                _ = activationManager.ActivateApplication(
                    appUserModelId,
                    app.Arguments ?? string.Empty,
                    ActivateOptions.None,
                    out var processId);

                if (processId != 0)
                {
                    try
                    {
                        return Process.GetProcessById((int)processId);
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            }
            catch (Exception exception)
            {
                logService.Warn("Packaged app activation failed; falling back to shell launch.", new Dictionary<string, object?>
                {
                    ["app"] = app.DisplayName,
                    ["launchPath"] = app.LaunchPath,
                    ["error"] = exception.Message
                });
            }
        }

        return Process.Start(CreateAppLaunchStartInfo(app));
    }

    public ProcessStartInfo CreateExecutableLaunchStartInfo(string executablePath, string? arguments = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false
        };

        if (Path.IsPathRooted(executablePath))
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                startInfo.WorkingDirectory = directory;
            }
        }

        return startInfo;
    }

    public bool StopExplorerShell()
    {
        var stoppedCount = 0;
        var sawExplorer = false;
        var stableAbsentSince = DateTimeOffset.MinValue;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(7);
        var attempt = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            attempt++;
            var explorerProcesses = Process.GetProcessesByName("explorer");
            if (explorerProcesses.Length == 0)
            {
                if (!sawExplorer)
                {
                    logService.Info("Explorer shutdown requested, but explorer.exe is not running.");
                    return true;
                }

                if (stableAbsentSince == DateTimeOffset.MinValue)
                {
                    stableAbsentSince = DateTimeOffset.UtcNow;
                }

                if (DateTimeOffset.UtcNow - stableAbsentSince >= TimeSpan.FromMilliseconds(900))
                {
                    break;
                }

                Thread.Sleep(150);
                continue;
            }

            sawExplorer = true;
            stableAbsentSince = DateTimeOffset.MinValue;

            foreach (var explorerProcess in explorerProcesses)
            {
                using (explorerProcess)
                {
                    try
                    {
                        var processId = explorerProcess.Id;
                        explorerProcess.Kill(entireProcessTree: false);
                        if (!explorerProcess.WaitForExit(1800))
                        {
                            logService.Warn("Explorer process did not exit within the expected timeout.", new Dictionary<string, object?>
                            {
                                ["processId"] = processId,
                                ["attempt"] = attempt
                            });
                            continue;
                        }

                        stoppedCount++;
                        logService.Info("Stopped explorer.exe for Nebula shell ownership.", new Dictionary<string, object?>
                        {
                            ["processId"] = processId,
                            ["attempt"] = attempt
                        });
                    }
                    catch (Exception exception)
                    {
                        logService.Error("Failed to stop explorer.exe.", exception, new Dictionary<string, object?>
                        {
                            ["processId"] = TryGetProcessId(explorerProcess),
                            ["attempt"] = attempt
                        });
                    }
                }
            }

            Thread.Sleep(250);
        }

        var explorerRunning = IsExplorerRunning;
        logService.Info("Explorer stop settle loop finished.", new Dictionary<string, object?>
        {
            ["stoppedCount"] = stoppedCount,
            ["explorerRunning"] = explorerRunning,
            ["attempts"] = attempt
        });

        return stoppedCount > 0 && !explorerRunning;
    }

    public bool StartExplorerShell()
    {
        if (IsExplorerRunning)
        {
            logService.Info("Explorer start requested, but explorer.exe is already running.");
            return true;
        }

        try
        {
            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = File.Exists(explorerPath) ? explorerPath : "explorer.exe",
                UseShellExecute = false
            });

            logService.Info("Started explorer.exe as the Windows shell fallback.");
            return true;
        }
        catch (Exception exception)
        {
            logService.Error("Failed to start explorer.exe.", exception);
            return false;
        }
    }

    private static ProcessStartInfo CreateShellIndependentLaunchStartInfo(string launchTarget, string? arguments)
    {
        var escapedTarget = EscapeForCmd(launchTarget);
        var escapedArguments = string.IsNullOrWhiteSpace(arguments) ? string.Empty : $" {arguments}";

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" \"{escapedTarget}\"{escapedArguments}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static ProcessStartInfo CreatePackagedAppFallbackStartInfo(AppLaunchItem app)
    {
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" \"{EscapeForCmd(app.LaunchPath)}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static bool IsPackagedApp(AppLaunchItem app)
    {
        return app.Source.Equals("UWP", StringComparison.OrdinalIgnoreCase)
               || app.LaunchPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectLaunchCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension)
            && DirectLaunchExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return !Path.IsPathRooted(path) && !path.Contains(Path.DirectorySeparatorChar) && !path.Contains(Path.AltDirectorySeparatorChar);
    }

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string EscapeForCmd(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManager
    {
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);

        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            nint itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string? verb,
            out uint processId);

        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            nint itemArray,
            out uint processId);
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0x00000000,
        DesignMode = 0x00000001,
        NoErrorUi = 0x00000002,
        NoSplashScreen = 0x00000004
    }
}
