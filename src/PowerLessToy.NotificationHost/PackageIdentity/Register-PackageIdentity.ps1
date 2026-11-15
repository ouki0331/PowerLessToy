# ==============================================================================
# Registers Package Identity (Sparse Package) for PowerLessToy.NotificationHost
# Grants userNotificationListener capability without full MSIX installation
# ==============================================================================

[CmdletBinding()]
param (
    [switch]$Unregister
)

$manifestPath = Join-Path $PSScriptRoot "appxmanifest.xml"

if ($Unregister) {
    try {
        Get-AppxPackage -Name "PowerLessToy.NotificationHost" -ErrorAction SilentlyContinue | Remove-AppxPackage
        Write-Host "Unregistered PowerLessToy.NotificationHost package identity." -ForegroundColor Green
    } catch {
        Write-Warning "Failed to unregister package: $($_.Exception.Message)"
    }
    return
}

if (!(Test-Path $manifestPath)) {
    Write-Error "appxmanifest.xml was not found at $manifestPath"
    return
}

try {
    Write-Host "Registering Sparse Package identity with userNotificationListener capability..." -ForegroundColor Cyan
    Add-AppxPackage -Path $manifestPath -Register -DisableDevelopmentMode
    Write-Host "Successfully registered Package Identity for PowerLessToy.NotificationHost." -ForegroundColor Green
} catch {
    Write-Warning "Sparse package registration returned: $($_.Exception.Message)"
    Write-Host "Note: NotificationHost will still run. If UserNotificationListener access is denied in Windows Settings, it will seamlessly use the fallback window scanner." -ForegroundColor Cyan
}
