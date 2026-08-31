using System.Collections.ObjectModel;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Core.Services;

public sealed class AppStateService : ObservableObjectBase, IAppStateService
{
    private const int WorkspaceCount = 11;
    private const int NormalWorkspaceCount = 8;
    private readonly ObservableCollection<WorkspaceModel> _workspaces;
    private readonly ReadOnlyObservableCollection<WorkspaceModel> _readonlyWorkspaces;
    private AppConfig _config = AppConfig.CreateDefault();
    private string _activeWindowTitle = "Desktop";
    private int _activeWorkspaceIndex = 1;
    private bool _isLauncherOpen;
    private bool _isControlCenterOpen;
    private bool _isNotificationCenterOpen;
    private bool _isOverviewOpen;
    private bool _isClipboardHistoryOpen;
    private bool _isSafeMode;
    private bool _isExplorerRunning = true;
    private bool _isForegroundFullscreen;
    private bool _isShortcutGuideVisible;

    public AppStateService()
    {
        _workspaces = new ObservableCollection<WorkspaceModel>(Enumerable.Range(1, WorkspaceCount).Select(index => new WorkspaceModel(index)));
        _readonlyWorkspaces = new ReadOnlyObservableCollection<WorkspaceModel>(_workspaces);
        UpdateWorkspaceFlags();
    }

    public AppConfig Config
    {
        get => _config;
        set => SetProperty(ref _config, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        set => SetProperty(ref _activeWindowTitle, value);
    }

    public int ActiveWorkspaceIndex
    {
        get => _activeWorkspaceIndex;
        set
        {
            var normalized = Math.Clamp(value, 1, WorkspaceCount);
            if (_activeWorkspaceIndex == normalized)
            {
                return;
            }

            _activeWorkspaceIndex = normalized;
            UpdateWorkspaceFlags();
            OnPropertyChanged();
        }
    }

    public bool IsLauncherOpen
    {
        get => _isLauncherOpen;
        set
        {
            if (SetProperty(ref _isLauncherOpen, value) && value)
            {
                IsControlCenterOpen = false;
                IsNotificationCenterOpen = false;
            }
        }
    }

    public bool IsControlCenterOpen
    {
        get => _isControlCenterOpen;
        set
            => SetProperty(ref _isControlCenterOpen, value);
    }

    public bool IsNotificationCenterOpen
    {
        get => _isNotificationCenterOpen;
        set => SetProperty(ref _isNotificationCenterOpen, value);
    }

    public bool IsOverviewOpen
    {
        get => _isOverviewOpen;
        set
        {
            if (SetProperty(ref _isOverviewOpen, value) && value)
            {
                IsLauncherOpen = false;
                IsControlCenterOpen = false;
                IsNotificationCenterOpen = false;
            }
        }
    }

    public bool IsClipboardHistoryOpen
    {
        get => _isClipboardHistoryOpen;
        set
        {
            if (SetProperty(ref _isClipboardHistoryOpen, value) && value)
            {
                IsLauncherOpen = false;
                IsControlCenterOpen = false;
                IsNotificationCenterOpen = false;
                IsOverviewOpen = false;
            }
        }
    }

    public bool IsSafeMode
    {
        get => _isSafeMode;
        set => SetProperty(ref _isSafeMode, value);
    }

    public bool IsExplorerRunning
    {
        get => _isExplorerRunning;
        set => SetProperty(ref _isExplorerRunning, value);
    }

    public bool IsForegroundFullscreen
    {
        get => _isForegroundFullscreen;
        set => SetProperty(ref _isForegroundFullscreen, value);
    }

    public bool IsShortcutGuideVisible
    {
        get => _isShortcutGuideVisible;
        set => SetProperty(ref _isShortcutGuideVisible, value);
    }

    public ReadOnlyObservableCollection<WorkspaceModel> Workspaces => _readonlyWorkspaces;

    private void UpdateWorkspaceFlags()
    {
        foreach (var workspace in _workspaces)
        {
            workspace.IsActive = workspace.Index == _activeWorkspaceIndex;
            workspace.IsDiscordDesktop = workspace.Index > NormalWorkspaceCount;
        }
    }
}
