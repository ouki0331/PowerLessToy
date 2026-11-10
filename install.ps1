# ==============================================================================
# PowerLessToy 一括ビルド & デプロイスクリプト (PowerLessToy Installer / Deploy Script)
# 対象: Windows 10 / 11 (x64 & ARM64) | .NET 10.0 対応
# ==============================================================================

[CmdletBinding()]
param (
    [ValidateSet("ARM64", "x64")]
    [string]$Architecture = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" })
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PowerLessToy 自動ビルド & デプロイツール (.NET 10.0)" -ForegroundColor Cyan
Write-Host " ターゲットアーキテクチャ: $Architecture" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $scriptDir "src"
$pluginsDir = Join-Path $env:LOCALAPPDATA "PowerToys\RunPlugins"

if (!(Test-Path $srcDir)) {
    Write-Error "ソースコードフォルダが見つかりません: $srcDir"
    exit 1
}

# 1. 出力先ディレクトリの準備
$targetNotify = Join-Path $pluginsDir "NotifyPosition"
$targetAccess = Join-Path $pluginsDir "AccessHistory"

if (!(Test-Path $targetNotify)) { New-Item -ItemType Directory -Force -Path $targetNotify | Out-Null }
if (!(Test-Path $targetAccess)) { New-Item -ItemType Directory -Force -Path $targetAccess | Out-Null }

# 2. 実行中の PowerToys プロセスを一時停止
Write-Host "`n[1/5] 実行中の PowerToys プロセスを終了しています..." -ForegroundColor Cyan
Stop-Process -Name "PowerToys*", "PowerLessToy*" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 3. AccessHistory WPF アプリのビルド & パブリッシュ
Write-Host "`n[2/5] AccessHistory.App をビルド中 (.NET 10.0, $Architecture)..." -ForegroundColor Cyan
$accessAppDir = Join-Path $srcDir "PowerLessToy.AccessHistory.App"
$tempPublishDir = Join-Path $env:TEMP "PowerLessToy_AccessHistory_Publish"
if (Test-Path $tempPublishDir) { Remove-Item -Path $tempPublishDir -Recurse -Force }

$rid = if ($Architecture -eq "ARM64") { "win-arm64" } else { "win-x64" }
dotnet publish $accessAppDir -c Release -p:Platform=$Architecture -r $rid --no-self-contained -o $tempPublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "AccessHistory.App のビルドに失敗しました。"
    exit 1
}

# 4. AccessHistory プラグインのビルド
Write-Host "`n[3/5] Community.PowerToys.Run.Plugin.AccessHistory をビルド中..." -ForegroundColor Cyan
$accessPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.AccessHistory"
dotnet build $accessPluginDir -c Release -p:Platform=$Architecture
if ($LASTEXITCODE -ne 0) {
    Write-Error "AccessHistory プラグインのビルドに失敗しました。"
    exit 1
}

# 5. NotifyPosition 2.0 プラグインのビルド
Write-Host "`n[4/5] Community.PowerToys.Run.Plugin.NotifyPosition をビルド中..." -ForegroundColor Cyan
$notifyPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.NotifyPosition"
dotnet build $notifyPluginDir -c Release -p:Platform=$Architecture
if ($LASTEXITCODE -ne 0) {
    Write-Error "NotifyPosition プラグインのビルドに失敗しました。"
    exit 1
}

# 6. プラグインフォルダへ配置
Write-Host "`n[5/5] プラグインディレクトリへファイルを配置しています..." -ForegroundColor Cyan

# AccessHistory の配置
Copy-Item -Path "$tempPublishDir\*" -Destination $targetAccess -Recurse -Force
$accessBinDir = Join-Path $accessPluginDir "bin\$Architecture\Release\net10.0-windows10.0.26100.0"
Copy-Item -Path "$accessBinDir\*" -Destination $targetAccess -Recurse -Force
$nativeArch = if ($Architecture -eq "ARM64") { "win-arm64" } else { "win-x64" }
$sqliteNative = Join-Path $targetAccess "runtimes\$nativeArch\native\e_sqlite3.dll"
if (Test-Path $sqliteNative) {
    Copy-Item $sqliteNative -Destination $targetAccess -Force
}

# NotifyPosition の配置
$notifyBinDir = Join-Path $notifyPluginDir "bin\$Architecture\Release\net10.0-windows10.0.26100.0"
Copy-Item -Path "$notifyBinDir\*" -Destination $targetNotify -Recurse -Force

# デスクトップにショートカットを作成 (AccessHistory)
try {
    $wscript = New-Object -ComObject WScript.Shell
    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "AccessHistory.lnk"
    $shortcut = $wscript.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $targetAccess "PowerLessToy.AccessHistory.App.exe"
    $iconPath = Join-Path $targetAccess "Images\history.dark.png"
    if (Test-Path $iconPath) {
        $shortcut.IconLocation = $iconPath
    }
    $shortcut.Save()
    Write-Host "デスクトップショートカットを作成しました: $shortcutPath" -ForegroundColor Green
} catch {
    Write-Warning "ショートカットの作成をスキップしました。"
}

