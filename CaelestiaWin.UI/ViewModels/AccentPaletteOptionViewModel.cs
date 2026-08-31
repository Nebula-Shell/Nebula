namespace CaelestiaWin.UI.ViewModels;

public sealed class AccentPaletteOptionViewModel(
    string name,
    string accentColor,
    string secondaryAccentColor)
{
    public string Name { get; } = name;

    public string AccentColor { get; } = accentColor;

    public string SecondaryAccentColor { get; } = secondaryAccentColor;
}
