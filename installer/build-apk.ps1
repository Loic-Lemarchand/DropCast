<#
.SYNOPSIS
    Builds the DropCast Android MAUI app into a signed APK.

.DESCRIPTION
    1. Creates a release keystore if one doesn't exist yet
    2. Publishes the .NET MAUI Android project in Release mode
    3. Copies the final signed APK to installer\output\

.PARAMETER KeystorePassword
    Password for the signing keystore. Defaults to "DropCast2025".
    Change this for production builds.

.PARAMETER SkipKeystoreCreation
    Skip keystore creation (use existing keystore).

.EXAMPLE
    .\build-apk.ps1
    .\build-apk.ps1 -KeystorePassword "MySecurePass123"
#>
param(
    [string]$KeystorePassword = "DropCast2025",
    [switch]$SkipKeystoreCreation
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot          # repo root
$ProjectDir = Join-Path $Root "DropCast.Android"
$ProjectFile = Join-Path $ProjectDir "DropCast.Android.csproj"
$KeystoreDir = Join-Path $ProjectDir "keystore"
$KeystorePath = Join-Path $KeystoreDir "dropcast.keystore"
$KeyAlias = "dropcast"
$OutputDir = Join-Path $Root "installer\output"

Write-Host "=== DropCast APK Builder ===" -ForegroundColor Cyan

# --- Locate JDK keytool ---
$keytool = $null
$jdkBase = Join-Path $env:LOCALAPPDATA "Microsoft\jdk"
if (Test-Path $jdkBase) {
    $keytool = Get-ChildItem -Path $jdkBase -Recurse -Filter "keytool.exe" | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $keytool) {
    # Fallback: try JAVA_HOME
    if ($env:JAVA_HOME -and (Test-Path "$env:JAVA_HOME\bin\keytool.exe")) {
        $keytool = "$env:JAVA_HOME\bin\keytool.exe"
    }
}

# --- Create keystore if needed ---
if (-not $SkipKeystoreCreation -and -not (Test-Path $KeystorePath)) {
    if (-not $keytool) {
        Write-Error "keytool.exe not found. Install a JDK or set JAVA_HOME."
    }
    Write-Host "`n--- Creating release keystore ---" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $KeystoreDir -Force | Out-Null

    & $keytool -genkeypair -v `
        -keystore $KeystorePath `
        -alias $KeyAlias `
        -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $KeystorePassword `
        -keypass $KeystorePassword `
        -dname "CN=DropCast, O=DropCast, L=Unknown, ST=Unknown, C=FR"

    if ($LASTEXITCODE -ne 0) { Write-Error "Keystore creation failed." }
    Write-Host "Keystore created: $KeystorePath" -ForegroundColor Green
    Write-Host "IMPORTANT: Keep this keystore safe! You need the same keystore for all future updates." -ForegroundColor Yellow
} elseif (Test-Path $KeystorePath) {
    Write-Host "Using existing keystore: $KeystorePath" -ForegroundColor DarkGray
} else {
    Write-Host "Skipping keystore creation (--SkipKeystoreCreation)" -ForegroundColor Yellow
}

# --- Build signed APK ---
Write-Host "`n--- Publishing Release APK ---" -ForegroundColor Cyan
dotnet publish $ProjectFile `
    -f net10.0-android `
    -c Release `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=$KeystorePath `
    -p:AndroidSigningKeyAlias=$KeyAlias `
    -p:AndroidSigningKeyPass=$KeystorePassword `
    -p:AndroidSigningStorePass=$KeystorePassword

if ($LASTEXITCODE -ne 0) { Write-Error "APK build failed." }

# --- Find and copy the signed APK ---
Write-Host "`n--- Locating signed APK ---" -ForegroundColor Cyan
$publishDir = Join-Path $ProjectDir "bin\Release\net10.0-android\publish"
$apk = Get-ChildItem -Path $publishDir -Filter "*-Signed.apk" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $apk) {
    # Fallback: look in the broader bin output
    $apk = Get-ChildItem -Path (Join-Path $ProjectDir "bin\Release") -Filter "*-Signed.apk" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $apk) {
    # Last resort: any .apk
    $apk = Get-ChildItem -Path (Join-Path $ProjectDir "bin\Release") -Filter "*.apk" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $apk) {
    Write-Error "Could not find the built APK. Check the build output above."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$destApk = Join-Path $OutputDir "DropCast.apk"
Copy-Item -Path $apk.FullName -Destination $destApk -Force

Write-Host "`n=== Done! ===" -ForegroundColor Green
Write-Host "APK: $destApk  ($([math]::Round((Get-Item $destApk).Length / 1MB, 1)) MB)" -ForegroundColor Green
