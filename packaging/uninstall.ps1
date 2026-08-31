param(
    [switch]$Quiet,
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "Programs\NebulaShell"
$userDataRoot = Join-Path $env:LOCALAPPDATA "NebulaShell"
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Nebula Shell"
$startupRunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NebulaShell"

Get-Process CaelestiaWin.App -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $startupRunKey) {
    Remove-ItemProperty -Path $startupRunKey -Name "NebulaShell" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $startupRunKey -Name "CaelestiaWin" -ErrorAction SilentlyContinue
}

if (Test-Path $startMenuRoot) {
    Remove-Item -LiteralPath $startMenuRoot -Recurse -Force
}

if (Test-Path $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}

if ($RemoveUserData -and (Test-Path $userDataRoot)) {
    Remove-Item -LiteralPath $userDataRoot -Recurse -Force
}

if (Test-Path $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force
}

if (-not $Quiet) {
    Write-Host "Nebula Shell has been removed."
    if (-not $RemoveUserData) {
        Write-Host "User config and logs were preserved at $userDataRoot"
    }
}
