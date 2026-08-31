using CaelestiaWin.Core.Common;

namespace CaelestiaWin.UI.ViewModels;

public sealed class WallpaperOptionViewModel : ObservableObjectBase
{
    private bool _isCurrent;

    public WallpaperOptionViewModel(string path, string title, string subtitle)
    {
        Path = path;
        Title = title;
        Subtitle = subtitle;
    }

    public string Path { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}
