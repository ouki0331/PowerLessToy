# ==============================================================================
# PowerLessToy GitHub Release Publisher Script
# ==============================================================================

[CmdletBinding()]
param (
    [string]$Tag = "v0.1.0",
    [string]$DistDir = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"

Write-Host "Creating/Updating GitHub Release for tag: $Tag"

$notes = @"
# PowerLessToy $Tag

## 🌟 包含组件 (Components)
- **NotifyPosition 2.0**: Windows 原生通知深色卡片置换与「同意/驳回」一键代理。
- **AccessHistory**: 零依赖高性能文件日志诊断器、Explorer COM 访问追踪与 Local / SMB / WSL 三栏面板。
- **VirtualDesktopLoop**: 虚拟桌面无限循环切换（Win + Ctrl + 左右）。

## 📦 架构支持 (Architectures)
- Windows 10/11 x64 (Intel / AMD)
- Windows 11 ARM64 (Snapdragon X / Apple Silicon 虚拟机)

## 🚀 安装方法 (Quick Install)
解压 ``PowerLessToy-$Tag-win-x64.zip``（或 ``win-arm64``），右键管理员或普通 PowerShell 运行：
```powershell
.\install.ps1
```
"@

# Check if release exists
& gh release view $Tag 2>$null | Out-Null
$exists = ($LASTEXITCODE -eq 0)

$zipFiles = @(Get-ChildItem -Path (Join-Path $DistDir "*.zip") | ForEach-Object { $_.FullName })
Write-Host "Found $($zipFiles.Count) release packages to upload."

$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "release_notes_$Tag.md"
[System.IO.File]::WriteAllText($notesFile, $notes, [System.Text.Encoding]::UTF8)

if ($exists) {
    Write-Host "Release $Tag already exists, uploading assets..."
    & gh release upload $Tag $zipFiles --clobber
} else {
    Write-Host "Creating release $Tag..."
    & gh release create $Tag $zipFiles --title "PowerLessToy $Tag" --notes-file $notesFile
}
