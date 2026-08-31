using System.Windows.Media;
using CaelestiaWin.Core.Common;

namespace CaelestiaWin.UI.ViewModels;

public sealed class FileExplorerItemViewModel(
    string name,
    string displayName,
    string fullPath,
    bool isDirectory,
    string typeLabel,
    string sizeLabel,
    string modifiedLabel,
    string glyph,
    long sortSize,
    DateTime sortModifiedAt,
    string sortType) : ObservableObjectBase
{
    private ImageSource? _iconSource;
    private bool _isCut;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public string FullPath { get; } = fullPath;

    public bool IsDirectory { get; } = isDirectory;

    public string TypeLabel { get; } = typeLabel;

    public string SizeLabel { get; } = sizeLabel;

    public string ModifiedLabel { get; } = modifiedLabel;

    public string Glyph { get; } = glyph;

    public long SortSize { get; } = sortSize;

    public DateTime SortModifiedAt { get; } = sortModifiedAt;

    public string SortType { get; } = sortType;

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (SetProperty(ref _iconSource, value))
            {
                OnPropertyChanged(nameof(ShowGlyphFallback));
            }
        }
    }

    public bool ShowGlyphFallback => IconSource is null;

    public bool IsCut
    {
        get => _isCut;
        set => SetProperty(ref _isCut, value);
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }
}

