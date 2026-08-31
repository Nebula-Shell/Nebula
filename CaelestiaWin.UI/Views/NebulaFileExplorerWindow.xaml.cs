using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.UI.ViewModels;

namespace CaelestiaWin.UI.Views;

public partial class NebulaFileExplorerWindow : Window
{
    private readonly IFileShellContextMenuService _fileShellContextMenuService;
    private readonly NebulaFileExplorerViewModel _viewModel;
    private readonly DispatcherTimer _pathTypingTimer;
    private FileExplorerItemViewModel? _pendingContextMenuItem;
    private Point _fileDragStartPoint;
    private Point _sidebarDragStartPoint;
    private FileExplorerLocationViewModel? _sidebarDragLocation;
    private Popup? _dragGhostPopup;

    public NebulaFileExplorerWindow(
        NebulaFileExplorerViewModel viewModel,
        IFileShellContextMenuService fileShellContextMenuService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _fileShellContextMenuService = fileShellContextMenuService;
        _pathTypingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _pathTypingTimer.Tick += OnPathTypingTimerTick;
        _viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
        Activated += OnActivated;
    }

    public Task OpenPathAsync(string? path)
    {
        return _viewModel.OpenPathAsync(path);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Hide();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            PathTextBox.Focus();
            PathTextBox.CaretIndex = PathTextBox.Text.Length;
        }));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current?.Dispatcher.HasShutdownStarted == true)
        {
            return;
        }

        _pathTypingTimer.Stop();
        e.Cancel = true;
        Hide();
    }

    private void DragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PathTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Tab || e.Key == Key.Right) && _viewModel.TryAcceptInlineSuggestion())
        {
            StopPathTyping();
            PathTextBox.CaretIndex = PathTextBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_viewModel.NavigateToInputPathCommand.CanExecute(null))
        {
            StopPathTyping();
            _viewModel.NavigateToInputPathCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PathTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        StartPathTyping();
    }

    private void PathTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete
            || (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            StartPathTyping();
        }
    }

    private void PathTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        StopPathTyping();
        _viewModel.EndPathEditing();
    }

    private void ViewMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        var contextMenu = BuildViewMenu();
        contextMenu.PlacementTarget = ViewMenuButton;
        contextMenu.Placement = PlacementMode.Bottom;
        contextMenu.Closed += (_, _) => ViewMenuButton.ClearValue(ContextMenuProperty);
        ViewMenuButton.ContextMenu = contextMenu;
        Dispatcher.BeginInvoke(() => contextMenu.IsOpen = true, DispatcherPriority.Input);
        e.Handled = true;
    }

    private void PathChrome_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsPathEditMode
            || e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        BeginPathEditingAndFocus();
        e.Handled = true;
    }

    private void PathSegmentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FileExplorerPathSegmentViewModel segment })
        {
            return;
        }

        if (segment.IsCurrent)
        {
            BeginPathEditingAndFocus();
            e.Handled = true;
            return;
        }

        if (_viewModel.NavigateToBreadcrumbCommand.CanExecute(segment))
        {
            _viewModel.NavigateToBreadcrumbCommand.Execute(segment);
            e.Handled = true;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.T)
        {
            if (_viewModel.NewTabCommand.CanExecute(null))
            {
                _viewModel.NewTabCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.W)
        {
            _viewModel.CloseActiveTab();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.Tab)
        {
            _ = _viewModel.SwitchAdjacentTabAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        // Zoom shortcuts: Ctrl+Plus/Minus
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            if (_viewModel.ZoomInCommand.CanExecute(null))
            {
                _viewModel.ZoomInCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            if (_viewModel.ZoomOutCommand.CanExecute(null))
            {
                _viewModel.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        // Go up folder: Ctrl+Backspace
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Back)
        {
            if (_viewModel.UpCommand.CanExecute(null))
            {
                _viewModel.UpCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        var selectedItem = GetSelectedFileItem();
        
        // Rename: F2
        if (e.Key == Key.F2 && selectedItem is not null)
        {
            BeginRenameEdit(selectedItem);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C && selectedItem is not null)
        {
            _viewModel.CopyItemToClipboard(selectedItem);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X && selectedItem is not null)
        {
            _viewModel.CutItemToClipboard(selectedItem);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            if (_viewModel.PasteIntoCurrentDirectoryCommand.CanExecute(null))
            {
                _viewModel.PasteIntoCurrentDirectoryCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }
    }

    private void RenameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not FileExplorerItemViewModel item)
        {
            return;
        }

        if (e.Key == Key.Return)
        {
            if (_viewModel.CommitRenameCommand.CanExecute(item))
            {
                _viewModel.CommitRenameCommand.Execute(item);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_viewModel.CancelRenameCommand.CanExecute(item))
            {
                _viewModel.CancelRenameCommand.Execute(item);
                e.Handled = true;
            }

            return;
        }
    }

    private void FileListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not FileExplorerItemViewModel item)
        {
            return;
        }

        if (_viewModel.OpenItemCommand.CanExecute(item))
        {
            _viewModel.OpenItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void FileListView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragStartPoint = e.GetPosition(FileListView);
    }

    private async void FileListView_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        var item = SelectItemUnderPointer<ListViewItem>(FileListView, e);
        if (item?.IsDirectory != true)
        {
            return;
        }

        await _viewModel.OpenInNewTabAsync(item.FullPath);
        e.Handled = true;
    }

    private void FileGridView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragStartPoint = e.GetPosition(FileGridView);
    }

    private async void FileGridView_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        var item = SelectItemUnderPointer<ListBoxItem>(FileGridView, e);
        if (item?.IsDirectory != true)
        {
            return;
        }

        await _viewModel.OpenInNewTabAsync(item.FullPath);
        e.Handled = true;
    }

    private void FileListView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        TryBeginFileDrag(FileListView, e);
    }

    private void FileGridView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        TryBeginFileDrag(FileGridView, e);
    }

    private void FileGridView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileGridView.SelectedItem is not FileExplorerItemViewModel item)
        {
            return;
        }

        if (_viewModel.OpenItemCommand.CanExecute(item))
        {
            _viewModel.OpenItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void StartPathTyping()
    {
        _viewModel.SetPathTypingActive(true);
        _pathTypingTimer.Stop();
        _pathTypingTimer.Start();
    }

    private void StopPathTyping()
    {
        _pathTypingTimer.Stop();
        _viewModel.SetPathTypingActive(false);
    }

    private void OnPathTypingTimerTick(object? sender, EventArgs e)
    {
        StopPathTyping();
    }

    private void FileListView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingContextMenuItem = SelectItemUnderPointer<ListViewItem>(FileListView, e);
    }

    private void FileGridView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingContextMenuItem = SelectItemUnderPointer<ListBoxItem>(FileGridView, e);
    }

    private void FileListView_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        OpenItemContextMenu(FileListView, e);
    }

    private void FileGridView_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        OpenItemContextMenu(FileGridView, e);
    }

    private void OpenItemContextMenu(ItemsControl host, ContextMenuEventArgs e)
    {
        var item = _pendingContextMenuItem ?? (host as Selector)?.SelectedItem as FileExplorerItemViewModel;
        _pendingContextMenuItem = null;

        if (item is null)
        {
            host.ContextMenu = null;
            e.Handled = true;
            return;
        }

        var contextMenu = BuildItemContextMenu(item);
        contextMenu.PlacementTarget = host;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        contextMenu.Closed += (_, _) => host.ClearValue(ContextMenuProperty);
        host.ContextMenu = contextMenu;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildItemContextMenu(FileExplorerItemViewModel item)
    {
        var contextMenu = new ContextMenu
        {
            DataContext = item,
            Style = (Style)FindResource("ExplorerContextMenuStyle")
        };

        contextMenu.Resources.Add(typeof(Separator), FindResource("ExplorerMenuSeparatorStyle"));

        contextMenu.Items.Add(CreateQuickActionHostMenuItem(contextMenu, item));
        contextMenu.Items.Add(new Separator());

        AddShellContextMenuItems(contextMenu, item);

        if (item.IsDirectory && _viewModel.CanPinItemToSidebar(item))
        {
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateCommandMenuItem(
                "Pin to sidebar",
                "\uE718",
                item,
                _viewModel.PinItemToSidebarCommand));
        }

        if (contextMenu.Items.Count > 0 && contextMenu.Items[^1] is not Separator)
        {
            contextMenu.Items.Add(new Separator());
        }

        contextMenu.Items.Add(CreateCommandMenuItem(
            "Show in Windows Explorer",
            "\uE838",
            item,
            _viewModel.RevealInWindowsExplorerCommand));

        contextMenu.Items.Add(CreateCommandMenuItem(
            "Copy path",
            "\uE8C8",
            item,
            _viewModel.CopyItemPathCommand));

        return contextMenu;
    }

    private void AddShellContextMenuItems(ContextMenu contextMenu, FileExplorerItemViewModel item)
    {
        var menuItems = _fileShellContextMenuService.GetMenuItems(item.FullPath);
        foreach (var menuItem in menuItems)
        {
            if (menuItem.IsSeparator)
            {
                if (contextMenu.Items.Count > 0 && contextMenu.Items[^1] is not Separator)
                {
                    contextMenu.Items.Add(new Separator());
                }

                continue;
            }

            contextMenu.Items.Add(CreateShellVerbMenuItem(item, menuItem));
        }
    }

    private MenuItem CreateShellVerbMenuItem(FileExplorerItemViewModel item, ShellMenuItem shellMenuItem)
    {
        var menuItem = new MenuItem
        {
            Header = shellMenuItem.Label,
            IsEnabled = shellMenuItem.IsEnabled,
            Icon = GetShellVerbIcon(shellMenuItem.Label),
            Style = (Style)FindResource("ExplorerMenuItemStyle")
        };

        menuItem.Click += (_, _) =>
        {
            _fileShellContextMenuService.TryInvoke(item.FullPath, shellMenuItem.InvokeToken);
        };

        return menuItem;
    }

    private MenuItem CreateCommandMenuItem(string header, string icon, FileExplorerItemViewModel item, ICommand command)
    {
        return new MenuItem
        {
            Header = header,
            Icon = icon,
            Command = command,
            CommandParameter = item,
            Style = (Style)FindResource("ExplorerMenuItemStyle")
        };
    }

    private MenuItem CreateSidebarCommandMenuItem(string header, string icon, FileExplorerLocationViewModel location, ICommand command)
    {
        return new MenuItem
        {
            Header = header,
            Icon = icon,
            Command = command,
            CommandParameter = location,
            Style = (Style)FindResource("ExplorerMenuItemStyle")
        };
    }

    private ContextMenu BuildViewMenu()
    {
        var contextMenu = new ContextMenu
        {
            DataContext = _viewModel,
            Style = (Style)FindResource("ExplorerContextMenuStyle")
        };

        contextMenu.Resources.Add(typeof(Separator), FindResource("ExplorerMenuSeparatorStyle"));

        contextMenu.Items.Add(CreateViewMenuItem("List view", "\uE8A5", _viewModel.IsListView, () => _viewModel.UseListView()));
        contextMenu.Items.Add(CreateViewMenuItem("Grid view", "\uE80A", _viewModel.IsGridView, () => _viewModel.UseGridView()));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateViewMenuItem("Sort by name", "\uE8CB", _viewModel.SortDescription == "Name", () => _viewModel.SortByNameCommand.Execute(null)));
        contextMenu.Items.Add(CreateViewMenuItem("Sort by type", "\uE8EC", _viewModel.SortDescription == "Type", () => _viewModel.SortByTypeCommand.Execute(null)));
        contextMenu.Items.Add(CreateViewMenuItem("Sort by size", "\uE7C3", _viewModel.SortDescription == "Size", () => _viewModel.SortBySizeCommand.Execute(null)));
        contextMenu.Items.Add(CreateViewMenuItem("Sort by modified", "\uE823", _viewModel.SortDescription == "Modified", () => _viewModel.SortByModifiedCommand.Execute(null)));
        contextMenu.Items.Add(CreateViewMenuItem($"Direction: {_viewModel.SortDirectionLabel}", "\uE8D4", _viewModel.SortDescending, () => _viewModel.ToggleSortDirectionCommand.Execute(null)));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateViewMenuItem("Zoom in", "\uE8A3", false, () => _viewModel.ZoomInCommand.Execute(null), _viewModel.CanZoomIn));
        contextMenu.Items.Add(CreateViewMenuItem("Zoom out", "\uE71F", false, () => _viewModel.ZoomOutCommand.Execute(null), _viewModel.CanZoomOut));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(CreateViewMenuItem("Show hidden files", "\uE8F4", _viewModel.ShowHiddenFiles, () => _viewModel.ToggleHiddenFilesSetting()));
        contextMenu.Items.Add(CreateViewMenuItem("Show file extensions", "\uE8B7", _viewModel.ShowFileExtensions, () => _viewModel.ToggleFileExtensionsSetting()));

        return contextMenu;
    }

    private MenuItem CreateViewMenuItem(string header, string icon, bool isChecked, Action action, bool isEnabled = true)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            Icon = icon,
            IsCheckable = true,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            Style = (Style)FindResource("ExplorerMenuItemStyle")
        };

        menuItem.Click += (_, _) => action();
        return menuItem;
    }

    private void BeginPathEditingAndFocus()
    {
        _viewModel.BeginPathEditing();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PathTextBox.Focus();
            PathTextBox.CaretIndex = PathTextBox.Text.Length;
        }), DispatcherPriority.Input);
    }

    private void SidebarLocationButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileExplorerLocationViewModel location })
        {
            _sidebarDragLocation = location;
            _sidebarDragStartPoint = e.GetPosition(this);
        }
    }

    private async void SidebarLocationButton_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || sender is not FrameworkElement { DataContext: FileExplorerLocationViewModel location }
            || location.IsSeparator
            || string.IsNullOrWhiteSpace(location.Path))
        {
            return;
        }

        await _viewModel.OpenInNewTabAsync(location.Path);
        e.Handled = true;
    }

    private void SidebarLocationButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_sidebarDragLocation?.CanReorder != true || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _sidebarDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _sidebarDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(typeof(FileExplorerLocationViewModel), _sidebarDragLocation);
        RunDragDropWithGhost(
            (DependencyObject)sender,
            data,
            DragDropEffects.Move,
            _sidebarDragLocation.Glyph,
            _sidebarDragLocation.DisplayName,
            "Move sidebar shortcut");
        _sidebarDragLocation = null;
        _viewModel.ClearSidebarDropIndicators();
        SidebarDropHint.Visibility = Visibility.Collapsed;
    }

    private void SidebarLocationButton_OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileExplorerLocationViewModel target } || target.IsSeparator)
        {
            _viewModel.ClearSidebarDropIndicators();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SidebarDropHint.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(typeof(FileExplorerLocationViewModel)))
        {
            if (!target.CanReorder || e.Data.GetData(typeof(FileExplorerLocationViewModel)) is not FileExplorerLocationViewModel)
            {
                _viewModel.ClearSidebarDropIndicators();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var placement = GetSidebarDropPlacement((FrameworkElement)sender, e.GetPosition((IInputElement)sender), allowInsert: true, allowDropInto: false);
            _viewModel.SetSidebarDropIndicator(target, placement == SidebarDropPlacement.Before, placement == SidebarDropPlacement.After, false);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (TryExtractFileDropPaths(e.Data, out var paths))
        {
            var allFolders = paths.All(Directory.Exists);
            var placement = GetSidebarDropPlacement((FrameworkElement)sender, e.GetPosition((IInputElement)sender), allowInsert: allFolders, allowDropInto: true, insertThreshold: 0.12d);
            _viewModel.SetSidebarDropIndicator(target, placement == SidebarDropPlacement.Before, placement == SidebarDropPlacement.After, placement == SidebarDropPlacement.Inside);
            e.Effects = placement == SidebarDropPlacement.None
                ? DragDropEffects.None
                : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        _viewModel.ClearSidebarDropIndicators();
        SidebarDropHint.Visibility = Visibility.Collapsed;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void SidebarLocationButton_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileExplorerLocationViewModel target })
        {
            return;
        }

        SidebarDropHint.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(typeof(FileExplorerLocationViewModel))
            && e.Data.GetData(typeof(FileExplorerLocationViewModel)) is FileExplorerLocationViewModel source)
        {
            var placement = GetSidebarDropPlacement((FrameworkElement)sender, e.GetPosition((IInputElement)sender), allowInsert: true, allowDropInto: false);
            _viewModel.MoveSidebarLocation(source, target, insertAfter: placement == SidebarDropPlacement.After);
            _viewModel.ClearSidebarDropIndicators();
            e.Handled = true;
            return;
        }

        if (TryExtractFileDropPaths(e.Data, out var paths))
        {
            var allFolders = paths.All(Directory.Exists);
            var placement = GetSidebarDropPlacement((FrameworkElement)sender, e.GetPosition((IInputElement)sender), allowInsert: allFolders, allowDropInto: true, insertThreshold: 0.12d);
            _viewModel.ClearSidebarDropIndicators();

            if (allFolders && placement is SidebarDropPlacement.Before or SidebarDropPlacement.After)
            {
                var insertIndex = _viewModel.GetSidebarInsertIndexForTarget(target, insertAfter: placement == SidebarDropPlacement.After);
                _viewModel.PinFolderPathsToSidebar(paths, insertIndex);
                e.Handled = true;
                return;
            }

            if (placement == SidebarDropPlacement.Inside)
            {
                var preferMove = !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && IsMoveOperation(e.Data);
                await _viewModel.HandleDroppedPathsAsync(paths, target.Path, preferMove);
            }
        }

        _viewModel.ClearSidebarDropIndicators();
        e.Handled = true;
    }

    private void SidebarLocationButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not FileExplorerLocationViewModel location || !location.CanRemove)
        {
            if (sender is Button plainButton)
            {
                plainButton.ContextMenu = null;
            }

            return;
        }

        var contextMenu = new ContextMenu
        {
            Style = (Style)FindResource("ExplorerContextMenuStyle")
        };
        contextMenu.Resources.Add(typeof(Separator), FindResource("ExplorerMenuSeparatorStyle"));
        contextMenu.Items.Add(CreateSidebarCommandMenuItem("Unpin from sidebar", "\uE77A", location, _viewModel.UnpinSidebarLocationCommand));
        button.ContextMenu = contextMenu;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void SidebarPanel_OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(FileExplorerLocationViewModel)))
        {
            _viewModel.ClearSidebarDropIndicators();
            SidebarDropHint.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (TryExtractFileDropPaths(e.Data, out var paths) && paths.All(Directory.Exists))
        {
            _viewModel.ClearSidebarDropIndicators();
            SidebarDropHint.Visibility = Visibility.Visible;
            e.Effects = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        _viewModel.ClearSidebarDropIndicators();
        SidebarDropHint.Visibility = Visibility.Collapsed;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void SidebarPanel_OnDragLeave(object sender, DragEventArgs e)
    {
        _viewModel.ClearSidebarDropIndicators();
        SidebarDropHint.Visibility = Visibility.Collapsed;
    }

    private void SidebarPanel_OnDrop(object sender, DragEventArgs e)
    {
        _viewModel.ClearSidebarDropIndicators();
        SidebarDropHint.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(typeof(FileExplorerLocationViewModel))
            && e.Data.GetData(typeof(FileExplorerLocationViewModel)) is FileExplorerLocationViewModel source)
        {
            var lastTarget = _viewModel.SidebarLocations.LastOrDefault(location => location.CanReorder);
            if (lastTarget is not null)
            {
                _viewModel.MoveSidebarLocation(source, lastTarget, insertAfter: true);
            }

            e.Handled = true;
            return;
        }

        if (TryExtractFileDropPaths(e.Data, out var paths) && paths.All(Directory.Exists))
        {
            _viewModel.PinFolderPathsToSidebar(paths);
        }

        e.Handled = true;
    }

    private void FileListView_OnDragOver(object sender, DragEventArgs e)
    {
        HandleFileSurfaceDragOver(FileListView, e);
    }

    private void FileGridView_OnDragOver(object sender, DragEventArgs e)
    {
        HandleFileSurfaceDragOver(FileGridView, e);
    }

    private async void FileListView_OnDrop(object sender, DragEventArgs e)
    {
        await HandleFileSurfaceDropAsync(FileListView, e);
    }

    private async void FileGridView_OnDrop(object sender, DragEventArgs e)
    {
        await HandleFileSurfaceDropAsync(FileGridView, e);
    }

    private void HandleFileSurfaceDragOver(ItemsControl host, DragEventArgs e)
    {
        if (!TryGetDropTargetPath(host, e.OriginalSource as DependencyObject, out _) || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private async Task HandleFileSurfaceDropAsync(ItemsControl host, DragEventArgs e)
    {
        if (!TryGetDropTargetPath(host, e.OriginalSource as DependencyObject, out var targetPath)
            || !TryExtractFileDropPaths(e.Data, out var paths))
        {
            e.Handled = true;
            return;
        }

        var preferMove = !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && IsMoveOperation(e.Data);
        await _viewModel.HandleDroppedPathsAsync(paths, targetPath, preferMove);
        e.Handled = true;
    }

    private void TryBeginFileDrag(Selector host, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(host);
        if (Math.Abs(currentPosition.X - _fileDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _fileDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (host.SelectedItem is not FileExplorerItemViewModel item)
        {
            return;
        }

        try
        {
            var fileDropList = new StringCollection
            {
                item.FullPath
            };

            var dataObject = new DataObject();
            dataObject.SetFileDropList(fileDropList);
            dataObject.SetData("NebulaSourcePath", _viewModel.CurrentPath);
            dataObject.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
            RunDragDropWithGhost(
                host,
                dataObject,
                DragDropEffects.Move | DragDropEffects.Copy,
                item.Glyph,
                item.DisplayName,
                item.TypeLabel);
        }
        catch
        {
        }
    }

    private MenuItem CreateQuickActionHostMenuItem(ContextMenu contextMenu, FileExplorerItemViewModel item)
    {
        var quickActions = new UniformGrid
        {
            Columns = 3
        };

        quickActions.Children.Add(CreateQuickActionButton(contextMenu, item, "\uE8C8", "Copy", () => _viewModel.CopyItemToClipboard(item)));
        quickActions.Children.Add(CreateQuickActionButton(contextMenu, item, "\uE8C6", "Cut", () => _viewModel.CutItemToClipboard(item)));
        quickActions.Children.Add(CreateQuickActionButton(
            contextMenu,
            item,
            "\uE77F",
            "Paste",
            () =>
            {
                if (_viewModel.PasteIntoCurrentDirectoryCommand.CanExecute(null))
                {
                    _viewModel.PasteIntoCurrentDirectoryCommand.Execute(null);
                }
            },
            isEnabled: _viewModel.CanPasteIntoCurrentDirectory));

        return new MenuItem
        {
            Header = quickActions,
            StaysOpenOnClick = true,
            Style = (Style)FindResource("ExplorerQuickActionHostStyle")
        };
    }

    private Button CreateQuickActionButton(ContextMenu contextMenu, FileExplorerItemViewModel item, string icon, string label, Action action, bool isEnabled = true)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        content.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 6, 0, 0),
            FontFamily = (FontFamily)FindResource("ShellBodyFont"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("ShellForegroundBrush")
        });

        var button = new Button
        {
            Style = (Style)FindResource("ExplorerQuickActionButtonStyle"),
            IsEnabled = isEnabled,
            Content = content,
            Tag = item
        };
        button.Click += (_, _) =>
        {
            action();
            contextMenu.IsOpen = false;
        };

        return button;
    }

    private void RunDragDropWithGhost(DependencyObject dragSource, IDataObject dataObject, DragDropEffects effects, string glyph, string title, string? subtitle)
    {
        ShowDragGhost(glyph, title, subtitle);
        var dragElement = dragSource as UIElement;

        GiveFeedbackEventHandler giveFeedbackHandler = (_, e) =>
        {
            UpdateDragGhostPosition();
            e.UseDefaultCursors = true;
            e.Handled = true;
        };

        QueryContinueDragEventHandler queryContinueDragHandler = (_, _) => UpdateDragGhostPosition();

        if (dragElement is not null)
        {
            dragElement.GiveFeedback += giveFeedbackHandler;
            dragElement.QueryContinueDrag += queryContinueDragHandler;
        }

        try
        {
            DragDrop.DoDragDrop(dragSource, dataObject, effects);
        }
        finally
        {
            if (dragElement is not null)
            {
                dragElement.GiveFeedback -= giveFeedbackHandler;
                dragElement.QueryContinueDrag -= queryContinueDragHandler;
            }

            HideDragGhost();
        }
    }

    private void ShowDragGhost(string glyph, string title, string? subtitle)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("ShellForegroundBrush"),
            FontFamily = (FontFamily)FindResource("ShellBodyFont"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 180
        };

        var subtitleBlock = new TextBlock
        {
            Text = subtitle ?? string.Empty,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("ShellMutedBrush"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 180,
            Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible
        };

        var content = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Background = (Brush)new BrushConverter().ConvertFromString("#E0181D26")!,
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border
                    {
                        Width = 26,
                        Height = 26,
                        CornerRadius = new CornerRadius(13),
                        Background = (Brush)new BrushConverter().ConvertFromString("#14223C")!,
                        Child = new TextBlock
                        {
                            Text = glyph,
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            FontSize = 13,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = (Brush)FindResource("AccentBrush")
                        }
                    },
                    new StackPanel
                    {
                        Margin = new Thickness(10,0,0,0),
                        Children =
                        {
                            titleBlock,
                            subtitleBlock
                        }
                    }
                }
            }
        };

        _dragGhostPopup ??= new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            IsHitTestVisible = false,
            StaysOpen = true
        };

        _dragGhostPopup.Child = content;
        _dragGhostPopup.IsOpen = true;
        UpdateDragGhostPosition();
    }

    private void UpdateDragGhostPosition()
    {
        if (_dragGhostPopup is null || !_dragGhostPopup.IsOpen)
        {
            return;
        }

        if (!TryGetCursorPosition(out var cursorPosition))
        {
            return;
        }

        var windowPoint = PointFromScreen(new Point(cursorPosition.X, cursorPosition.Y));
        _dragGhostPopup.HorizontalOffset = windowPoint.X + 18;
        _dragGhostPopup.VerticalOffset = windowPoint.Y + 22;
    }

    private void HideDragGhost()
    {
        if (_dragGhostPopup is not null)
        {
            _dragGhostPopup.IsOpen = false;
            _dragGhostPopup.Child = null;
        }
    }

    private static bool TryGetCursorPosition(out NativePoint point)
    {
        return GetCursorPos(out point);
    }

    private static SidebarDropPlacement GetSidebarDropPlacement(FrameworkElement targetElement, Point position, bool allowInsert, bool allowDropInto, double insertThreshold = 0.25d)
    {
        if (targetElement.ActualHeight <= 0)
        {
            return SidebarDropPlacement.None;
        }

        var ratio = position.Y / targetElement.ActualHeight;
        if (allowInsert && ratio <= insertThreshold)
        {
            return SidebarDropPlacement.Before;
        }

        if (allowInsert && ratio >= 1d - insertThreshold)
        {
            return SidebarDropPlacement.After;
        }

        return allowDropInto ? SidebarDropPlacement.Inside : SidebarDropPlacement.None;
    }

    private static bool TryExtractFileDropPaths(IDataObject dataObject, out IReadOnlyList<string> paths)
    {
        paths = [];

        if (!dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (dataObject.GetData(DataFormats.FileDrop) is not string[] rawPaths)
        {
            return false;
        }

        paths = rawPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return paths.Count > 0;
    }

    private bool TryGetDropTargetPath(ItemsControl host, DependencyObject? source, out string targetPath)
    {
        targetPath = _viewModel.CurrentPath;

        if (FindAncestor<ListViewItem>(source) is { DataContext: FileExplorerItemViewModel listItem } && listItem.IsDirectory)
        {
            targetPath = listItem.FullPath;
            return true;
        }

        if (FindAncestor<ListBoxItem>(source) is { DataContext: FileExplorerItemViewModel gridItem } && gridItem.IsDirectory)
        {
            targetPath = gridItem.FullPath;
            return true;
        }

        return !string.IsNullOrWhiteSpace(targetPath);
    }

    private static bool IsMoveOperation(IDataObject dataObject)
    {
        if (dataObject.GetData("Preferred DropEffect") is MemoryStream stream && stream.Length >= 4)
        {
            var buffer = new byte[4];
            stream.Position = 0;
            stream.ReadExactly(buffer, 0, 4);
            var effect = (DragDropEffects)BitConverter.ToInt32(buffer, 0);
            return effect.HasFlag(DragDropEffects.Move);
        }

        return true;
    }

    private FileExplorerItemViewModel? GetSelectedFileItem()
    {
        return FileListView.Visibility == Visibility.Visible
            ? FileListView.SelectedItem as FileExplorerItemViewModel
            : FileGridView.SelectedItem as FileExplorerItemViewModel;
    }

    private static string? GetShellVerbIcon(string label)
    {
        return label.ToLowerInvariant() switch
        {
            var value when value.StartsWith("open with", StringComparison.Ordinal) => "\uE7AC",
            var value when value.StartsWith("open", StringComparison.Ordinal) => "\uE8A7",
            var value when value.StartsWith("copy", StringComparison.Ordinal) => "\uE8C8",
            var value when value.StartsWith("cut", StringComparison.Ordinal) => "\uE8C6",
            var value when value.StartsWith("paste", StringComparison.Ordinal) => "\uE77F",
            var value when value.StartsWith("delete", StringComparison.Ordinal) => "\uE74D",
            var value when value.StartsWith("rename", StringComparison.Ordinal) => "\uE8AC",
            var value when value.StartsWith("share", StringComparison.Ordinal) => "\uE72D",
            var value when value.StartsWith("properties", StringComparison.Ordinal) => "\uE713",
            var value when value.Contains("7-zip", StringComparison.Ordinal) => "\uE7B8",
            var value when value.Contains("compress", StringComparison.Ordinal) => "\uE7B8",
            var value when value.Contains("extract", StringComparison.Ordinal) => "\uE7B8",
            _ => null
        };
    }

    private static FileExplorerItemViewModel? SelectItemUnderPointer<TContainer>(ItemsControl itemsControl, MouseButtonEventArgs e)
        where TContainer : DependencyObject
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return null;
        }

        var container = FindAncestor<TContainer>(source);
        if (container is null)
        {
            if (itemsControl is Selector emptySelector)
            {
                emptySelector.SetCurrentValue(Selector.SelectedItemProperty, null);
            }

            return null;
        }

        var item = itemsControl.ItemContainerGenerator.ItemFromContainer(container);
        if (!Equals(item, DependencyProperty.UnsetValue) && itemsControl is Selector selector)
        {
            selector.SetCurrentValue(Selector.SelectedItemProperty, item);
            return item as FileExplorerItemViewModel;
        }

        return null;
    }

    private void BeginRenameEdit(FileExplorerItemViewModel item)
    {
        _viewModel.BeginRenameCommand.Execute(item);
        
        // Auto-focus the rename textbox on next dispatcher pass after UI updates
        Dispatcher.BeginInvoke(() =>
        {
            FocusRenameTextBox(item);
        }, DispatcherPriority.Render);
    }

    private void FocusRenameTextBox(FileExplorerItemViewModel item)
    {
        // Try to find the textbox in the active view (list or grid)
        TextBox? renameBox = null;
        
        // Check if we're in grid view
        if (FileGridView.IsVisible && FileGridView.ItemsSource is not null)
        {
            renameBox = FindRenameTextBoxInItemsControl(FileGridView, item);
        }
        
        // If not found, try list view
        if (renameBox is null && FileListView.IsVisible && FileListView.ItemsSource is not null)
        {
            renameBox = FindRenameTextBoxInItemsControl(FileListView, item);
        }
        
        renameBox?.Focus();
        renameBox?.SelectAll();
    }

    private TextBox? FindRenameTextBoxInItemsControl(ItemsControl itemsControl, FileExplorerItemViewModel targetItem)
    {
        // Get the index of the target item
        int index = itemsControl.Items.IndexOf(targetItem);
        if (index < 0)
        {
            return null;
        }
        
        // Get the container for this item
        var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(index);
        if (container is null)
        {
            return null;
        }
        
        // Find the textbox in the container
        // Name differs: "RenameBox" for list view, "GridRenameBox" for grid view
        return FindChild<TextBox>(container, tb =>
            tb.Name is "RenameBox" or "GridRenameBox");
    }

    private static TChild? FindChild<TChild>(DependencyObject? parent, Func<TChild, bool>? predicate = null)
        where TChild : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }
        
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is TChild typedChild && (predicate is null || predicate(typedChild)))
            {
                return typedChild;
            }
            
            var result = FindChild(child, predicate);
            if (result is not null)
            {
                return result;
            }
        }
        
        return null;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject? current)
        where TAncestor : DependencyObject
    {
        while (current is not null)
        {
            if (current is TAncestor ancestor)
            {
                return ancestor;
            }

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                _ => null
            };
        }

        return null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum SidebarDropPlacement
    {
        None,
        Before,
        After,
        Inside
    }
}
