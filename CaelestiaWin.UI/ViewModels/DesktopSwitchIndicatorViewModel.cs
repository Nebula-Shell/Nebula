using System.ComponentModel;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.UI.ViewModels;

public sealed class DesktopSwitchIndicatorViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly DispatcherTimer _hideTimer;
    private string _label = "Desktop 1";
    private bool _isVisible;
    private int _lastWorkspaceIndex;

    public DesktopSwitchIndicatorViewModel(IAppStateService appStateService)
    {
        _appStateService = appStateService;
        _lastWorkspaceIndex = _appStateService.ActiveWorkspaceIndex;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            IsVisible = false;
        };

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IAppStateService.ActiveWorkspaceIndex))
        {
            return;
        }

        var current = _appStateService.ActiveWorkspaceIndex;
        if (current == _lastWorkspaceIndex)
        {
            return;
        }

        _lastWorkspaceIndex = current;
        Label = current switch
        {
            9 => "Discord Quick Access",
            10 => "Spotify Quick Access",
            11 => "GitHub Desktop Quick Access",
            _ => $"Desktop {current}"
        };
        IsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
