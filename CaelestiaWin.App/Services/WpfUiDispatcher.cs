using System.Windows;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.App.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Application.Current.Dispatcher.InvokeAsync(action).Task;
    }
}
