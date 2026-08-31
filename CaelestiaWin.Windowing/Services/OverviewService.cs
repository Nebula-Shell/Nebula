using System.Collections.ObjectModel;
using System.ComponentModel;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Windowing.Services;

public sealed class OverviewService : ObservableObjectBase, IOverviewService
{
    private readonly IAppStateService _appStateService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWindowActionService _windowActionService;
    private readonly ObservableCollection<OverviewWindowItem> _windows = [];
    private readonly ReadOnlyObservableCollection<OverviewWindowItem> _readonlyWindows;
    private bool _isOpen;

    public OverviewService(
        IAppStateService appStateService,
        IWorkspaceService workspaceService,
        IWindowActionService windowActionService)
    {
        _appStateService = appStateService;
        _workspaceService = workspaceService;
        _windowActionService = windowActionService;
        _readonlyWindows = new ReadOnlyObservableCollection<OverviewWindowItem>(_windows);
        _isOpen = _appStateService.IsOverviewOpen;
        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public ReadOnlyObservableCollection<OverviewWindowItem> Windows => _readonlyWindows;

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Refresh();
        _appStateService.IsOverviewOpen = true;
    }

    public void Close()
    {
        _appStateService.IsOverviewOpen = false;
    }

    public void Refresh()
    {
        _workspaceService.Synchronize();
        var items = _workspaceService.GetAllTrackedWindows()
            .OrderBy(window => _workspaceService.GetWorkspaceForWindow(window.Handle) == _workspaceService.ActiveWorkspaceIndex ? 0 : 1)
            .ThenBy(window => _workspaceService.GetWorkspaceForWindow(window.Handle))
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .Select(window => new OverviewWindowItem(
                window.Handle,
                window.Title,
                $"{window.ProcessName ?? "App"} • Workspace {_workspaceService.GetWorkspaceForWindow(window.Handle)}",
                _workspaceService.GetWorkspaceForWindow(window.Handle)))
            .ToArray();

        _windows.Clear();
        foreach (var item in items)
        {
            _windows.Add(item);
        }
    }

    public bool ActivateWindow(nint hwnd)
    {
        var workspaceIndex = _workspaceService.GetWorkspaceForWindow(hwnd);
        if (workspaceIndex != _workspaceService.ActiveWorkspaceIndex)
        {
            _ = _workspaceService.SwitchTo(workspaceIndex);
        }

        var focused = _windowActionService.FocusWindow(hwnd);
        if (focused)
        {
            Close();
        }

        return focused;
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IAppStateService.IsOverviewOpen))
        {
            return;
        }

        IsOpen = _appStateService.IsOverviewOpen;
        if (IsOpen)
        {
            Refresh();
        }
    }
}
