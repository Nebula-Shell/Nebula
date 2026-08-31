using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class OverviewViewModel : ObservableObjectBase
{
    private readonly IOverviewService _overviewService;
    private bool _isOpen;

    public OverviewViewModel(IOverviewService overviewService)
    {
        _overviewService = overviewService;
        _isOpen = _overviewService.IsOpen;
        ActivateWindowCommand = new RelayCommand<OverviewWindowItem>(ActivateWindow);
        CloseCommand = new RelayCommand(_overviewService.Close);
        _overviewService.PropertyChanged += OnOverviewServicePropertyChanged;
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public ReadOnlyObservableCollection<OverviewWindowItem> Windows => _overviewService.Windows;

    public ICommand ActivateWindowCommand { get; }

    public ICommand CloseCommand { get; }

    private void ActivateWindow(OverviewWindowItem? item)
    {
        if (item is null)
        {
            return;
        }

        _ = _overviewService.ActivateWindow(item.Handle);
    }

    private void OnOverviewServicePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IOverviewService.IsOpen))
        {
            IsOpen = _overviewService.IsOpen;
        }
    }
}
