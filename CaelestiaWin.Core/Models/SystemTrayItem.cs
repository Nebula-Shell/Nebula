using CaelestiaWin.Core.Common;

namespace CaelestiaWin.Core.Models;

public sealed class SystemTrayItem : ObservableObjectBase
{
    private string _glyph = string.Empty;
    private string _label = string.Empty;
    private string _toolTip = string.Empty;
    private string _fontFamilyName = "Segoe MDL2 Assets";
    private string _iconPath = string.Empty;
    private bool _isAvailable = true;
    private bool _isPlaceholder;

    public required string Id { get; init; }

    public string Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string ToolTip
    {
        get => _toolTip;
        set => SetProperty(ref _toolTip, value);
    }

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set => SetProperty(ref _fontFamilyName, value);
    }

    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set => SetProperty(ref _isPlaceholder, value);
    }
}
