using System.Windows;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.UI.Views;

namespace CaelestiaWin.App.Services;

public sealed class ShellSettingsService(ShellSettingsWindow window) : IShellSettingsService
{
    public void Toggle()
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        Show();
    }

    public void Show(ShellSettingsSection? section = null)
    {
        window.Show();
        window.Activate();
        window.Focus();

        if (section is not null)
        {
            window.NavigateToSection(section.Value);
        }
    }
}
