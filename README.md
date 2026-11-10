# PowerLessToy

**PowerLessToy** は、Windows 10/11 向けの軽量かつ高機能な PowerToys 拡張プラグイン & ユーティリティ集です。全コンポーネントが最新の **.NET 10.0** に対応しています。

---

## 🌟 主な機能

### 1. 🔔 NotifyPosition 2.0 (スマート通知マネージャー)
Windows 標準の通知（トースト）をインターセプトし、モダンな深色カード通知へリプレイスします。
- **承認・却下アクションの完全プロキシ**: 会議招待やワークフロー承認の「同意 / 却下」ボタンを自動抽出し、ワンクリックで元の通知へプロキシ実行。
- **自由な配置設定**: `notify topleft`, `notify topcenter`, `notify topright` など、画面の任意の位置に配置可能。
- **プレビュー機能**: `notify test` で承認ボタン付きのカスタム通知を即座にテスト可能。

### 2. 📁 AccessHistory (エクスプローラーアクセス履歴 & あいまい検索)
Windows エクスプローラーによるファイル・フォルダの閲覧履歴を自動記録するスマートパネル。
- **3列表示**: ローカル (Local)、ネットワーク共有 (SMB)、Linux サブシステム (WSL) を自動分類して頻度順に一覧化。
- **全画面 80% 枠なしデザイン**: 邪魔な Windows タイトルバーを排除したモダンな深色 UI。
- **高速あいまい検索**: `Ctrl + P` で即座に検索ボックスへフォーカス。スペース区切りの OR 検索に対応。
- **自動非表示**: パネル外クリックや `Esc` キーで瞬時にフェードアウト。

### 3. 🔄 VirtualDesktopLoop (仮想デスクトップ循環切替)
Windows 10/11 の標準仕様（端のデスクトップで移動が止まる制限）を突破し、シームレスなループ切替を実現。
- **シームレスな端点ループ**: 最後のデスクトップ（例: デスクトップ 3）で `Win + Ctrl + Right` を押すと、瞬時に最初のデスクトップ（デスクトップ 1）へ循環。同様に最初のデスクトップで `Win + Ctrl + Left` を押すと末尾へ循環。
- **ゼロ遅延 & ネイティブアニメーション**: 途中の移動（1➔2、2➔3）は Windows 本体のネイティブ処理を 100% 透過し、端点のみをスマートにインターセプトしてアニメーション付きでループ。
- **常駐自動起動**: PowerLessToy 起動と同時にバックグラウンドで自動有効化。PowerToys Run (`vdesk` または `loop`) からもリアルタイムにデスクトップ数・状態の確認やオン/オフが可能。

---

## 🛠️ 必要要件
- Windows 10 (21H2 以降) / Windows 11 (x64 または ARM64)
- [Microsoft PowerToys](https://github.com/microsoft/PowerToys)
- [Microsoft .NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/)

---

## 🚀 導入・デプロイ方法
詳しい導入手順やトラブルシューティングについては、[DEPLOYMENT.md](DEPLOYMENT.md) をご覧ください。

### クイックインストール:
```powershell
.\install.ps1
```

---

## 🔍 診断・ログ機能 (Diagnostics & Logs)
AccessHistory には外部依存なしの専用ファイルロガーが組み込まれており、PowerToys 拡張プロセスおよび独立 WPF アプリの双方から詳細な動作ログが記録されます。

- **ログ保存先フォルダ**:
  ```text
  %LOCALAPPDATA%\PowerLessToy\AccessHistory\Logs\
  (例: C:\Users\<ユーザー名>\AppData\Local\PowerLessToy\AccessHistory\Logs\)
  ```
- **ログファイル形式**: `accesshistory-YYYYMMDD-{プロセス名}-{PID}.log`
  - `accesshistory-20261110-PowerToys.PowerLauncher-1234.log` (プラグイン側)
  - `accesshistory-20261110-PowerLessToy.AccessHistory.App-5678.log` (WPF アプリ側)
- **ログの確認方法**:
  - AccessHistory 画面右下の **「📁 ログを開く」** ボタンをクリックするか、上記フォルダを直接開いて確認できます。
  - COM (Shell.Application) 呼び出し、SQLite データベース、AssemblyLoadContext、Explorer 監視の全成否が記録されます。

---

## 📄 ライセンス
[MIT License](LICENSE)
