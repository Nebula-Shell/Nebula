using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CaelestiaWin.Terminal;

public sealed class TerminalLineTextBlock : TextBlock
{
    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line),
        typeof(TerminalLine),
        typeof(TerminalLineTextBlock),
        new PropertyMetadata(null, OnLineChanged));

    public TerminalLine? Line
    {
        get => (TerminalLine?)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    private static void OnLineChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is TerminalLineTextBlock textBlock)
        {
            textBlock.RenderLine(eventArgs.NewValue as TerminalLine);
        }
    }

    private void RenderLine(TerminalLine? line)
    {
        Inlines.Clear();
        if (line is null)
        {
            return;
        }

        foreach (var run in line.Runs)
        {
            Inlines.Add(new Run(run.Text)
            {
                Foreground = GetBrush(run.Foreground)
            });
        }
    }

    private static Brush GetBrush(string value)
    {
        if (BrushCache.TryGetValue(value, out var cached))
        {
            return cached;
        }

        var brush = (Brush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        BrushCache[value] = brush;
        return brush;
    }
}
