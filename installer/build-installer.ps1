<#
.SYNOPSIS
    Builds DropCast in Release mode and compiles the Inno Setup installer.

.DESCRIPTION
    1. Restores NuGet packages
    2. Builds the project in Release configuration
    3. Compiles the installer using Inno Setup (ISCC.exe)

    The resulting installer is placed in installer\output\

.PARAMETER SkipBuild
    Skip the MSBuild step (use existing bin\Release output).

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -SkipBuild
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot  # repo root (one level up from installer/)

Write-Host "=== DropCast Installer Builder ===" -ForegroundColor Cyan

# --- Locate tools ---

# MSBuild
$msbuild = $null
$vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswherePath) {
    $msbuild = & $vswherePath -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Error "MSBuild not found. Please install Visual Studio with the .NET desktop development workload."
}
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

# NuGet
$nuget = Join-Path $Root "installer\.nuget\nuget.exe"
if (-not (Test-Path $nuget)) {
    Write-Host "Downloading nuget.exe..." -ForegroundColor Yellow
    $nugetDir = Join-Path $Root "installer\.nuget"
    New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
}

# Inno Setup
$iscc = $null
foreach ($candidate in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)) {
    if (Test-Path $candidate) { $iscc = $candidate; break }
}
if (-not $iscc) {
    Write-Error @"
Inno Setup 6 not found.
Download the free installer from: https://jrsoftware.org/isdl.php
Then re-run this script.
"@
}
Write-Host "ISCC:    $iscc" -ForegroundColor DarkGray

# --- Build ---
if (-not $SkipBuild) {
    Write-Host "`n--- Restoring NuGet packages ---" -ForegroundColor Cyan
    & $nuget restore (Join-Path $Root "DropCast.csproj") -PackagesDirectory (Join-Path $Root "packages")
    if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed." }

    Write-Host "`n--- Building Release ---" -ForegroundColor Cyan
    & $msbuild (Join-Path $Root "DropCast.csproj") /p:Configuration=Release /verbosity:minimal /t:Build
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed." }
} else {
    Write-Host "`n--- Skipping build (using existing bin\Release) ---" -ForegroundColor Yellow
}

# --- Verify build output ---
$exe = Join-Path $Root "bin\Release\DropCast.exe"
if (-not (Test-Path $exe)) {
    Write-Error "bin\Release\DropCast.exe not found. Build may have failed."
}

# --- Compile installer ---
Write-Host "`n--- Compiling installer ---" -ForegroundColor Cyan
$issFile = Join-Path $Root "installer\DropCast.iss"
& $iscc $issFile
if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed." }

$outputDir = Join-Path $Root "installer\output"
Write-Host "`n=== Done! ===" -ForegroundColor Green
Write-Host "Installer: $outputDir" -ForegroundColor Green
Get-ChildItem $outputDir -Filter "*.exe" | ForEach-Object {
    Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green
}
