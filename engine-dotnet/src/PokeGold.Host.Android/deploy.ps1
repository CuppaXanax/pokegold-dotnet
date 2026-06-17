<#
.SYNOPSIS
    Deploy PokéGold .NET to a connected Android device and launch it.

.PARAMETER Device
    ADB device serial, mDNS serial, IP, or IP:port. If omitted, uses the default (single) device.

.PARAMETER Config
    Build configuration to deploy (default: Release).

.PARAMETER NoBuild
    Skip the build step — deploy the last-built APK directly.

.EXAMPLE
    .\deploy.ps1                                   # build + deploy to default device
    .\deploy.ps1 -Device 192.168.69.229:38483      # deploy to a specific device over WiFi
    .\deploy.ps1 -Device 192.168.69.229            # deploy to the current Wireless Debugging port for an IP
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

function Invoke-AdbCaptured {
    param([string[]]$Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $adb @Arguments 2>&1
        [PSCustomObject]@{
            ExitCode = $LASTEXITCODE
            Output = @($output)
        }
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Get-AdbDeviceList {
    $result = Invoke-AdbCaptured -Arguments @("devices")
    if ($result.ExitCode -ne 0) {
        Write-Error "Failed to list adb devices: $($result.Output -join [Environment]::NewLine)"
    }

    $result.Output |
        Select-Object -Skip 1 |
        Where-Object { $_ -match '\S' } |
        ForEach-Object {
            if ($_ -match '^(?<serial>\S+)\s+(?<state>\S+)') {
                [PSCustomObject]@{
                    Serial = $Matches.serial
                    State = $Matches.state
                }
            }
        }
}

function Get-AdbMdnsServiceList {
    $result = Invoke-AdbCaptured -Arguments @("mdns", "services")
    if ($result.ExitCode -ne 0) {
        Write-Error "Failed to list adb mDNS services: $($result.Output -join [Environment]::NewLine)"
    }

    $result.Output |
        Where-Object { $_ -match '^\S+\s+_adb.*-connect\._tcp\s+\S+:\d+$' } |
        ForEach-Object {
            $parts = $_ -split '\s+'
            [PSCustomObject]@{
                Instance = $parts[0]
                Service = $parts[1]
                Endpoint = $parts[2]
                Host = ($parts[2] -replace ':\d+$', '')
                Serial = "$($parts[0]).$($parts[1])"
            }
        }
}

function Resolve-AdbDevice {
    param([string]$RequestedDevice)

    if (-not $RequestedDevice) {
        return $null
    }

    $devices = @(Get-AdbDeviceList)
    $matchingDevice = $devices | Where-Object { $_.Serial -eq $RequestedDevice } | Select-Object -First 1
    if ($matchingDevice) {
        if ($matchingDevice.State -ne "device") {
            Write-Error "ADB device '$RequestedDevice' is '$($matchingDevice.State)', not ready."
        }

        return $RequestedDevice
    }

    $requestedHost = $RequestedDevice
    $requestedEndpoint = $null
    if ($RequestedDevice -match '^(?<host>.+):(?<port>\d+)$') {
        $requestedHost = $Matches.host
        $requestedEndpoint = $RequestedDevice
    }

    $mdnsServices = @(Get-AdbMdnsServiceList)
    $mdnsMatches = @()
    if ($requestedEndpoint) {
        $mdnsMatches = @($mdnsServices | Where-Object { $_.Endpoint -eq $requestedEndpoint })
    }
    if ($mdnsMatches.Count -eq 0) {
        $mdnsMatches = @($mdnsServices | Where-Object { $_.Host -eq $requestedHost })
    }

    $activeMdnsMatches = @($mdnsMatches | Where-Object {
        $service = $_
        $devices | Where-Object { $_.Serial -eq $service.Serial -and $_.State -eq "device" }
    })

    if ($activeMdnsMatches.Count -eq 1) {
        $resolved = $activeMdnsMatches[0]
        Write-Host "Resolved ADB target $RequestedDevice -> $($resolved.Serial) ($($resolved.Endpoint))" -ForegroundColor Yellow
        return $resolved.Serial
    }

    if ($activeMdnsMatches.Count -gt 1) {
        Write-Error "ADB target '$RequestedDevice' matches multiple active mDNS devices: $($activeMdnsMatches.Serial -join ', '). Use one of those serials explicitly."
    }

    if ($requestedEndpoint) {
        $connectResult = Invoke-AdbCaptured -Arguments @("connect", $RequestedDevice)
        if ($connectResult.ExitCode -eq 0) {
            Start-Sleep -Milliseconds 500
            $devices = @(Get-AdbDeviceList)
            $matchingDevice = $devices | Where-Object { $_.Serial -eq $RequestedDevice -and $_.State -eq "device" } | Select-Object -First 1
            if ($matchingDevice) {
                return $RequestedDevice
            }
        }
    }

    $connectedSummary = if ($devices.Count -gt 0) {
        ($devices | ForEach-Object { "$($_.Serial) [$($_.State)]" }) -join ', '
    } else {
        "none"
    }
    $mdnsSummary = if ($mdnsServices.Count -gt 0) {
        ($mdnsServices | ForEach-Object { "$($_.Serial) ($($_.Endpoint))" }) -join ', '
    } else {
        "none"
    }

    Write-Error "ADB device '$RequestedDevice' not found. Connected devices: $connectedSummary. mDNS services: $mdnsSummary."
}

$resolvedDevice = Resolve-AdbDevice $Device
$adbArgs = @()
if ($resolvedDevice) { $adbArgs = @("-s", $resolvedDevice) }

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
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Start-Sleep -Milliseconds 500

$launchResult = Invoke-AdbCaptured -Arguments ($adbArgs + @("shell", "monkey", "-p", "com.pokegold.engine", "-c", "android.intent.category.LAUNCHER", "1"))
if ($launchResult.ExitCode -ne 0) {
    Write-Error "Failed to launch com.pokegold.engine: $($launchResult.Output -join [Environment]::NewLine)"
}

Write-Host "PokéGold .NET deployed!" -ForegroundColor Green
