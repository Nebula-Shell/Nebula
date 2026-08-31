using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class NebulaFileExplorerViewModel : ObservableObjectBase
{
    private static readonly EnumerationOptions DirectoryEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0
    };

    private static readonly BuiltInSidebarLocation[] BuiltInSidebarLocations =
    [
        new("home", "Home", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "\uE80F"),
        new("desktop", "Desktop", () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE7F4"),
        new("documents", "Documents", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5"),
        new("downloads", "Downloads", () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "\uE896"),
        new("pictures", "Pictures", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uE91B"),
        new("music", "Music", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "\uE189")
    ];

    private static readonly string[] OneDriveEnvironmentVariableNames =
    [
        "OneDriveCommercial",
        "OneDriveConsumer",
        "OneDrive"
    ];

    private static readonly Dictionary<string, string> OneDriveFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["desktop"] = "Desktop",
        ["documents"] = "Documents",
        ["downloads"] = "Downloads",
        ["pictures"] = "Pictures",
        ["music"] = "Music"
    };

    private const string PreferredDropEffectFormat = "Preferred DropEffect";

    private readonly IFileExplorerIndexService _indexService;
    private readonly IFileExplorerSidebarStateService _sidebarStateService;
    private readonly INotificationService _notificationService;
    private readonly IDiagnosticLogService _logService;
    private readonly HashSet<Guid> _pendingTaskRemovals = [];
    private readonly List<FileExplorerItemViewModel> _loadedItems = [];
    private List<string> _history = [];
    private int _historyIndex = -1;
    private string _currentPath = string.Empty;
    private string _pathInput = string.Empty;
    private string _searchQuery = string.Empty;
    private string _inlineSuggestionPrefix = string.Empty;
    private string _inlineSuggestionSuffix = string.Empty;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private bool _isGridView;
    private bool _showHiddenFiles;
    private bool _showFileExtensions = true;
    private bool _isViewMenuOpen;
    private bool _isTypingPath;
    private bool _isPathEditMode;
    private FileExplorerSortKind _sortKind = FileExplorerSortKind.Name;
    private bool _sortDescending;
    private int _gridZoomLevel = 2;
    private IReadOnlyList<string> _clipboardPaths = [];
    private bool _clipboardIsCut;
    private FileExplorerTabViewModel? _activeTab;

    public NebulaFileExplorerViewModel(
        IFileExplorerIndexService indexService,
        IFileExplorerSidebarStateService sidebarStateService,
        INotificationService notificationService,
        IDiagnosticLogService logService)
    {
        _indexService = indexService;
        _sidebarStateService = sidebarStateService;
        _notificationService = notificationService;
        _logService = logService;

        SidebarLocations = new ObservableCollection<FileExplorerLocationViewModel>();
        BreadcrumbSegments = new ObservableCollection<FileExplorerPathSegmentViewModel>();
        Items = new ObservableCollection<FileExplorerItemViewModel>();
        FileOperationTasks = new ObservableCollection<FileOperationTaskViewModel>();
        Tabs = new ObservableCollection<FileExplorerTabViewModel>();
        FileOperationTasks.CollectionChanged += OnFileOperationTasksChanged;

        BackCommand = new AsyncRelayCommand(GoBackAsync, () => CanGoBack && !IsLoading);
        ForwardCommand = new AsyncRelayCommand(GoForwardAsync, () => CanGoForward && !IsLoading);
        UpCommand = new AsyncRelayCommand(GoUpAsync, () => CanGoUp && !IsLoading);
        RefreshCommand = new AsyncRelayCommand(() => OpenPathAsync(CurrentPath, false), () => !IsLoading);
        NewFolderCommand = new AsyncRelayCommand(CreateFolderAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(CurrentPath));
        OpenLocationCommand = new AsyncRelayCommand<FileExplorerLocationViewModel>(location => OpenPathAsync(location?.Path), location => location is not null && !location.IsSeparator && !IsLoading);
        OpenItemCommand = new AsyncRelayCommand<FileExplorerItemViewModel>(OpenItemAsync, item => item is not null && !IsLoading);
        NavigateToInputPathCommand = new AsyncRelayCommand(NavigateToInputPathAsync, () => !string.IsNullOrWhiteSpace(PathInput) && !IsLoading);
        NavigateToBreadcrumbCommand = new AsyncRelayCommand<FileExplorerPathSegmentViewModel>(NavigateToBreadcrumbAsync, segment => segment is not null && !IsLoading);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        SetListViewCommand = new RelayCommand(() => IsGridView = false, () => !IsLoading);
        SetGridViewCommand = new RelayCommand(() => IsGridView = true, () => !IsLoading);
        ToggleHiddenFilesCommand = new RelayCommand(ToggleHiddenFiles, () => !IsLoading);
        ToggleFileExtensionsCommand = new RelayCommand(ToggleFileExtensions, () => !IsLoading);
        SortByNameCommand = new RelayCommand(() => SetSortKind(FileExplorerSortKind.Name), () => !IsLoading);
        SortByTypeCommand = new RelayCommand(() => SetSortKind(FileExplorerSortKind.Type), () => !IsLoading);
        SortBySizeCommand = new RelayCommand(() => SetSortKind(FileExplorerSortKind.Size), () => !IsLoading);
        SortByModifiedCommand = new RelayCommand(() => SetSortKind(FileExplorerSortKind.Modified), () => !IsLoading);
        ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection, () => !IsLoading);
        ZoomInCommand = new RelayCommand(() => AdjustGridZoom(1), () => !IsLoading && CanZoomIn);
        ZoomOutCommand = new RelayCommand(() => AdjustGridZoom(-1), () => !IsLoading && CanZoomOut);
        RevealInWindowsExplorerCommand = new RelayCommand<FileExplorerItemViewModel>(RevealInWindowsExplorer, item => item is not null);
        CopyItemPathCommand = new RelayCommand<FileExplorerItemViewModel>(CopyItemPath, item => item is not null);
        CopyItemCommand = new RelayCommand<FileExplorerItemViewModel>(CopyItemToClipboard, item => item is not null);
        CutItemCommand = new RelayCommand<FileExplorerItemViewModel>(CutItemToClipboard, item => item is not null);
        BeginRenameCommand = new RelayCommand<FileExplorerItemViewModel>(BeginRename, item => item is not null && !IsLoading);
        CommitRenameCommand = new AsyncRelayCommand<FileExplorerItemViewModel>(CommitRenameAsync, item => item is not null && item.IsRenaming);
        CancelRenameCommand = new RelayCommand<FileExplorerItemViewModel>(CancelRename, item => item is not null && item.IsRenaming);
        PasteIntoCurrentDirectoryCommand = new AsyncRelayCommand(PasteIntoCurrentDirectoryAsync, () => CanPasteIntoCurrentDirectory && !IsLoading);
        PinItemToSidebarCommand = new RelayCommand<FileExplorerItemViewModel>(PinItemToSidebar, CanPinItemToSidebar);
        UnpinSidebarLocationCommand = new RelayCommand<FileExplorerLocationViewModel>(UnpinSidebarLocation, location => location?.CanRemove == true);
        NewTabCommand = new AsyncRelayCommand(CreateNewTabAsync, () => !IsLoading);
        SelectTabCommand = new AsyncRelayCommand<FileExplorerTabViewModel>(SwitchToTabAsync, tab => tab is not null && !IsLoading);
        CloseTabCommand = new RelayCommand<FileExplorerTabViewModel>(CloseTab, tab => tab is not null);

        PopulateSidebar();
        CreateInitialTab();
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<FileExplorerLocationViewModel> SidebarLocations { get; }

    public ObservableCollection<FileExplorerPathSegmentViewModel> BreadcrumbSegments { get; }

    public ObservableCollection<FileExplorerItemViewModel> Items { get; }

    public ObservableCollection<FileOperationTaskViewModel> FileOperationTasks { get; }

    public ObservableCollection<FileExplorerTabViewModel> Tabs { get; }

    public ICommand BackCommand { get; }

    public ICommand ForwardCommand { get; }

    public ICommand UpCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand NewFolderCommand { get; }

    public ICommand OpenLocationCommand { get; }

    public ICommand OpenItemCommand { get; }

    public ICommand NavigateToInputPathCommand { get; }

    public ICommand NavigateToBreadcrumbCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand SetListViewCommand { get; }

    public ICommand SetGridViewCommand { get; }

    public ICommand ToggleHiddenFilesCommand { get; }

    public ICommand ToggleFileExtensionsCommand { get; }

    public ICommand SortByNameCommand { get; }

    public ICommand SortByTypeCommand { get; }

    public ICommand SortBySizeCommand { get; }

    public ICommand SortByModifiedCommand { get; }

    public ICommand ToggleSortDirectionCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand RevealInWindowsExplorerCommand { get; }

    public ICommand CopyItemPathCommand { get; }

    public ICommand CopyItemCommand { get; }

    public ICommand CutItemCommand { get; }

    public ICommand BeginRenameCommand { get; }

    public ICommand CommitRenameCommand { get; }

    public ICommand CancelRenameCommand { get; }

    public ICommand PasteIntoCurrentDirectoryCommand { get; }

    public ICommand PinItemToSidebarCommand { get; }

    public ICommand UnpinSidebarLocationCommand { get; }

    public ICommand NewTabCommand { get; }

    public ICommand SelectTabCommand { get; }

    public ICommand CloseTabCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(CanGoUp));
            }
        }
    }

    public string PathInput
    {
        get => _pathInput;
        set
        {
            if (SetProperty(ref _pathInput, value))
            {
                UpdatePathSuggestion();
                (NavigateToInputPathCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string InlineSuggestionPrefix
    {
        get => _inlineSuggestionPrefix;
        private set
        {
            if (SetProperty(ref _inlineSuggestionPrefix, value))
            {
                OnPropertyChanged(nameof(IsInlineSuggestionVisible));
            }
        }
    }

    public string InlineSuggestionSuffix
    {
        get => _inlineSuggestionSuffix;
        private set
        {
            if (SetProperty(ref _inlineSuggestionSuffix, value))
            {
                OnPropertyChanged(nameof(IsInlineSuggestionVisible));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            ApplyItemsView();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
            {
                return;
            }

            NotifyNavigationCommands();
            NotifyViewCommands();
        }
    }

    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (!SetProperty(ref _isGridView, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsLoadingEmptyList));
            OnPropertyChanged(nameof(IsLoadingEmptyGrid));
        }
    }

    public bool IsListView => !IsGridView;

    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (!SetProperty(ref _showHiddenFiles, value))
            {
                return;
            }

            _ = RefreshCurrentDirectoryAsync();
        }
    }

    public bool ShowFileExtensions
    {
        get => _showFileExtensions;
        set
        {
            if (!SetProperty(ref _showFileExtensions, value))
            {
                return;
            }

            _ = RefreshCurrentDirectoryAsync();
        }
    }

    public bool IsViewMenuOpen
    {
        get => _isViewMenuOpen;
        set => SetProperty(ref _isViewMenuOpen, value);
    }

    public bool IsTypingPath
    {
        get => _isTypingPath;
        private set
        {
            if (SetProperty(ref _isTypingPath, value))
            {
                OnPropertyChanged(nameof(IsInlineSuggestionVisible));
            }
        }
    }

    public bool IsInlineSuggestionVisible => IsTypingPath && !string.IsNullOrEmpty(InlineSuggestionSuffix);

    public bool IsPathEditMode
    {
        get => _isPathEditMode;
        private set
        {
            if (SetProperty(ref _isPathEditMode, value))
            {
                OnPropertyChanged(nameof(IsBreadcrumbMode));
            }
        }
    }

    public bool IsBreadcrumbMode => !IsPathEditMode;

    public string SortDescription => _sortKind switch
    {
        FileExplorerSortKind.Type => "Type",
        FileExplorerSortKind.Size => "Size",
        FileExplorerSortKind.Modified => "Modified",
        _ => "Name"
    };

    public bool SortDescending
    {
        get => _sortDescending;
        private set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                OnPropertyChanged(nameof(SortDirectionLabel));
                ApplyItemsView();
            }
        }
    }

    public string SortDirectionLabel => SortDescending ? "Descending" : "Ascending";

    public int GridZoomLevel
    {
        get => _gridZoomLevel;
        private set
        {
            if (!SetProperty(ref _gridZoomLevel, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanZoomIn));
            OnPropertyChanged(nameof(CanZoomOut));
            OnPropertyChanged(nameof(GridCardWidth));
            OnPropertyChanged(nameof(GridCardHeight));
            OnPropertyChanged(nameof(GridCardIconSize));
            OnPropertyChanged(nameof(GridCardPreviewSize));
        }
    }

    public bool CanZoomIn => GridZoomLevel < 4;

    public bool CanZoomOut => GridZoomLevel > 0;

    public double GridCardWidth => 64 + (GridZoomLevel * 12);

    public double GridCardHeight => 84 + (GridZoomLevel * 14);

    public double GridCardIconSize => 18 + (GridZoomLevel * 4);

    public double GridCardPreviewSize => 34 + (GridZoomLevel * 6);

    public string ItemCountText => $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")}";

    public bool IsLoadingEmpty => IsLoading && Items.Count == 0;

    public bool IsLoadingEmptyList => IsLoadingEmpty && IsListView;

    public bool IsLoadingEmptyGrid => IsLoadingEmpty && IsGridView;

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public bool CanPasteIntoCurrentDirectory => !string.IsNullOrWhiteSpace(CurrentPath) && ClipboardHasFileDropContent();

    public FileExplorerTabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (!SetProperty(ref _activeTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasMultipleTabs));
        }
    }

    public bool HasMultipleTabs => Tabs.Count > 1;

    public bool HasActiveFileOperations => FileOperationTasks.Any(task => !task.IsCompleted);

    public bool HasAnyFileOperations => FileOperationTasks.Count > 0;

    public FileOperationTaskViewModel? PrimaryFileOperationTask => FileOperationTasks.FirstOrDefault(task => !task.IsCompleted) ?? FileOperationTasks.FirstOrDefault();

    public string ActiveFileOperationIndicatorGlyph => PrimaryFileOperationTask?.IndicatorGlyph ?? "\uE823";

    public string ActiveFileOperationSummary => PrimaryFileOperationTask is null
        ? "No active tasks"
        : $"{PrimaryFileOperationTask.Title} · {PrimaryFileOperationTask.SummaryText}";

    public bool CanGoUp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
            {
                return false;
            }

            try
            {
                return Directory.GetParent(CurrentPath) is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetPathTypingActive(bool isActive)
    {
        IsTypingPath = isActive && !string.IsNullOrWhiteSpace(PathInput);
    }

    public void BeginPathEditing()
    {
        PathInput = CurrentPath;
        IsPathEditMode = true;
        SetPathTypingActive(false);
        ClearInlineSuggestion();
    }

    public void EndPathEditing()
    {
        IsPathEditMode = false;
        SetPathTypingActive(false);
        ClearInlineSuggestion();
        PathInput = CurrentPath;
    }

    public void UseListView()
    {
        if (!IsLoading)
        {
            IsGridView = false;
        }
    }

    public void UseGridView()
    {
        if (!IsLoading)
        {
            IsGridView = true;
        }
    }

    public void ToggleHiddenFilesSetting()
    {
        if (!IsLoading)
        {
            ToggleHiddenFiles();
        }
    }

    public void ToggleFileExtensionsSetting()
    {
        if (!IsLoading)
        {
            ToggleFileExtensions();
        }
    }

    public async Task OpenInNewTabAsync(string? path)
    {
        await CreateNewTabAsync(path);
    }

    public async Task SwitchAdjacentTabAsync(int offset)
    {
        if (Tabs.Count <= 1 || ActiveTab is null || offset == 0)
        {
            return;
        }

        var currentIndex = Tabs.IndexOf(ActiveTab);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = (currentIndex + offset) % Tabs.Count;
        if (targetIndex < 0)
        {
            targetIndex += Tabs.Count;
        }

        await SwitchToTabAsync(Tabs[targetIndex]);
    }

    public void CloseActiveTab()
    {
        CloseTab(ActiveTab);
    }

    private void CreateInitialTab()
    {
        var initialPath = ResolvePath(null);
        var initialTab = CreateTab(initialPath);
        initialTab.IsActive = true;
        ActiveTab = initialTab;
        Tabs.Add(initialTab);
        SyncActiveTabState(initialPath);
        NotifyTabCommands();
    }

    private FileExplorerTabViewModel CreateTab(string path)
    {
        var tab = new FileExplorerTabViewModel(GetTabTitle(path), path);
        tab.History.Add(path);
        tab.HistoryIndex = 0;
        return tab;
    }

    private void EnsureActiveTab()
    {
        if (ActiveTab is not null)
        {
            return;
        }

        CreateInitialTab();
    }

    private async Task CreateNewTabAsync()
    {
        await CreateNewTabAsync(CurrentPath);
    }

    private async Task CreateNewTabAsync(string? path)
    {
        var resolvedPath = ResolvePath(path);
        SaveActiveTabState();

        var tab = CreateTab(resolvedPath);
        Tabs.Add(tab);
        await SwitchToTabAsync(tab);
    }

    private async Task SwitchToTabAsync(FileExplorerTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (ReferenceEquals(ActiveTab, tab) && !string.IsNullOrWhiteSpace(CurrentPath))
        {
            return;
        }

        SaveActiveTabState();

        foreach (var candidate in Tabs)
        {
            candidate.IsActive = ReferenceEquals(candidate, tab);
        }

        ActiveTab = tab;
        RestoreHistoryFromActiveTab();
        await OpenPathAsync(tab.Path, addToHistory: false);
        NotifyTabCommands();
    }

    private void CloseTab(FileExplorerTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (Tabs.Count == 1)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var tabIndex = Tabs.IndexOf(tab);
        var isClosingActiveTab = ReferenceEquals(ActiveTab, tab);

        if (isClosingActiveTab)
        {
            SaveActiveTabState();
        }

        Tabs.Remove(tab);

        if (!isClosingActiveTab)
        {
            OnPropertyChanged(nameof(HasMultipleTabs));
            NotifyTabCommands();
            return;
        }

        var replacementIndex = Math.Clamp(tabIndex - 1, 0, Tabs.Count - 1);
        var replacement = Tabs[replacementIndex];
        _ = SwitchToTabAsync(replacement);
    }

    private void SaveActiveTabState()
    {
        if (ActiveTab is null)
        {
            return;
        }

        ActiveTab.Path = string.IsNullOrWhiteSpace(CurrentPath) ? ActiveTab.Path : CurrentPath;
        ActiveTab.Title = GetTabTitle(ActiveTab.Path);
        ActiveTab.History.Clear();
        ActiveTab.History.AddRange(_history);
        ActiveTab.HistoryIndex = _historyIndex;
    }

    private void RestoreHistoryFromActiveTab()
    {
        if (ActiveTab is null)
        {
            _history = [];
            _historyIndex = -1;
            return;
        }

        _history = [.. ActiveTab.History];
        _historyIndex = ActiveTab.HistoryIndex;
        NotifyNavigationCommands();
    }

    private void SyncActiveTabState(string path)
    {
        if (ActiveTab is null)
        {
            return;
        }

        ActiveTab.Path = path;
        ActiveTab.Title = GetTabTitle(path);
        ActiveTab.History.Clear();
        ActiveTab.History.AddRange(_history);
        ActiveTab.HistoryIndex = _historyIndex;
        NotifyTabCommands();
    }

    private static string GetTabTitle(string path)
    {
        var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return path;
    }

    public async Task OpenPathAsync(string? path, bool addToHistory = true)
    {
        EnsureActiveTab();
        var resolvedPath = ResolvePath(path);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return;
        }

        IsLoading = true;
        StatusText = string.Empty;
        IsPathEditMode = false;
        SetPathTypingActive(false);
        ClearInlineSuggestion();
        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsLoadingEmpty));
        OnPropertyChanged(nameof(IsLoadingEmptyList));
        OnPropertyChanged(nameof(IsLoadingEmptyGrid));

        try
        {
            var entries = await Task.Run(() => LoadDirectoryItems(resolvedPath));
            _loadedItems.Clear();
            _loadedItems.AddRange(entries);
            ApplyItemsView();

            CurrentPath = resolvedPath;
            PathInput = resolvedPath;
            UpdateSidebarSelection(resolvedPath);
            RebuildBreadcrumbs(resolvedPath);
            StatusText = string.Empty;
            OnPropertyChanged(nameof(ItemCountText));
            OnPropertyChanged(nameof(IsLoadingEmpty));
            OnPropertyChanged(nameof(IsLoadingEmptyList));
            OnPropertyChanged(nameof(IsLoadingEmptyGrid));
            IsViewMenuOpen = false;
            NotifySidebarCommands();

            if (addToHistory)
            {
                PushHistory(resolvedPath);
            }

            SyncActiveTabState(resolvedPath);
        }
        catch (Exception exception)
        {
            StatusText = "This folder couldn't be opened.";
            _logService.Warn("Nebula file explorer failed to open a path.", new Dictionary<string, object?>
            {
                ["path"] = resolvedPath,
                ["error"] = exception.Message
            });
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsLoadingEmpty));
            OnPropertyChanged(nameof(IsLoadingEmptyList));
            OnPropertyChanged(nameof(IsLoadingEmptyGrid));
        }
    }

    public async Task OpenItemAsync(FileExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            await OpenPathAsync(item.FullPath);
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            StatusText = "That item couldn't be opened.";
            _logService.Warn("Nebula file explorer failed to open a file.", new Dictionary<string, object?>
            {
                ["path"] = item.FullPath,
                ["error"] = exception.Message
            });
        }
    }

    public void RevealInWindowsExplorer(FileExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var arguments = item.IsDirectory
                ? $"\"{item.FullPath}\""
                : $"/select,\"{item.FullPath}\"";

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = File.Exists(explorerPath) ? explorerPath : "explorer.exe",
                Arguments = arguments,
                UseShellExecute = false
            });
        }
        catch (Exception exception)
        {
            StatusText = "Windows Explorer couldn't reveal that item.";
            _logService.Warn("Nebula file explorer failed to reveal an item in Windows Explorer.", new Dictionary<string, object?>
            {
                ["path"] = item.FullPath,
                ["error"] = exception.Message
            });
        }
    }

    public void CopyItemPath(FileExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.FullPath);
            StatusText = "Path copied.";
        }
        catch (Exception exception)
        {
            StatusText = "That path couldn't be copied.";
            _logService.Warn("Nebula file explorer failed to copy an item path.", new Dictionary<string, object?>
            {
                ["path"] = item.FullPath,
                ["error"] = exception.Message
            });
        }
    }

    public void CopyItemToClipboard(FileExplorerItemViewModel? item)
    {
        SetFileClipboard(item, isCut: false);
    }

    public void CutItemToClipboard(FileExplorerItemViewModel? item)
    {
        SetFileClipboard(item, isCut: true);
    }

    public void BeginRename(FileExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        // Exit rename mode on any other items
        foreach (var i in Items)
        {
            if (!ReferenceEquals(i, item))
            {
                i.IsRenaming = false;
            }
        }

        item.RenameText = item.DisplayName;
        item.IsRenaming = true;
    }

    public void CancelRename(FileExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsRenaming = false;
        item.RenameText = string.Empty;
    }

    public async Task CommitRenameAsync(FileExplorerItemViewModel? item)
    {
        if (item is null || !item.IsRenaming || string.IsNullOrWhiteSpace(item.RenameText))
        {
            CancelRename(item);
            return;
        }

        var newName = item.RenameText.Trim();

        // Validate name hasn't changed
        if (string.Equals(newName, item.DisplayName, StringComparison.Ordinal))
        {
            CancelRename(item);
            return;
        }

        // Validate name for invalid characters
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText = "The name contains invalid characters.";
            return;
        }

        try
        {
            var oldPath = item.FullPath;
            var newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? CurrentPath, newName);

            // Check if target already exists
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                StatusText = "A file or folder with that name already exists.";
                return;
            }

            // Perform rename
            if (item.IsDirectory)
            {
                Directory.Move(oldPath, newPath);
            }
            else
            {
                File.Move(oldPath, newPath);
            }

            // Refresh the view
            await OpenPathAsync(CurrentPath, addToHistory: false);
            StatusText = $"Renamed to '{newName}'.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Access denied. You don't have permission to rename this item.";
        }
        catch (IOException ex)
        {
            StatusText = "The item couldn't be renamed due to a file system error.";
            _logService.Warn("Failed to rename item.", new Dictionary<string, object?>
            {
                ["oldName"] = item.DisplayName,
                ["newName"] = item.RenameText,
                ["error"] = ex.Message
            });
        }
        catch (Exception ex)
        {
            StatusText = "The item couldn't be renamed.";
            _logService.Warn("Failed to rename item with unexpected error.", new Dictionary<string, object?>
            {
                ["error"] = ex.Message
            });
        }
        finally
        {
            CancelRename(item);
        }
    }

    public async Task PasteIntoCurrentDirectoryAsync()
    {
        if (!TryGetClipboardFileOperation(out var paths, out var isCut))
        {
            StatusText = "Nothing to paste here.";
            return;
        }

        await TransferPathsAsync(paths, CurrentPath, isCut);
    }

    public async Task HandleDroppedPathsAsync(IReadOnlyList<string> sourcePaths, string destinationPath, bool preferMove)
    {
        await TransferPathsAsync(sourcePaths, destinationPath, preferMove);
    }

    public bool CanPinItemToSidebar(FileExplorerItemViewModel? item)
    {
        return item is { IsDirectory: true } && !IsSidebarPathPresent(item.FullPath);
    }

    public void PinItemToSidebar(FileExplorerItemViewModel? item)
    {
        if (!CanPinItemToSidebar(item))
        {
            return;
        }

        PinFolderPathsToSidebar([item!.FullPath]);
    }

    public void UnpinSidebarLocation(FileExplorerLocationViewModel? location)
    {
        if (location?.CanRemove != true)
        {
            return;
        }

        SidebarLocations.Remove(location);
        SaveSidebarState();
        StatusText = "Removed from sidebar.";
        NotifySidebarCommands();
    }

    public bool MoveSidebarLocation(FileExplorerLocationViewModel? source, FileExplorerLocationViewModel? target, bool insertAfter = false)
    {
        if (source is null || target is null || ReferenceEquals(source, target) || !source.CanReorder || !target.CanReorder)
        {
            return false;
        }

        var sourceIndex = SidebarLocations.IndexOf(source);
        var targetIndex = SidebarLocations.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        if (insertAfter)
        {
            targetIndex++;
        }

        if (targetIndex > sourceIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, SidebarLocations.Count - 1);
        SidebarLocations.Move(sourceIndex, targetIndex);
        SaveSidebarState();
        NotifySidebarCommands();
        return true;
    }

    public bool PinFolderPathsToSidebar(IReadOnlyList<string> folderPaths, int? insertIndex = null)
    {
        var addedAny = false;
        var targetIndex = insertIndex ?? GetSidebarPinInsertIndex();
        foreach (var folderPath in folderPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsSidebarPathPresent(folderPath))
            {
                continue;
            }

            var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = folderPath;
            }

            var entry = new FileExplorerLocationViewModel(
                CreateCustomSidebarId(folderPath),
                name,
                folderPath,
                "\uE8B7",
                iconSource: FileExplorerVisualResolver.GetVisual(folderPath, preferThumbnail: false, preferredSize: 48));

            if (targetIndex >= 0 && targetIndex <= SidebarLocations.Count)
            {
                SidebarLocations.Insert(targetIndex, entry);
                targetIndex++;
            }
            else
            {
                SidebarLocations.Add(entry);
            }

            addedAny = true;
        }

        if (!addedAny)
        {
            return false;
        }

        SaveSidebarState();
        UpdateSidebarSelection(CurrentPath);
        StatusText = "Pinned to sidebar.";
        NotifySidebarCommands();
        return true;
    }

    public bool TryAcceptInlineSuggestion()
    {
        if (string.IsNullOrEmpty(InlineSuggestionSuffix))
        {
            return false;
        }

        PathInput = InlineSuggestionPrefix + InlineSuggestionSuffix;
        SetPathTypingActive(false);
        return true;
    }

    private async Task NavigateToInputPathAsync()
    {
        SetPathTypingActive(false);
        await OpenPathAsync(PathInput);
    }

    private async Task NavigateToBreadcrumbAsync(FileExplorerPathSegmentViewModel? segment)
    {
        if (segment is null)
        {
            return;
        }

        if (segment.IsCurrent)
        {
            BeginPathEditing();
            return;
        }

        await OpenPathAsync(segment.FullPath);
    }

    private async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }

        _historyIndex--;
        await OpenPathAsync(_history[_historyIndex], addToHistory: false);
        NotifyNavigationCommands();
    }

    private async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }

        _historyIndex++;
        await OpenPathAsync(_history[_historyIndex], addToHistory: false);
        NotifyNavigationCommands();
    }

    private async Task GoUpAsync()
    {
        if (!CanGoUp)
        {
            return;
        }

        var parent = Directory.GetParent(CurrentPath);
        if (parent is null)
        {
            return;
        }

        await OpenPathAsync(parent.FullName);
    }

    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath))
        {
            return;
        }

        var baseName = "New Folder";
        var index = 0;
        string candidatePath;

        do
        {
            var folderName = index == 0 ? baseName : $"{baseName} {index + 1}";
            candidatePath = Path.Combine(CurrentPath, folderName);
            index++;
        } while (Directory.Exists(candidatePath));

        try
        {
            Directory.CreateDirectory(candidatePath);
            await OpenPathAsync(CurrentPath, addToHistory: false);
        }
        catch (Exception exception)
        {
            StatusText = "A new folder couldn't be created here.";
            _logService.Warn("Nebula file explorer failed to create a folder.", new Dictionary<string, object?>
            {
                ["path"] = CurrentPath,
                ["error"] = exception.Message
            });
        }
    }

    private void ToggleHiddenFiles()
    {
        ShowHiddenFiles = !ShowHiddenFiles;
    }

    private void ToggleFileExtensions()
    {
        ShowFileExtensions = !ShowFileExtensions;
    }

    private async Task RefreshCurrentDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) || IsLoading)
        {
            return;
        }

        await OpenPathAsync(CurrentPath, addToHistory: false);
    }

    private void SetSortKind(FileExplorerSortKind sortKind)
    {
        if (_sortKind == sortKind)
        {
            return;
        }

        _sortKind = sortKind;
        OnPropertyChanged(nameof(SortDescription));
        ApplyItemsView();
    }

    private void ToggleSortDirection()
    {
        SortDescending = !SortDescending;
    }

    private void AdjustGridZoom(int delta)
    {
        var nextZoomLevel = Math.Clamp(GridZoomLevel + delta, 0, 4);
        if (nextZoomLevel == GridZoomLevel)
        {
            return;
        }

        GridZoomLevel = nextZoomLevel;
        NotifyViewCommands();
    }

    private void PopulateSidebar()
    {
        SidebarLocations.Clear();

        var persistedEntries = _sidebarStateService.LoadEntries();
        var builtInsById = BuiltInSidebarLocations.ToDictionary(location => location.Id, StringComparer.OrdinalIgnoreCase);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (persistedEntries.Count == 0)
        {
            foreach (var location in BuiltInSidebarLocations)
            {
                AddBuiltInSidebarLocation(location, usedPaths);
            }
        }
        else
        {
            foreach (var entry in persistedEntries)
            {
                if (entry.IsBuiltIn)
                {
                    if (builtInsById.TryGetValue(entry.Id, out var builtIn))
                    {
                        AddBuiltInSidebarLocation(builtIn, usedPaths);
                    }

                    continue;
                }

                AddCustomSidebarLocation(entry.Path, entry.CustomDisplayName, usedPaths);
            }

            foreach (var location in BuiltInSidebarLocations.Where(location => !usedPaths.Contains(location.ResolvePath())))
            {
                AddBuiltInSidebarLocation(location, usedPaths);
            }
        }

        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (drives.Count == 0)
        {
            return;
        }

        SidebarLocations.Add(new FileExplorerLocationViewModel(
            "drives-separator",
            string.Empty,
            string.Empty,
            string.Empty,
            isSeparator: true,
            sectionTitle: "Drives"));

        foreach (var drive in drives)
        {
            var driveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
            var displayName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? driveName
                : $"{driveName}  {drive.VolumeLabel}";

            SidebarLocations.Add(new FileExplorerLocationViewModel(
                $"drive:{drive.RootDirectory.FullName}",
                displayName,
                drive.RootDirectory.FullName,
                "\uEDA2",
                iconSource: FileExplorerVisualResolver.GetDriveVisual(drive.RootDirectory.FullName),
                isDrive: true));
        }

        NotifySidebarCommands();
    }

    private void AddBuiltInSidebarLocation(BuiltInSidebarLocation location, HashSet<string> usedPaths)
    {
        var (path, displayName) = ResolveBuiltInSidebarLocation(location);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        if (!usedPaths.Add(path))
        {
            return;
        }

        SidebarLocations.Add(new FileExplorerLocationViewModel(
            location.Id,
            displayName,
            path,
            location.Glyph,
            isBuiltIn: true));
    }

    private void AddCustomSidebarLocation(string path, string? displayName, HashSet<string> usedPaths)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !usedPaths.Add(path))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : displayName;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        SidebarLocations.Add(new FileExplorerLocationViewModel(
            CreateCustomSidebarId(path),
            name,
            path,
            "\uE8B7",
            iconSource: FileExplorerVisualResolver.GetVisual(path, preferThumbnail: false, preferredSize: 48)));
    }

    private IReadOnlyList<FileExplorerItemViewModel> LoadDirectoryItems(string path)
    {
        var items = new List<FileExplorerItemViewModel>();
        var directoryInfo = new DirectoryInfo(path);

        IEnumerable<DirectoryInfo> directories;
        try
        {
            directories = directoryInfo.EnumerateDirectories("*", DirectoryEnumerationOptions);
        }
        catch (Exception exception)
        {
            throw new IOException($"Failed to enumerate directories in {path}.", exception);
        }

        foreach (var directory in directories
                     .Where(ShouldInclude)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(CreateDirectoryItem(directory));
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = directoryInfo.EnumerateFiles("*", DirectoryEnumerationOptions);
        }
        catch (Exception exception)
        {
            throw new IOException($"Failed to enumerate files in {path}.", exception);
        }

        foreach (var file in files
                     .Where(ShouldInclude)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(CreateFileItem(file));
        }

        return items;
    }

    private FileExplorerItemViewModel CreateDirectoryItem(DirectoryInfo directory)
    {
        return new FileExplorerItemViewModel(
            directory.Name,
            directory.Name,
            directory.FullName,
            true,
            "Folder",
            string.Empty,
            FormatTimestamp(directory.LastWriteTime),
            "\uE8B7",
            0,
            directory.LastWriteTime,
            "Folder");
    }

    private FileExplorerItemViewModel CreateFileItem(FileInfo file)
    {
        var displayName = ShowFileExtensions
            ? file.Name
            : GetNameWithoutExtension(file.Name);

        var item = new FileExplorerItemViewModel(
            file.Name,
            displayName,
            file.FullName,
            false,
            string.IsNullOrWhiteSpace(file.Extension) ? "File" : $"{file.Extension.Trim('.').ToUpperInvariant()} file",
            FormatSize(file.Length),
            FormatTimestamp(file.LastWriteTime),
            "\uE8A5",
            file.Length,
            file.LastWriteTime,
            string.IsNullOrWhiteSpace(file.Extension) ? "File" : file.Extension.Trim('.').ToUpperInvariant())
        {
            IconSource = FileExplorerVisualResolver.GetVisual(file.FullName, preferThumbnail: true, preferredSize: 64)
        };

        return item;
    }

    private bool ShouldInclude(FileSystemInfo entry)
    {
        return ShowHiddenFiles || !entry.Attributes.HasFlag(FileAttributes.Hidden);
    }

    private void ApplyItemsView()
    {
        var filteredItems = string.IsNullOrWhiteSpace(SearchQuery)
            ? _loadedItems
            : _loadedItems
                .Where(item => item.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                               || item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                               || item.TypeLabel.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

        IOrderedEnumerable<FileExplorerItemViewModel> orderedItems = filteredItems
            .OrderBy(item => item.IsDirectory ? 0 : 1)
            .ThenBy(item => 0);

        orderedItems = _sortKind switch
        {
            FileExplorerSortKind.Type => SortDescending
                ? orderedItems.ThenByDescending(item => item.SortType, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : orderedItems.ThenBy(item => item.SortType, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            FileExplorerSortKind.Size => SortDescending
                ? orderedItems.ThenByDescending(item => item.SortSize).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : orderedItems.ThenBy(item => item.SortSize).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            FileExplorerSortKind.Modified => SortDescending
                ? orderedItems.ThenByDescending(item => item.SortModifiedAt).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : orderedItems.ThenBy(item => item.SortModifiedAt).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => SortDescending
                ? orderedItems.ThenByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : orderedItems.ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        Items.Clear();
        foreach (var item in orderedItems)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsLoadingEmpty));
        OnPropertyChanged(nameof(IsLoadingEmptyList));
        OnPropertyChanged(nameof(IsLoadingEmptyGrid));
    }

    private void PushHistory(string path)
    {
        if (_historyIndex >= 0 && string.Equals(_history[_historyIndex], path, StringComparison.OrdinalIgnoreCase))
        {
            NotifyNavigationCommands();
            SyncActiveTabState(path);
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(path);
        _historyIndex = _history.Count - 1;
        NotifyNavigationCommands();
        SyncActiveTabState(path);
    }

    private void NotifyNavigationCommands()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        (BackCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ForwardCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (UpCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (RefreshCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (NewFolderCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (NavigateToInputPathCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (OpenLocationCommand as AsyncRelayCommand<FileExplorerLocationViewModel>)?.NotifyCanExecuteChanged();
        (OpenItemCommand as AsyncRelayCommand<FileExplorerItemViewModel>)?.NotifyCanExecuteChanged();
        (NavigateToBreadcrumbCommand as AsyncRelayCommand<FileExplorerPathSegmentViewModel>)?.NotifyCanExecuteChanged();
    }

    private void NotifyViewCommands()
    {
        (SetListViewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SetGridViewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleHiddenFilesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleFileExtensionsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortByNameCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortByTypeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortBySizeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortByModifiedCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleSortDirectionCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ZoomInCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ZoomOutCommand as RelayCommand)?.NotifyCanExecuteChanged();
        NotifySidebarCommands();
        NotifyTabCommands();
    }

    private void NotifyTabCommands()
    {
        OnPropertyChanged(nameof(HasMultipleTabs));
        (NewTabCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (SelectTabCommand as AsyncRelayCommand<FileExplorerTabViewModel>)?.NotifyCanExecuteChanged();
        (CloseTabCommand as RelayCommand<FileExplorerTabViewModel>)?.NotifyCanExecuteChanged();
    }

    private void NotifySidebarCommands()
    {
        (PasteIntoCurrentDirectoryCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (PinItemToSidebarCommand as RelayCommand<FileExplorerItemViewModel>)?.NotifyCanExecuteChanged();
        (UnpinSidebarLocationCommand as RelayCommand<FileExplorerLocationViewModel>)?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasActiveFileOperations));
        OnPropertyChanged(nameof(HasAnyFileOperations));
        OnPropertyChanged(nameof(PrimaryFileOperationTask));
        OnPropertyChanged(nameof(ActiveFileOperationIndicatorGlyph));
        OnPropertyChanged(nameof(ActiveFileOperationSummary));
    }

    public void ClearSidebarDropIndicators()
    {
        foreach (var location in SidebarLocations.Where(location => !location.IsSeparator))
        {
            location.ShowDropBefore = false;
            location.ShowDropAfter = false;
            location.ShowDropInside = false;
        }
    }

    public void SetSidebarDropIndicator(FileExplorerLocationViewModel target, bool showBefore, bool showAfter, bool showInside)
    {
        foreach (var location in SidebarLocations.Where(location => !location.IsSeparator))
        {
            var isTarget = ReferenceEquals(location, target);
            location.ShowDropBefore = isTarget && showBefore;
            location.ShowDropAfter = isTarget && showAfter;
            location.ShowDropInside = isTarget && showInside;
        }
    }

    public int GetSidebarInsertIndexForTarget(FileExplorerLocationViewModel? target, bool insertAfter)
    {
        if (target is null)
        {
            return GetSidebarPinInsertIndex();
        }

        var targetIndex = SidebarLocations.IndexOf(target);
        if (targetIndex < 0)
        {
            return GetSidebarPinInsertIndex();
        }

        return insertAfter ? targetIndex + 1 : targetIndex;
    }

    private static string ResolvePath(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : path.Trim();

        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.GetFullPath(candidate);
        }

        return Directory.Exists(candidate)
            ? candidate
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void UpdatePathSuggestion()
    {
        var input = PathInput;
        if (string.IsNullOrWhiteSpace(input))
        {
            ClearInlineSuggestion();
            return;
        }

        var suggestion = FindPathSuggestion(input);
        if (string.IsNullOrWhiteSpace(suggestion)
            || suggestion.Length <= input.Length
            || !suggestion.StartsWith(input, StringComparison.OrdinalIgnoreCase))
        {
            ClearInlineSuggestion();
            return;
        }

        InlineSuggestionPrefix = input;
        InlineSuggestionSuffix = suggestion[input.Length..];
    }

    private string? FindPathSuggestion(string input)
    {
        try
        {
            var trimmedInput = input.Trim();
            if (trimmedInput.Length == 0)
            {
                return null;
            }

            var currentBase = string.IsNullOrWhiteSpace(CurrentPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : CurrentPath;

            var indexedSuggestion = _indexService.TryGetPathSuggestion(trimmedInput, currentBase);
            if (!string.IsNullOrWhiteSpace(indexedSuggestion))
            {
                return indexedSuggestion;
            }

            if (TryResolvePathSuggestion(trimmedInput, currentBase, out var directSuggestion))
            {
                return directSuggestion;
            }

            foreach (var location in SidebarLocations.Where(location => !location.IsSeparator))
            {
                if (location.Path.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
                {
                    return location.Path;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }

        return null;
    }

    private static bool TryResolvePathSuggestion(string input, string currentBase, out string suggestion)
    {
        suggestion = string.Empty;

        string expandedInput;
        try
        {
            expandedInput = Path.IsPathRooted(input)
                ? input
                : Path.GetFullPath(Path.Combine(currentBase, input));
        }
        catch
        {
            return false;
        }

        var normalizedInput = expandedInput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentPath = Path.GetDirectoryName(expandedInput);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return false;
        }

        var leaf = Path.GetFileName(normalizedInput);
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return false;
        }

        if (!Directory.Exists(parentPath))
        {
            return false;
        }

        var candidates = Directory.EnumerateFileSystemEntries(parentPath)
            .Select(Path.GetFullPath)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var candidateLeaf = Path.GetFileName(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (candidateLeaf.StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
            {
                suggestion = candidate;
                return true;
            }
        }

        return false;
    }

    private void ClearInlineSuggestion()
    {
        InlineSuggestionPrefix = string.Empty;
        InlineSuggestionSuffix = string.Empty;
    }

    private void UpdateSidebarSelection(string resolvedPath)
    {
        foreach (var location in SidebarLocations.Where(location => !location.IsSeparator))
        {
            location.IsActive = string.Equals(location.Path, resolvedPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RebuildBreadcrumbs(string resolvedPath)
    {
        BreadcrumbSegments.Clear();

        var root = Path.GetPathRoot(resolvedPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = resolvedPath
            .Substring(root.Length)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var currentPath = normalizedRoot + Path.DirectorySeparatorChar;
        var segmentPaths = new List<(string DisplayName, string FullPath)>
        {
            (normalizedRoot, currentPath)
        };

        foreach (var part in parts)
        {
            currentPath = Path.Combine(currentPath, part);
            segmentPaths.Add((part, currentPath));
        }

        for (var index = 0; index < segmentPaths.Count; index++)
        {
            var segment = segmentPaths[index];
            var isCurrent = index == segmentPaths.Count - 1;
            BreadcrumbSegments.Add(new FileExplorerPathSegmentViewModel(
                segment.DisplayName,
                segment.FullPath,
                isCurrent,
                showSeparator: !isCurrent));
        }
    }

    private static string GetNameWithoutExtension(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension) ? fileName : withoutExtension;
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        return timestamp.ToString("MMM d, yyyy HH:mm");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{size} {units[unitIndex]}";
        }

        var divisor = Math.Pow(1024, unitIndex);
        return $"{bytes / divisor:0.#} {units[unitIndex]}";
    }

    private void SetFileClipboard(FileExplorerItemViewModel? item, bool isCut)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            // Clear cut flag on all items
            foreach (var clipboardItem in Items)
            {
                clipboardItem.IsCut = false;
            }

            var fileDropList = new StringCollection
            {
                item.FullPath
            };

            var dataObject = new DataObject();
            dataObject.SetFileDropList(fileDropList);
            dataObject.SetData(PreferredDropEffectFormat, CreateDropEffectStream(isCut ? DragDropEffects.Move : DragDropEffects.Copy));
            Clipboard.SetDataObject(dataObject, true);

            _clipboardPaths = [item.FullPath];
            _clipboardIsCut = isCut;
            
            // Mark item as cut if this is a cut operation
            if (isCut)
            {
                item.IsCut = true;
            }
            
            StatusText = isCut ? "Ready to move." : "Ready to copy.";
            NotifySidebarCommands();
        }
        catch (Exception exception)
        {
            StatusText = "That item couldn't be copied.";
            _logService.Warn("Nebula file explorer failed to place an item on the clipboard.", new Dictionary<string, object?>
            {
                ["path"] = item.FullPath,
                ["cut"] = isCut,
                ["error"] = exception.Message
            });
        }
    }

    private async Task TransferPathsAsync(IReadOnlyList<string> sourcePaths, string destinationPath, bool preferMove)
    {
        if (sourcePaths.Count == 0 || string.IsNullOrWhiteSpace(destinationPath) || !Directory.Exists(destinationPath))
        {
            return;
        }

        IsLoading = true;
        var operationVerb = preferMove ? "Moving" : "Copying";
        var operationTask = new FileOperationTaskViewModel($"{operationVerb} files")
        {
            StatusText = "Preparing..."
        };
        var notificationId = Guid.NewGuid();
        FileOperationTasks.Insert(0, operationTask);
        TrimCompletedFileTasks();

        _notificationService.Push(
            operationTask.Title,
            $"Preparing {sourcePaths.Count} item{(sourcePaths.Count == 1 ? string.Empty : "s")}...",
            kind: NotificationKind.Info,
            source: "Files",
            showToast: false,
            notificationId: notificationId,
            progressFraction: 0d);

        try
        {
            var progress = new Progress<FileOperationProgress>(update =>
            {
                operationTask.Title = update.Title;
                operationTask.StatusText = update.StatusText;
                operationTask.TotalUnits = update.TotalUnits;
                operationTask.CompletedUnits = update.CompletedUnits;
                StatusText = update.StatusText;
                _notificationService.Update(
                    notificationId,
                    title: update.Title,
                    message: update.StatusText,
                    progressFraction: update.TotalUnits <= 0 ? 0d : Math.Clamp((double)update.CompletedUnits / update.TotalUnits, 0d, 1d));
            });

            await Task.Run(() => TransferPathsCore(sourcePaths, destinationPath, preferMove, progress));
            
            // Clear cut indicators after successful transfer
            if (preferMove && _clipboardIsCut)
            {
                foreach (var item in Items)
                {
                    item.IsCut = false;
                }
                _clipboardIsCut = false;
                _clipboardPaths = [];
            }
            
            await OpenPathAsync(CurrentPath, addToHistory: false);
            operationTask.CompletedUnits = operationTask.TotalUnits;
            operationTask.StatusText = preferMove ? "Move complete." : "Copy complete.";
            operationTask.IsCompleted = true;
            StatusText = preferMove ? "Moved successfully." : "Copied successfully.";

            _notificationService.Update(
                notificationId,
                title: operationTask.Title,
                message: StatusText,
                progressFraction: 1d,
                isCompleted: true,
                hasError: false);
        }
        catch (Exception exception)
        {
            StatusText = "That file operation couldn't be completed.";
            operationTask.StatusText = StatusText;
            operationTask.HasError = true;
            operationTask.IsCompleted = true;
            _logService.Warn("Nebula file explorer failed to transfer files.", new Dictionary<string, object?>
            {
                ["destinationPath"] = destinationPath,
                ["preferMove"] = preferMove,
                ["error"] = exception.Message
            });

            _notificationService.Update(
                notificationId,
                title: operationTask.Title,
                message: exception.Message,
                progressFraction: operationTask.ProgressFraction,
                isCompleted: true,
                hasError: true);
        }
        finally
        {
            IsLoading = false;
            NotifySidebarCommands();
        }
    }

    private void TransferPathsCore(IReadOnlyList<string> sourcePaths, string destinationPath, bool preferMove, IProgress<FileOperationProgress>? progress)
    {
        var normalizedDestinationPath = destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var totalUnits = CountTransferUnits(sourcePaths);
        var completedUnits = 0;
        progress?.Report(new FileOperationProgress(
            preferMove ? "Moving files" : "Copying files",
            "Preparing transfer...",
            completedUnits,
            totalUnits));

        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                continue;
            }

            var sourceParent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(sourceParent, normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetPath = GetAvailableDestinationPath(Path.Combine(destinationPath, sourceName), Directory.Exists(sourcePath));
            if (string.Equals(
                    sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(sourcePath))
            {
                if (normalizedDestinationPath.StartsWith(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (preferMove)
                {
                    MoveDirectory(sourcePath, targetPath, ref completedUnits, totalUnits, progress);
                }
                else
                {
                    CopyDirectory(sourcePath, targetPath, ref completedUnits, totalUnits, progress);
                }

                continue;
            }

            if (preferMove)
            {
                MoveFile(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: false);
            }

            completedUnits++;
            progress?.Report(CreateTransferProgress(preferMove, sourceName, completedUnits, totalUnits));
        }
    }

    private static void MoveFile(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        try
        {
            File.Move(sourcePath, targetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
            File.Delete(sourcePath);
        }
    }

    private static void MoveDirectory(string sourcePath, string targetPath, ref int completedUnits, int totalUnits, IProgress<FileOperationProgress>? progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        try
        {
            Directory.Move(sourcePath, targetPath);
            completedUnits += CountTransferUnits([targetPath]);
            progress?.Report(CreateTransferProgress(true, Path.GetFileName(targetPath), completedUnits, totalUnits));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CopyDirectory(sourcePath, targetPath, ref completedUnits, totalUnits, progress);
            Directory.Delete(sourcePath, recursive: true);
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath, ref int completedUnits, int totalUnits, IProgress<FileOperationProgress>? progress)
    {
        Directory.CreateDirectory(targetPath);
        completedUnits++;
        progress?.Report(CreateTransferProgress(false, Path.GetFileName(targetPath), completedUnits, totalUnits));

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var destinationDirectory = directory.Replace(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(destinationDirectory);
            completedUnits++;
            progress?.Report(CreateTransferProgress(false, Path.GetFileName(destinationDirectory), completedUnits, totalUnits));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var destinationFilePath = file.Replace(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.Copy(file, destinationFilePath, overwrite: false);
            completedUnits++;
            progress?.Report(CreateTransferProgress(false, Path.GetFileName(destinationFilePath), completedUnits, totalUnits));
        }
    }

    private static string GetAvailableDestinationPath(string path, bool isDirectory)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = isDirectory ? string.Empty : Path.GetExtension(path);
        var baseName = isDirectory
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(path);

        for (var index = 2; index < 500; index++)
        {
            var candidate = Path.Combine(parent, $"{baseName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(parent, $"{baseName}-{Guid.NewGuid():N}{extension}");
    }

    private static int CountTransferUnits(IReadOnlyList<string> sourcePaths)
    {
        var total = 0;
        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(sourcePath))
            {
                total++;
                continue;
            }

            if (!Directory.Exists(sourcePath))
            {
                continue;
            }

            total++;

            try
            {
                total += Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories).Count();
            }
            catch
            {
            }

            try
            {
                total += Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).Count();
            }
            catch
            {
            }
        }

        return Math.Max(total, 1);
    }

    private static FileOperationProgress CreateTransferProgress(bool preferMove, string itemName, int completedUnits, int totalUnits)
    {
        var trimmedName = string.IsNullOrWhiteSpace(itemName) ? "item" : itemName;
        return new FileOperationProgress(
            preferMove ? "Moving files" : "Copying files",
            $"{(preferMove ? "Moving" : "Copying")} {trimmedName}",
            completedUnits,
            totalUnits);
    }

    private void TrimCompletedFileTasks()
    {
        while (FileOperationTasks.Count > 4)
        {
            var removable = FileOperationTasks.LastOrDefault(task => task.IsCompleted) ?? FileOperationTasks.Last();
            FileOperationTasks.Remove(removable);
        }
    }

    private bool TryGetClipboardFileOperation(out IReadOnlyList<string> paths, out bool isCut)
    {
        paths = [];
        isCut = false;

        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            var fileDropList = Clipboard.GetFileDropList();
            paths = fileDropList.Cast<string>()
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                return false;
            }

            var dataObject = Clipboard.GetDataObject();
            if (dataObject?.GetData(PreferredDropEffectFormat) is MemoryStream stream && stream.Length >= 4)
            {
                var buffer = new byte[4];
                stream.Position = 0;
                stream.ReadExactly(buffer, 0, 4);
                var effect = (DragDropEffects)BitConverter.ToInt32(buffer, 0);
                isCut = effect.HasFlag(DragDropEffects.Move);
            }
            else if (_clipboardPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
            {
                isCut = _clipboardIsCut;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ClipboardHasFileDropContent()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch
        {
            return _clipboardPaths.Count > 0;
        }
    }

    private bool IsSidebarPathPresent(string path)
    {
        return SidebarLocations.Any(location =>
            !location.IsSeparator &&
            string.Equals(location.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private int GetSidebarPinInsertIndex()
    {
        var driveSeparatorIndex = SidebarLocations
            .Select((location, index) => (location, index))
            .FirstOrDefault(pair => pair.location.IsSeparator && string.Equals(pair.location.SectionTitle, "Drives", StringComparison.OrdinalIgnoreCase))
            .index;

        return driveSeparatorIndex > 0 ? driveSeparatorIndex : SidebarLocations.Count;
    }

    private void OnFileOperationTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var oldItem in e.OldItems.OfType<FileOperationTaskViewModel>())
            {
                oldItem.PropertyChanged -= OnFileOperationTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newItem in e.NewItems.OfType<FileOperationTaskViewModel>())
            {
                newItem.PropertyChanged += OnFileOperationTaskPropertyChanged;
            }
        }

        NotifySidebarCommands();
    }

    private void OnFileOperationTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileOperationTaskViewModel.StatusText)
            or nameof(FileOperationTaskViewModel.CompletedUnits)
            or nameof(FileOperationTaskViewModel.TotalUnits)
            or nameof(FileOperationTaskViewModel.IsCompleted)
            or nameof(FileOperationTaskViewModel.HasError))
        {
            if (sender is FileOperationTaskViewModel task
                && task.IsCompleted
                && _pendingTaskRemovals.Add(task.Id))
            {
                _ = RemoveCompletedTaskAfterDelayAsync(task);
            }

            NotifySidebarCommands();
        }
    }

    private async Task RemoveCompletedTaskAfterDelayAsync(FileOperationTaskViewModel task)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            FileOperationTasks.Remove(task);
        }
        finally
        {
            _pendingTaskRemovals.Remove(task.Id);
            NotifySidebarCommands();
        }
    }

    private void SaveSidebarState()
    {
        var entries = SidebarLocations
            .Where(location => !location.IsSeparator && !location.IsDrive)
            .Select(location => new FileExplorerSidebarEntry
            {
                Id = location.Id,
                Path = location.Path,
                IsBuiltIn = location.IsBuiltIn,
                CustomDisplayName = location.IsBuiltIn ? null : location.DisplayName
            })
            .ToList();

        _sidebarStateService.SaveEntries(entries);
    }

    private static string CreateCustomSidebarId(string path)
    {
        return $"custom:{path.Trim().ToLowerInvariant()}";
    }

    private static (string Path, string DisplayName) ResolveBuiltInSidebarLocation(BuiltInSidebarLocation location)
    {
        var defaultPath = location.ResolvePath();
        if (string.IsNullOrWhiteSpace(defaultPath))
        {
            return (defaultPath, location.DisplayName);
        }

        if (!OneDriveFolderNames.TryGetValue(location.Id, out var folderName))
        {
            return (defaultPath, location.DisplayName);
        }

        foreach (var oneDriveRoot in GetOneDriveRoots())
        {
            var redirectedPath = Path.Combine(oneDriveRoot, folderName);
            var usesOneDriveAlready = defaultPath.StartsWith(oneDriveRoot, StringComparison.OrdinalIgnoreCase);
            if (!usesOneDriveAlready && !Directory.Exists(redirectedPath))
            {
                continue;
            }

            var resolvedPath = usesOneDriveAlready ? defaultPath : redirectedPath;
            if (!Directory.Exists(resolvedPath))
            {
                continue;
            }

            return (resolvedPath, location.DisplayName);
        }

        return (defaultPath, location.DisplayName);
    }

    private static IReadOnlyList<string> GetOneDriveRoots()
    {
        return OneDriveEnvironmentVariableNames
            .Select(Environment.GetEnvironmentVariable)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    private static MemoryStream CreateDropEffectStream(DragDropEffects effect)
    {
        return new MemoryStream(BitConverter.GetBytes((int)effect));
    }

    private sealed record FileOperationProgress(string Title, string StatusText, int CompletedUnits, int TotalUnits);

    private sealed record BuiltInSidebarLocation(string Id, string DisplayName, Func<string> PathFactory, string Glyph)
    {
        public string ResolvePath() => PathFactory();
    }

    private enum FileExplorerSortKind
    {
        Name,
        Type,
        Size,
        Modified
    }
}
