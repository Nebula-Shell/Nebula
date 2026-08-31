using System.Text.Json;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Core.Services;

public sealed class FavoriteAppsService : IFavoriteAppsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _sync = new();
    private readonly string _favoritesPath;
    private List<AppLaunchItem>? _favorites;

    public FavoriteAppsService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell");
        Directory.CreateDirectory(root);
        _favoritesPath = Path.Combine(root, "launcher-favorites.json");
    }

    public IReadOnlyList<AppLaunchItem> GetFavorites()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return _favorites!.ToArray();
        }
    }

    public bool IsFavorite(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        lock (_sync)
        {
            EnsureLoaded();
            return _favorites!.Any(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void ToggleFavorite(AppLaunchItem app)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var existingIndex = _favorites!.FindIndex(existing => string.Equals(existing.Id, app.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _favorites.RemoveAt(existingIndex);
            }
            else
            {
                _favorites.Insert(0, app);
            }

            Save();
        }
    }

    private void EnsureLoaded()
    {
        if (_favorites is not null)
        {
            return;
        }

        try
        {
            if (!File.Exists(_favoritesPath))
            {
                _favorites = [];
                return;
            }

            var json = File.ReadAllText(_favoritesPath);
            _favorites = JsonSerializer.Deserialize<List<AppLaunchItem>>(json) ?? [];
        }
        catch
        {
            _favorites = [];
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_favorites, JsonOptions);
        File.WriteAllText(_favoritesPath, json);
    }
}
