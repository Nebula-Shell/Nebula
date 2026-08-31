using CaelestiaWin.Core.Enums;

namespace CaelestiaWin.Core.Models;

public sealed record WindowLayout(
    int WorkspaceIndex,
    WindowLayoutMode Mode,
    IReadOnlyList<nint> WindowHandles);
