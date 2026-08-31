using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.UI.Views;

public partial class ScreenSnipOverlayWindow : Window
{
    private readonly TaskCompletionSource<ScreenCaptureRegion?> _completion = new();
    private Point _startPoint;
    private bool _isSelecting;

    public ScreenSnipOverlayWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public Task<ScreenCaptureRegion?> CaptureAsync()
    {
        Show();
        Activate();
        Focus();
        return _completion.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        _completion.TrySetResult(null);
        base.OnClosed(e);
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
        }
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isSelecting = true;
        _startPoint = e.GetPosition(SelectionCanvas);
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
        Canvas.SetLeft(SelectionBorder, _startPoint.X);
        Canvas.SetTop(SelectionBorder, _startPoint.Y);
        Mouse.Capture(this);
    }

    private void Window_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        UpdateSelection(e.GetPosition(SelectionCanvas));
    }

    private void Window_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        Mouse.Capture(null);
        UpdateSelection(e.GetPosition(SelectionCanvas));

        var width = SelectionBorder.Width;
        var height = SelectionBorder.Height;
        if (width < 6 || height < 6)
        {
            Cancel();
            return;
        }

        var left = Canvas.GetLeft(SelectionBorder);
        var top = Canvas.GetTop(SelectionBorder);
        var dpi = VisualTreeHelper.GetDpi(this);
        var region = new ScreenCaptureRegion(
            (int)Math.Round((Left + left) * dpi.DpiScaleX),
            (int)Math.Round((Top + top) * dpi.DpiScaleY),
            Math.Max(1, (int)Math.Round(width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(height * dpi.DpiScaleY)));

        _completion.TrySetResult(region);
        Close();
    }

    private void UpdateSelection(Point currentPoint)
    {
        var left = Math.Min(_startPoint.X, currentPoint.X);
        var top = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _startPoint.X);
        var height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
    }

    private void Cancel()
    {
        _completion.TrySetResult(null);
        Close();
    }
}
