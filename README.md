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

## 📄 ライセンス
[MIT License](LICENSE)
