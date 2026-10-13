# ==============================================================================
# PowerLessToy build and deployment script
# Primary target: Windows 11 x64 | Compatibility target: Windows 11 ARM64
# ==============================================================================

[CmdletBinding()]
param (
    [ValidateSet("x64", "ARM64")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $scriptDir "src"
$pluginsDir = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\PowerToys Run\Plugins"
$targetNotify = Join-Path $pluginsDir "NotifyPosition"
$targetAccess = Join-Path $pluginsDir "AccessHistory"
$targetFramework = "net10.0-windows10.0.22621.0"
$rid = if ($Architecture -eq "ARM64") { "win-arm64" } else { "win-x64" }
$tempPublishDir = Join-Path $env:TEMP "PowerLessToy_AccessHistory_Publish_$Architecture"
$exitCode = 0

function Find-PowerToysExecutable {
    $running = Get-Process -Name "PowerToys" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($running) {
        try {
            if ($running.Path -and (Test-Path $running.Path)) { return $running.Path }
        } catch { }
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "PowerToys\PowerToys.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\PowerToys\PowerToys.exe"),
        (Join-Path $env:ProgramFiles "PowerToys\PowerToys.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Start-PowerToysIfAvailable([string]$Executable) {
    if ($Executable -and (Test-Path $Executable)) {
        Write-Host "Starting PowerToys..." -ForegroundColor Green
        Start-Process $Executable
    } else {
        Write-Warning "PowerToys.exe was not found. Start PowerToys manually after installation."
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PowerLessToy build and deployment (.NET 10)" -ForegroundColor Cyan
Write-Host " Primary platform: x64 | Selected platform: $Architecture" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

if (!(Test-Path $srcDir)) {
    throw "Source directory not found: $srcDir"
}

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or !$sdkVersion.StartsWith("10.")) {
    throw ".NET 10 SDK is required. Detected SDK: $sdkVersion"
}

$powerToysExe = Find-PowerToysExecutable

try {
    New-Item -ItemType Directory -Force -Path $targetNotify, $targetAccess | Out-Null

    Write-Host "`n[1/5] Stopping PowerToys..." -ForegroundColor Cyan
    Stop-Process -Name "PowerToys*", "PowerLessToy*" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    Write-Host "`n[2/5] Publishing AccessHistory.App ($rid)..." -ForegroundColor Cyan
    if (Test-Path $tempPublishDir) {
        Remove-Item -Path $tempPublishDir -Recurse -Force
    }
    $accessAppDir = Join-Path $srcDir "PowerLessToy.AccessHistory.App"
    & dotnet publish $accessAppDir -c Release -p:Platform=$Architecture -r $rid --no-self-contained -o $tempPublishDir
    if ($LASTEXITCODE -ne 0) { throw "AccessHistory.App publish failed." }

    Write-Host "`n[3/5] Building AccessHistory plugin..." -ForegroundColor Cyan
    $accessPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.AccessHistory"
    & dotnet build $accessPluginDir -c Release -p:Platform=$Architecture
    if ($LASTEXITCODE -ne 0) { throw "AccessHistory plugin build failed." }

    Write-Host "`n[4/5] Building NotifyPosition plugin..." -ForegroundColor Cyan
    $notifyPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.NotifyPosition"
    & dotnet build $notifyPluginDir -c Release -p:Platform=$Architecture
    if ($LASTEXITCODE -ne 0) { throw "NotifyPosition plugin build failed." }

    Write-Host "`n[5/5] Deploying plugin files..." -ForegroundColor Cyan
    Copy-Item -Path "$tempPublishDir\*" -Destination $targetAccess -Recurse -Force

    $accessBinDir = Join-Path $accessPluginDir "bin\$Architecture\Release\$targetFramework"
    $notifyBinDir = Join-Path $notifyPluginDir "bin\$Architecture\Release\$targetFramework"
    Copy-Item -Path "$accessBinDir\*" -Destination $targetAccess -Recurse -Force
    Copy-Item -Path "$notifyBinDir\*" -Destination $targetNotify -Recurse -Force

    $sqliteNative = Join-Path $targetAccess "runtimes\$rid\native\e_sqlite3.dll"
    if (Test-Path $sqliteNative) {
        Copy-Item $sqliteNative -Destination $targetAccess -Force
    }

    try {
        $wscript = New-Object -ComObject WScript.Shell
        $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "AccessHistory.lnk"
        $shortcut = $wscript.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $targetAccess "PowerLessToy.AccessHistory.App.exe"
        $shortcut.WorkingDirectory = $targetAccess
        $shortcut.Save()
        Write-Host "Desktop shortcut created: $shortcutPath" -ForegroundColor Green
    } catch {
        Write-Warning "Desktop shortcut creation was skipped: $($_.Exception.Message)"
    }
} catch {
    $exitCode = 1
    Write-Error $_
} finally {
    Start-PowerToysIfAvailable $powerToysExe
    if (Test-Path $tempPublishDir) {
        Remove-Item -Path $tempPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($exitCode -ne 0) { exit $exitCode }

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Installation completed successfully." -ForegroundColor Green
Write-Host " Plugin directory: $pluginsDir" -ForegroundColor White
Write-Host " NotifyPosition: type 'notify' or 'notify test' in PowerToys Run" -ForegroundColor White
Write-Host " AccessHistory: type 'his' in PowerToys Run" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
