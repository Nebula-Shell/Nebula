namespace CaelestiaWin.UI.ViewModels;

public sealed class FileExplorerPathSegmentViewModel(
    string displayName,
    string fullPath,
    bool isCurrent,
    bool showSeparator)
{
    public string DisplayName { get; } = displayName;

    public string FullPath { get; } = fullPath;

    public bool IsCurrent { get; } = isCurrent;

    public bool ShowSeparator { get; } = showSeparator;
}
