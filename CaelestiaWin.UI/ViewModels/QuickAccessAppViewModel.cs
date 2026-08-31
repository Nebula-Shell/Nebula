using System.Windows.Input;
using CaelestiaWin.Core.Common;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class QuickAccessAppViewModel : ObservableObjectBase, IDesktopStripItemViewModel
{
    private bool _isActive;

    public QuickAccessAppViewModel(string key, string name, string indicatorGlyph, string indicatorFontFamilyName, Func<Task> activateAsync)
    {
        Key = key;
        Name = name;
        IndicatorGlyph = indicatorGlyph;
        IndicatorFontFamilyName = indicatorFontFamilyName;
        ActivateCommand = new AsyncRelayCommand(activateAsync);
    }

    public string Key { get; }

    public string Name { get; }

    public string IndicatorGlyph { get; }

    public string IndicatorFontFamilyName { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ICommand ActivateCommand { get; }
}
