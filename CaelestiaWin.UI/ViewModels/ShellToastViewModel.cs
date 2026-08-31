using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class ShellToastViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly INotificationService _notificationService;
    private readonly DispatcherTimer _hideTimer;
    private NotificationItem? _currentNotification;
    private bool _isVisible;

    public ShellToastViewModel(IAppStateService appStateService, INotificationService notificationService)
    {
        _appStateService = appStateService;
        _notificationService = notificationService;
        DismissCommand = new RelayCommand(Hide);
        InvokeActionCommand = new RelayCommand(InvokeAction);
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _hideTimer.Tick += (_, _) => Hide();

        if (notificationService.Notifications is INotifyCollectionChanged notifications)
        {
            notifications.CollectionChanged += OnNotificationsChanged;
        }

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
    }

    public ICommand DismissCommand { get; }

    public ICommand InvokeActionCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public NotificationItem? CurrentNotification
    {
        get => _currentNotification;
        private set
        {
            if (SetProperty(ref _currentNotification, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Message));
                OnPropertyChanged(nameof(SourceLabel));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(HasAction));
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(ProgressFraction));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string Title => CurrentNotification?.Title ?? string.Empty;

    public string Message => CurrentNotification?.Message ?? string.Empty;

    public string SourceLabel => CurrentNotification?.Source ?? "Nebula";

    public string KindLabel => CurrentNotification?.Kind.ToString() ?? string.Empty;

    public string ActionLabel => CurrentNotification?.PrimaryActionLabel ?? string.Empty;

    public bool HasAction => !string.IsNullOrWhiteSpace(CurrentNotification?.PrimaryActionLabel)
                             && !string.IsNullOrWhiteSpace(CurrentNotification?.PrimaryActionId);

    public bool HasProgress => CurrentNotification?.HasProgress == true;

    public double ProgressFraction => CurrentNotification?.NormalizedProgressFraction ?? 0d;

    public bool IsCompleted => CurrentNotification?.IsCompleted == true;

    public bool HasError => CurrentNotification?.HasError == true;

    public HorizontalAlignment ToastHorizontalAlignment => GetToastPosition() switch
    {
        NotificationToastPositionKind.TopLeft or NotificationToastPositionKind.BottomLeft => HorizontalAlignment.Left,
        _ => HorizontalAlignment.Right
    };

    public VerticalAlignment ToastVerticalAlignment => GetToastPosition() switch
    {
        NotificationToastPositionKind.BottomLeft or NotificationToastPositionKind.BottomRight => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Top
    };

    public Thickness ToastMargin => GetToastPosition() switch
    {
        NotificationToastPositionKind.TopLeft => new Thickness(28, 92, 0, 0),
        NotificationToastPositionKind.TopRight => new Thickness(0, 92, 28, 0),
        NotificationToastPositionKind.BottomLeft => new Thickness(28, 0, 0, 42),
        _ => new Thickness(0, 0, 28, 42)
    };

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Replace
            && eventArgs.NewItems?.OfType<NotificationItem>().FirstOrDefault() is { } updated
            && CurrentNotification?.Id == updated.Id)
        {
            CurrentNotification = updated;
            return;
        }

        if (!_appStateService.Config.Notifications.EnableShellToasts)
        {
            return;
        }

        var notification = eventArgs.NewItems?.OfType<NotificationItem>().FirstOrDefault();
        if (notification is null || !notification.ShowToast)
        {
            return;
        }

        CurrentNotification = notification;
        IsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IAppStateService.Config))
        {
            return;
        }

        OnPropertyChanged(nameof(ToastHorizontalAlignment));
        OnPropertyChanged(nameof(ToastVerticalAlignment));
        OnPropertyChanged(nameof(ToastMargin));
    }

    private void Hide()
    {
        _hideTimer.Stop();
        IsVisible = false;
    }

    private void InvokeAction()
    {
        if (CurrentNotification is null || !HasAction)
        {
            return;
        }

        _notificationService.InvokeAction(CurrentNotification.Id);
        Hide();
    }

    private NotificationToastPositionKind GetToastPosition()
    {
        return _appStateService.Config.Notifications.ShellToastPosition;
    }
}
