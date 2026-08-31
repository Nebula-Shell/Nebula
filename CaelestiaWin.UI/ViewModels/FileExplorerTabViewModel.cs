using CaelestiaWin.Core.Common;

namespace CaelestiaWin.UI.ViewModels;

public sealed class FileExplorerTabViewModel(string title, string path) : ObservableObjectBase
{
    private string _title = title;
    private string _path = path;
    private bool _isActive;
    private int _historyIndex = -1;

    public Guid Id { get; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public List<string> History { get; } = [];

    public int HistoryIndex
    {
        get => _historyIndex;
        set => SetProperty(ref _historyIndex, value);
    }
}
