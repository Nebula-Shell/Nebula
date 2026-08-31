param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "Programs\NebulaShell"
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Nebula Shell"
$appExe = Join-Path $installRoot "CaelestiaWin.App.exe"
$payloadZip = Join-Path $PSScriptRoot "NebulaShell.zip"
$uninstallScript = Join-Path $installRoot "uninstall.ps1"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NebulaShell"

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [string]$Arguments = "",
        [string]$Description = "Nebula Shell"
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
    $shortcut.Description = $Description
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Save()
}

if (-not (Test-Path $payloadZip)) {
    throw "Installer payload is missing: $payloadZip"
}

Get-Process CaelestiaWin.App -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Expand-Archive -LiteralPath $payloadZip -DestinationPath $installRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $uninstallScript -Force

New-Item -ItemType Directory -Path $startMenuRoot -Force | Out-Null
New-Shortcut -Path (Join-Path $startMenuRoot "Nebula Shell.lnk") -TargetPath $appExe -Description "Start Nebula Shell"
New-Shortcut -Path (Join-Path $startMenuRoot "Nebula Shell Safe Mode.lnk") -TargetPath $appExe -Arguments "--safe-mode" -Description "Start Nebula Shell in safe mode"

New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "Nebula Shell" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "0.1.0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "Nebula Shell" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $installRoot -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $appExe -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "QuietUninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`" -Quiet" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null

if (-not $Quiet) {
    Write-Host "Nebula Shell installed to $installRoot"
    Write-Host "Use the Start Menu shortcut 'Nebula Shell' to launch it."
}
