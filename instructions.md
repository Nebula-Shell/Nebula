# Nebula Shell Installation Guide

Nebula Shell is a custom Windows desktop shell prototype. Install and test it deliberately: it can stop Explorer when launched depending on configuration, and it manages windows globally.

## Recommended Requirements

- Windows 10 19041 or newer.
- 64-bit Windows.
- A keyboard available for recovery shortcuts.
- Keep Task Manager access available while testing.

The packaged app payload is self-contained and includes the .NET 8 runtime files needed by Nebula Shell. The installer bootstrapper itself is a small .NET Framework executable, which is available on normal Windows 10 installations.

## Install From The EXE Installer

1. Build or download `NebulaShell-Setup.exe`.
2. Run `NebulaShell-Setup.exe`.
3. The installer copies Nebula Shell to:

```text
%LocalAppData%\Programs\NebulaShell
```

4. Launch Nebula Shell from the Start Menu:

```text
Start Menu > Nebula Shell > Nebula Shell
```

5. If you need recovery mode, launch:

```text
Start Menu > Nebula Shell > Nebula Shell Safe Mode
```

Safe Mode starts the shell with `--safe-mode`, which disables heavier shell behavior such as hotkeys and advanced window orchestration.

## Runtime Files

Nebula stores user configuration, session state, and logs under:

```text
%LocalAppData%\NebulaShell
```

Important files:

- `%LocalAppData%\NebulaShell\config.json`
- `%LocalAppData%\NebulaShell\session.json`
- `%LocalAppData%\NebulaShell\logs\nebula.log`

If `config.json` is missing or malformed, Nebula regenerates safe defaults.

## Start On Login

Nebula supports HKCU Run-key startup through its config. Edit:

```text
%LocalAppData%\NebulaShell\config.json
```

Then set:

```json
{
  "startup": {
    "startOnLogin": true
  }
}
```

Restart Nebula after editing. Keep this disabled while testing unstable builds.

## Safe Testing Workflow

1. Start Nebula normally.
2. Verify the top bar, launcher, control center, workspaces, and window tiling.
3. If the shell misbehaves, open Task Manager with `Ctrl+Shift+Esc`.
4. End `CaelestiaWin.App.exe` if needed.
5. Start Explorer manually from Task Manager:

```text
File > Run new task > explorer.exe
```

6. Reopen Nebula in Safe Mode from the Start Menu.

## Returning To Explorer

Use Nebula's recovery/control-center action when available. If the UI is unavailable:

1. Press `Ctrl+Shift+Esc`.
2. End `CaelestiaWin.App.exe`.
3. Run `explorer.exe`.

## Uninstall

Use Windows Settings:

```text
Settings > Apps > Nebula Shell > Uninstall
```

Or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\NebulaShell\uninstall.ps1"
```

By default, uninstall preserves user config and logs at `%LocalAppData%\NebulaShell`. To remove user data too:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\NebulaShell\uninstall.ps1" -RemoveUserData
```

## Important Limitation

This installer does not register Nebula Shell as the Windows shell replacement. That is intentional. The current package installs Nebula as a user-launched shell candidate with recovery paths preserved.
