using System.Windows.Media;
using CaelestiaWin.Core.Common;

namespace CaelestiaWin.UI.ViewModels;

public sealed class FileExplorerLocationViewModel(
    string id,
    string displayName,
    string path,
    string glyph,
    bool isSeparator = false,
    string? sectionTitle = null,
    ImageSource? iconSource = null,
    bool isBuiltIn = false,
    bool isDrive = false) : ObservableObjectBase
{
    private bool _isActive;
    private bool _showDropBefore;
    private bool _showDropAfter;
    private bool _showDropInside;

    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public string Path { get; } = path;

    public string Glyph { get; } = glyph;

    public bool IsSeparator { get; } = isSeparator;

    public string? SectionTitle { get; } = sectionTitle;

    public ImageSource? IconSource { get; } = iconSource;

    public bool IsBuiltIn { get; } = isBuiltIn;

    public bool IsDrive { get; } = isDrive;

    public bool ShowGlyphFallback => IconSource is null;

    public bool CanReorder => !IsSeparator && !IsDrive;

    public bool CanRemove => !IsSeparator && !IsBuiltIn && !IsDrive;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool ShowDropBefore
    {
        get => _showDropBefore;
        set => SetProperty(ref _showDropBefore, value);
    }

    public bool ShowDropAfter
    {
        get => _showDropAfter;
        set => SetProperty(ref _showDropAfter, value);
    }

    public bool ShowDropInside
    {
        get => _showDropInside;
        set => SetProperty(ref _showDropInside, value);
    }
}
