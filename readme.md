# Nebula Shell

Nebula Shell is a native Windows 10 desktop shell prototype inspired by the feel of Hyprland and Caelestia. It runs as a fullscreen WPF host on top of Explorer during development, with a keyboard-first launcher, top bar, control center, notification center, overview mode, dynamic tiling, workspace-aware window orchestration, and a future-ready shell architecture.

## Project Overview

The current build includes the Milestone 1 shell foundation, Milestone 2 experience layer, and the first Milestone 3 / Milestone 5 workflow infrastructure:

- Fullscreen desktop host window with an atmospheric shell surface
- Glassy top bar with workspaces, active window title, and system area
- Searchable launcher overlay with fuzzy matching and keyboard navigation
- Right-side control center with power actions and system placeholders
- Notification center with in-memory notifications and dismissal
- Media widget abstraction ready for real transport integration
- Global hotkeys driven by JSON configuration
- Active foreground window tracking and visible top-level window enumeration
- Workspace-aware window assignment and visibility management
- Directional focus navigation, overview mode, and dynamic tiling
- Safe mode, crash recovery, Explorer-awareness, and startup registration readiness
- Robust config loading, validation, regeneration, and diagnostics
- Session continuity, tray baseline, multi-monitor detection, and leveled logging

## Architecture Summary

The solution is split into modular projects:

- `CaelestiaWin.App`
  Bootstraps the application, configures dependency injection, registers exception handlers, starts services, and owns shell orchestration.
- `CaelestiaWin.Core`
  Shared contracts, enums, config models, launcher/window models, and the app state service.
- `CaelestiaWin.Platform.Windows`
  Native Win32 helpers for foreground tracking, window enumeration, power actions, and app discovery.
- `CaelestiaWin.UI`
  WPF views, shell view models, theme dictionaries, commands, converters, and polished shell visuals.
- `CaelestiaWin.Hotkeys`
  Global hotkey registration and dispatch through a hidden message source.
- `CaelestiaWin.Windowing`
  Active-window and workspace orchestration abstractions.
- `CaelestiaWin.Config`
  JSON config loading, validation, fallback recovery, and default config generation.

## Feature List

- Borderless fullscreen host window with layered atmospheric background
- Hyprland/Caelestia-inspired dark translucent styling baseline
- Workspace indicators for 1 through 9
- Live foreground window title in the top bar
- Clock/date summary in the top bar and control center
- Launcher overlay with Start Menu shortcut discovery and executable alias scanning
- Search prioritization for exact, prefix, token-prefix, contains, and fuzzy subsequence matches
- Recent apps and launcher command mode for `shutdown`, `restart`, `lock`, and `sign out`
- Keyboard navigation with arrow keys, Enter, Escape, and single-click launch
- Slide-in control center with volume, network, battery, accent controls, and media widget
- Slide-in notification center with in-memory notifications
- Global hotkeys for launcher, terminal, control center, notification center, focus navigation, tiling moves, overview, and workspaces
- Safe startup on top of Explorer with graceful degradation and log output
- Registry-backed start-on-login support with session persistence across restarts
- Baseline tray area with shell-owned volume and network actions

## Solution Tree

```text
NebulaShell.sln
config.example.json
README.md
CaelestiaWin.App/
CaelestiaWin.Config/
CaelestiaWin.Core/
CaelestiaWin.Hotkeys/
CaelestiaWin.Platform.Windows/
CaelestiaWin.UI/
CaelestiaWin.Windowing/
```

## Build Instructions

1. Install the .NET 8 SDK with Windows desktop support.
2. Open `NebulaShell.sln` in Visual Studio 2022 or later.
3. Build the `CaelestiaWin.App` project, or run:

```powershell
dotnet build CaelestiaWin.App\CaelestiaWin.App.csproj
```

## Run Instructions

From Visual Studio, set `CaelestiaWin.App` as the startup project and run it. Or use:

```powershell
dotnet run --project CaelestiaWin.App\CaelestiaWin.App.csproj
```

The shell opens as a fullscreen WPF host over Explorer. This milestone does not replace Explorer.

## Config File Location

Runtime config is stored at:

```text
%LocalAppData%\NebulaShell\config.json
```

Session snapshots are stored at:

