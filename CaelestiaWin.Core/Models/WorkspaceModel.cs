using CaelestiaWin.Core.Common;

namespace CaelestiaWin.Core.Models;

public sealed class WorkspaceModel(int index) : ObservableObjectBase
{
    private bool _isActive;
    private bool _isDiscordDesktop;
    private int _windowCount;

    public int Index { get; } = index;

    public string Name { get; } = index.ToString();

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsDiscordDesktop
    {
        get => _isDiscordDesktop;
        set => SetProperty(ref _isDiscordDesktop, value);
    }

    public int WindowCount
    {
        get => _windowCount;
        set => SetProperty(ref _windowCount, value);
    }
}
