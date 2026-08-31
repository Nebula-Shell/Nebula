using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class OverviewView : UserControl
{
    private OverviewViewModel? _viewModel;

    public OverviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = eventArgs.NewValue as OverviewViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateOpenState(_viewModel.IsOpen, animate: false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(OverviewViewModel.IsOpen) && _viewModel is not null)
        {
            UpdateOpenState(_viewModel.IsOpen, animate: true);
        }
    }

    private void Backdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is not null && ReferenceEquals(eventArgs.OriginalSource, Backdrop))
        {
            _viewModel.CloseCommand.Execute(null);
        }
    }

    private void UpdateOpenState(bool isOpen, bool animate)
    {
        var duration = TimeSpan.FromMilliseconds(AnimationHelper.GetDoubleResource("AnimationNormalMs", 220d));
        var easing = AnimationHelper.CreateOverlayEasing();

        if (isOpen)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;

            if (!animate)
            {
                Opacity = 1;
                return;
            }

            BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(0, 1, duration, easing));
            return;
        }

        IsHitTestVisible = false;
        if (!animate)
        {
            Visibility = Visibility.Collapsed;
            Opacity = 0;
            return;
        }

        var fadeOut = AnimationHelper.CreateDoubleAnimation(0, duration, easing);
        fadeOut.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
