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

PowerLessToy は、Windows 10/11 向けの軽量かつ高機能な PowerToys 拡張プラグイン & ユーティリティ集です（.NET 10.0 対応）。

## 🌟 主な収録モジュール
- **NotifyPosition 2.0**: Windows 標準通知をモダンな深色カード通知へリプレイス。「承認・却下 (Approve / Reject)」アクションの完全プロキシ実行に対応。
- **AccessHistory**: 外部依存ゼロの専用ファイルロガーを内蔵し、エクスプローラーのアクセス履歴（Local / SMB / WSL）を自動記録・分類・あいまい検索。
- **VirtualDesktopLoop**: 仮想デスクトップの端点制限を解消し、`Win + Ctrl + 左右` でのシームレスな無限ループ切替を実現。

## 📦 対応アーキテクチャ
- **Windows 10 / 11 x64** (Intel / AMD)
- **Windows 11 ARM64** (Snapdragon X / Apple Silicon 仮想マシン等)

## 🚀 インストール手順 (Quick Install)
1. 動作環境に合わせたアーカイブ（``PowerLessToy-$Tag-win-x64.zip`` または ``PowerLessToy-$Tag-win-arm64.zip``）をダウンロードして展開します。
2. 展開先フォルダで PowerShell を起動し、以下のスクリプトを実行します：
```powershell
.\install.ps1
```
※ 詳しい機能説明および手動デプロイ手順については、同梱の ``README.md`` および ``DEPLOYMENT.md`` をご参照ください。
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