```text
%LocalAppData%\NebulaShell\session.json
```

If the file is missing, Nebula Shell generates a default config automatically. If the JSON is malformed, it backs the broken file up and restores defaults.

`config.example.json` in the repository mirrors the default schema.

## Default Hotkeys

- `Win` opens the launcher
- `Win+Enter` opens Windows Terminal, with PowerShell or `cmd.exe` fallback
- `Win+B` toggles the control center
- `Win+N` toggles the notification center
- `Win+C` opens the shell configuration panel
- `Win+F` toggles true fullscreen for the focused window
- `Win+Q` closes the focused window
- `Win+H/J/K/L` moves focus left/down/up/right
- `Win+Shift+H/J/K/L` reflows the focused window through the soft-tiling layout
- `Win+Left` and `Win+Right` cycle to the previous or next logical workspace
- `Win+Tab` toggles the overview overlay
- `Win+1..9` switches logical workspaces
- `Win+Shift+1..9` moves the focused window to another workspace

If Windows reserves one of those `Win+...` combinations, Nebula Shell automatically falls back to:

- `Ctrl+Space` for the launcher if the single-`Win` listener is unavailable
- `Ctrl+Alt+Enter` for terminal
- `Ctrl+Alt+B` for the control center
- `Ctrl+Alt+N` for the notification center
- `Ctrl+Alt+C` for the shell configuration panel
- `Ctrl+Alt+F` for true fullscreen
- `Ctrl+Alt+Q` to close the focused window
- `Ctrl+Alt+H/J/K/L` for directional focus
- `Ctrl+Alt+Shift+H/J/K/L` for window moves
- `Ctrl+Alt+Left` and `Ctrl+Alt+Right` for workspace cycling
- `Ctrl+Alt+Tab` for overview
- `Ctrl+Alt+1..9` for workspaces
- `Ctrl+Alt+Shift+1..9` for move-to-workspace

## Integration Notes

- Notifications are managed by `INotificationService`, stored in memory, and surfaced by `NotificationCenterViewModel`.
- Launcher commands are provided by `ILauncherCommandService`, while app ranking and recents are handled by `ILauncherSearchService` plus `IRecentAppsService`.
- Runtime accent changes flow through `IThemeManager`, then persist back to `%LocalAppData%\\NebulaShell\\config.json`.
- System status lives behind `ISystemStatusService`, which is the extension point for richer network, battery, and hardware toggles.
- `ISystemTrayService` currently provides a shell-owned baseline tray model for volume and network, which keeps the UI decoupled from future real tray extraction work.
- Workspace ownership lives in `IWorkspaceService`, while `IWindowNavigationService` handles directional focus and `IWindowLayoutService` owns dynamic tiling reflow.
- `ISessionService` persists the active workspace plus visible window metadata to `%LocalAppData%\\NebulaShell\\session.json`, and can optionally relaunch apps on restore.
- `IStartupService` manages the HKCU Run-key integration behind the new `startOnLogin` config while preserving compatibility with the older `enableAutoStart` flag.
- `IMonitorService` exposes monitor bounds and primary-display awareness for future per-monitor shell expansion.
- Safe mode is available through `--safe-mode`, which disables hotkeys, active window orchestration, and heavier shell modules for recovery scenarios.
- Crash recovery relaunches Nebula in a guarded mode so the shell host can recover without requiring a full logoff.

## Current Limitations

- The workspace system hides and shows windows at the app level; it does not integrate with Windows virtual desktops yet
- The tiling engine is a dynamic tiling window manager built on top of the Windows desktop compositor, not a replacement for DWM itself
- Volume, brightness, Wi-Fi, and Bluetooth controls are still baseline integrations
- Explorer remains active underneath the host window by design
- The tray area is a baseline shell-owned representation, not a full extraction of Explorer notification icons yet
- Global hotkey registration may partially fail if Windows or another app already owns a shortcut
- App discovery currently prioritizes Start Menu shortcuts and common executable aliases rather than the full installed-app universe

## Next Roadmap

- Richer tiling policies and per-monitor orchestration
- Dynamic wallpaper-based theming
- Real Windows media transport integration
- OS-backed notification ingestion
- Optional Explorer replacement path later
