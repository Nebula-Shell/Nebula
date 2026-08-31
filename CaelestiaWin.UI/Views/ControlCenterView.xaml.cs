using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class ControlCenterView : UserControl
{
    private ControlCenterViewModel? _viewModel;
    private int _transitionVersion;
    private Point _dragStartPoint;

    public ControlCenterView()
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

        _viewModel = eventArgs.NewValue as ControlCenterViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateOpenState(_viewModel.IsOpen, animate: false);
        }
    }

    private void Backdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is not null && ReferenceEquals(eventArgs.OriginalSource, Backdrop))
        {
            _viewModel.CloseCommand.Execute(null);
        }
    }

    private void SoundCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null || IsInteractiveChild(eventArgs.OriginalSource as DependencyObject))
        {
            return;
        }

        if (_viewModel.OpenSoundMenuCommand.CanExecute(null))
        {
            _viewModel.OpenSoundMenuCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ControlCenterViewModel.IsOpen) && _viewModel is not null)
        {
            UpdateOpenState(_viewModel.IsOpen, animate: true);
        }
    }

    private void UpdateOpenState(bool isOpen, bool animate)
    {
        var duration = TimeSpan.FromMilliseconds(AnimationHelper.GetDoubleResource("AnimationNormalMs", 220d));
        var easing = AnimationHelper.CreateOverlayEasing();
        var panelOffset = AnimationHelper.GetDoubleResource("AnimationSidePanelOffset", 36d);
        var transitionVersion = ++_transitionVersion;

        if (isOpen)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;

            if (!animate)
            {
                ClearTransitionAnimations();
                BackdropDimming.Opacity = 1;
                Panel.Opacity = 1;
                PanelTranslate.X = 0;
                Panel.CacheMode = null;
                return;
            }

            Panel.CacheMode = CreateTransitionCache();
            BackdropDimming.BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(0, 1, duration, easing));
            var panelFadeIn = AnimationHelper.CreateDoubleAnimation(0, 1, duration, easing);
            panelFadeIn.Completed += (_, _) =>
            {
                if (transitionVersion == _transitionVersion)
                {
                    Panel.CacheMode = null;
                }
            };
            Panel.BeginAnimation(OpacityProperty, panelFadeIn);
            PanelTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(panelOffset, 0, duration, easing));
            return;
        }

        IsHitTestVisible = false;

        if (!animate)
        {
            ClearTransitionAnimations();
            Visibility = Visibility.Collapsed;
            BackdropDimming.Opacity = 0;
            Panel.Opacity = 0;
            PanelTranslate.X = panelOffset;
            Panel.CacheMode = null;
            return;
        }

        Panel.CacheMode = CreateTransitionCache();
        var fadeOut = AnimationHelper.CreateDoubleAnimation(0, duration, easing);
        fadeOut.Completed += (_, _) =>
        {
            if (transitionVersion != _transitionVersion)
            {
                return;
            }

            Visibility = Visibility.Collapsed;
            Panel.CacheMode = null;
        };
        BackdropDimming.BeginAnimation(OpacityProperty, AnimationHelper.CreateDoubleAnimation(0, duration, easing));
        Panel.BeginAnimation(OpacityProperty, fadeOut);
        PanelTranslate.BeginAnimation(TranslateTransform.XProperty, AnimationHelper.CreateDoubleAnimation(panelOffset, duration, easing));
    }

    private void ClearTransitionAnimations()
    {
        BackdropDimming.BeginAnimation(OpacityProperty, null);
        Panel.BeginAnimation(OpacityProperty, null);
        PanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private static BitmapCache CreateTransitionCache()
    {
        return new BitmapCache
        {
            EnableClearType = true,
            RenderAtScale = 1,
            SnapsToDevicePixels = true
        };
    }

    private static bool IsInteractiveChild(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Slider or ButtonBase or Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Widget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void Widget_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _viewModel is null || !_viewModel.IsEditMode)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var widgetKind = FindWidgetKind(e.OriginalSource as DependencyObject);
            if (widgetKind != null)
            {
                var container = FindVisualAncestor<ContentPresenter>(e.OriginalSource as DependencyObject);
                if (container != null)
                {
                    DragDrop.DoDragDrop(container, widgetKind.Value, DragDropEffects.Move);
                }
            }
        }
    }

    private void Widget_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(CaelestiaWin.Core.Enums.ControlCenterWidgetKind)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Widget_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsEditMode) return;

        if (e.Data.GetDataPresent(typeof(CaelestiaWin.Core.Enums.ControlCenterWidgetKind)))
        {
            var sourceWidget = (CaelestiaWin.Core.Enums.ControlCenterWidgetKind)e.Data.GetData(typeof(CaelestiaWin.Core.Enums.ControlCenterWidgetKind));
            var targetWidget = FindWidgetKind(e.OriginalSource as DependencyObject);
            
            if (targetWidget != null)
            {
                var sourceIndex = _viewModel.ActiveWidgets.IndexOf(sourceWidget);
                var targetIndex = _viewModel.ActiveWidgets.IndexOf(targetWidget.Value);
                
                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    _viewModel.MoveWidgetCommand.Execute(new Tuple<int, int>(sourceIndex, targetIndex));
                }
            }
        }
    }

    private CaelestiaWin.Core.Enums.ControlCenterWidgetKind? FindWidgetKind(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ContentPresenter cp && cp.Content is CaelestiaWin.Core.Enums.ControlCenterWidgetKind kind1) return kind1;
            if (source is ContentControl cc && cc.Content is CaelestiaWin.Core.Enums.ControlCenterWidgetKind kind2) return kind2;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
