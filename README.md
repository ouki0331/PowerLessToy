# PowerLessToy

**PowerLessToy** は、Windows 10/11 向けの軽量かつ高機能な PowerToys 拡張プラグイン & ユーティリティ集です。全コンポーネントが最新の **.NET 10.0** に対応しています。

---

## 🌟 主な機能

### 1. 🔔 NotifyPosition 2.0 (macOS風アクリル通知マネージャー)
Windows 標準のトースト通知を、macOSのような洗練された半透明アクリル（フロストガラス）通知カードへリプレイス・整列表示します。
- **独立単一インスタンス (NotificationHost)**: PowerToys Run プロセスから通知監視・描画を分離。PowerToys 本体の安定性を維持しつつ、安全に独立常駐します。
- **画面中央上部 (TopCenter) デフォルト配置**: 視認性に優れた中央上部に表示。アクティブウィンドウやカーソル位置のマルチモニター・DPI に自動追従。
- **垂直スタック & 自動補位アニメーション**: 最大5件まで整列スタック。超過分はキューで待機し、カード消去時に下位カードがスムーズに上方向へスライド補位。
- **マウスホバーでカウントダウン一時停止**: カードにカーソルを合わせている間は自動消去タイマー（デフォルト8秒）がストップ。
- **3つの動作モード**:
  - `Replace`: 原生通知を削除し、カスタム通知のみを表示（※Windowsの仕様上、一瞬フラッシュする場合があります）。
  - `Mirror`: 原生通知とカスタム通知を両方表示（テスト・比較用）。
  - `DoNotDisturb`: Windowsの「応答不可/集中モード」と併用し、システム横断幕を出さずにカスタム通知のみ完全表示。
- **動作検証コマンド**:
  - `notify test`: 1件のプレビュー通知を表示
  - `notify test 5`: 5件の通知を連続送信し、スタック・補位アニメーション・ホバー動作を一括検証
  - `notify settings`: 詳細設定画面を開く

### 2. 📁 AccessHistory (エクスプローラーアクセス履歴 & あいまい検索)
Windows エクスプローラーによるファイル・フォルダの閲覧履歴を自動記録するスマートパネル。
- **3列表示**: ローカル (Local)、ネットワーク共有 (SMB)、Linux サブシステム (WSL) を自動分類して頻度順に一覧化。
- **全画面 80% 枠なしデザイン**: 邪魔な Windows タイトルバーを排除したモダンな深色 UI。
- **高速あいまい検索**: `Ctrl + P` で即座に検索ボックスへフォーカス。スペース区切りの OR 検索に対応。
- **自動非表示**: パネル外クリックや `Esc` キーで瞬時にフェードアウト。

### 3. 🔄 VirtualDesktopLoop (仮想デスクトップ循環切替)
Windows 10/11 の標準仕様（端のデスクトップで移動が止まる制限）を突破し、シームレスなループ切替を実現。
- **シームレスな端点ループ**: 最後のデスクトップで `Win + Ctrl + Right` を押すと、瞬時に最初のデスクトップへ循環。同様に最初のデスクトップで `Win + Ctrl + Left` を押すと末尾へ循環。
- **ゼロ遅延 & ネイティブアニメーション**: 途中の移動は Windows 本体のネイティブ処理を 100% 透過し、端点のみをスマートにインターセプトしてアニメーション付きでループ。
- **常駐自動起動**: PowerLessToy 起動と同時にバックグラウンドで自動有効化。PowerToys Run (`vdesk` または `loop`) からリアルタイムに状態確認やオン/オフが可能。

---

## 🛠️ 必要要件
- Windows 10 (21H2 以降) / Windows 11 (24H2 推奨, build 26100 / 26200+)
- [Microsoft PowerToys](https://github.com/microsoft/PowerToys) (0.102+ 推奨)
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
外部依存ライブラリを一切使用しない専用の高性能ゼロ依存ファイルロガーが組み込まれています。

- **NotifyPosition ログ保存先**:
  ```text
  %LOCALAPPDATA%\PowerLessToy\NotifyPosition\Logs\
  (例: C:\Users\<ユーザー名>\AppData\Local\PowerLessToy\NotifyPosition\Logs\)
  ```
- **AccessHistory ログ保存先**:
  ```text
  %LOCALAPPDATA%\PowerLessToy\AccessHistory\Logs\
  ```
- **ログファイル形式**: `{モジュール名}-YYYYMMDD-{プロセス名}-{PID}.log`
  - `notifyposition-20261115-PowerLessToy.NotificationHost-1234.log`
  - `notifyposition-20261115-PowerToys.PowerLauncher-5678.log`

---

## 📄 ライセンス
[MIT License](LICENSE)
