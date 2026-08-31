using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Terminal;

public partial class MainWindow : Window
{
    private readonly TerminalViewModel _viewModel = new();
    private bool _scrollPending;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTerminalAppearance();
        DataContext = _viewModel;
        _viewModel.Lines.CollectionChanged += OnLinesChanged;
        Loaded += (_, _) => CommandBox.Focus();
        Activated += (_, _) => ApplyTerminalAppearance();
    }

    private void CommandBox_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case Key.Up:
                eventArgs.Handled = true;
                _viewModel.PreviousHistory();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
            case Key.Down:
                eventArgs.Handled = true;
                _viewModel.NextHistory();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
        }
    }

    private async void CommandBox_OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case Key.Enter:
                eventArgs.Handled = true;
                await _viewModel.SubmitAsync();
                break;
            case Key.Up:
                if (eventArgs.Handled)
                {
                    break;
                }

                eventArgs.Handled = true;
                _viewModel.PreviousHistory();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
            case Key.Down:
                if (eventArgs.Handled)
                {
                    break;
                }

                eventArgs.Handled = true;
                _viewModel.NextHistory();
                CommandBox.CaretIndex = CommandBox.Text.Length;
                break;
            case Key.Right when CommandBox.CaretIndex == CommandBox.Text.Length:
                eventArgs.Handled = _viewModel.TryAcceptInlineSuggestion();
                if (eventArgs.Handled)
                {
                    CommandBox.CaretIndex = CommandBox.Text.Length;
                }

                break;
            case Key.Tab:
                eventArgs.Handled = true;
                if (_viewModel.TryAcceptInlineSuggestion() || _viewModel.TryCompleteCommand())
                {
                    CommandBox.CaretIndex = CommandBox.Text.Length;
                }

                break;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            OutputScrollViewer.ScrollToEnd();
            _scrollPending = false;
        });
    }

    private void ShellSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (IsFromTextInput(eventArgs.OriginalSource))
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        DragMove();
    }

    private static bool IsFromTextInput(object source)
    {
        if (source is not DependencyObject current)
        {
            return false;
        }

        while (current is not null)
        {
            if (current is TextBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ToggleMaximizeRestore();
    }

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ApplyTerminalAppearance()
    {
        var config = LoadSharedConfig();
        var accentColor = ParseColor(config.Theme.AccentColor, Color.FromRgb(0x79, 0xE6, 0xF5));
        var secondaryAccent = ParseColor(config.Theme.SecondaryAccentColor, Lighten(accentColor, 0.42d));
        var foreground = ParseColor(config.Theme.ForegroundColor, Color.FromRgb(0xF5, 0xFB, 0xFF));
        var mutedForeground = ParseColor(config.Theme.MutedForegroundColor, Color.FromRgb(0x8F, 0xA7, 0xB7));
        var baseBackground = ParseColor(config.Theme.BackgroundColor, Color.FromRgb(0x0A, 0x11, 0x18));
        var baseSurface = ParseColor(config.Theme.PanelColor, Color.FromRgb(0x11, 0x1B, 0x24));
        var opacity = config.Terminal.FollowShellTransparency
            ? config.Theme.PanelOpacity
            : config.Terminal.Opacity;
        var tintSurfaces = config.Theme.TintShellSurfacesWithAccent;

        if (!config.Theme.EnableTransparency)
        {
            opacity = 1d;
        }

        var surfaceBase = tintSurfaces ? Blend(baseSurface, accentColor, 0.12d) : baseSurface;
        var panelColor = WithAlpha(surfaceBase, Math.Clamp(opacity, 0.35d, 1d));
        var panelAltBase = Lighten(baseSurface, 0.08d);
        var panelAltColor = WithAlpha(tintSurfaces ? Blend(panelAltBase, accentColor, 0.20d) : panelAltBase, Math.Min(1d, Math.Clamp(opacity, 0.35d, 1d) + 0.07d));
        var hairlineColor = tintSurfaces
            ? Color.FromArgb(0x2A, secondaryAccent.R, secondaryAccent.G, secondaryAccent.B)
            : Color.FromArgb(0x28, foreground.R, foreground.G, foreground.B);
        var backgroundEnd = tintSurfaces ? Blend(Darken(baseBackground, 0.32d), accentColor, 0.08d) : Darken(baseBackground, 0.32d);

        var backgroundBrush = new LinearGradientBrush(
            WithAlpha(baseBackground, config.Theme.EnableTransparency ? config.Theme.BackgroundOpacity : 1d),
            backgroundEnd,
            new Point(0, 0),
            new Point(1, 1));
        backgroundBrush.Freeze();

        Application.Current.Resources["NebulaBackgroundBrush"] = backgroundBrush;
        Application.Current.Resources["NebulaPanelBrush"] = CreateBrush(panelColor);
        Application.Current.Resources["NebulaPanelAltBrush"] = CreateBrush(panelAltColor);
        Application.Current.Resources["NebulaHairlineBrush"] = CreateBrush(hairlineColor);
        Application.Current.Resources["NebulaTextBrush"] = CreateBrush(foreground);
        Application.Current.Resources["NebulaMutedBrush"] = CreateBrush(mutedForeground);
        Application.Current.Resources["NebulaAccentBrush"] = CreateBrush(Color.FromArgb(0xFF, accentColor.R, accentColor.G, accentColor.B));

        ShellBorder.Effect = config.Theme.EnableShadows ? ShellBorder.Effect : null;
    }

    private static AppConfig LoadSharedConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "config.json");

        if (!File.Exists(configPath))
        {
            return AppConfig.CreateDefault();
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };

            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), options)
                   ?? AppConfig.CreateDefault();
        }
        catch (JsonException)
        {
            return AppConfig.CreateDefault();
        }
        catch (IOException)
        {
            return AppConfig.CreateDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return AppConfig.CreateDefault();
        }
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color color ? color : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, double opacity)
    {
        return Color.FromArgb((byte)(Math.Clamp(opacity, 0d, 1d) * 255), color.R, color.G, color.B);
    }

    private static Color Lighten(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(color.R + ((255 - color.R) * amount)),
            (byte)(color.G + ((255 - color.G) * amount)),
            (byte)(color.B + ((255 - color.B) * amount)));
    }

    private static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(color.R * (1d - amount)),
            (byte)(color.G * (1d - amount)),
            (byte)(color.B * (1d - amount)));
    }

    private static Color Blend(Color baseColor, Color tintColor, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(baseColor.R + ((tintColor.R - baseColor.R) * amount)),
            (byte)(baseColor.G + ((tintColor.G - baseColor.G) * amount)),
            (byte)(baseColor.B + ((tintColor.B - baseColor.B) * amount)));
    }
}
