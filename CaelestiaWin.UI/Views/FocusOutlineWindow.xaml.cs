using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using CaelestiaWin.UI.Interop;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class FocusOutlineWindow : Window
{
    private readonly FocusOutlineViewModel _viewModel;
    private nint _handle;

    public FocusOutlineWindow(FocusOutlineViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _handle = new WindowInteropHelper(this).Handle;
        UpdateWindowState();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        UpdateWindowState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FocusOutlineViewModel.IsVisible)
            or nameof(FocusOutlineViewModel.Left)
            or nameof(FocusOutlineViewModel.Top)
            or nameof(FocusOutlineViewModel.Width)
            or nameof(FocusOutlineViewModel.Height)
            or nameof(FocusOutlineViewModel.TargetWindowHandle))
        {
            Dispatcher.BeginInvoke(UpdateWindowState);
        }
    }

    private void UpdateWindowState()
    {
        if (!_viewModel.IsVisible)
        {
            Hide();
            return;
        }

        Left = _viewModel.Left;
        Top = _viewModel.Top;
        Width = Math.Max(1, _viewModel.Width);
        Height = Math.Max(1, _viewModel.Height);

        if (!IsVisible)
        {
            Show();
        }

        WindowZOrder.PlaceBelow(_handle, _viewModel.TargetWindowHandle);
    }
}
