<#
.SYNOPSIS
    Deploy PokéGold .NET to a connected Android device and launch it.

.PARAMETER Device
    ADB device serial or IP:port. If omitted, uses the default (single) device.

.PARAMETER Config
    Build configuration to deploy (default: Release).

.PARAMETER NoBuild
    Skip the build step — deploy the last-built APK directly.

.EXAMPLE
    .\deploy.ps1                                   # build + deploy to default device
    .\deploy.ps1 -Device 192.168.69.229:38483      # deploy to a specific device over WiFi
    .\deploy.ps1 -NoBuild                          # skip build, just push + launch
#>
param(
    [string]$Device,
    [string]$Config = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

# --- Resolve toolchain paths ---------------------------------------------------

$sdkRoot = "$env:LOCALAPPDATA\Android\Sdk"
$adb = Join-Path $sdkRoot "platform-tools\adb.exe"
if (-not (Test-Path $adb)) {
    Write-Error "adb not found at $adb. Install Android SDK platform-tools."
}

$adbArgs = @()
if ($Device) { $adbArgs = @("-s", $Device) }

# --- Build (unless -NoBuild) ---------------------------------------------------

if (-not $NoBuild) {
    $jdkDir = Get-ChildItem "C:\Program Files\Microsoft\jdk-17*" -Directory -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $jdkDir) { Write-Error "JDK 17 not found. Install via: winget install Microsoft.OpenJDK.17" }

    $env:JAVA_HOME = $jdkDir
    $env:ANDROID_SDK_ROOT = $sdkRoot

    $engineDir = (Resolve-Path "$PSScriptRoot\..\..\..").Path
    $gameProj = Join-Path $engineDir "engine-dotnet\src\PokeGold.Game\PokeGold.Game.fsproj"
    $androidProj = Join-Path $PSScriptRoot "PokeGold.Host.Android.fsproj"

    Write-Host "Building Game..." -ForegroundColor Cyan
    dotnet build $gameProj -c $Config --nologo -q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Publishing APK..." -ForegroundColor Cyan
    dotnet publish $androidProj -c $Config --nologo -q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# --- Find APK ------------------------------------------------------------------

$apk = Get-ChildItem (Join-Path $PSScriptRoot "bin\$Config\net9.0-android\publish") -Filter "*-Signed.apk" |
    Select-Object -First 1

if (-not $apk) { Write-Error "No signed APK found. Run without -NoBuild first." }
Write-Host "APK: $($apk.Name)  ($([math]::Round($apk.Length/1MB,1)) MB)" -ForegroundColor Green

# --- Deploy + Launch ------------------------------------------------------------

Write-Host "Installing..." -ForegroundColor Cyan
& $adb @adbArgs install -r $apk.FullName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Launching..." -ForegroundColor Cyan
& $adb @adbArgs shell am force-stop com.pokegold.engine
Start-Sleep -Milliseconds 500
& $adb @adbArgs shell monkey -p com.pokegold.engine -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null

Write-Host "PokéGold .NET deployed!" -ForegroundColor Green
