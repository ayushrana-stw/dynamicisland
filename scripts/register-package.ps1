<#
.SYNOPSIS
  Registers Dynamic Island with Windows so it can read notifications from other apps.

.DESCRIPTION
  Windows only lets apps with a package identity use the notification listener.
  This registers packaging/AppxManifest.xml as a sparse package that points at the
  built DynamicIsland.exe. Run it once per build output folder (Debug or Release).

  Requires Developer Mode: Settings > System > For developers > Developer Mode.
  No admin rights needed. Undo with:  .\scripts\register-package.ps1 -Unregister

.EXAMPLE
  .\scripts\register-package.ps1
  .\scripts\register-package.ps1 -Configuration Release
  .\scripts\register-package.ps1 -ExePath D:\Apps\DynamicIsland
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    # Folder containing DynamicIsland.exe; defaults to the build output for -Configuration.
    [string] $ExePath,

    [switch] $Unregister
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packageDir = Join-Path $root 'packaging'

$existing = Get-AppxPackage -Name 'DynamicIsland' -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Removing the previous registration...'
    Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
    $existing | Remove-AppxPackage
}
if ($Unregister) {
    Write-Host 'Unregistered.' -ForegroundColor Green
    return
}

# 1. Developer Mode lets Windows accept this locally built, unsigned package.
$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) {
    Write-Host 'Developer Mode is off.' -ForegroundColor Yellow
    Write-Host 'Turn it on in Settings > System > For developers > Developer Mode, then run this script again.'
    Start-Process 'ms-settings:developers'
    exit 1
}

# 2. Find the built exe.
if (-not $ExePath) {
    $ExePath = Join-Path $root "src\DynamicIsland.App\bin\$Configuration\net9.0-windows10.0.19041.0"
}
if (-not (Test-Path (Join-Path $ExePath 'DynamicIsland.exe'))) {
    Write-Host "DynamicIsland.exe not found in $ExePath. Build first: dotnet build -c $Configuration" -ForegroundColor Yellow
    exit 1
}
$ExePath = (Resolve-Path $ExePath).Path

# 3. Package logos (generated once; Windows shows them in the notification-access settings).
$images = Join-Path $packageDir 'Images'
if (-not (Test-Path (Join-Path $images 'Square150x150Logo.png'))) {
    New-Item -ItemType Directory -Force $images | Out-Null
    Add-Type -AssemblyName System.Drawing
    foreach ($logo in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
        $size = $logo[1]
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        $h = [math]::Round($size * 0.42); $w = [math]::Round($size * 0.9)
        $x = ($size - $w) / 2; $y = ($size - $h) / 2; $r = $h
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, $r, $h, 90, 180); $path.AddArc($x + $w - $r, $y, $r, $h, 270, 180); $path.CloseFigure()
        $g.FillPath([System.Drawing.Brushes]::Black, $path)
        $dot = [math]::Max(3, [math]::Round($size * 0.12))
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(52, 199, 89))), $x + $w - $r / 2 - $dot, $y + ($h - $dot) / 2, $dot, $dot)
        $bmp.Save((Join-Path $images "$($logo[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    }
}

# 4. Register.
Write-Host "Registering Dynamic Island for $ExePath ..."
Add-AppxPackage -Register (Join-Path $packageDir 'AppxManifest.xml') -ExternalLocation $ExePath
Write-Host 'Done. Start Dynamic Island and allow notification access when Windows asks.' -ForegroundColor Green
