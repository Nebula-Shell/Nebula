using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.IO;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Core.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IToastNotificationService _toastNotificationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAppStateService _appStateService;
    private readonly ILoggerService _logService;
    private readonly ObservableCollection<NotificationItem> _notifications = [];
    private readonly ReadOnlyObservableCollection<NotificationItem> _readonlyNotifications;

    public NotificationService(
        IToastNotificationService toastNotificationService,
        IUiDispatcher uiDispatcher,
        IAppStateService appStateService,
        ILoggerService logService)
    {
        _toastNotificationService = toastNotificationService;
        _uiDispatcher = uiDispatcher;
        _appStateService = appStateService;
        _logService = logService;
        _readonlyNotifications = new ReadOnlyObservableCollection<NotificationItem>(_notifications);
    }

    public ReadOnlyObservableCollection<NotificationItem> Notifications => _readonlyNotifications;

    public event EventHandler<NotificationActionRequestedEventArgs>? ActionRequested;

    public void Push(
        string title,
        string message,
        DateTimeOffset? timestamp = null,
        NotificationKind kind = NotificationKind.Info,
        string source = "Nebula",
        bool showToast = true,
        string? primaryActionLabel = null,
        string? primaryActionId = null,
        Guid? notificationId = null,
        double? progressFraction = null,
        bool isCompleted = false,
        bool hasError = false)
    {
        var notification = new NotificationItem(
            notificationId ?? Guid.NewGuid(),
            title.Trim(),
            message.Trim(),
            timestamp ?? DateTimeOffset.Now,
            kind,
            string.IsNullOrWhiteSpace(source) ? "Nebula" : source.Trim(),
            showToast,
            string.IsNullOrWhiteSpace(primaryActionLabel) ? null : primaryActionLabel.Trim(),
            string.IsNullOrWhiteSpace(primaryActionId) ? null : primaryActionId.Trim(),
            progressFraction,
            isCompleted,
            hasError);

        _ = _uiDispatcher.InvokeAsync(() =>
        {
            _notifications.Insert(0, notification);
            TrimToConfiguredLimit();
        });

        if (!showToast || !_appStateService.Config.Notifications.EnableWindowsToasts)
        {
            PlayNotificationSound(notification);
            return;
        }

        try
        {
            _toastNotificationService.Show(notification);
        }
        catch (Exception exception)
        {
            _logService.Warn("Notification toast dispatch failed.", new Dictionary<string, object?>
            {
                ["title"] = notification.Title,
                ["source"] = notification.Source,
                ["error"] = exception.Message
            });
        }

        PlayNotificationSound(notification);
    }

    public void Update(
        Guid notificationId,
        string? title = null,
        string? message = null,
        double? progressFraction = null,
        bool? isCompleted = null,
        bool? hasError = null,
        DateTimeOffset? timestamp = null,
        bool? showToast = null)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            var index = -1;
            for (var position = 0; position < _notifications.Count; position++)
            {
                if (_notifications[position].Id != notificationId)
                {
                    continue;
                }

                index = position;
                break;
            }

            if (index < 0 || index >= _notifications.Count)
            {
                return;
            }

            var existing = _notifications[index];
            _notifications[index] = existing with
            {
                Title = title?.Trim() ?? existing.Title,
                Message = message?.Trim() ?? existing.Message,
                ProgressFraction = progressFraction ?? existing.ProgressFraction,
                IsCompleted = isCompleted ?? existing.IsCompleted,
                HasError = hasError ?? existing.HasError,
                Timestamp = timestamp ?? existing.Timestamp,
                ShowToast = showToast ?? existing.ShowToast
            };
        });
    }

    public void InvokeAction(Guid notificationId)
    {
        var existing = _notifications.FirstOrDefault(notification => notification.Id == notificationId);
        if (existing?.PrimaryActionId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new NotificationActionRequestedEventArgs(existing, existing.PrimaryActionId));
    }

    public void Dismiss(Guid notificationId)
    {
        var existing = _notifications.FirstOrDefault(notification => notification.Id == notificationId);
        if (existing is not null)
        {
            _notifications.Remove(existing);
        }
    }

    public void ClearAll()
    {
        _notifications.Clear();
    }

    private void TrimToConfiguredLimit()
    {
        var maxItems = Math.Clamp(_appStateService.Config.Notifications.MaxItems, 1, 100);
        while (_notifications.Count > maxItems)
        {
            _notifications.RemoveAt(_notifications.Count - 1);
        }
    }

    private void PlayNotificationSound(NotificationItem notification)
    {
        if (!_appStateService.Config.Notifications.EnableNotificationSounds)
        {
            return;
        }

        try
        {
            var customPath = _appStateService.Config.Notifications.CustomSoundPath;
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                try
                {
#pragma warning disable CA1416 // Nebula is Windows-only, SoundPlayer is available
                    var player = new System.Media.SoundPlayer(customPath);
                    player.Play();
#pragma warning restore CA1416
                    return;
                }
                catch
                {
                    // fall through to MessageBeep on any playback error
                }
            }

            switch (notification.Kind)
            {
                case NotificationKind.Error:
                    MessageBeep(MessageBeepType.Hand);
                    break;
                case NotificationKind.Warning:
                    MessageBeep(MessageBeepType.Exclamation);
                    break;
                case NotificationKind.Success:
                case NotificationKind.Info:
                default:
                    MessageBeep(MessageBeepType.Asterisk);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logService.Warn("Notification sound playback failed.", new Dictionary<string, object?>
            {
                ["title"] = notification.Title,
                ["source"] = notification.Source,
                ["error"] = exception.Message
            });
        }
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(MessageBeepType type);

    private enum MessageBeepType : uint
    {
        Asterisk = 0x00000040,
        Exclamation = 0x00000030,
        Hand = 0x00000010
    }
}
