using System.Windows.Input;

namespace CaelestiaWin.UI.ViewModels;

public interface IDesktopStripItemViewModel
{
    string Name { get; }

    string IndicatorGlyph { get; }

    string IndicatorFontFamilyName { get; }

    bool IsActive { get; }

    ICommand ActivateCommand { get; }
}
