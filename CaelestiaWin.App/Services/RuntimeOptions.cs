using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.App.Services;

public sealed class RuntimeOptions : ICurrentProcessService
{
    public RuntimeOptions(string[] args)
    {
        Arguments = args;
        IsSafeMode = args.Any(argument => string.Equals(argument, "--safe-mode", StringComparison.OrdinalIgnoreCase));
        RestartedAfterCrash = args.Any(argument => string.Equals(argument, "--restarted-after-crash", StringComparison.OrdinalIgnoreCase));
    }

    public string[] Arguments { get; }

    public bool IsSafeMode { get; }

    public bool RestartedAfterCrash { get; }

    public bool RestartOnCrash { get; set; } = true;

    public string ExecutablePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to determine the current Nebula Shell executable path.");

    public string BuildStartupArguments(bool safeMode)
    {
        var filtered = Arguments
            .Where(argument =>
                !string.Equals(argument, "--safe-mode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(argument, "--restarted-after-crash", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (safeMode)
        {
            filtered.Add("--safe-mode");
        }

        filtered.Add("--restarted-after-crash");
        return string.Join(' ', filtered);
    }
}
