<#
.SYNOPSIS
  Installs what the Android app needs to build: the .NET Android workload, the Android SDK and a JDK.

.DESCRIPTION
  Requires the .NET 10 SDK (Avalonia 12's Android support needs it; the desktop app stays on .NET 9).
  The Android SDK goes to %LOCALAPPDATA%\Android\Sdk (Android Studio's default) and the JDK to
  %LOCALAPPDATA%\Android\jdk. src/DynamicIsland.Android picks both up automatically.

  -Emulator also downloads an Android 15 emulator image and creates the "island_test" virtual phone.

.EXAMPLE
  .\scripts\setup-android.ps1
  .\scripts\setup-android.ps1 -Emulator
#>
param(
    [switch] $Emulator
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\DynamicIsland.Android\DynamicIsland.Android.csproj'
$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$jdk = Join-Path $env:LOCALAPPDATA 'Android\jdk'

if (-not (dotnet --list-sdks | Select-String '^10\.')) {
    throw 'The .NET 10 SDK is required. Install it with:  winget install Microsoft.DotNet.SDK.10'
}

Write-Host 'Installing the .NET Android workload...'
dotnet workload install android
if ($LASTEXITCODE -ne 0) { throw 'Workload install failed.' }

Write-Host "Installing the Android SDK to $sdk and a JDK to $jdk..."
dotnet build $project -t:InstallAndroidDependencies -f net10.0-android `
    "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk" -p:AcceptAndroidSDKLicenses=True
if ($LASTEXITCODE -ne 0) { throw 'Android SDK install failed.' }

if ($Emulator) {
    $env:JAVA_HOME = $jdk
    $image = 'system-images;android-35;google_apis;x86_64'
    $tools = Join-Path $sdk 'cmdline-tools\latest\bin'

    Write-Host 'Installing the emulator and an Android 15 image (about 1.5 GB)...'
    & "$tools\sdkmanager.bat" --sdk_root=$sdk --install emulator $image
    'no' | & "$tools\avdmanager.bat" create avd --name island_test --package $image --device pixel_7 --force

    Write-Host "Start it with:  & `"$sdk\emulator\emulator.exe`" -avd island_test"
}

Write-Host 'Done. Build and run on a connected phone or emulator with:'
Write-Host '  dotnet build src/DynamicIsland.Android -t:Run'
