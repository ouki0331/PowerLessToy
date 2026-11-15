# ==============================================================================
# PowerLessToy 一括ビルド & デプロイスクリプト (PowerLessToy Installer / Deploy Script)
# 対象: Windows 10 / 11 (x64 & ARM64) | PowerToys 0.102+ & .NET 10.0 対応
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

# 1. PowerToys プラグインディレクトリの検出 (Modern vs Legacy)
$modernPluginsDir = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\PowerToys Run\Plugins"
$legacyPluginsDir = Join-Path $env:LOCALAPPDATA "PowerToys\RunPlugins"

$primaryPluginsDir = $modernPluginsDir
if (!(Test-Path $primaryPluginsDir)) {
    if (Test-Path $legacyPluginsDir) {
        $primaryPluginsDir = $legacyPluginsDir
    } else {
        New-Item -ItemType Directory -Force -Path $primaryPluginsDir | Out-Null
    }
}

$targetNotify = Join-Path $primaryPluginsDir "NotifyPosition"
$targetAccess = Join-Path $primaryPluginsDir "AccessHistory"

if (!(Test-Path $targetNotify)) { New-Item -ItemType Directory -Force -Path $targetNotify | Out-Null }
if (!(Test-Path $targetAccess)) { New-Item -ItemType Directory -Force -Path $targetAccess | Out-Null }

# 2. 実行中の PowerToys および PowerLessToy ホストプロセスを停止
Write-Host "`n[1/5] 実行中の PowerToys / PowerLessToy プロセスを終了しています..." -ForegroundColor Cyan
Stop-Process -Name "PowerToys*", "PowerLessToy*" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$rid = if ($Architecture -eq "ARM64") { "win-arm64" } else { "win-x64" }

# 3. リリースパッケージ展開元からの直接配置判定 (src フォルダが存在しない場合のスタンドアロン対応)
$prebuiltNotify = Join-Path $scriptDir "NotifyPosition"
$prebuiltAccess = Join-Path $scriptDir "AccessHistory"
$isPrebuiltPackage = (Test-Path $prebuiltNotify) -or (Test-Path $prebuiltAccess)

if ($isPrebuiltPackage -and !(Test-Path $srcDir)) {
    Write-Host "`n[2/5] リリースパッケージから直接ファイルを配置しています..." -ForegroundColor Cyan
    if (Test-Path $prebuiltAccess) {
        Copy-Item -Path "$prebuiltAccess\*" -Destination $targetAccess -Recurse -Force
        Write-Host "  -> AccessHistory を配置しました: $targetAccess" -ForegroundColor Green
    }
    if (Test-Path $prebuiltNotify) {
        Copy-Item -Path "$prebuiltNotify\*" -Destination $targetNotify -Recurse -Force
        Write-Host "  -> NotifyPosition を配置しました: $targetNotify" -ForegroundColor Green
    }
} else {
    if (!(Test-Path $srcDir)) {
        Write-Error "ソースコードフォルダが見つかりません: $srcDir"
        exit 1
    }

    # 4. AccessHistory のビルド & パブリッシュ
    Write-Host "`n[2/5] AccessHistory.App をビルド中 (.NET 10.0, $Architecture)..." -ForegroundColor Cyan
    $accessAppDir = Join-Path $srcDir "PowerLessToy.AccessHistory.App"
    $tempAccessPublish = Join-Path $env:TEMP "PowerLessToy_AccessHistory_Publish"
    if (Test-Path $tempAccessPublish) { Remove-Item -Path $tempAccessPublish -Recurse -Force }

    dotnet publish $accessAppDir -c Release -p:Platform=$Architecture -r $rid --no-self-contained -o $tempAccessPublish
    if ($LASTEXITCODE -ne 0) {
        Write-Error "AccessHistory.App のビルドに失敗しました。"
        exit 1
    }

    $accessPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.AccessHistory"
    dotnet build $accessPluginDir -c Release -p:Platform=$Architecture
    if ($LASTEXITCODE -ne 0) {
        Write-Error "AccessHistory プラグインのビルドに失敗しました。"
        exit 1
    }

    # 5. NotifyPosition & NotificationHost のビルド & パブリッシュ
    Write-Host "`n[3/5] PowerLessToy.NotificationHost をビルド中 (.NET 10.0, $Architecture)..." -ForegroundColor Cyan
    $notifyHostDir = Join-Path $srcDir "PowerLessToy.NotificationHost"
    $tempNotifyPublish = Join-Path $env:TEMP "PowerLessToy_NotificationHost_Publish"
    if (Test-Path $tempNotifyPublish) { Remove-Item -Path $tempNotifyPublish -Recurse -Force }

    dotnet publish $notifyHostDir -c Release -p:Platform=$Architecture -r $rid --no-self-contained -o $tempNotifyPublish
    if ($LASTEXITCODE -ne 0) {
        Write-Error "PowerLessToy.NotificationHost のビルドに失敗しました。"
        exit 1
    }

    Write-Host "`n[4/5] Community.PowerToys.Run.Plugin.NotifyPosition をビルド中..." -ForegroundColor Cyan
    $notifyPluginDir = Join-Path $srcDir "Community.PowerToys.Run.Plugin.NotifyPosition"
    dotnet build $notifyPluginDir -c Release -p:Platform=$Architecture
    if ($LASTEXITCODE -ne 0) {
        Write-Error "NotifyPosition プラグインのビルドに失敗しました。"
        exit 1
    }

    # 6. プラグインフォルダへ配置
    Write-Host "`n[5/5] プラグインディレクトリへファイルを配置しています..." -ForegroundColor Cyan

    # AccessHistory
    Copy-Item -Path "$tempAccessPublish\*" -Destination $targetAccess -Recurse -Force
    $accessBinDir = Join-Path $accessPluginDir "bin\$Architecture\Release\net10.0-windows10.0.26100.0"
    Copy-Item -Path "$accessBinDir\*" -Destination $targetAccess -Recurse -Force
    $sqliteNative = Join-Path $targetAccess "runtimes\$rid\native\e_sqlite3.dll"
    if (Test-Path $sqliteNative) {
        Copy-Item $sqliteNative -Destination $targetAccess -Force
    }

    # NotifyPosition
    Copy-Item -Path "$tempNotifyPublish\*" -Destination $targetNotify -Recurse -Force
    $notifyBinDir = Join-Path $notifyPluginDir "bin\$Architecture\Release\net10.0-windows10.0.26100.0"
    Copy-Item -Path "$notifyBinDir\*" -Destination $targetNotify -Recurse -Force

    # Legacy フォルダが存在する場合はそちらにも同期
    if (Test-Path $legacyPluginsDir -and $primaryPluginsDir -ne $legacyPluginsDir) {
        Copy-Item -Path $targetNotify -Destination $legacyPluginsDir -Recurse -Force
        Copy-Item -Path $targetAccess -Destination $legacyPluginsDir -Recurse -Force
    }
}

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
$ptCandidates = @(
    (Join-Path $env:LOCALAPPDATA "PowerToys\PowerToys.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\PowerToys\PowerToys.exe"),
    (Join-Path $env:ProgramFiles "PowerToys\PowerToys.exe")
)
$foundPt = $null
foreach ($pt in $ptCandidates) {
    if (Test-Path $pt) { $foundPt = $pt; break }
}

