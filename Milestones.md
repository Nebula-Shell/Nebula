# Nebula Shell – Development Roadmap

A custom Windows 10 shell in C# (WPF, .NET 8) inspired by the **Hyprland + Caelestia** experience.

---

## Philosophy

- Keyboard-first workflow
- Smooth, minimal, glassmorphic UI
- Modular architecture
- Native Windows integration (not a Linux compositor)
- Progressive enhancement (usable from early milestones)

---

# Milestone 1 — Core Shell Prototype

## Goal
Build a usable, polished shell that already feels like a desktop environment.

## Features

### Desktop Host
- Borderless fullscreen shell window
- Wallpaper/background support
- Layer for overlays and panels

### Top Bar
- Workspace indicators (1–9)
- Active window title
- Clock/date
- System placeholders (network, audio, battery)
- Control center toggle

### Launcher
- Hotkey: `Win + Space`
- Fuzzy search
- App discovery (Start Menu + executables)
- Keyboard navigation
- Smooth animations

### Control Center
- Hotkey: `Win + B`
- Slide-in panel (right side)
- Volume + brightness placeholders
- Wi-Fi / Bluetooth tiles
- Power actions (shutdown, restart, lock, sign out)

### Window Tracking
- Detect active window
- Display window title
- Enumerate visible windows

### Hotkeys
- `Win + Space` → launcher
- `Win + Enter` → terminal
- `Win + B` → control center
- `Win + Q` → close window
- `Win + 1..9` → switch workspace

### Workspaces (Logical)
- 9 workspace states
- UI indicators
- Switching logic (no real window isolation yet)

### Config System
- JSON config
- Keybinds
- theme settings
- animation speed

### Theme
- Dark glassmorphic UI
- Rounded corners
- Accent color

---

# Milestone 2 — Caelestia Experience Layer

## Goal
Make the shell feel premium and closer to Caelestia UX.

## Features

### Notification Center
- Slide-in panel (right)
- Notification stack
- Dismiss actions
- Timestamp grouping

### Media Widget
- Current playing media
- Play / pause / next
- Integration with Windows media sessions

### Control Center (Enhanced)
- Real system info (Wi-Fi, battery, audio)
- Toggles (airplane mode, etc.)
- Better visual cards

### Launcher Improvements
- Better fuzzy search ranking
- Recent apps
- Command mode (e.g. “shutdown”, “settings”)

### Theming Upgrade
- Wallpaper-based accent color extraction
- Theme switching
- Transparency tuning

### Animations
- Spring-like motion
- Smoother transitions
- Configurable animation curves

---

# Milestone 3 — Hyprland Workflow Layer

## Goal
Introduce keyboard-driven window management and workflows.

## Features

### Window Management
- Focus switching (`H/J/K/L`)
- Move windows via keyboard
- Resize windows
- Fullscreen toggle

### Tiling System (Soft Tiling)
- Snap-based layouts
- Horizontal / vertical splits
- Stack layouts
- Grid layouts

### Workspace System (Advanced)
- Assign windows to workspaces
- Switch workspace views
- Move windows between workspaces
- Optional workspace overview UI

### Overview Mode
- Show all windows visually
- Workspace previews
- Mouse + keyboard navigation

### Multi-Monitor Support
- Independent workspace per monitor
- Window placement awareness

---

# Milestone 4 — System Integration

## Goal
Make the shell deeply integrated and closer to a real replacement.

## Features

### System Tray Integration
- Display tray icons
- Interact with apps (basic support)

### Startup Integration
- Launch on login
- Delay options
- Safe startup mode

### Session Management
- Restore previous session
- Reopen apps
- Workspace persistence

### Performance Optimization
- Reduce memory usage
- Improve startup time
- Optimize rendering

---

# Milestone 5 — Optional Shell Replacement Mode

## Goal
Allow running without Explorer (advanced users only).

## Features

### Shell Replacement
- Compatible with Windows Shell Launcher (Enterprise/Education only)
- Persistent shell process (no launcher exit)

### Recovery System
- Fallback mechanism if shell crashes
- Safe mode launch

### Explorer Compatibility Layer
- Ensure basic system functionality still works
- Handle apps expecting Explorer

⚠️ This milestone is **optional** and should only be attempted after stability.

---

# Milestone 6 — Extensibility & Ecosystem

## Goal
Turn the shell into a platform.

## Features

### Plugin System
- Load external modules
- Widget API
- Extension points

### Widget Framework
- Clock widgets
- System monitors
- Custom user widgets

### Config UI
- Graphical settings panel
- Keybind editor
- Theme editor

### Scripting Support (Optional)
- Custom commands
- Automation hooks

---

# Milestone 7 — Polish & Identity

## Goal
Make it feel like a real product.

## Features

- Design consistency pass
- Micro-interactions
- Accessibility improvements
- Performance tuning
- Branding
- Installer / packaging

---

# Long-Term Vision

> A modern, keyboard-first Windows shell that feels like Hyprland + Caelestia,
> but built natively for Windows with smooth visuals, modular design,
> and deep customization.

---

# Suggested Development Order

1. Milestone 1 (must be solid)
2. Polish Milestone 1
3. Milestone 2 (UX depth)
4. Milestone 3 (workflow power)
5. Milestone 4 (integration)
6. Milestone 5 (optional)
7. Milestone 6–7 (platform + polish)

---

# Notes

- Do NOT rush shell replacement.
- Focus on UX feel early.
- Keep everything modular.
- Always prioritize keyboard workflow.
- Treat animations as first-class citizens.

---