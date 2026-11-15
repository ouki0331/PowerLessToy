# 🚀 PowerLessToy デプロイ・導入ガイド (Deployment Guide)

本ドキュメントでは、**PowerLessToy**（.NET 10.0 / Windows 11 24H2 対応）のビルド・デプロイ手順および環境設定について解説します。

---

## 1. 動作環境・前提要件

- **OS**: Windows 10 (21H2 以降) / Windows 11 (24H2 推奨, build 26100 / 26200+)
- **対応アーキテクチャ**: `x64` (Intel/AMD) または `ARM64` (Apple Silicon Mac 仮想環境 / Snapdragon X Elite 等)
- **必須ランタイム・ツール**:
  1. **Microsoft PowerToys** (0.102+ 推奨)
  2. **Microsoft .NET 10.0 Desktop Runtime** (`Microsoft.WindowsDesktop.App 10.0`)
  3. **.NET 10.0 SDK** (ソースコードからビルドする場合のみ)

---

## 2. モジュール構成

PowerLessToy は以下の 3 つの主要モジュールで構成されています。

| モジュール名 | キーワード | 種別 | 役割・特徴 |
| :--- | :---: | :--- | :--- |
| **NotifyPosition 2.0** | `notify` | PowerToys Run プラグイン + 独立 Host | Windows 標準通知を捕捉し、macOS風アクリル通知へリプレイス。画面中央上部スタック配置、自動補位、ホバー一時停止に対応。 |
| **AccessHistory** | `his` | PowerToys Run プラグイン + 独立 WPF | ファイルエクスプローラーの閲覧履歴（Local / SMB / WSL）を自動記録。全画面 80% の枠なし深色パネルであいまい検索、フォーカスアウトで自動消去。 |
| **VirtualDesktopLoop** | `vdesk` / `loop` | バックグラウンド常駐フック | `Win + Ctrl + 左右` による仮想デスクトップ切替で、端のデスクトップ（先頭/末尾）から反対側への無限循環ループ切替を実現。 |

---

## 3. クイックデプロイ (自動インストールスクリプト)

リポジトリ直下の `install.ps1` を使用すると、環境に応じたアーキテクチャ（x64 / ARM64）の自動判別、ビルドまたは展開済みバイナリの配置、ショートカット生成、PowerToys 再起動が一括で実行されます。

### 実行方法 (PowerShell):
```powershell
# 1. 展開フォルダまたはリポジトリのルートディレクトリに移動
cd PowerLessToy

# 2. 自動デプロイスクリプトを実行
.\install.ps1
```

※ アーキテクチャを明示的に指定する場合:
```powershell
.\install.ps1 -Architecture x64
# または
.\install.ps1 -Architecture ARM64
```

---

## 4. 手動デプロイ手順 (Manual Deployment)

ビルド済みのバイナリを配置するだけの手動手順です。

### プラグインの配置先
PowerToys は起動時に以下のディレクトリから拡張プラグインを読み込みます：
```text
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\
(通常: C:\Users\<ユーザー名>\AppData\Local\Microsoft\PowerToys\PowerToys Run\Plugins\)
```
※ 旧バージョンの PowerToys では `%LOCALAPPDATA%\PowerToys\RunPlugins\` も参照されます。

### ステップ 1: プラグインフォルダの作成
以下の 2 つのフォルダを作成します：
- `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\NotifyPosition\`
- `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\AccessHistory\`

### ステップ 2: ファイルのコピー
1. **NotifyPosition 2.0**:
   - `Community.PowerToys.Run.Plugin.NotifyPosition.dll`
   - `PowerLessToy.NotificationHost.exe`
   - `PowerLessToy.NotificationHost.runtimeconfig.json`
   - `plugin.json`
   - `Images/` フォルダ (`notifyposition.dark.png`, `notifyposition.light.png`)
   - `PackageIdentity/` フォルダ (任意: Sparse Package 登録用スクリプト)

2. **AccessHistory**:
   - `Community.PowerToys.Run.Plugin.AccessHistory.dll`
   - `PowerLessToy.AccessHistory.App.exe`
   - `PowerLessToy.AccessHistory.App.runtimeconfig.json`
   - `Dapper.dll`
   - `Microsoft.Data.Sqlite.dll`
   - `e_sqlite3.dll`
   - `plugin.json`
   - `Images/` フォルダ (`history.dark.png`, `history.light.png`)

### ステップ 3: PowerToys の再起動
1. タスクバーの通知領域（タスクトレイ）で **PowerToys** アイコンを右クリックし、「終了」を選択します。
2. スタートメニューから再度 **PowerToys** を起動します。

---

## 5. トラブルシューティング・よくある質問

### Q1. PowerToys Run で `notify test` や `notify test 5` が反応しない
- **原因と対処**:
  1. `PowerLessToy.NotificationHost.exe` が正常に起動しているかタスクマネージャーで確認してください。
  2. `%LOCALAPPDATA%\PowerLessToy\NotifyPosition\Logs\` 内の最新ログファイルを確認してください。
  3. `PowerLessToy.NotificationHost.exe` を直接ダブルクリックして起動するか、`PowerLessToy.NotificationHost.exe --test` を実行して動作を確認してください。

### Q2. 通常のアプリ（メール、Teams、Slack 等）の通知がカスタム通知に置換されない
- **原因と対処**:
  1. PowerToys Run で `notify settings` と入力するか、`PowerLessToy.NotificationHost.exe --settings` を実行して設定画面を開きます。
  2. 最上部の「Windows 通知取得権限」が `Allowed` になっているか確認してください。
  3. `Denied` または `Unspecified` の場合は、「権限設定を開く」をクリックし、Windows 設定アプリ（プライバシーとセキュリティ -> 通知）で通知アクセスを許可してください。
  4. 動作モードが `Replace` になっていることを確認してください。

### Q3. 原生通知が一瞬フラッシュ（チラつき）するのを完全に無くしたい
- **対処法**:
  - Windows の「応答不可 (Do Not Disturb) / 集中モード」を ON にし、NotifyPosition の設定を `DoNotDisturb` モードに設定してください。
  - Windows 本体の横断幕トーストが完全に抑制され、NotificationHost によるアクリル通知のみが完璧に表示されます。

### Q4. ログファイルの保存場所と確認方法
- **NotifyPosition**: `%LOCALAPPDATA%\PowerLessToy\NotifyPosition\Logs\`
- **AccessHistory**: `%LOCALAPPDATA%\PowerLessToy\AccessHistory\Logs\`
- ログには通知の受信、ID、アプリ名、スタック座標、DPI、DWM アクリル適用結果、エラー詳細（StackTrace, HResult）が全て記録されます。
