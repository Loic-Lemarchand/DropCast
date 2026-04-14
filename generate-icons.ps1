# generate-icons.ps1
# Generates all required icon sizes for Windows and Android from a source PNG.
# Prerequisites: ImageMagick CLI ('magick') must be on PATH for .ico creation.
# Usage: place icon_source.png (512x512+ transparent PNG) in project root, then run this script.

param(
    [string]$SourcePng = "icon_source.png"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourcePng)) {
    Write-Error "Source file '$SourcePng' not found. Place your high-res transparent PNG in the project root."
    exit 1
}

Add-Type -AssemblyName System.Drawing

function Resize-Png([string]$src, [string]$dest, [int]$size) {
    $original = [System.Drawing.Image]::FromFile((Resolve-Path $src))
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($original, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $original.Dispose()
    Write-Host "  Created: $dest ($size x $size)"
}

# --- Windows PNG sizes (for .ico packing) ---
$winDir = "icons\windows"
New-Item -ItemType Directory -Force -Path $winDir | Out-Null

$winSizes = @(16, 24, 32, 48, 256)
foreach ($s in $winSizes) {
    Resize-Png $SourcePng "$winDir\icon_${s}x${s}.png" $s
}

# --- Create .ico using ImageMagick ---
$icoPath = "DropCast.ico"
$magick = Get-Command magick -ErrorAction SilentlyContinue
if ($magick) {
    $pngs = $winSizes | ForEach-Object { "$winDir\icon_${_}x${_}.png" }
    & magick @pngs $icoPath
    Write-Host "  Created: $icoPath (multi-size .ico)"
} else {
    Write-Warning "ImageMagick ('magick') not found on PATH. Skipping .ico creation."
    Write-Warning "Install from https://imagemagick.org or manually combine PNGs into .ico"
}

# --- Android PNG sizes ---
$androidSizes = @{
    "mipmap-mdpi"    = 48
    "mipmap-hdpi"    = 72
    "mipmap-xhdpi"   = 96
    "mipmap-xxhdpi"  = 144
    "mipmap-xxxhdpi" = 192
}

foreach ($entry in $androidSizes.GetEnumerator()) {
    $dir = "DropCast.Android\Platforms\Android\Resources\$($entry.Key)"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Resize-Png $SourcePng "$dir\appicon.png" $entry.Value
}

# --- Play Store icon (512x512) ---
$storeDir = "icons\store"
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null
Resize-Png $SourcePng "$storeDir\play_store_512x512.png" 512

Write-Host ""
Write-Host "Done! Summary:"
Write-Host "  Windows .ico:     $icoPath"
Write-Host "  Windows PNGs:     $winDir\"
Write-Host "  Android mipmaps:  DropCast.Android\Platforms\Android\Resources\mipmap-*\"
Write-Host "  Play Store:       $storeDir\play_store_512x512.png"
