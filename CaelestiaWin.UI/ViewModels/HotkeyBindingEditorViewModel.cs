using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.UI.ViewModels;

public sealed class HotkeyBindingEditorViewModel : ObservableObjectBase
{
    private string _gesture;

    public HotkeyBindingEditorViewModel(HotkeyBindingConfig binding)
    {
        Action = binding.Action;
        Workspace = binding.Workspace;
        Direction = binding.Direction;
        OriginalGesture = binding.Gesture;
        _gesture = binding.Gesture;
        DisplayName = BuildDisplayName(binding);
        Description = BuildDescription(binding);
    }

    public HotkeyActionKind Action { get; }

    public int? Workspace { get; }

    public WindowDirection? Direction { get; }

    public string OriginalGesture { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value);
    }

    public HotkeyBindingConfig ToConfig()
    {
        return new HotkeyBindingConfig
        {
            Action = Action,
            Workspace = Workspace,
            Direction = Direction,
            Gesture = string.IsNullOrWhiteSpace(Gesture) ? OriginalGesture : Gesture.Trim()
        };
    }

    private static string BuildDisplayName(HotkeyBindingConfig binding)
    {
        return binding.Action switch
        {
            HotkeyActionKind.ToggleLauncher => "Open launcher",
            HotkeyActionKind.OpenTerminal => "Open terminal",
            HotkeyActionKind.OpenFileExplorer => "Open file explorer",
            HotkeyActionKind.ToggleControlCenter => "Toggle control center",
            HotkeyActionKind.ToggleNotificationCenter => "Toggle notification center",
            HotkeyActionKind.ToggleClipboardHistory => "Open clipboard history",
            HotkeyActionKind.CaptureRegion => "Capture screen region",
            HotkeyActionKind.ToggleSettingsPanel => "Open shell settings",
            HotkeyActionKind.ToggleDiscordDesktop => "Toggle Discord quick access",
            HotkeyActionKind.ToggleSpotifyDesktop => "Toggle Spotify quick access",
            HotkeyActionKind.ToggleGitHubDesktop => "Toggle GitHub Desktop quick access",
            HotkeyActionKind.ToggleFocusedWindowFullscreen => "Toggle true fullscreen",
            HotkeyActionKind.ToggleFocusedWindowFloat => "Toggle floating window",
            HotkeyActionKind.CloseFocusedWindow => "Close focused window",
            HotkeyActionKind.CycleWorkspacePrevious => "Previous workspace",
            HotkeyActionKind.CycleWorkspaceNext => "Next workspace",
            HotkeyActionKind.MoveWindowToWorkspacePrevious => "Send window to previous workspace",
            HotkeyActionKind.MoveWindowToWorkspaceNext => "Send window to next workspace",
            HotkeyActionKind.VolumeUp => "Volume up",
            HotkeyActionKind.VolumeDown => "Volume down",
            HotkeyActionKind.ToggleMute => "Toggle mute",
            HotkeyActionKind.MediaPlayPause => "Play / pause media",
            HotkeyActionKind.MediaNext => "Next media track",
            HotkeyActionKind.MediaPrevious => "Previous media track",
            HotkeyActionKind.BrightnessUp => "Brightness up",
            HotkeyActionKind.BrightnessDown => "Brightness down",
            HotkeyActionKind.ToggleOverview => "Toggle overview",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Left => "Focus window left",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Right => "Focus window right",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Up => "Focus window up",
            HotkeyActionKind.FocusWindow when binding.Direction is WindowDirection.Down => "Focus window down",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Left => "Move window left",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Right => "Move window right",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Up => "Move window up",
            HotkeyActionKind.MoveWindow when binding.Direction is WindowDirection.Down => "Move window down",
            HotkeyActionKind.SwitchWorkspace when binding.Workspace is int workspace => $"Switch to workspace {workspace}",
            HotkeyActionKind.MoveWindowToWorkspace when binding.Workspace is int workspace => $"Send window to workspace {workspace}",
            _ => binding.Action.ToString()
        };
    }

    private static string BuildDescription(HotkeyBindingConfig binding)
    {
        return binding.Action switch
        {
            HotkeyActionKind.ToggleLauncher => "Keyboard-first app and command search.",
            HotkeyActionKind.OpenFileExplorer => "Opens the default file browser you selected in Nebula.",
            HotkeyActionKind.CaptureRegion => "Select a region, copy it to the clipboard, and save it from the notification.",
            HotkeyActionKind.ToggleClipboardHistory => "Choose from recent text copied inside Nebula.",
            HotkeyActionKind.ToggleDiscordDesktop => "Show Discord as a hidden quick access app, or launch Discord if it is closed.",
            HotkeyActionKind.ToggleSpotifyDesktop => "Show Spotify as a hidden quick access app, or launch Spotify if it is closed.",
            HotkeyActionKind.ToggleGitHubDesktop => "Show GitHub Desktop as a hidden quick access app, or launch it if it is closed.",
            HotkeyActionKind.ToggleFocusedWindowFullscreen => "Puts the focused app above Nebula chrome.",
            HotkeyActionKind.ToggleFocusedWindowFloat => "Toggles the focused window as always-on-top (floating).",
            HotkeyActionKind.SwitchWorkspace when binding.Workspace is int workspace => $"Jump directly to desktop {workspace}.",
            HotkeyActionKind.MoveWindowToWorkspace when binding.Workspace is int workspace => $"Move the focused window to desktop {workspace}.",
            HotkeyActionKind.MoveWindowToWorkspacePrevious => "Move the focused window to the previous desktop without switching.",
            HotkeyActionKind.MoveWindowToWorkspaceNext => "Move the focused window to the next desktop without switching.",
            HotkeyActionKind.FocusWindow => "Directional focus navigation across the active desktop.",
            HotkeyActionKind.MoveWindow => "Retile and reposition the focused window.",
            HotkeyActionKind.VolumeUp or HotkeyActionKind.VolumeDown or HotkeyActionKind.ToggleMute => "Handled directly by Nebula without Explorer.",
            HotkeyActionKind.MediaPlayPause or HotkeyActionKind.MediaNext or HotkeyActionKind.MediaPrevious => "Controls the active media session or the shell media fallback.",
            HotkeyActionKind.BrightnessUp or HotkeyActionKind.BrightnessDown => "Adjusts display brightness when Windows exposes a WMI brightness-capable panel.",
            _ => "Shell shortcut."
        };
    }
}
