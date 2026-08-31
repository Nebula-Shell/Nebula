using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.App.Services;

public sealed class LauncherSearchService : ILauncherSearchService
{
    public IReadOnlyList<LauncherSearchResult> SearchApps(
        IReadOnlyList<AppLaunchItem> apps,
        IReadOnlyList<AppLaunchItem> recentApps,
        IReadOnlyList<AppLaunchItem> favoriteApps,
        string query,
        LauncherConfig config)
    {
        if (apps.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return BuildResultsForEmptyQuery(apps, recentApps, favoriteApps, config);
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var recentIds = BuildRecentIdSet(recentApps);
        var favoriteIds = BuildAppIdSet(favoriteApps);
        var results = new List<LauncherSearchResult>(Math.Min(config.MaxResults, apps.Count));
        var scored = new List<(AppLaunchItem App, int Score)>(apps.Count);

        for (var index = 0; index < apps.Count; index++)
        {
            var app = apps[index];
            var score = Score(app, normalizedQuery, recentIds, favoriteIds);
            if (score > 0)
            {
                scored.Add((app, score));
            }
        }

        scored.Sort(static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.App.DisplayName, right.App.DisplayName);
        });

        var resultCount = Math.Min(config.MaxResults, scored.Count);
        for (var index = 0; index < resultCount; index++)
        {
            var entry = scored[index];
            results.Add(CreateResult(entry.App, normalizedQuery, entry.Score, recentIds.Contains(entry.App.Id), favoriteIds.Contains(entry.App.Id)));
        }

        return results;
    }

    private static IReadOnlyList<LauncherSearchResult> BuildResultsForEmptyQuery(
        IReadOnlyList<AppLaunchItem> apps,
        IReadOnlyList<AppLaunchItem> recentApps,
        IReadOnlyList<AppLaunchItem> favoriteApps,
        LauncherConfig config)
    {
        var results = new List<LauncherSearchResult>();
        var recentIds = BuildRecentIdSet(recentApps);
        var favoriteIds = BuildAppIdSet(favoriteApps);

        results.AddRange(favoriteApps
            .Take(Math.Min(config.MaxResults, 12))
            .Select((app, index) => CreateResult(app, string.Empty, 12000 - index, isRecent: recentIds.Contains(app.Id), isFavorite: true)));

        if (config.ShowRecentAppsOnEmptyQuery)
        {
            results.AddRange(recentApps
                .Where(app => !favoriteIds.Contains(app.Id))
                .Take(config.RecentAppLimit)
                .Select((app, index) => CreateResult(app, string.Empty, 10000 - index, isRecent: true, isFavorite: false)));
        }

        results.AddRange(apps
            .Where(app => !recentIds.Contains(app.Id))
            .Where(app => !favoriteIds.Contains(app.Id))
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, config.MaxResults - results.Count))
            .Select((app, index) => CreateResult(app, string.Empty, 5000 - index, isRecent: false, isFavorite: false)));

        return results.Take(config.MaxResults).ToArray();
    }

    private static int Score(AppLaunchItem app, string query, HashSet<string> recentIds, HashSet<string> favoriteIds)
    {
        var name = app.DisplayName.ToLowerInvariant();
        var recentBoost = recentIds.Contains(app.Id) ? 250 : 0;
        var favoriteBoost = favoriteIds.Contains(app.Id) ? 450 : 0;
        var boost = recentBoost + favoriteBoost;

        if (name == query)
        {
            return 10000 + boost;
        }

        if (name.StartsWith(query, StringComparison.Ordinal))
        {
            return 8500 - Math.Abs(name.Length - query.Length) + boost;
        }

        var tokenMatch = name.Split(' ', '-', '_').Any(token => token.StartsWith(query, StringComparison.Ordinal));
        if (tokenMatch)
        {
            return 7600 + boost;
        }

        var containsIndex = name.IndexOf(query, StringComparison.Ordinal);
        if (containsIndex >= 0)
        {
            return 6500 - containsIndex + boost;
        }

        if (IsSubsequence(name, query, out var gapPenalty))
        {
            return 5000 - gapPenalty + boost;
        }

        var description = app.Description?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(description) && description.Contains(query, StringComparison.Ordinal))
        {
            return 2200 + boost;
        }

        return 0;
    }

    private static HashSet<string> BuildRecentIdSet(IReadOnlyList<AppLaunchItem> recentApps)
    {
        var recentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < recentApps.Count; index++)
        {
            recentIds.Add(recentApps[index].Id);
        }

        return recentIds;
    }

    private static HashSet<string> BuildAppIdSet(IReadOnlyList<AppLaunchItem> apps)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < apps.Count; index++)
        {
            ids.Add(apps[index].Id);
        }

        return ids;
    }

    private static LauncherSearchResult CreateResult(AppLaunchItem app, string query, int score, bool isRecent, bool isFavorite)
    {
        var title = app.DisplayName;
        var matchIndex = string.IsNullOrWhiteSpace(query)
            ? -1
            : title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var matchLength = matchIndex >= 0 ? query.Length : 0;

        return new LauncherSearchResult
        {
            Key = $"app:{app.Id}",
            Kind = Core.Enums.LauncherResultKind.App,
            Title = title,
            Subtitle = app.Description ?? app.ResolvedTargetPath ?? app.LaunchPath,
            SourceLabel = app.Source,
            App = app,
            IsRecent = isRecent,
            IsFavorite = isFavorite,
            Score = score,
            MatchPrefix = matchIndex >= 0 ? (matchIndex > 0 ? title[..matchIndex] : string.Empty) : title,
            MatchText = matchIndex >= 0 ? title.Substring(matchIndex, Math.Min(matchLength, title.Length - matchIndex)) : string.Empty,
            MatchSuffix = matchIndex >= 0 && matchIndex + matchLength < title.Length ? title[(matchIndex + matchLength)..] : string.Empty
        };
    }

    private static bool IsSubsequence(string candidate, string query, out int gapPenalty)
    {
        gapPenalty = 0;
        var queryIndex = 0;
        var lastMatchIndex = -1;

        for (var candidateIndex = 0; candidateIndex < candidate.Length && queryIndex < query.Length; candidateIndex++)
        {
            if (candidate[candidateIndex] != query[queryIndex])
            {
                continue;
            }

            if (lastMatchIndex >= 0)
            {
                gapPenalty += candidateIndex - lastMatchIndex - 1;
            }

            lastMatchIndex = candidateIndex;
            queryIndex++;
        }

        return queryIndex == query.Length;
    }
}
