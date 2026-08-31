using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class LeftBarView : UserControl
{
    private TopBarViewModel? _viewModel;

    public LeftBarView()
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
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(TopBarViewModel.WorkspaceTransitionVersion) && _viewModel is not null)
        {
            AnimateWorkspaceTransition(_viewModel.WorkspaceTransitionDirection);
        }
    }

    private void AnimateWorkspaceTransition(int direction)
    {
        var offset = direction switch
        {
            < 0 => -16d,
            > 0 => 16d,
            _ => 0d
        };

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var morphStartScale = direction == 0 ? 0.9d : 0.84d;

        DesktopStripRail.Opacity = 0.58d;
        DesktopStripTranslate.Y = offset;
        DesktopStripScale.ScaleX = morphStartScale;
        DesktopStripScale.ScaleY = morphStartScale;

        DesktopStripTranslate.BeginAnimation(TranslateTransform.YProperty, AnimationHelper.CreateDoubleAnimation(0, duration, easing));
        DesktopStripRail.BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
        DesktopStripScale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
        DesktopStripScale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimationHelper.CreateDoubleAnimation(1, duration, easing));
    }

    private void DesktopStripRail_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (_viewModel is null || eventArgs.Delta == 0)
        {
            return;
        }

        _viewModel.CycleWorkspace(eventArgs.Delta < 0 ? 1 : -1);
        eventArgs.Handled = true;
    }

    private void TrayItem_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: SystemTrayItem item })
        {
            return;
        }

        if (_viewModel is null)
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

        contextMenu.PlacementTarget = sender as Button;
        contextMenu.IsOpen = true;
        eventArgs.Handled = true;
    }
}