if ($foundPt) {
    Write-Host "`nPowerToys を再起動しています ($foundPt)..." -ForegroundColor Green
    Start-Process $foundPt
}

# 8. インストール結果の診断情報の出力 (Diagnostics Report)
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [診断情報] PowerLessToy / NotifyPosition インストール結果" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$notifyLogDir = Join-Path $env:LOCALAPPDATA "PowerLessToy\NotifyPosition\Logs"
$accessLogDir = Join-Path $env:LOCALAPPDATA "PowerLessToy\AccessHistory\Logs"
$notifyHostExe = Join-Path $targetNotify "PowerLessToy.NotificationHost.exe"
$notifyPluginDll = Join-Path $targetNotify "Community.PowerToys.Run.Plugin.NotifyPosition.dll"
$accessExe = Join-Path $targetAccess "PowerLessToy.AccessHistory.App.exe"
$accessDll = Join-Path $targetAccess "Community.PowerToys.Run.Plugin.AccessHistory.dll"

Write-Host "・プラグイン配置先 (Modern): $targetNotify" -ForegroundColor White
Write-Host "・インストールアーキ:        $Architecture ($rid)" -ForegroundColor White

# PowerToys バージョン
$ptVersion = "未検出"
if ($foundPt) {
    try {
        $ptVersionInfo = (Get-Item $foundPt).VersionInfo
        $ptVersion = "$($ptVersionInfo.ProductVersion) (File: $($ptVersionInfo.FileVersion))"
    } catch {
        $ptVersion = "取得失敗 ($($_))"
    }
}
Write-Host "・PowerToys バージョン:      $ptVersion" -ForegroundColor White

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

# ファイル存在およびSHA256ハッシュ確認
Write-Host "・ファイル配置 & ハッシュ確認:" -ForegroundColor White
$filesToCheck = @(
    @{ Name = "NotificationHost.exe"; Path = $notifyHostExe },
    @{ Name = "NotifyPosition Plugin DLL"; Path = $notifyPluginDll },
    @{ Name = "AccessHistory.App.exe"; Path = $accessExe },
    @{ Name = "AccessHistory Plugin DLL"; Path = $accessDll }
)
foreach ($f in $filesToCheck) {
    if (Test-Path $f.Path) {
        $hash = (Get-FileHash -Path $f.Path -Algorithm SHA256).Hash.Substring(0, 16) + "..."
        Write-Host ("    {0,-28}: [OK] (SHA256: {1})" -f $f.Name, $hash) -ForegroundColor Green
    } else {
        Write-Host ("    {0,-28}: [NG] 見つかりません" -f $f.Name) -ForegroundColor Red
    }
}

Write-Host "`n・ログ保存先フォルダ:" -ForegroundColor White
Write-Host "    NotifyPosition: $notifyLogDir" -ForegroundColor Cyan
Write-Host "    AccessHistory:  $accessLogDir" -ForegroundColor Cyan

Write-Host "`n[診断と検証手順]:" -ForegroundColor Yellow
Write-Host "1. PowerToys Run で 'notify test' と入力してエンター -> 単一のカスタムアクリル通知が表示されることを確認" -ForegroundColor White
Write-Host "2. PowerToys Run で 'notify test 5' と入力 -> 5件の通知が垂直スタックし、画面中央上部に綺麗に整列・補位されることを確認" -ForegroundColor White
Write-Host "3. PowerToys Run で 'notify settings' と入力 -> 設定画面から動作モード (Replace/Mirror/DND) や通知権限の確認が可能" -ForegroundColor White
Write-Host "4. 動作異常時は上記ログフォルダ内の最新 log ファイルをご確認ください（全イベントがミリ秒単位で記録されます）。" -ForegroundColor White

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " インストールが正常に完了しました！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
