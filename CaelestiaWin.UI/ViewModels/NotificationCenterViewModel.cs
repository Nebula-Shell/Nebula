using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class NotificationCenterViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private bool _isOpen;
    private Thickness _panelMargin = new(0, 0, 20, 16);
    private bool _hasNotifications;
    private string _calendarTitle = string.Empty;

    public NotificationCenterViewModel(IAppStateService appStateService, INotificationService notificationService)
    {
        _appStateService = appStateService;
        Notifications = notificationService.Notifications;
        CalendarDays = [];
        IsOpen = appStateService.IsNotificationCenterOpen;
        HasNotifications = Notifications.Count > 0;
        BuildCalendar(DateTime.Today);

        ClearAllCommand = new RelayCommand(notificationService.ClearAll);
        CloseCommand = new RelayCommand(() => _appStateService.IsNotificationCenterOpen = false);
        InvokeNotificationActionCommand = new RelayCommand<NotificationItem>(item =>
        {
            if (item is not null)
            {
                notificationService.InvokeAction(item.Id);
            }
        });
        DismissNotificationCommand = new RelayCommand<NotificationItem>(item =>
        {
            if (item is not null)
            {
                notificationService.Dismiss(item.Id);
            }
        });

        UpdateRightInset();
        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        if (Notifications is INotifyCollectionChanged notifications)
        {
            notifications.CollectionChanged += (_, _) => HasNotifications = Notifications.Count > 0;
        }
    }

    public ReadOnlyObservableCollection<NotificationItem> Notifications { get; }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; }

    public ICommand ClearAllCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand InvokeNotificationActionCommand { get; }

    public ICommand DismissNotificationCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public Thickness PanelMargin
    {
        get => _panelMargin;
        private set => SetProperty(ref _panelMargin, value);
    }

    public bool HasNotifications
    {
        get => _hasNotifications;
        private set => SetProperty(ref _hasNotifications, value);
    }

    public string CalendarTitle
    {
        get => _calendarTitle;
        private set => SetProperty(ref _calendarTitle, value);
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.IsNotificationCenterOpen))
        {
            IsOpen = _appStateService.IsNotificationCenterOpen;
        }

        if (eventArgs.PropertyName == nameof(IAppStateService.IsControlCenterOpen))
        {
            UpdateRightInset();
        }
    }

    private void UpdateRightInset()
    {
        PanelMargin = _appStateService.IsControlCenterOpen
            ? new Thickness(0, 0, 476, 16)
            : new Thickness(0, 0, 20, 16);
    }

    private void BuildCalendar(DateTime date)
    {
        CalendarTitle = date.ToString("MMMM yyyy");
        CalendarDays.Clear();

        var firstDay = new DateTime(date.Year, date.Month, 1);
        var leadingBlankCount = (int)firstDay.DayOfWeek;
        for (var i = 0; i < leadingBlankCount; i++)
        {
            CalendarDays.Add(new CalendarDayViewModel());
        }

        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        for (var day = 1; day <= daysInMonth; day++)
        {
            CalendarDays.Add(new CalendarDayViewModel
            {
                Day = day,
                IsInCurrentMonth = true,
                IsToday = day == date.Day
            });
        }

        while (CalendarDays.Count % 7 != 0)
        {
            CalendarDays.Add(new CalendarDayViewModel());
        }
    }
}
