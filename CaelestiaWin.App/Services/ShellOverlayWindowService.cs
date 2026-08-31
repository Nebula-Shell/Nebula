using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.UI.Views;

namespace CaelestiaWin.App.Services;

public sealed class ShellOverlayWindowService(IServiceProvider serviceProvider) : IShellOverlayWindowService
{
    public void ActivateForInput()
    {
        var window = serviceProvider.GetRequiredService<ShellOverlayWindow>();

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        _ = window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window.Activate();
            window.Focus();
        }));
    }
}
