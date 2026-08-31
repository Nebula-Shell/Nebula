namespace CaelestiaWin.UI.ViewModels;

public sealed class PomodoroFocusBarViewModel
{
    public required string Label { get; init; }

    public required string ToolTip { get; init; }

    public required double Height { get; init; }

    public required double Width { get; init; }

    public required string Summary { get; init; }

    public bool IsToday { get; init; }

    public string FillBrush { get; init; } = "#A78BFA";
}
