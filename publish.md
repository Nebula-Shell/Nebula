# Publishing Nebula Shell

This guide describes how to publish Nebula Shell packages for GitHub Releases, Chocolatey, and WinGet.

## Build Release Artifacts

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\packaging\Build-Installer.ps1 -Version 0.1.0
```

Generated artifacts:

```text
dist\publish\NebulaShell\
dist\installer-work\NebulaShell.zip
dist\NebulaShell-Setup.exe
```

Before publishing, test the installer on a clean Windows user profile or VM.

## GitHub Release Checklist

1. Update the version in release notes and packaging command.
2. Build `dist\NebulaShell-Setup.exe`.
3. Run the installer locally.
4. Verify launch, safe mode, uninstall, and Explorer recovery.
5. Create a GitHub Release.
6. Upload:

```text
NebulaShell-Setup.exe
```

7. Include SHA256 in the release notes:

```powershell
Get-FileHash .\dist\NebulaShell-Setup.exe -Algorithm SHA256
```

## Chocolatey Package

Chocolatey packages generally install from a stable URL, usually a GitHub Release asset.

Suggested package layout:

```text
chocolatey/
  nebula-shell.nuspec
  tools/
    chocolateyinstall.ps1
    chocolateyuninstall.ps1
```

Example `nebula-shell.nuspec`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>nebula-shell</id>
    <version>0.1.0</version>
    <title>Nebula Shell</title>
    <authors>Nebula Shell contributors</authors>
    <owners>Nebula Shell contributors</owners>
    <projectUrl>https://github.com/YOUR_ORG/YOUR_REPO</projectUrl>
    <licenseUrl>https://github.com/YOUR_ORG/YOUR_REPO/blob/main/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>A custom Windows 10 shell prototype inspired by Hyprland and Caelestia.</description>
    <tags>windows shell desktop wpf dotnet</tags>
  </metadata>
</package>
```

Example `tools/chocolateyinstall.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$packageName = 'nebula-shell'
$url64 = 'https://github.com/Nebula-Shell/Nebula/releases/download/v0.1.0/NebulaShell-Setup.exe'
$checksum64 = 'REPLACE_WITH_SHA256'

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'exe'
  url64bit       = $url64
  checksum64     = $checksum64
  checksumType64 = 'sha256'
  silentArgs     = '/Q'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
```

Example `tools/chocolateyuninstall.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$uninstaller = Join-Path $env:LOCALAPPDATA 'Programs\NebulaShell\uninstall.ps1'
if (Test-Path $uninstaller) {
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstaller -Quiet
}
```

Build and test:

```powershell
choco pack .\chocolatey\nebula-shell.nuspec
choco install nebula-shell --source . --version 0.1.0
choco uninstall nebula-shell
```

Publish:

```powershell
choco apikey --key YOUR_API_KEY --source https://push.chocolatey.org/
choco push nebula-shell.0.1.0.nupkg --source https://push.chocolatey.org/
```

## WinGet Package

WinGet requires a public installer URL and a SHA256 hash.

1. Publish `NebulaShell-Setup.exe` to a GitHub Release.
2. Compute the hash:

```powershell
Get-FileHash .\dist\NebulaShell-Setup.exe -Algorithm SHA256
```

3. Install `wingetcreate`:

```powershell
winget install Microsoft.WingetCreate
```

4. Create or update manifests:

```powershell
wingetcreate new https://github.com/Nebula-Shell/Nebula/releases/download/v0.1.0/NebulaShell-Setup.exe
```

Use these expected values when prompted:

```text
Package Identifier: NebulaShell.NebulaShell
Package Name: Nebula Shell
Publisher: Nebula Shell contributors
Installer Type: exe
Silent Install Switches: /Q
Scope: user
```

5. Validate locally:

```powershell
winget validate .\manifests\n\NebulaShell\NebulaShell\0.1.0
```

6. Submit to the community repo:

```powershell
wingetcreate submit .\manifests\n\NebulaShell\NebulaShell\0.1.0
```

## Signing

The current local installer is unsigned. Before public distribution, sign the EXE with an Authenticode code-signing certificate:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a .\dist\NebulaShell-Setup.exe
```

After signing, recompute SHA256 and update Chocolatey/WinGet manifests.

## Release Safety Notes

- Do not publish builds that force shell replacement.
- Keep `--safe-mode` documented in every release.
- Test uninstall and Explorer recovery before each public release.
- Include known limitations in release notes.