# 7. PowerToys の再起動
$ptExe = Join-Path $env:LOCALAPPDATA "PowerToys\PowerToys.exe"
if (Test-Path $ptExe) {
    Write-Host "`nPowerToys を再起動しています..." -ForegroundColor Green
    Start-Process $ptExe
}

# 8. インストール結果の診断情報の出力 (Diagnostics Report)
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [診断情報] PowerLessToy / AccessHistory インストール結果" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$logDir = Join-Path $env:LOCALAPPDATA "PowerLessToy\AccessHistory\Logs"
$accessDll = Join-Path $targetAccess "Community.PowerToys.Run.Plugin.AccessHistory.dll"
$accessExe = Join-Path $targetAccess "PowerLessToy.AccessHistory.App.exe"
$sqliteFile = Join-Path $targetAccess "e_sqlite3.dll"

Write-Host "・プラグイン配置先:     $targetAccess" -ForegroundColor White
Write-Host "・インストールアーキ:   $Architecture" -ForegroundColor White

# PowerToys バージョン
$ptVersion = "未検出 (PowerToys.exe が見つかりません)"
if (Test-Path $ptExe) {
    try {
        $ptVersionInfo = (Get-Item $ptExe).VersionInfo
        $ptVersion = "$($ptVersionInfo.ProductVersion) (File: $($ptVersionInfo.FileVersion))"
    } catch {
        $ptVersion = "取得失敗 ($($_))"
    }
}
Write-Host "・PowerToys バージョン: $ptVersion" -ForegroundColor White

# .NET ランタイム一覧
Write-Host "・インストール済み .NET ランタイム:" -ForegroundColor White
try {
    $runtimes = dotnet --list-runtimes 2>$null
    if ($runtimes) {
        $runtimes | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    } else {
        Write-Host "    (dotnet コマンドからランタイム一覧を取得できませんでした)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "    (dotnet コマンドの実行に失敗しました)" -ForegroundColor Yellow
}

# ファイル存在チェック
Write-Host "・ファイル存在確認:" -ForegroundColor White
$checkFiles = @(
    @{ Name = "AccessHistory プラグイン DLL"; Path = $accessDll },
    @{ Name = "AccessHistory アプリ EXE"; Path = $accessExe },
    @{ Name = "SQLite Native (e_sqlite3.dll)"; Path = $sqliteFile }
)
foreach ($cf in $checkFiles) {
    $exists = Test-Path $cf.Path
    $statusColor = if ($exists) { "Green" } else { "Red" }
    $statusText = if ($exists) { "[OK] 存在します" } else { "[NG] 見つかりません" }
    Write-Host ("    {0,-30}: {1}" -f $cf.Name, $statusText) -ForegroundColor $statusColor
}

Write-Host "・AccessHistory ログ保存先:" -ForegroundColor White
Write-Host "    $logDir" -ForegroundColor Cyan

Write-Host "`n[診断とトラブルシューティングのヒント]:" -ForegroundColor Yellow
Write-Host "1. AccessHistory パネル (Local / SMB / WSL) に履歴が表示されない場合:" -ForegroundColor White
Write-Host "   - ログディレクトリ ($logDir) 内の最新ログファイルを確認してください。" -ForegroundColor DarkGray
Write-Host "   - ログには COM (Shell.Application) 呼び出し、SQLite 読み書き、Explorer 監視の全成否がミリ秒単位で記録されます。" -ForegroundColor DarkGray
Write-Host "2. 独立アプリを手動起動してテストする場合:" -ForegroundColor White
Write-Host "   - `"$accessExe`" を直接実行してください。画面右下の『📁 ログを開く』からワンクリックでログを参照できます。" -ForegroundColor DarkGray
Write-Host "3. PowerToys Run にプラグインが読み込まれない場合:" -ForegroundColor White
Write-Host "   - PowerToys のメインウィンドウまたはログを確認し、.NET 10.0 Desktop Runtime のロードエラーがないか確認してください。" -ForegroundColor DarkGray

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " インストールが正常に完了しました！" -ForegroundColor Green
Write-Host " - NotifyPosition 2.0: PowerToys Run で 'notify' または 'notify test' と入力" -ForegroundColor White
Write-Host " - VirtualDesktopLoop: 常駐自動起動 (Win + Ctrl + 左右 で仮想デスクトップ端点循環 / 'vdesk' で状態確認)" -ForegroundColor White
Write-Host " - AccessHistory: PowerToys Run で 'his' と入力、またはデスクトップのアイコンをクリック" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
