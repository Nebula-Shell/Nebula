using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CaelestiaWin.UI.Converters;

public sealed class StringHasTextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return hasText ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
