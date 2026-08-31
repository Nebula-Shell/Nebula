using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.App.Services;

public sealed class LauncherCommandService(IWindowActionService windowActionService) : ILauncherCommandService
{
    private static readonly (SystemCommandKind Command, string Title, string Subtitle, string[] Aliases)[] Commands =
    [
        (SystemCommandKind.Lock, "lock", "Lock this device", ["lock", "lock screen"]),
        (SystemCommandKind.SignOut, "sign out", "Sign out of Windows", ["signout", "sign out", "log out", "logout"]),
        (SystemCommandKind.Restart, "restart", "Restart the system", ["restart", "reboot"]),
        (SystemCommandKind.Shutdown, "shutdown", "Power off the system", ["shutdown", "power off", "shut down"])
    ];

    public IReadOnlyList<LauncherSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        return Commands
            .Select(command => new { Result = CreateResult(command, normalizedQuery), command.Command })
            .Where(entry => entry.Result is not null)
            .Select(entry => entry.Result!)
            .OrderByDescending(result => result.Score)
            .ToArray();
    }

    public Task ExecuteAsync(SystemCommandKind command, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case SystemCommandKind.Lock:
                windowActionService.Lock();
                break;
            case SystemCommandKind.SignOut:
                windowActionService.SignOut();
                break;
            case SystemCommandKind.Restart:
                windowActionService.Restart();
                break;
            case SystemCommandKind.Shutdown:
                windowActionService.Shutdown();
                break;
        }

        return Task.CompletedTask;
    }

    private static LauncherSearchResult? CreateResult(
        (SystemCommandKind Command, string Title, string Subtitle, string[] Aliases) command,
        string query)
    {
        var title = command.Title;
        var exact = command.Aliases.FirstOrDefault(alias => string.Equals(alias, query, StringComparison.OrdinalIgnoreCase));
        var prefix = command.Aliases.FirstOrDefault(alias => alias.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        var contains = command.Aliases.FirstOrDefault(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase));

        var score = exact is not null ? 10000 :
            prefix is not null ? 8700 :
            contains is not null ? 7000 :
            0;

        if (score == 0)
        {
            return null;
        }

        var matchIndex = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var matchLength = matchIndex >= 0 ? query.Length : 0;

        return new LauncherSearchResult
        {
            Key = $"command:{command.Command}",
            Kind = LauncherResultKind.Command,
            Title = title,
            Subtitle = command.Subtitle,
            SourceLabel = "Command",
            Command = command.Command,
            Score = score,
            MatchPrefix = matchIndex >= 0 ? (matchIndex > 0 ? title[..matchIndex] : string.Empty) : title,
            MatchText = matchIndex >= 0 ? title.Substring(matchIndex, Math.Min(matchLength, title.Length - matchIndex)) : string.Empty,
            MatchSuffix = matchIndex >= 0 && matchIndex + matchLength < title.Length ? title[(matchIndex + matchLength)..] : string.Empty
        };
    }
}
