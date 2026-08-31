namespace CaelestiaWin.UI.ViewModels;

public sealed class PomodoroHeatCellViewModel
{
    public string ToolTip { get; init; } = string.Empty;

    public string FillBrush { get; init; } = "#12000000";

    public bool IsToday { get; init; }

    public bool IsPlaceholder { get; init; }
}
