# ==============================================================================
# PowerLessToy Release Packaging Script
# Compiles x64 & ARM64 targets and creates release zip archives
# ==============================================================================

[CmdletBinding()]
param (
    [string]$Version = "0.1.0",
    [string]$OutputDir = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"

$architectures = @("x64", "ARM64")
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$srcDir = Join-Path $PSScriptRoot "src"
$accessAppDir = Join-Path $srcDir "PowerLessToy.AccessHistory.App"
$accessPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.AccessHistory"
$notifyPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.NotifyPosition"

foreach ($arch in $architectures) {
    Write-Host "=== Packaging for $arch ===" -ForegroundColor Cyan
    $rid = if ($arch -eq "ARM64") { "win-arm64" } else { "win-x64" }
    $stageDir = Join-Path $OutputDir "stage_$arch"
    if (Test-Path $stageDir) { Remove-Item -Path $stageDir -Recurse -Force }
    
    $stageAccess = Join-Path $stageDir "AccessHistory"
    $stageNotify = Join-Path $stageDir "NotifyPosition"
    New-Item -ItemType Directory -Force -Path $stageAccess | Out-Null
    New-Item -ItemType Directory -Force -Path $stageNotify | Out-Null

    # 1. Publish AccessHistory.App
    Write-Host "Publishing AccessHistory.App ($rid)..."
    dotnet publish $accessAppDir -c Release -p:Platform=$arch -r $rid --no-self-contained -o $stageAccess
    if ($LASTEXITCODE -ne 0) { throw "AccessHistory.App publish failed." }

    # 2. Build AccessHistory Plugin
    Write-Host "Building AccessHistory plugin ($arch)..."
    dotnet build $accessPluginDir -c Release -p:Platform=$arch
    if ($LASTEXITCODE -ne 0) { throw "AccessHistory build failed." }
    $accessBinDir = Join-Path $accessPluginDir "bin\$arch\Release\net10.0-windows10.0.26100.0"
    Copy-Item -Path "$accessBinDir\*" -Destination $stageAccess -Recurse -Force

    # Ensure e_sqlite3.dll is at root of AccessHistory
    $sqliteNative = Join-Path $stageAccess "runtimes\$rid\native\e_sqlite3.dll"
    if (Test-Path $sqliteNative) {
        Copy-Item $sqliteNative -Destination $stageAccess -Force
    }

    # 3. Build NotifyPosition Plugin
    Write-Host "Building NotifyPosition plugin ($arch)..."
    dotnet build $notifyPluginDir -c Release -p:Platform=$arch
    if ($LASTEXITCODE -ne 0) { throw "NotifyPosition build failed." }
    $notifyBinDir = Join-Path $notifyPluginDir "bin\$arch\Release\net10.0-windows10.0.26100.0"
    Copy-Item -Path "$notifyBinDir\*" -Destination $stageNotify -Recurse -Force

    # 4. Copy helper scripts & docs into stage
    Copy-Item (Join-Path $PSScriptRoot "install.ps1") -Destination $stageDir -Force
    Copy-Item (Join-Path $PSScriptRoot "README.md") -Destination $stageDir -Force
    Copy-Item (Join-Path $PSScriptRoot "DEPLOYMENT.md") -Destination $stageDir -Force

    # 5. Create ZIPs
    $allZip = Join-Path $OutputDir "PowerLessToy-v$Version-$rid.zip"
    $accessZip = Join-Path $OutputDir "AccessHistory-v$Version-$rid.zip"
    $notifyZip = Join-Path $OutputDir "NotifyPosition-v$Version-$rid.zip"

    if (Test-Path $allZip) { Remove-Item $allZip -Force }
    if (Test-Path $accessZip) { Remove-Item $accessZip -Force }
    if (Test-Path $notifyZip) { Remove-Item $notifyZip -Force }

    Write-Host "Compressing $allZip..."
    Compress-Archive -Path "$stageDir\*" -DestinationPath $allZip -Force

    Write-Host "Compressing $accessZip..."
    Compress-Archive -Path "$stageAccess\*" -DestinationPath $accessZip -Force

    Write-Host "Compressing $notifyZip..."
    Compress-Archive -Path "$stageNotify\*" -DestinationPath $notifyZip -Force

    Remove-Item -Path $stageDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nAll packages created in $OutputDir:" -ForegroundColor Green
Get-ChildItem -Path $OutputDir -Filter "*.zip" | Select-Object Name, Length
