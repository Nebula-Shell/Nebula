using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaelestiaWin.UI.Helpers;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class NotificationCenterView : UserControl
{
    private NotificationCenterViewModel? _viewModel;
    private int _transitionVersion;

    public NotificationCenterView()
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

        _viewModel = eventArgs.NewValue as NotificationCenterViewModel;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(NotificationCenterViewModel.IsOpen) && _viewModel is not null)
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
}
