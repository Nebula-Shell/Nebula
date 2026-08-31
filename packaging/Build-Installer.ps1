param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$distRoot = Join-Path $repoRoot "dist"
$publishRoot = Join-Path $distRoot "publish"
$publishDir = Join-Path $publishRoot "NebulaShell"
$installerWork = Join-Path $distRoot "installer-work"
$payloadZip = Join-Path $installerWork "NebulaShell.zip"
$setupExe = Join-Path $distRoot "NebulaShell-Setup.exe"

function Assert-UnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root)
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside repository output directory: $pathFull"
    }
}

Assert-UnderRoot -Path $distRoot -Root $repoRoot

if (Test-Path $publishRoot) {
    Assert-UnderRoot -Path $publishRoot -Root $distRoot
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

if (Test-Path $installerWork) {
    Assert-UnderRoot -Path $installerWork -Root $distRoot
    Remove-Item -LiteralPath $installerWork -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerWork -Force | Out-Null

dotnet publish (Join-Path $repoRoot "CaelestiaWin.App\CaelestiaWin.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=false `
    -p:UseSharedCompilation=false `
    -p:Version=$Version

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $payloadZip -CompressionLevel Optimal -Force

Copy-Item -LiteralPath (Join-Path $scriptRoot "uninstall.ps1") -Destination (Join-Path $installerWork "uninstall.ps1") -Force

if (Test-Path $setupExe) {
    Assert-UnderRoot -Path $setupExe -Root $distRoot
    Remove-Item -LiteralPath $setupExe -Force
}

$uninstallScript = Join-Path $installerWork "uninstall.ps1"
$installerSource = Join-Path $scriptRoot "NebulaShellInstaller.cs"
$installerIcon = Join-Path $repoRoot "assets\nebula-shell.ico"
$cscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "The .NET Framework C# compiler was not found. Expected csc.exe under %WINDIR%\Microsoft.NET\Framework64\v4.0.30319."
}

& $csc `
    /nologo `
    /target:exe `
    /platform:x64 `
    /optimize+ `
    /out:$setupExe `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /win32icon:$installerIcon `
    /resource:$payloadZip,NebulaShell.zip `
    /resource:$uninstallScript,uninstall.ps1 `
    $installerSource

$compilerExitCode = $LASTEXITCODE

if (-not (Test-Path $setupExe)) {
    throw "Installer compilation completed but did not create $setupExe"
}

if ($compilerExitCode -ne 0) {
    throw "Installer compilation failed with exit code $compilerExitCode"
}

Write-Host "Published app: $publishDir"
Write-Host "Installer:     $setupExe"

exit 0
