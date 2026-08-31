using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.App.Services;

public sealed class ShellLifetimeService : IShellLifetimeService
{
    public bool CanExit { get; private set; }

    public void AllowExit()
    {
        CanExit = true;
    }
}
