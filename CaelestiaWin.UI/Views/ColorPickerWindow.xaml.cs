using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CaelestiaWin.UI.Views;

public partial class ColorPickerWindow : Window, INotifyPropertyChanged
{
    private bool _isUpdatingHex;
    private string _selectedColorHex = "#79E6F5";
    private double _red;
    private double _green;
    private double _blue;
    private SolidColorBrush _previewBrush = new(Color.FromRgb(0x79, 0xE6, 0xF5));

    public ColorPickerWindow(string initialColor)
    {
        InitializeComponent();
        DataContext = this;
        SetColor(ParseColor(initialColor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SelectedColorHex
    {
        get => _selectedColorHex;
        set
        {
            if (_selectedColorHex == value)
            {
                return;
            }

            _selectedColorHex = value;
            OnPropertyChanged(nameof(SelectedColorHex));

            if (!_isUpdatingHex && TryParseColor(value, out var color))
            {
                SetColor(color, updateHex: false);
            }
        }
    }

    public double Red
    {
        get => _red;
        set
        {
            if (Math.Abs(_red - value) < 0.5d)
            {
                return;
            }

            _red = Math.Clamp(value, 0, 255);
            OnPropertyChanged(nameof(Red));
            UpdateFromChannels();
        }
    }

    public double Green
    {
        get => _green;
        set
        {
            if (Math.Abs(_green - value) < 0.5d)
            {
                return;
            }

            _green = Math.Clamp(value, 0, 255);
            OnPropertyChanged(nameof(Green));
            UpdateFromChannels();
        }
    }

    public double Blue
    {
        get => _blue;
        set
        {
            if (Math.Abs(_blue - value) < 0.5d)
            {
                return;
            }

            _blue = Math.Clamp(value, 0, 255);
            OnPropertyChanged(nameof(Blue));
            UpdateFromChannels();
        }
    }

    public SolidColorBrush PreviewBrush
    {
        get => _previewBrush;
        private set
        {
            _previewBrush = value;
            _previewBrush.Freeze();
            OnPropertyChanged(nameof(PreviewBrush));
        }
    }

    private void SetColor(Color color, bool updateHex = true)
    {
        _red = color.R;
        _green = color.G;
        _blue = color.B;
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        PreviewBrush = new SolidColorBrush(color);

        if (updateHex)
        {
            _isUpdatingHex = true;
            SelectedColorHex = ToHex(color);
            _isUpdatingHex = false;
        }
    }

    private void UpdateFromChannels()
    {
        var color = Color.FromRgb((byte)Red, (byte)Green, (byte)Blue);
        PreviewBrush = new SolidColorBrush(color);
        _isUpdatingHex = true;
        SelectedColorHex = ToHex(color);
        _isUpdatingHex = false;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseColor(SelectedColorHex, out var color))
        {
            SetColor(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));
        }
        else
        {
            SelectedColorHex = ToHex(color);
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static Color ParseColor(string value)
    {
        return TryParseColor(value, out var color) ? color : Color.FromRgb(0x79, 0xE6, 0xF5);
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        color = default;
        return false;
    }

    private static string ToHex(Color color)
    {
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
