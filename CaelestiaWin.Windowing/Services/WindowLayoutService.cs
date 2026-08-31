using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Windowing.Services;

public sealed class WindowLayoutService(
    IAppStateService appStateService,
    IWorkspaceService workspaceService,
    IActiveWindowService activeWindowService,
    IWindowActionService windowActionService,
    IGameModeService gameModeService) : IWindowLayoutService
{
    private const int MinimumTopReservedSpace = 76;
    private const int LeftRailReservedSpace = 96;
    private const double DynamicMasterRatio = 0.58d;
    private const double DynamicSpiralTailRatio = 0.62d;
    private readonly object _layoutSync = new();
    private readonly Dictionary<int, List<nint>> _workspaceOrder = [];
    private readonly Dictionary<int, WindowLayoutMode> _layoutModes = [];

    public bool MoveFocusedWindow(WindowDirection direction)
    {
        if (!Monitor.TryEnter(_layoutSync))
        {
            return false;
        }

        try
        {
            workspaceService.Synchronize();
            var activeWorkspace = workspaceService.ActiveWorkspaceIndex;
            var windows = workspaceService.GetWindowsForWorkspace(activeWorkspace)
                .Where(window => !window.Bounds.IsEmpty || window.IsMinimized)
                .ToArray();
            var current = activeWindowService.CurrentWindow;

            if (current is null || windows.Length == 0)
            {
                return false;
            }

            var order = EnsureOrder(activeWorkspace, windows.Select(window => window.Handle), current.Handle);
            if (!order.Contains(current.Handle))
            {
                order.Insert(0, current.Handle);
            }

            var target = FindDirectionalTarget(current, windows, direction);
            if (target is not null)
            {
                var currentIndex = order.IndexOf(current.Handle);
                var targetIndex = order.IndexOf(target.Handle);
                if (currentIndex >= 0 && targetIndex >= 0)
                {
                    (order[currentIndex], order[targetIndex]) = (order[targetIndex], order[currentIndex]);
                }
            }
            else
            {
                _ = order.Remove(current.Handle);
                if (direction is WindowDirection.Left or WindowDirection.Up)
                {
                    order.Insert(0, current.Handle);
                }
                else
                {
                    order.Add(current.Handle);
                }
            }

            _layoutModes[activeWorkspace] = ResolveLayoutMode(windows.Length, direction);
            ArrangeWorkspace(activeWorkspace, order, windows);
            return true;
        }
        finally
        {
            Monitor.Exit(_layoutSync);
        }
    }

    public void RefreshActiveWorkspaceLayout()
    {
        if (!Monitor.TryEnter(_layoutSync))
        {
            return;
        }

        try
        {
            workspaceService.Synchronize();
            var workspaceIndex = workspaceService.ActiveWorkspaceIndex;
            var anchorHandle = activeWindowService.CurrentWindow?.Handle;
            var windows = workspaceService.GetWindowsForWorkspace(workspaceIndex)
                .Where(window => !window.Bounds.IsEmpty || window.IsMinimized)
                .ToArray();
            var order = EnsureOrder(workspaceIndex, windows.Select(window => window.Handle), anchorHandle);
            ArrangeWorkspace(workspaceIndex, order, windows);
        }
        finally
        {
            Monitor.Exit(_layoutSync);
        }
    }

    public WindowLayout GetLayoutForWorkspace(int workspaceIndex)
    {
        lock (_layoutSync)
        {
            var order = _workspaceOrder.TryGetValue(workspaceIndex, out var handles)
                ? handles.ToArray()
                : Array.Empty<nint>();
            var mode = _layoutModes.TryGetValue(workspaceIndex, out var layoutMode)
                ? layoutMode
                : WindowLayoutMode.Floating;
            return new WindowLayout(workspaceIndex, mode, order);
        }
    }

    private void ArrangeWorkspace(int workspaceIndex, List<nint> order, IReadOnlyList<WindowDescriptor> windows)
    {
        if (!appStateService.Config.Windowing.EnableSoftTiling || windows.Count == 0)
        {
            _layoutModes[workspaceIndex] = WindowLayoutMode.Floating;
            return;
        }

        var windowsByHandle = new Dictionary<nint, WindowDescriptor>(windows.Count);
        for (var index = 0; index < windows.Count; index++)
        {
            windowsByHandle[windows[index].Handle] = windows[index];
        }

        var windowHandles = new HashSet<nint>(windowsByHandle.Keys);
        order.RemoveAll(handle => !windowHandles.Contains(handle));
        for (var index = 0; index < windows.Count; index++)
        {
            var handle = windows[index].Handle;
            if (!order.Contains(handle))
            {
                order.Add(handle);
            }
        }

        var arrangedWindows = new List<WindowDescriptor>(order.Count);
        for (var index = 0; index < order.Count; index++)
        {
            if (windowsByHandle.TryGetValue(order[index], out var window))
            {
                arrangedWindows.Add(window);
            }
        }

        if (arrangedWindows.Count == 0)
        {
            return;
        }

            for (var index = 0; index < arrangedWindows.Count; index++)
            {
                var handle = arrangedWindows[index].Handle;
                if (!windowActionService.IsWindowFullscreen(handle) && !windowActionService.IsWindowFloating(handle))
                {
                    continue;
                }

                _layoutModes[workspaceIndex] = WindowLayoutMode.Floating;
                return;
            }

        var anchorHandle = activeWindowService.CurrentWindow?.Handle ?? arrangedWindows[0].Handle;
        var workArea = ApplyReservedShellSpace(windowActionService.GetMonitorWorkArea(anchorHandle));
        var gap = Math.Max(0, appStateService.Config.Windowing.LayoutGap);
        var margin = Math.Max(0, appStateService.Config.Windowing.OuterMargin);
        var centeredWindows = new List<WindowDescriptor>(arrangedWindows.Count);
        var tiledWindows = new List<WindowDescriptor>(arrangedWindows.Count);
            for (var index = 0; index < arrangedWindows.Count; index++)
            {
                var window = arrangedWindows[index];
                if (gameModeService.ShouldCenterWindow(window) || gameModeService.ShouldExcludeFromTiling(window) || windowActionService.IsWindowFloating(window.Handle))
                {
                    centeredWindows.Add(window);
                }
                else
                {
                    tiledWindows.Add(window);
                }
            }

        var knownMode = _layoutModes.TryGetValue(workspaceIndex, out var layoutMode)
            ? layoutMode
            : ResolveLayoutMode(tiledWindows.Count, WindowDirection.Right);
        var preferHorizontal = UsesHorizontalPrimaryAxis(knownMode);
        var mode = NormalizeLayoutMode(knownMode, tiledWindows.Count);
        var tilingStrategy = appStateService.Config.Windowing.TilingStrategy;

        if (tiledWindows.Count > 2)
        {
            mode = tilingStrategy == WindowTilingStrategyKind.GoldenRatio
                ? WindowLayoutMode.Spiral
                : preferHorizontal
                    ? WindowLayoutMode.MasterHorizontal
                    : WindowLayoutMode.MasterVertical;
        }

        _layoutModes[workspaceIndex] = mode;
        var boundsByHandle = tiledWindows.Count switch
        {
            0 => new Dictionary<nint, WindowBounds>(),
            _ when tilingStrategy == WindowTilingStrategyKind.GoldenRatio => BuildDynamicSpiral(tiledWindows, workArea, margin, gap, preferHorizontal),
            2 when mode == WindowLayoutMode.SplitHorizontal => BuildSplitHorizontal(tiledWindows, workArea, margin, gap),
            2 when mode == WindowLayoutMode.SplitVertical => BuildSplitVertical(tiledWindows, workArea, margin, gap),
            1 when mode == WindowLayoutMode.Single => BuildSingle(tiledWindows[0], workArea, margin),
            _ => BuildDynamicMasterStack(tiledWindows, workArea, margin, gap, preferHorizontal)
        };

        foreach (var pair in BuildCenteredBounds(centeredWindows, workArea, margin, gap))
        {
            boundsByHandle[pair.Key] = pair.Value;
        }

        foreach (var pair in boundsByHandle)
        {
            var currentBounds = windowActionService.GetWindowBounds(pair.Key);
            if (currentBounds is not null && AreBoundsClose(currentBounds.Value, pair.Value))
            {
                continue;
            }

            _ = windowActionService.RestoreWindow(pair.Key);
            _ = windowActionService.ShowWindow(pair.Key);
            _ = windowActionService.MoveWindow(pair.Key, pair.Value);
        }
    }

    private WindowBounds ApplyReservedShellSpace(WindowBounds workArea)
    {
        if (appStateService.Config.ControlCenter.BarLayout == ShellBarLayoutKind.Left)
        {
            var adjustedWidth = Math.Max(220, workArea.Width - LeftRailReservedSpace);
            return new WindowBounds(workArea.Left + LeftRailReservedSpace, workArea.Top, adjustedWidth, workArea.Height);
        }

        var topInset = Math.Max(MinimumTopReservedSpace, appStateService.Config.Windowing.TopReservedSpace);
        var adjustedHeight = Math.Max(160, workArea.Height - topInset);
        return new WindowBounds(workArea.Left, workArea.Top + topInset, workArea.Width, adjustedHeight);
    }

    private static Dictionary<nint, WindowBounds> BuildSingle(WindowDescriptor window, WindowBounds workArea, int margin)
    {
        return new Dictionary<nint, WindowBounds>
        {
            [window.Handle] = new(
                workArea.Left + margin,
                workArea.Top + margin,
                Math.Max(100, workArea.Width - (margin * 2)),
                Math.Max(100, workArea.Height - (margin * 2)))
        };
    }

    private static Dictionary<nint, WindowBounds> BuildSplitVertical(IReadOnlyList<WindowDescriptor> windows, WindowBounds workArea, int margin, int gap)
    {
        var root = InsetBounds(workArea, margin);
        var split = SplitVertical(root, gap, 0.5d);

        return new Dictionary<nint, WindowBounds>
        {
            [windows[0].Handle] = split.First,
            [windows[1].Handle] = split.Second
        };
    }

    private static Dictionary<nint, WindowBounds> BuildSplitHorizontal(IReadOnlyList<WindowDescriptor> windows, WindowBounds workArea, int margin, int gap)
    {
        var root = InsetBounds(workArea, margin);
        var split = SplitHorizontal(root, gap, 0.5d);

        return new Dictionary<nint, WindowBounds>
        {
            [windows[0].Handle] = split.First,
            [windows[1].Handle] = split.Second
        };
    }

    private static Dictionary<nint, WindowBounds> BuildDynamicMasterStack(IReadOnlyList<WindowDescriptor> windows, WindowBounds workArea, int margin, int gap, bool preferHorizontal)
    {
        var root = InsetBounds(workArea, margin);
        if (windows.Count == 1)
        {
            return new Dictionary<nint, WindowBounds>
            {
                [windows[0].Handle] = root
            };
        }

        if (windows.Count == 2)
        {
            var split = preferHorizontal
                ? SplitHorizontal(root, gap, 0.5d)
                : SplitVertical(root, gap, 0.5d);

            return new Dictionary<nint, WindowBounds>
            {
                [windows[0].Handle] = split.First,
                [windows[1].Handle] = split.Second
            };
        }

        var masterSplit = preferHorizontal
            ? SplitHorizontal(root, gap, DynamicMasterRatio)
            : SplitVertical(root, gap, DynamicMasterRatio);
        var result = new Dictionary<nint, WindowBounds>(windows.Count)
        {
            [windows[0].Handle] = masterSplit.First
        };
        LayoutBalancedTail(result, windows.Skip(1).ToArray(), masterSplit.Second, gap, !preferHorizontal);
        return result;
    }

    private static Dictionary<nint, WindowBounds> BuildDynamicSpiral(IReadOnlyList<WindowDescriptor> windows, WindowBounds workArea, int margin, int gap, bool preferHorizontal)
    {
        var root = InsetBounds(workArea, margin);
        if (windows.Count == 1)
        {
            return new Dictionary<nint, WindowBounds>
            {
                [windows[0].Handle] = root
            };
        }

        if (windows.Count == 2)
        {
            var split = preferHorizontal
                ? SplitHorizontal(root, gap, 0.5d)
                : SplitVertical(root, gap, 0.5d);
            return new Dictionary<nint, WindowBounds>
            {
                [windows[0].Handle] = split.First,
                [windows[1].Handle] = split.Second
            };
        }

        var result = new Dictionary<nint, WindowBounds>();
        var rootSplit = preferHorizontal
            ? SplitHorizontal(root, gap, 0.5d)
            : SplitVertical(root, gap, 0.5d);
        result[windows[0].Handle] = rootSplit.First;

        var tailBounds = rootSplit.Second;
        var splitHorizontally = !preferHorizontal;
        for (var index = 1; index < windows.Count; index++)
        {
            if (index == windows.Count - 1)
            {
                result[windows[index].Handle] = tailBounds;
                break;
            }

            var split = splitHorizontally
                ? SplitHorizontal(tailBounds, gap, index == 1 ? 0.5d : DynamicSpiralTailRatio)
                : SplitVertical(tailBounds, gap, index == 1 ? 0.5d : DynamicSpiralTailRatio);
            var nearTile = PickTileClosestToCenter(split.First, split.Second, root);
            var farTile = nearTile.Equals(split.First) ? split.Second : split.First;

            result[windows[index].Handle] = farTile;
            tailBounds = nearTile;
            splitHorizontally = !splitHorizontally;
        }

        return result;
    }

    private static Dictionary<nint, WindowBounds> BuildCenteredBounds(IReadOnlyList<WindowDescriptor> windows, WindowBounds workArea, int margin, int gap)
    {
        var result = new Dictionary<nint, WindowBounds>(windows.Count);
        if (windows.Count == 0)
        {
            return result;
        }

        var innerWidth = Math.Max(320, workArea.Width - (margin * 2));
        var innerHeight = Math.Max(220, workArea.Height - (margin * 2));
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var maxWidth = Math.Max(720, (int)Math.Round(innerWidth * 0.78d));
            var maxHeight = Math.Max(520, (int)Math.Round(innerHeight * 0.84d));
            var fallbackWidth = Math.Max(820, (int)Math.Round(innerWidth * 0.68d));
            var fallbackHeight = Math.Max(620, (int)Math.Round(innerHeight * 0.76d));
            var currentWidth = window.Bounds.Width;
            var currentHeight = window.Bounds.Height;
            var desiredWidth = currentWidth > 0 && currentWidth < maxWidth
                ? currentWidth
                : fallbackWidth;
            var desiredHeight = currentHeight > 0 && currentHeight < maxHeight
                ? currentHeight
                : fallbackHeight;
            var centeredWidth = Math.Clamp(desiredWidth, 720, maxWidth);
            var centeredHeight = Math.Clamp(desiredHeight, 520, maxHeight);
            var offset = index * 18;
            var x = workArea.Left + ((workArea.Width - centeredWidth) / 2);
            var y = workArea.Top + ((workArea.Height - centeredHeight) / 2) + offset;
            result[windows[index].Handle] = new WindowBounds(
                x,
                Math.Max(workArea.Top + margin, Math.Min(workArea.Bottom - centeredHeight - margin, y)),
                centeredWidth,
                centeredHeight);
        }

        return result;
    }

    private static WindowBounds InsetBounds(WindowBounds workArea, int margin)
    {
        return new WindowBounds(
            workArea.Left + margin,
            workArea.Top + margin,
            Math.Max(100, workArea.Width - (margin * 2)),
            Math.Max(100, workArea.Height - (margin * 2)));
    }

    private static void LayoutBalancedTail(
        Dictionary<nint, WindowBounds> result,
        IReadOnlyList<WindowDescriptor> windows,
        WindowBounds bounds,
        int gap,
        bool preferHorizontal)
    {
        if (windows.Count == 0)
        {
            return;
        }

        if (windows.Count == 1)
        {
            result[windows[0].Handle] = bounds;
            return;
        }

        if (windows.Count == 2)
        {
            var pairSplit = preferHorizontal
                ? SplitHorizontal(bounds, gap, 0.5d)
                : SplitVertical(bounds, gap, 0.5d);
            result[windows[0].Handle] = pairSplit.First;
            result[windows[1].Handle] = pairSplit.Second;
            return;
        }

        var splitHorizontally = ChooseSplitAxis(bounds, preferHorizontal);
        var split = splitHorizontally
            ? SplitHorizontal(bounds, gap, 0.5d)
            : SplitVertical(bounds, gap, 0.5d);
        var leadCount = (int)Math.Ceiling(windows.Count / 2d);
        LayoutBalancedTail(result, windows.Take(leadCount).ToArray(), split.First, gap, !splitHorizontally);
        LayoutBalancedTail(result, windows.Skip(leadCount).ToArray(), split.Second, gap, !splitHorizontally);
    }

    private static bool ChooseSplitAxis(WindowBounds bounds, bool preferHorizontal)
    {
        if (Math.Abs(bounds.Height - bounds.Width) < 80)
        {
            return preferHorizontal;
        }

        return bounds.Height > bounds.Width;
    }

    private static (WindowBounds First, WindowBounds Second) SplitVertical(WindowBounds bounds, int gap, double ratio)
    {
        var availableWidth = Math.Max(2, bounds.Width - gap);
        var clampedRatio = Math.Clamp(ratio, 0.35d, 0.65d);
        var firstWidth = Math.Max(1, (int)Math.Round(availableWidth * clampedRatio));
        firstWidth = Math.Min(firstWidth, availableWidth - 1);
        var secondWidth = Math.Max(1, availableWidth - firstWidth);

        return (
            new WindowBounds(bounds.Left, bounds.Top, firstWidth, bounds.Height),
            new WindowBounds(bounds.Left + firstWidth + gap, bounds.Top, secondWidth, bounds.Height));
    }

    private static (WindowBounds First, WindowBounds Second) SplitHorizontal(WindowBounds bounds, int gap, double ratio)
    {
        var availableHeight = Math.Max(2, bounds.Height - gap);
        var clampedRatio = Math.Clamp(ratio, 0.35d, 0.65d);
        var firstHeight = Math.Max(1, (int)Math.Round(availableHeight * clampedRatio));
        firstHeight = Math.Min(firstHeight, availableHeight - 1);
        var secondHeight = Math.Max(1, availableHeight - firstHeight);

        return (
            new WindowBounds(bounds.Left, bounds.Top, bounds.Width, firstHeight),
            new WindowBounds(bounds.Left, bounds.Top + firstHeight + gap, bounds.Width, secondHeight));
    }

    private static WindowBounds PickTileClosestToCenter(WindowBounds first, WindowBounds second, WindowBounds root)
    {
        var firstScore = DistanceFromCenter(first, root);
        var secondScore = DistanceFromCenter(second, root);
        return firstScore <= secondScore ? first : second;
    }

    private static double DistanceFromCenter(WindowBounds tile, WindowBounds root)
    {
        var deltaX = tile.CenterX - root.CenterX;
        var deltaY = tile.CenterY - root.CenterY;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private List<nint> EnsureOrder(int workspaceIndex, IEnumerable<nint> handles, nint? preferredAnchorHandle = null)
    {
        if (!_workspaceOrder.TryGetValue(workspaceIndex, out var order))
        {
            order = [];
            _workspaceOrder[workspaceIndex] = order;
        }

        var handleList = handles.Distinct().ToArray();
        var handleSet = handleList.ToHashSet();
        order.RemoveAll(handle => !handleSet.Contains(handle));
        var newHandles = handleList.Where(handle => !order.Contains(handle)).ToArray();
        if (newHandles.Length == 0)
        {
            return order;
        }

        if (preferredAnchorHandle is nint anchorHandle && anchorHandle != nint.Zero)
        {
            var anchorIndex = order.IndexOf(anchorHandle);
            if (anchorIndex >= 0)
            {
                order.InsertRange(anchorIndex + 1, newHandles);
                return order;
            }
        }

        foreach (var handle in newHandles)
        {
            order.Add(handle);
        }

        return order;
    }

    private static WindowDescriptor? FindDirectionalTarget(WindowDescriptor current, IReadOnlyList<WindowDescriptor> windows, WindowDirection direction)
    {
        WindowDescriptor? bestWindow = null;
        double? bestScore = null;

        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (window.Handle == current.Handle)
            {
                continue;
            }

            var score = ScoreCandidate(current, window, direction);
            if (score is null || (bestScore is not null && score.Value >= bestScore.Value))
            {
                continue;
            }

            bestScore = score.Value;
            bestWindow = window;
        }

        return bestWindow;
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

    private static WindowLayoutMode ResolveLayoutMode(int count, WindowDirection direction)
    {
        if (count <= 1)
        {
            return WindowLayoutMode.Single;
        }

        if (count == 2)
        {
            return direction is WindowDirection.Up or WindowDirection.Down
                ? WindowLayoutMode.SplitHorizontal
                : WindowLayoutMode.SplitVertical;
        }

        return direction is WindowDirection.Up or WindowDirection.Down
            ? WindowLayoutMode.MasterHorizontal
            : WindowLayoutMode.MasterVertical;
    }

    private static WindowLayoutMode NormalizeLayoutMode(WindowLayoutMode currentMode, int windowCount)
    {
        if (windowCount <= 1)
        {
            return WindowLayoutMode.Single;
        }

        if (windowCount == 2)
        {
            return currentMode is WindowLayoutMode.SplitHorizontal or WindowLayoutMode.SplitVertical
                ? currentMode
                : WindowLayoutMode.SplitVertical;
        }

        return currentMode switch
        {
            WindowLayoutMode.MasterHorizontal => WindowLayoutMode.MasterHorizontal,
            WindowLayoutMode.SplitHorizontal => WindowLayoutMode.MasterHorizontal,
            WindowLayoutMode.Spiral => WindowLayoutMode.Spiral,
            _ => WindowLayoutMode.MasterVertical
        };
    }

    private static bool UsesHorizontalPrimaryAxis(WindowLayoutMode mode)
    {
        return mode is WindowLayoutMode.SplitHorizontal or WindowLayoutMode.MasterHorizontal;
    }

    private static bool AreBoundsClose(WindowBounds left, WindowBounds right)
    {
        const int tolerance = 3;
        return Math.Abs(left.Left - right.Left) <= tolerance
               && Math.Abs(left.Top - right.Top) <= tolerance
               && Math.Abs(left.Width - right.Width) <= tolerance
               && Math.Abs(left.Height - right.Height) <= tolerance;
    }
}
