using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Core.Services;

public sealed class RecentAppsService : IRecentAppsService
{
    private readonly List<AppLaunchItem> _recentApps = [];

    public IReadOnlyList<AppLaunchItem> GetRecentApps(int maxResults)
    {
        return _recentApps.Take(Math.Max(0, maxResults)).ToArray();
    }

    public void RecordLaunch(AppLaunchItem app)
    {
        _recentApps.RemoveAll(existing => string.Equals(existing.Id, app.Id, StringComparison.OrdinalIgnoreCase));
        _recentApps.Insert(0, app);

        if (_recentApps.Count > 20)
        {
            _recentApps.RemoveRange(20, _recentApps.Count - 20);
        }
    }
}
