using System.ComponentModel;
using System.Windows.Input;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class WorkspaceItemViewModel : ObservableObjectBase, IDesktopStripItemViewModel
{
    private readonly WorkspaceModel _workspaceModel;
    private readonly Action<WorkspaceItemViewModel> _activateWorkspace;

    public WorkspaceItemViewModel(WorkspaceModel workspaceModel, Action<WorkspaceItemViewModel> activateWorkspace)
    {
        _workspaceModel = workspaceModel;
        _activateWorkspace = activateWorkspace;
        ActivateCommand = new RelayCommand(() => _activateWorkspace(this));
        _workspaceModel.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public string Name => _workspaceModel.Name;

    public string IndicatorGlyph => _workspaceModel.IsDiscordDesktop && _workspaceModel.IsActive
        ? "\uF075"
        : _workspaceModel.IsActive
            ? "\u2605"
            : "\u25CF";

    public string IndicatorFontFamilyName => "JetBrainsMono Nerd Font, JetBrains Mono, Cascadia Mono, Segoe UI Symbol, Segoe UI";

    public bool IsActive => _workspaceModel.IsActive;

    public bool IsDiscordDesktop => _workspaceModel.IsDiscordDesktop;

    public int Index => _workspaceModel.Index;

    public ICommand ActivateCommand { get; }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(WorkspaceModel.IsActive) or nameof(WorkspaceModel.IsDiscordDesktop))
        {
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsDiscordDesktop));
            OnPropertyChanged(nameof(IndicatorGlyph));
            OnPropertyChanged(nameof(IndicatorFontFamilyName));
        }
    }
}
