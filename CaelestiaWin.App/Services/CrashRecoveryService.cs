using System.Diagnostics;
using System.Windows;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.App.Services;

public sealed class CrashRecoveryService(
    RuntimeOptions runtimeOptions,
    IShellLifetimeService shellLifetimeService,
    IDiagnosticLogService logService)
{
    public void HandleCriticalFailure(string context, Exception exception, bool preferSafeMode)
    {
        logService.Error(context, exception, new Dictionary<string, object?>
        {
            ["safeMode"] = runtimeOptions.IsSafeMode,
            ["restartOnCrash"] = runtimeOptions.RestartOnCrash
        });

        if (!runtimeOptions.RestartOnCrash || runtimeOptions.RestartedAfterCrash)
        {
            shellLifetimeService.AllowExit();
            return;
        }

        try
        {
            var arguments = runtimeOptions.BuildStartupArguments(preferSafeMode || runtimeOptions.IsSafeMode);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = runtimeOptions.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false
            });
        }
        catch (Exception restartException)
        {
            logService.Error("Nebula Shell failed while trying to restart after a crash.", restartException);
        }
        finally
        {
            shellLifetimeService.AllowExit();
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown(-1));
        }
    }
}
