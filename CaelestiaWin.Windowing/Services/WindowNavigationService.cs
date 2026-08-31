using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Windowing.Services;

public sealed class WindowNavigationService : IWindowNavigationService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IActiveWindowService _activeWindowService;
    private readonly IWindowActionService _windowActionService;
    private readonly LinkedList<nint> _focusHistory = [];

    public WindowNavigationService(
        IWorkspaceService workspaceService,
        IActiveWindowService activeWindowService,
        IWindowActionService windowActionService)
    {
        _workspaceService = workspaceService;
        _activeWindowService = activeWindowService;
        _windowActionService = windowActionService;
        _activeWindowService.CurrentWindowChanged += OnCurrentWindowChanged;
    }

    public bool Focus(WindowDirection direction)
    {
        var current = _activeWindowService.CurrentWindow;
        if (current is null)
        {
            return false;
        }

        var candidates = _workspaceService.GetWindowsForWorkspace(_workspaceService.ActiveWorkspaceIndex)
            .Where(window => window.Handle != current.Handle && !window.IsMinimized && !window.Bounds.IsEmpty)
            .ToArray();

        var next = candidates
            .Select(candidate => new { Window = candidate, Score = ScoreCandidate(current, candidate, direction) })
            .Where(candidate => candidate.Score is not null)
            .OrderBy(candidate => candidate.Score)
            .Select(candidate => candidate.Window)
            .FirstOrDefault();

        if (next is not null)
        {
            return _windowActionService.FocusWindow(next.Handle);
        }

        var fallback = _focusHistory
            .Where(handle => handle != current.Handle)
            .FirstOrDefault(handle =>
                _workspaceService.GetWorkspaceForWindow(handle) == _workspaceService.ActiveWorkspaceIndex &&
                _windowActionService.IsWindowAlive(handle));

        return fallback != nint.Zero && _windowActionService.FocusWindow(fallback);
    }

    private void OnCurrentWindowChanged(object? sender, ForegroundWindowChangedEventArgs eventArgs)
    {
        if (eventArgs.Window is null)
        {
            return;
        }

        _ = _focusHistory.Remove(eventArgs.Window.Handle);
        _focusHistory.AddFirst(eventArgs.Window.Handle);

        while (_focusHistory.Count > 12)
        {
            _focusHistory.RemoveLast();
        }
    }

    private static double? ScoreCandidate(WindowDescriptor current, WindowDescriptor candidate, WindowDirection direction)
    {
        var deltaX = candidate.Bounds.CenterX - current.Bounds.CenterX;
        var deltaY = candidate.Bounds.CenterY - current.Bounds.CenterY;

        return direction switch
        {
            WindowDirection.Left when deltaX < -8 => Math.Abs(deltaX) * 1000d + Math.Abs(deltaY),
            WindowDirection.Right when deltaX > 8 => Math.Abs(deltaX) * 1000d + Math.Abs(deltaY),
            WindowDirection.Up when deltaY < -8 => Math.Abs(deltaY) * 1000d + Math.Abs(deltaX),
            WindowDirection.Down when deltaY > 8 => Math.Abs(deltaY) * 1000d + Math.Abs(deltaX),
            _ => null
        };
    }
}
