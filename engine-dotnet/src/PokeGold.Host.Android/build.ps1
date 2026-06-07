<#
.SYNOPSIS
    Build and optionally deploy PokeGold to an Android device via ADB.

.PARAMETER Deploy
    After building, install the APK on the connected device via adb.

.PARAMETER Config
    Build configuration (default: Release).

.EXAMPLE
    .\build.ps1              # build only
    .\build.ps1 -Deploy      # build + adb install
#>
param(
    [switch]$Deploy,
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"

# --- Resolve toolchain paths ---------------------------------------------------

$jdkDir = Get-ChildItem "C:\Program Files\Microsoft\jdk-17*" -Directory -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $jdkDir) {
    Write-Error "JDK 17 not found. Install via: winget install Microsoft.OpenJDK.17"
}

$sdkRoot = "$env:LOCALAPPDATA\Android\Sdk"
if (-not (Test-Path $sdkRoot)) {
    Write-Error "Android SDK not found at $sdkRoot. See engine-dotnet README for setup."
}

$env:JAVA_HOME = $jdkDir
$env:ANDROID_SDK_ROOT = $sdkRoot

Write-Host "JAVA_HOME       = $env:JAVA_HOME"
Write-Host "ANDROID_SDK_ROOT = $env:ANDROID_SDK_ROOT"
Write-Host ""

# --- Build & publish -----------------------------------------------------------

$projectDir = $PSScriptRoot
$projFile = Join-Path $projectDir "PokeGold.Host.Android.fsproj"

Write-Host "Publishing $Config APK..." -ForegroundColor Cyan
dotnet publish $projFile -c $Config --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$apk = Get-ChildItem (Join-Path $projectDir "bin\$Config\net9.0-android\publish") -Filter "*-Signed.apk" |
    Select-Object -First 1

if (-not $apk) {
    Write-Error "No signed APK found after publish."
}

Write-Host ""
Write-Host "APK: $($apk.FullName)  ($([math]::Round($apk.Length/1MB,1)) MB)" -ForegroundColor Green

# --- Deploy (optional) ---------------------------------------------------------

if ($Deploy) {
    $adb = Join-Path $sdkRoot "platform-tools\adb.exe"
    if (-not (Test-Path $adb)) {
        Write-Error "adb not found at $adb"
    }

    Write-Host ""
    Write-Host "Installing on device..." -ForegroundColor Cyan
    & $adb install -r $apk.FullName
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ""
    Write-Host "Launching..." -ForegroundColor Cyan
    & $adb shell am start -n "com.pokegold.engine/crc6450eaborttable.Activity"
    # If the activity hash doesn't match, use:
    #   adb shell monkey -p com.pokegold.engine 1
    Write-Host "Done!" -ForegroundColor Green
}
