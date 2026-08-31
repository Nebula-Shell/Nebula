namespace CaelestiaWin.UI.ViewModels;

public sealed class CalendarDayViewModel
{
    public int Day { get; init; }

    public bool IsInCurrentMonth { get; init; }

    public bool IsToday { get; init; }

    public string DisplayText => Day > 0 ? Day.ToString() : string.Empty;
}
