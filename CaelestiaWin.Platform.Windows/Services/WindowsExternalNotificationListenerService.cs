using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsExternalNotificationListenerService(
    INotificationService notificationService,
    IDiagnosticLogService logService) : IExternalNotificationListenerService
{
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        TryStart();
    }

    public void Stop()
    {
        _started = false;
    }

    private void TryStart()
    {
        try
        {
            // Windows exposes all-app toast mirroring through UserNotificationListener, but
            // unpackaged WPF shells do not always get a projected WinRT type. Keep the probe
            // isolated so notification mirroring can light up later without risking startup.
            var listenerType = Type.GetType(
                "Windows.UI.Notifications.Management.UserNotificationListener, Windows, ContentType=WindowsRuntime",
                throwOnError: false);

            if (listenerType is null)
            {
                LogUnavailable("WinRT notification listener projection is unavailable for this process.");
                return;
            }

            LogUnavailable("WinRT notification listener projection exists, but this build keeps listener activation guarded until packaged app identity is available.");
        }
        catch (Exception exception)
        {
            logService.Warn("Windows app notification listener probe failed.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void LogUnavailable(string reason)
    {
        logService.Warn("Windows app notification mirroring is unavailable.", new Dictionary<string, object?>
        {
            ["reason"] = reason
        });

        notificationService.Push(
            "App notification access unavailable",
            "Nebula can show its own notifications, but Windows does not expose all-app notification mirroring to this unpackaged shell process yet.",
            kind: NotificationKind.Warning,
            source: "Notifications",
            showToast: false);
    }
}
