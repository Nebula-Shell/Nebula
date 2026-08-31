namespace CaelestiaWin.Core.Models;

public sealed record PomodoroFocusBucket(
    DateOnly Date,
    int FocusedSeconds);
