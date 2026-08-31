namespace CaelestiaWin.Core.Models;

public sealed class ShellMenuItem
{
    public static ShellMenuItem Separator { get; } = new()
    {
        IsSeparator = true
    };

    public string Label { get; init; } = string.Empty;

    public string InvokeToken { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    public bool IsSeparator { get; init; }
}
