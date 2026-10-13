# 🚀 PowerLessToy デプロイ・導入ガイド (Deployment Guide)

本ドキュメントでは、**PowerLessToy**（.NET 10.0 対応）のビルド・デプロイ手順および環境設定について解説します。

---

## 1. 動作環境・前提要件

- **OS**: Windows 10 (21H2 以降) / Windows 11 (24H2 推奨)
- **対応アーキテクチャ**: `x64` (Intel/AMD) または `ARM64` (Apple Silicon Mac 仮想環境 / Snapdragon X Elite 等)
- **必須ランタイム・ツール**:
  1. **Microsoft PowerToys** (最新版推奨)
  2. **Microsoft .NET 10.0 Desktop Runtime** (`Microsoft.WindowsDesktop.App 10.0`)
  3. **.NET 10.0 SDK** (ソースコードからビルドする場合のみ)

---

## 2. モジュール構成

PowerLessToy は以下の 2 つの主要モジュールで構成されています。

| モジュール名 | キーワード | 種別 | 役割・特徴 |
| :--- | :---: | :--- | :--- |
| **NotifyPosition 2.0** | `notify` | PowerToys Run プラグイン | Windows 標準通知を捕捉し、モダンな深色カード通知へ置換。「承認・却下 (Approve/Reject)」ボタンの自動プロキシ実行に対応。 |
| **AccessHistory** | `his` | PowerToys Run プラグイン + 独立 WPF | ファイルエクスプローラーの閲覧履歴（Local / SMB / WSL）を自動記録。全画面 80% の枠なし深色パネルであいまい検索、フォーカスアウトで自動消去。 |

---

## 3. クイックデプロイ (自動インストールスクリプト)

リポジトリ直下の `install.ps1` を使用すると、環境に応じたアーキテクチャ（x64 / ARM64）の自動判別、ビルド、プラグイン配置、ショートカット生成、PowerToys 再起動が一括で実行されます。

### 実行方法 (PowerShell):
```powershell
# 1. リポジトリのルートディレクトリに移動
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
%LOCALAPPDATA%\PowerToys\RunPlugins\
(通常: C:\Users\<ユーザー名>\AppData\Local\PowerToys\RunPlugins\)
```

### ステップ 1: プラグインフォルダの作成
以下の 2 つのフォルダを作成します：
- `%LOCALAPPDATA%\PowerToys\RunPlugins\NotifyPosition\`
- `%LOCALAPPDATA%\PowerToys\RunPlugins\AccessHistory\`

### ステップ 2: ファイルのコピー
1. **NotifyPosition 2.0**:
   - `Community.PowerToys.Run.Plugin.NotifyPosition.dll`
   - `plugin.json`
   - `Images/` フォルダ (`notifyposition.dark.png`, `notifyposition.light.png`)
   - 関連依存 DLL

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

## 5. ソースコードからの手動ビルド (Build from Source)

.NET 10.0 SDK を使用して各プロジェクトを手動でビルドするコマンドです。

```powershell
# 1. AccessHistory.App (WPF 独立アプリ) のパブリッシュ
cd src\PowerLessToy.AccessHistory.App
dotnet publish -c Release -p:Platform=ARM64 -r win-arm64 --no-self-contained -o C:\Users\<ユーザー名>\AccessAppPublish
# (x64 環境の場合は -p:Platform=x64 -r win-x64)

# 2. AccessHistory プラグインのビルド
cd ..\Community.PowerToys.Run.Plugin.AccessHistory
dotnet build -c Release -p:Platform=ARM64

# 3. NotifyPosition 2.0 プラグインのビルド
cd ..\Community.PowerToys.Run.Plugin.NotifyPosition
dotnet build -c Release -p:Platform=ARM64
```

---

## 6. トラブルシューティング・よくある質問

### Q1. Mac 仮想環境 (VMware / Parallels) で `Alt + Space` が効かない
- **原因**: macOS 側の Spotlight や Raycast、Alfred 等のグローバルショートカットと重複して吸い取られるためです。
- **対処法**:
  - `Win + R`（Mac キーボードの `Command + R`）で PowerToys Run を起動する設定（`override_win_r_key: true`）を利用するか、
  - 予備キー `Alt + Q` (Mac では `Option + Q`) を押下してください。

### Q2. .NET 10.0 ランタイムが要求される
- 実行環境に .NET 10.0 Desktop Runtime が未導入の場合、Microsoft 公式サイトまたは Windows Update 経由で **.NET Desktop Runtime 10.0 (x64 または ARM64)** をインストールしてください。

### Q3. 「承認・却下」ボタンがカスタム通知に表示されない
- 通知元アプリケーションが Windows の「インタラクティブ通知（ボタン付きトースト）」として送信している場合のみ、UIAutomation がボタンを検出し、動的に表示します。
- 動作テストを行いたい場合は、PowerToys Run で `notify test` と入力してエンターキーを押すと、承認ボタン付きのプレビュー通知が確認できます。
