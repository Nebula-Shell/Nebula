using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class LauncherOverlayView : UserControl
{
    private LauncherViewModel? _viewModel;

    public LauncherOverlayView()
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

        _viewModel = eventArgs.NewValue as LauncherViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateOpenState(_viewModel.IsOpen, animate: false);
        }
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                eventArgs.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                eventArgs.Handled = true;
                break;
            case Key.Enter:
                if (_viewModel.LaunchSelectedCommand.CanExecute(null))
                {
                    _viewModel.LaunchSelectedCommand.Execute(null);
                }

                eventArgs.Handled = true;
                break;
            case Key.Escape:
                _viewModel.Close();
                eventArgs.Handled = true;
                break;
        }
    }

    private void Backdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is not null && ReferenceEquals(eventArgs.OriginalSource, Backdrop))
        {
            _viewModel.Close();
        }
    }

    private void ResultsList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (FindAncestor<Button>(eventArgs.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not ItemsControl itemsControl)
        {
            return;
        }

        var clickedItem = ItemsControl.ContainerFromElement(itemsControl, eventArgs.OriginalSource as DependencyObject) as ListBoxItem;
        if (clickedItem?.DataContext is null)
        {
            return;
        }

        _viewModel.SelectedItem = clickedItem.DataContext as LauncherSearchResult;
        if (_viewModel.LaunchSelectedCommand.CanExecute(null))
        {
            _viewModel.LaunchSelectedCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(LauncherViewModel.IsOpen) && _viewModel is not null)
        {
            UpdateOpenState(_viewModel.IsOpen, animate: true);
        }
    }

    private void UpdateOpenState(bool isOpen, bool animate)
    {
        var duration = TimeSpan.FromMilliseconds(AnimationHelper.GetDoubleResource("AnimationNormalMs", 220d));
        var easing = AnimationHelper.CreateOverlayEasing();
        var scaleFrom = AnimationHelper.GetDoubleResource("AnimationLauncherScaleFrom", 0.96d);

        if (isOpen)
        {
                Visibility = Visibility.Visible;
                IsHitTestVisible = true;

                if (!animate)
                {
                    Opacity = 1;
                    PanelScale.ScaleX = 1;
                    PanelScale.ScaleY = 1;
                    FocusSearchBox();
                    return;
                }

            BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(0, 1, duration, easing));
            PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(scaleFrom, 1, duration, easing));
            PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(scaleFrom, 1, duration, easing));
            FocusSearchBox();
            return;
        }

        IsHitTestVisible = false;

        if (!animate)
        {
            Visibility = Visibility.Collapsed;
            Opacity = 0;
            PanelScale.ScaleX = scaleFrom;
            PanelScale.ScaleY = scaleFrom;
            return;
        }

        var fadeOut = AnimationHelper.CreateDoubleAnimation(0, duration, easing);
        fadeOut.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, fadeOut);
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(Math.Min(0.99d, scaleFrom + 0.02d), duration, easing));
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(Math.Min(0.99d, scaleFrom + 0.02d), duration, easing));
    }

    private void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            Keyboard.Focus(SearchBox);
        }));
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
