using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class ShellSettingsWindow : Window
{
    private ShellSettingsViewModel? _viewModel;
    private string _currentSectionName = nameof(AppearanceSection);

    public ShellSettingsWindow(ShellSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
        Loaded += (_, _) => ShowSection(_currentSectionName);
    }

    private void OnCloseRequested(object? sender, EventArgs eventArgs)
    {
        Hide();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (IsVisible)
        {
            _viewModel?.ReloadFromConfig();
            _viewModel?.BeginAutoRefresh();
            return;
        }

        _viewModel?.EndAutoRefresh();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (Application.Current?.Dispatcher.HasShutdownStarted == true)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private void NavigateButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string sectionName })
        {
            return;
        }

        ShowSection(sectionName);
    }

    public void NavigateToSection(ShellSettingsSection section)
    {
        var sectionName = section switch
        {
            ShellSettingsSection.Appearance => nameof(AppearanceSection),
            ShellSettingsSection.ControlCenter => nameof(ControlCenterSection),
            ShellSettingsSection.Sound => nameof(SoundSection),
            ShellSettingsSection.Wifi => nameof(WifiSection),
            ShellSettingsSection.Launcher => nameof(LauncherSection),
            ShellSettingsSection.Defaults => nameof(DefaultsSection),
            ShellSettingsSection.Windowing => nameof(WindowingSection),
            ShellSettingsSection.GameCenter => nameof(GameCenterSection),
            ShellSettingsSection.Startup => nameof(StartupSection),
            ShellSettingsSection.Shortcuts => nameof(ShortcutsSection),
            ShellSettingsSection.Notifications => nameof(NotificationsSection),
            ShellSettingsSection.SystemInfo => nameof(SystemInfoSection),
            _ => nameof(AppearanceSection)
        };

        ShowSection(sectionName);
    }

    private void ShowSection(string sectionName)
    {
        _currentSectionName = sectionName;

        foreach (var name in GetSectionNames())
        {
            if (FindName(name) is FrameworkElement section)
            {
                section.Visibility = string.Equals(name, sectionName, StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        SettingsTitle.Text = GetSectionTitle(sectionName);
        Dispatcher.BeginInvoke(() => SettingsScrollViewer.ScrollToHome());
    }

    private static IEnumerable<string> GetSectionNames()
    {
        yield return nameof(AppearanceSection);
        yield return nameof(ControlCenterSection);
        yield return nameof(SoundSection);
        yield return nameof(LauncherSection);
        yield return nameof(DefaultsSection);
        yield return nameof(WindowingSection);
        yield return nameof(GameCenterSection);
        yield return nameof(StartupSection);
        yield return nameof(WifiSection);
        yield return nameof(ShortcutsSection);
        yield return nameof(NotificationsSection);
        yield return nameof(SystemInfoSection);
    }

    private static string GetSectionTitle(string sectionName) => sectionName switch
    {
        nameof(AppearanceSection) => "Appearance",
        nameof(ControlCenterSection) => "Control Center",
        nameof(SoundSection) => "Sound",
        nameof(LauncherSection) => "Launcher",
        nameof(DefaultsSection) => "Defaults",
        nameof(WindowingSection) => "Windowing",
        nameof(GameCenterSection) => "Game Center",
        nameof(StartupSection) => "Startup",
        nameof(WifiSection) => "Wi-Fi",
        nameof(ShortcutsSection) => "Keyboard",
        nameof(NotificationsSection) => "Notifications",
        nameof(SystemInfoSection) => "System Info",
        _ => "Settings"
    };
}
