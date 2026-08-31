using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class TopBarView : UserControl
{
    private const double DesktopIndicatorStride = 40d;
    private TopBarViewModel? _viewModel;
    private bool _hasPositionedDesktopIndicator;
    private bool _isPomodoroPillVisible;

    public TopBarView()
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

        _viewModel = eventArgs.NewValue as TopBarViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Dispatcher.BeginInvoke(() => UpdateActiveDesktopIndicator(false), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(() => UpdatePomodoroPillVisibility(false), DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(TopBarViewModel.WorkspaceTransitionVersion) && _viewModel is not null)
        {
            AnimateWorkspaceTransition(_viewModel.WorkspaceTransitionDirection);
            UpdateActiveDesktopIndicator(true);
            QueueTopBarLayoutUpdate();
            return;
        }

        if (eventArgs.PropertyName is nameof(TopBarViewModel.ShowMediaPill)
            or nameof(TopBarViewModel.IsShowingQuickAccessApps))
        {
            QueueTopBarLayoutUpdate();
        }

        if (eventArgs.PropertyName == nameof(TopBarViewModel.ShowPomodoroPill))
        {
            UpdatePomodoroPillVisibility(true);
            QueueTopBarLayoutUpdate();
            return;
        }

        if (eventArgs.PropertyName is nameof(TopBarViewModel.PomodoroRemainingText)
            or nameof(TopBarViewModel.PomodoroStatusText))
        {
            QueueTopBarLayoutUpdate();
        }
    }

    private void AnimateWorkspaceTransition(int direction)
    {
        var offset = direction switch
        {
            < 0 => -22d,
            > 0 => 22d,
            _ => 0d
        };

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var morphStartScale = direction == 0 ? 0.9d : 0.84d;

        DesktopStripRail.Opacity = 0.58d;
        LeftClusterTranslate.X = offset;
        LeftClusterScale.ScaleX = morphStartScale;
        LeftClusterScale.ScaleY = morphStartScale;

        LeftClusterTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(0, duration, easing));
        DesktopStripRail.BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
        LeftClusterScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
        LeftClusterScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
    }

    private void UpdateActiveDesktopIndicator(bool animate)
    {
        if (_viewModel is null)
        {
            return;
        }

        var activeIndex = _viewModel.ActiveDesktopStripIndex;
        if (activeIndex < 0)
        {
            ActiveDesktopIndicator.BeginAnimation(OpacityProperty, null);
            ActiveDesktopIndicator.Opacity = 0;
            return;
        }

        var targetX = activeIndex * DesktopIndicatorStride;
        var duration = TimeSpan.FromMilliseconds(_viewModel.IsShowingQuickAccessApps ? 170 : 230);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (!animate || !_hasPositionedDesktopIndicator)
        {
            ActiveDesktopIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ActiveDesktopIndicatorTranslate.X = targetX;
            ActiveDesktopIndicatorScale.ScaleX = 1;
            ActiveDesktopIndicatorScale.ScaleY = 1;
            ActiveDesktopIndicator.Opacity = 1;
            _hasPositionedDesktopIndicator = true;
            return;
        }

        ActiveDesktopIndicator.Opacity = 1;

        if (_viewModel.IsShowingQuickAccessApps || _viewModel.WorkspaceTransitionDirection == 0)
        {
            ActiveDesktopIndicatorTranslate.X = targetX;
            ActiveDesktopIndicatorScale.ScaleX = 0.72;
            ActiveDesktopIndicatorScale.ScaleY = 0.72;
            ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(1, duration, new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }));
            ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(1, duration, new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }));
            return;
        }

        ActiveDesktopIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(targetX, duration, easing));

        ActiveDesktopIndicatorScale.ScaleX = 1.62;
        ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));

        ActiveDesktopIndicatorScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(0.92, 1, duration, easing));
    }

    private void LeftCluster_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (_viewModel is null || eventArgs.Delta == 0)
        {
            return;
        }

        _viewModel.CycleWorkspace(eventArgs.Delta < 0 ? 1 : -1);
        eventArgs.Handled = true;
    }

    private void ActiveAppButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.HorizontalOffset = GetCenteredMenuOffset(element.ContextMenu, element);
        element.ContextMenu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void ActiveWindowContextMenu_OnOpened(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ContextMenu { PlacementTarget: FrameworkElement target } contextMenu)
        {
            return;
        }

        contextMenu.HorizontalOffset = GetCenteredMenuOffset(contextMenu, target);
    }

    private static double GetCenteredMenuOffset(ContextMenu contextMenu, FrameworkElement target)
    {
        var menuWidth = contextMenu.ActualWidth > 0 ? contextMenu.ActualWidth : 238d;
        return (target.ActualWidth - menuWidth) / 2d;
    }

    private void TrayItem_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: SystemTrayItem item })
        {
            return;
        }

        ShowTrayContextMenu(sender as Button, item);
        eventArgs.Handled = true;
    }

    private void ShowTrayContextMenu(Button? targetButton, SystemTrayItem item)
    {
        if (_viewModel is null || targetButton is null)
        {
            return;
        }

        var contextMenu = new ContextMenu { Style = (Style)FindResource("TopBarContextMenuStyle") };

        var openItem = new MenuItem { Header = "Open", Style = (Style)FindResource("TopBarMenuItemStyle") };
        openItem.Click += (_, _) => _viewModel.ActivateTrayItemCommand.Execute(item);
        contextMenu.Items.Add(openItem);

        var taskManagerItem = new MenuItem { Header = "Open Task Manager", Style = (Style)FindResource("TopBarMenuItemStyle") };
        taskManagerItem.Click += (_, _) => _viewModel.OpenTaskManagerCommand.Execute(null);
        contextMenu.Items.Add(taskManagerItem);

        var killItem = new MenuItem { Header = "Kill Process", Style = (Style)FindResource("TopBarMenuItemStyle") };
        killItem.Click += (_, _) => _viewModel.TerminateTrayItemCommand.Execute(item);
        contextMenu.Items.Add(killItem);

        contextMenu.PlacementTarget = targetButton;
        contextMenu.HorizontalOffset = GetCenteredMenuOffset(contextMenu, targetButton);
        contextMenu.IsOpen = true;
    }

    private void LayoutRoot_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateTopBarLayout();
    }

    private void Cluster_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, LeftCluster))
        {
            UpdateActiveDesktopIndicator(false);
        }

        QueueTopBarLayoutUpdate();
    }

    private void QueueTopBarLayoutUpdate()
    {
        Dispatcher.BeginInvoke(UpdateTopBarLayout, DispatcherPriority.Loaded);
    }

    private void UpdateTopBarLayout()
    {
        var mediaOffset = 46 + DesktopStripRail.ActualWidth + 14;
        MediaCluster.Margin = new Thickness(mediaOffset, 0, 0, 0);
        var pomodoroOffset = mediaOffset + (MediaCluster.Visibility == Visibility.Visible ? MediaCluster.ActualWidth + 10 : 0);
        PomodoroCluster.Margin = new Thickness(pomodoroOffset, 0, 0, 0);
        var leftReservedWidth = mediaOffset;
        if (MediaCluster.Visibility == Visibility.Visible)
        {
            leftReservedWidth = Math.Max(leftReservedWidth, mediaOffset + MediaCluster.ActualWidth);
        }

        if (PomodoroCluster.Visibility == Visibility.Visible)
        {
            leftReservedWidth = Math.Max(leftReservedWidth, pomodoroOffset + PomodoroCluster.ActualWidth);
        }

        var reservedSideWidth = Math.Max(leftReservedWidth, RightCluster.ActualWidth);
        var availableWidth = LayoutRoot.ActualWidth - (reservedSideWidth * 2) - 48;
        TitlePresenter.MaxWidth = Math.Max(120, availableWidth);
    }

    private void UpdatePomodoroPillVisibility(bool animate)
    {
        if (_viewModel is null)
        {
            return;
        }

        var shouldShow = _viewModel.ShowPomodoroPill;
        var duration = TimeSpan.FromMilliseconds(190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        PomodoroCluster.BeginAnimation(OpacityProperty, null);
        PomodoroClusterTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (!animate)
        {
            PomodoroCluster.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            PomodoroCluster.Opacity = shouldShow ? 1 : 0;
            PomodoroClusterTranslate.X = 0;
            PomodoroClusterScale.ScaleX = 1;
            PomodoroClusterScale.ScaleY = 1;
            _isPomodoroPillVisible = shouldShow;
            return;
        }

        if (shouldShow)
        {
            PomodoroCluster.Visibility = Visibility.Visible;
            PomodoroCluster.Opacity = 0;
            PomodoroClusterTranslate.X = -16;
            PomodoroClusterScale.ScaleX = 0.78;
            PomodoroClusterScale.ScaleY = 0.82;
            PomodoroCluster.BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
            PomodoroClusterTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(0, duration, easing));
            PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
            PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
            _isPomodoroPillVisible = true;
            return;
        }

        if (!_isPomodoroPillVisible)
        {
            PomodoroCluster.Visibility = Visibility.Collapsed;
            PomodoroCluster.Opacity = 0;
            return;
        }

        var fadeOut = AnimationHelper.CreateDoubleAnimation(0, duration, easing);
        fadeOut.Completed += (_, _) =>
        {
            PomodoroCluster.Visibility = Visibility.Collapsed;
        };
        PomodoroCluster.BeginAnimation(OpacityProperty, fadeOut);
        PomodoroClusterTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(-16, duration, easing));
        PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(0.8, duration, easing));
        PomodoroClusterScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(0.82, duration, easing));
        _isPomodoroPillVisible = false;
    }
}
