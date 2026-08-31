namespace CaelestiaWin.UI.ViewModels;

public sealed class ShortcutGuideItemViewModel(string title, string gesture, string description)
{
    public string Title { get; } = title;

    public string Gesture { get; } = gesture;

    public string Description { get; } = description;
}
