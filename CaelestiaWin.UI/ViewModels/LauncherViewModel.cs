using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class LauncherViewModel : ObservableObjectBase
{
    private readonly IAppStateService _appStateService;
    private readonly IAppDiscoveryService _appDiscoveryService;
    private readonly ILauncherSearchService _launcherSearchService;
    private readonly ILauncherCommandService _launcherCommandService;
    private readonly IRecentAppsService _recentAppsService;
    private readonly IFavoriteAppsService _favoriteAppsService;
    private readonly IExplorerIntegrationService _explorerIntegrationService;
    private readonly IFileExplorerService _fileExplorerService;
    private readonly IShellSettingsService _shellSettingsService;
    private readonly IDiagnosticLogService _logService;
    private readonly DispatcherTimer _searchTimer;
    private IReadOnlyList<AppLaunchItem> _allApps = [];
    private bool _isLoaded;
    private string _query = string.Empty;
    private LauncherSearchResult? _selectedItem;
    private bool _isOpen;
    private bool _isAllAppsMode;
    private string _statusText = string.Empty;

    public LauncherViewModel(
        IAppStateService appStateService,
        IAppDiscoveryService appDiscoveryService,
        ILauncherSearchService launcherSearchService,
        ILauncherCommandService launcherCommandService,
        IRecentAppsService recentAppsService,
        IFavoriteAppsService favoriteAppsService,
        IExplorerIntegrationService explorerIntegrationService,
        IFileExplorerService fileExplorerService,
        IShellSettingsService shellSettingsService,
        IDiagnosticLogService logService)
    {
        _appStateService = appStateService;
        _appDiscoveryService = appDiscoveryService;
        _launcherSearchService = launcherSearchService;
        _launcherCommandService = launcherCommandService;
        _recentAppsService = recentAppsService;
        _favoriteAppsService = favoriteAppsService;
        _explorerIntegrationService = explorerIntegrationService;
        _fileExplorerService = fileExplorerService;
        _shellSettingsService = shellSettingsService;
        _logService = logService;

        Results = new ObservableCollection<LauncherSearchResult>();
        IsOpen = _appStateService.IsLauncherOpen;
        LaunchItemCommand = new AsyncRelayCommand<LauncherSearchResult>(LaunchItemAsync);
        LaunchSelectedCommand = new AsyncRelayCommand(LaunchSelectedAsync, () => SelectedItem is not null);
        ToggleFavoriteCommand = new RelayCommand<LauncherSearchResult>(ToggleFavorite, item => item?.App is not null);
        ToggleAllAppsModeCommand = new RelayCommand(ToggleAllAppsMode);

        _searchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_appStateService.Config.Launcher.SearchDebounceMs)
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RefreshResults();
        };

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
    }

    public ObservableCollection<LauncherSearchResult> Results { get; }

    public ICommand LaunchItemCommand { get; }

    public ICommand LaunchSelectedCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand ToggleAllAppsModeCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value))
            {
                return;
            }

            if (!IsOpen)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value) && IsAllAppsMode)
            {
                IsAllAppsMode = false;
            }

            _searchTimer.Interval = TimeSpan.FromMilliseconds(_appStateService.Config.Launcher.SearchDebounceMs);
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public LauncherSearchResult? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value) && LaunchSelectedCommand is AsyncRelayCommand command)
            {
                command.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool UseGridDisplay => _appStateService.Config.Launcher.DisplayMode == LauncherDisplayModeKind.Grid;

    public bool IsAllAppsMode
    {
        get => _isAllAppsMode;
        private set
        {
            if (SetProperty(ref _isAllAppsMode, value))
            {
                OnPropertyChanged(nameof(AllAppsButtonText));
            }
        }
    }

    public string AllAppsButtonText => IsAllAppsMode ? "Filtered" : "All apps";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedItem = null;
            return;
        }

        var currentIndex = SelectedItem is null ? -1 : Results.IndexOf(SelectedItem);
        var nextIndex = currentIndex + delta;

        if (nextIndex < 0)
        {
            nextIndex = Results.Count - 1;
        }
        else if (nextIndex >= Results.Count)
        {
            nextIndex = 0;
        }

        SelectedItem = Results[nextIndex];
    }

    public void Close()
    {
        _appStateService.IsLauncherOpen = false;
    }

    private async Task LaunchItemAsync(LauncherSearchResult? item)
    {
        var launchItem = item ?? SelectedItem;
        if (launchItem is null)
        {
            return;
        }

        try
        {
            if (launchItem.Kind == Core.Enums.LauncherResultKind.Command && launchItem.Command is Core.Enums.SystemCommandKind command)
            {
                await _launcherCommandService.ExecuteAsync(command);
                Close();
                return;
            }

            if (launchItem.App is null)
            {
                return;
            }

            if (TryLaunchInternalApp(launchItem.App))
            {
                Close();
                return;
            }

            using var process = TryLaunchApp(launchItem.App);

            _logService.Info("Launcher opened an application.", new Dictionary<string, object?>
            {
                ["app"] = launchItem.App.DisplayName,
                ["source"] = launchItem.App.Source
            });
            _recentAppsService.RecordLaunch(launchItem.App);

            Close();
        }
        catch (Exception exception)
        {
            _logService.Error("Launcher failed to open an application.", exception, new Dictionary<string, object?>
            {
                ["title"] = launchItem.Title,
                ["kind"] = launchItem.Kind
            });
            StatusText = "Couldn't open that app.";
        }
    }

    private Process? TryLaunchApp(AppLaunchItem app)
    {
        return _explorerIntegrationService.LaunchApp(app);
    }

    private Task LaunchSelectedAsync()
    {
        return LaunchItemAsync(SelectedItem);
    }

    private async Task EnsureAppsLoadedAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        StatusText = "Indexing installed apps...";

        try
        {
            _allApps = await _appDiscoveryService.GetAppsAsync(false);
            _isLoaded = true;
        }
        catch (Exception exception)
        {
            _logService.Error("Launcher failed while loading apps.", exception);
            StatusText = "Unable to read installed apps.";
        }
    }

    private void RefreshResults()
    {
        Results.Clear();

        var items = new List<LauncherSearchResult>();
        var recentApps = _recentAppsService.GetRecentApps(_appStateService.Config.Launcher.RecentAppLimit);
        var favoriteApps = _favoriteAppsService.GetFavorites();

        if (_appStateService.Config.Launcher.EnableCommandMode)
        {
            items.AddRange(_launcherCommandService.Search(Query));
        }

        items.AddRange(BuildInternalResults(Query));
        items.AddRange(_launcherSearchService.SearchApps(_allApps, recentApps, favoriteApps, Query, _appStateService.Config.Launcher));

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].App is { } app)
            {
                items[index].IsFavorite = _favoriteAppsService.IsFavorite(app.Id);
            }
        }

        var orderedItems = items
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase);

        var selectedItems = Query.Length == 0 && IsAllAppsMode
            ? orderedItems.ToArray()
            : orderedItems
                .Take(_appStateService.Config.Launcher.MaxResults)
                .ToArray();

        foreach (var item in selectedItems)
        {
            Results.Add(item);
        }

        SelectedItem = Results.FirstOrDefault();
        StatusText = Results.Count == 0 ? "No matching apps." : string.Empty;
    }

    private void ToggleAllAppsMode()
    {
        IsAllAppsMode = !IsAllAppsMode;
        _searchTimer.Stop();
        RefreshResults();
    }

    private void ToggleFavorite(LauncherSearchResult? item)
    {
        if (item?.App is null)
        {
            return;
        }

        _favoriteAppsService.ToggleFavorite(item.App);
        RefreshResults();
    }

    private async void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IAppStateService.Config))
        {
            OnPropertyChanged(nameof(UseGridDisplay));
            return;
        }

        if (eventArgs.PropertyName != nameof(IAppStateService.IsLauncherOpen))
        {
            return;
        }

        IsOpen = _appStateService.IsLauncherOpen;
        if (IsOpen)
        {
            await EnsureAppsLoadedAsync();
            RefreshResults();
            return;
        }

        IsAllAppsMode = false;
        if (_appStateService.Config.Launcher.ClearQueryOnClose)
        {
            Query = string.Empty;
        }
    }

    private bool TryLaunchInternalApp(AppLaunchItem app)
    {
        if (string.Equals(app.Id, "nebula.files", StringComparison.OrdinalIgnoreCase))
        {
            _ = _fileExplorerService.OpenNebulaExplorerAsync(app.Arguments);
            return true;
        }

        if (!app.Id.StartsWith("nebula.settings", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var section = app.Arguments?.ToLowerInvariant() switch
        {
            "wifi" => ShellSettingsSection.Wifi,
            "games" => ShellSettingsSection.GameCenter,
            _ => ShellSettingsSection.Appearance
        };

        _shellSettingsService.Show(section);
        return true;
    }

    private static IReadOnlyList<LauncherSearchResult> BuildInternalResults(string query)
    {
        var extraResults = new List<LauncherSearchResult>();

        if (Matches(query, "files", "explorer", "finder", "file manager"))
        {
            extraResults.Add(CreateInternalResult(
                new AppLaunchItem(
                    "nebula.files",
                    "Nebula Files",
                    "nebula://files",
                    null,
                    "Browse folders and files with Nebula's shell file explorer.",
                    "Nebula",
                    null),
                query,
                9900));
        }

        if (!Matches(query, "settings", "preferences", "config", "shell"))
        {
            if (Matches(query, "wifi", "network", "internet"))
            {
                var wifiApp = new AppLaunchItem(
                    "nebula.settings.wifi",
                    "Wi-Fi Settings",
                    "nebula://settings",
                    "wifi",
                    "Manage wireless networks, passwords, and connectivity.",
                    "Nebula",
                    null);

                extraResults.Add(CreateInternalResult(wifiApp, query, 9800));
            }

            if (Matches(query, "games", "gaming", "game center", "game mode"))
            {
                extraResults.Add(CreateInternalResult(
                    new AppLaunchItem(
                        "nebula.settings.games",
                        "Game Center",
                        "nebula://settings",
                        "games",
                        "Tune auto-detected games, launchers, and shell performance behavior.",
                        "Nebula",
                        null),
                    query,
                    9825));
            }

            return extraResults;
        }

        var settingsApp = new AppLaunchItem(
            "nebula.settings",
            "Nebula Settings",
            "nebula://settings",
            null,
            "Appearance, startup, shortcuts, Wi-Fi, and shell behavior.",
            "Nebula",
            null);

        var results = new List<LauncherSearchResult>(extraResults)
        {
            CreateInternalResult(settingsApp, query, 10000)
        };

        if (Matches(query, "wifi", "network", "internet"))
        {
            results.Add(CreateInternalResult(
                new AppLaunchItem(
                    "nebula.settings.wifi",
                    "Wi-Fi Settings",
                    "nebula://settings",
                    "wifi",
                    "Manage wireless networks, passwords, and connectivity.",
                    "Nebula",
                    null),
                query,
                9850));
        }

        if (Matches(query, "games", "gaming", "game center", "game mode"))
        {
            results.Add(CreateInternalResult(
                new AppLaunchItem(
                    "nebula.settings.games",
                    "Game Center",
                    "nebula://settings",
                    "games",
                    "Tune auto-detected games, launchers, and shell performance behavior.",
                    "Nebula",
                    null),
                query,
                9875));
        }

        return results;
    }

    private static LauncherSearchResult CreateInternalResult(AppLaunchItem app, string query, int score)
    {
        var matchIndex = string.IsNullOrWhiteSpace(query)
            ? -1
            : app.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var matchLength = matchIndex >= 0 ? query.Length : 0;

        return new LauncherSearchResult
        {
            Key = $"internal:{app.Id}",
            Kind = LauncherResultKind.App,
            Title = app.DisplayName,
            Subtitle = app.Description ?? "Nebula settings",
            SourceLabel = "Nebula",
            Score = score,
            App = app,
            MatchPrefix = matchIndex >= 0 ? (matchIndex > 0 ? app.DisplayName[..matchIndex] : string.Empty) : app.DisplayName,
            MatchText = matchIndex >= 0 ? app.DisplayName.Substring(matchIndex, Math.Min(matchLength, app.DisplayName.Length - matchIndex)) : string.Empty,
            MatchSuffix = matchIndex >= 0 && matchIndex + matchLength < app.DisplayName.Length ? app.DisplayName[(matchIndex + matchLength)..] : string.Empty
        };
    }

    private static bool Matches(string query, params string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return aliases.Any(alias => alias.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                                    || query.Trim().Contains(alias, StringComparison.OrdinalIgnoreCase));
    }
}
