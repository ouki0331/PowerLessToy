using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;

namespace NotifyPositionChanger
{
    public partial class MainWindow : Window
    {
        // ═══════════════════════════════════════════
        //  Win32 API
        // ═══════════════════════════════════════════

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_ASYNCWINDOWPOS = 0x4000;
        const uint SWP_NOSIZE = 0x0001;
        const int SM_CXSCREEN = 0;
        const int SM_CYSCREEN = 1;

        // ═══════════════════════════════════════════
        //  通知位置の状態
        // ═══════════════════════════════════════════

        enum PositionPreset { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight, Custom }

        PositionPreset _preset = PositionPreset.TopRight;
        int _customX = 0;
        int _customY = 0;
        bool _resizeEnabled = false;
        int _notifyWidth = 364;
        int _notifyHeight = 120;
        int _intervalMs = 50;

        CancellationTokenSource? _cts;
        Task? _monitorTask;
        bool _isMonitoring = false;

        TaskbarIcon? _trayIcon;

        static readonly string[] NotificationClassNames =
        {
            "Windows.UI.Core.CoreWindow",
            "Windows.UI.Composition.DesktopWindowContentBridge",
        };

        static readonly string[] NotificationProcessNames =
        {
            "ShellExperienceHost",
            "explorer",
        };

        readonly string _settingsPath;

        // ═══════════════════════════════════════════
        //  Google Chat 連携
        // ═══════════════════════════════════════════

        GoogleChatService _chatService;
        bool _googleChatEnabled = false;
        // 同じ通知で連続ポップアップしないようにするデバウンス
        DateTime _lastPopupTime = DateTime.MinValue;
        string _lastPopupSender = "";
        readonly TimeSpan _popupCooldown = TimeSpan.FromSeconds(5);

        // ═══════════════════════════════════════════
        //  初期化
        // ═══════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();

            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NotifyPositionChanger", "settings.json");

            _chatService = new GoogleChatService();
            _chatService.OnLog += msg => Dispatcher.BeginInvoke(() => AppendLog($"[Chat] {msg}"));

            SetupTrayIcon();
            LoadSettings();
            UpdateChatAuthUI();
            UpdateCredentialsPathUI();
        }

        // ═══════════════════════════════════════════
        //  トレイアイコン
        // ═══════════════════════════════════════════

        void SetupTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Notify Position Changer",
                Visibility = Visibility.Collapsed
            };

            try
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Information;
            }
            catch { }

            _trayIcon.TrayMouseDoubleClick += (s, e) => ShowFromTray();

            var menu = new System.Windows.Controls.ContextMenu();

            var showItem = new System.Windows.Controls.MenuItem { Header = "設定を開く" };
            showItem.Click += (s, e) => ShowFromTray();
            menu.Items.Add(showItem);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
            exitItem.Click += (s, e) => ExitApplication();
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenu = menu;
        }

        // ═══════════════════════════════════════════
        //  ウィンドウイベント
        // ═══════════════════════════════════════════

        void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        void CloseToTray_Click(object sender, RoutedEventArgs e) => HideToTray();
        void MinimizeToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

        void HideToTray()
        {
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.Visibility = Visibility.Visible;
                _trayIcon.ShowBalloonTip("Notify Position Changer",
                    "トレイに常駐しています。ダブルクリックで設定を開きます。",
                    BalloonIcon.Info);
            }
        }

        void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null)
                _trayIcon.Visibility = Visibility.Collapsed;
        }

        void ExitApplication()
        {
            StopMonitoring();
            SaveSettings();
            _chatService.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            Application.Current.Shutdown();
        }

        // ═══════════════════════════════════════════
        //  通知位置の設定変更イベント
        // ═══════════════════════════════════════════

        void Position_Changed(object sender, RoutedEventArgs e)
        {
            if (RbTopLeft.IsChecked == true) _preset = PositionPreset.TopLeft;
            else if (RbTopCenter.IsChecked == true) _preset = PositionPreset.TopCenter;
            else if (RbTopRight.IsChecked == true) _preset = PositionPreset.TopRight;
            else if (RbBottomLeft.IsChecked == true) _preset = PositionPreset.BottomLeft;
            else if (RbBottomCenter.IsChecked == true) _preset = PositionPreset.BottomCenter;
            else if (RbBottomRight.IsChecked == true) _preset = PositionPreset.BottomRight;
            SaveSettings();
        }

        void CustomPosition_Changed(object sender, RoutedEventArgs e) => SaveSettings();
        void ResizeOption_Changed(object sender, RoutedEventArgs e) => SaveSettings();

        void Interval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntervalLabel == null) return;
            _intervalMs = (int)e.NewValue;
            IntervalLabel.Text = $"{_intervalMs}ms";
            SaveSettings();
        }

        // ═══════════════════════════════════════════
        //  Google Chat 設定イベント
        // ═══════════════════════════════════════════

        void GoogleChat_Changed(object sender, RoutedEventArgs e)
        {
            _googleChatEnabled = ChkGoogleChat.IsChecked == true;
            SaveSettings();
        }

        async void ChatAuth_Click(object sender, RoutedEventArgs e)
        {
            if (!_chatService.HasCredentialsFile())
            {
                MessageBox.Show(
                    "先に credentials.json を選択してください。\n\n" +
                    "Google Cloud Console → APIとサービス → 認証情報 → \n" +
                    "OAuth 2.0 クライアントID → デスクトップアプリ → \n" +
                    "credentials.json をダウンロード",
                    "credentials.json が必要です",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnChatAuth.IsEnabled = false;
            BtnChatAuth.Content = "認証中...";
            AppendLog("[Chat] OAuth認証を開始... ブラウザが開きます");

            try
            {
                bool success = await _chatService.AuthenticateAsync();
                UpdateChatAuthUI();

                if (success)
                {
                    AppendLog("[Chat] 認証成功！Google Chat連携が有効になりました");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Chat] 認証エラー: {ex.Message}");
            }
            finally
            {
                BtnChatAuth.IsEnabled = true;
            }
        }

        void SelectCredentials_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "credentials.json を選択",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "credentials.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _chatService.SetCredentialsFile(dialog.FileName);
                    UpdateCredentialsPathUI();
                    AppendLog($"[Chat] credentials.json を配置しました");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"ファイルのコピーに失敗: {ex.Message}", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        void ChatLogout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Google Chat からログアウトしますか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _chatService.Logout();
                UpdateChatAuthUI();
            }
        }

        void UpdateChatAuthUI()
        {
            if (_chatService.IsAuthenticated)
            {
                ChatAuthStatus.Text = "認証済み ✓";
                ChatAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0x6A));
                BtnChatAuth.Content = "再認証";
                BtnChatLogout.Visibility = Visibility.Visible;
            }
            else
            {
                ChatAuthStatus.Text = "未認証";
                ChatAuthStatus.Foreground = FindResource("SubTextBrush") as System.Windows.Media.SolidColorBrush;
                BtnChatAuth.Content = "認証";
                BtnChatLogout.Visibility = Visibility.Collapsed;
            }
        }

        void UpdateCredentialsPathUI()
        {
            if (_chatService.HasCredentialsFile())
            {
                CredentialsPathText.Text = $"✓ {_chatService.GetCredentialsPath()}";
            }
            else
            {
                CredentialsPathText.Text = "";
            }
        }

        // ═══════════════════════════════════════════
        //  監視の開始 / 停止
        // ═══════════════════════════════════════════

        void ToggleMonitoring_Click(object sender, RoutedEventArgs e)
        {
            if (_isMonitoring) StopMonitoring();
            else StartMonitoring();
        }

        void StartMonitoring()
        {
            ReadCurrentSettings();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _monitorTask = Task.Run(() => MonitorLoop(token), token);

            _isMonitoring = true;
            StatusText.Text = "監視中";
            StatusText.Foreground = FindResource("HighlightBrush") as System.Windows.Media.SolidColorBrush;
            ToggleButton.Content = "⏹ 停止";
            AppendLog("監視を開始しました");
        }

        void StopMonitoring()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            _isMonitoring = false;

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "停止中";
                StatusText.Foreground = FindResource("SubTextBrush") as System.Windows.Media.SolidColorBrush;
                ToggleButton.Content = "▶ 開始";
                AppendLog("監視を停止しました");
            });
        }

        void ReadCurrentSettings()
        {
            if (ChkCustomPosition.IsChecked == true)
            {
                _preset = PositionPreset.Custom;
                int.TryParse(TxtCustomX.Text, out _customX);
                int.TryParse(TxtCustomY.Text, out _customY);
            }

            _resizeEnabled = ChkResizeNotify.IsChecked == true;
            int.TryParse(TxtWidth.Text, out _notifyWidth);
            int.TryParse(TxtHeight.Text, out _notifyHeight);

            if (_notifyWidth < 50) _notifyWidth = 364;
            if (_notifyHeight < 30) _notifyHeight = 120;

            _googleChatEnabled = ChkGoogleChat.IsChecked == true;
        }

        // ═══════════════════════════════════════════
        //  監視ループ
        // ═══════════════════════════════════════════

        void MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Dispatcher.Invoke(() => ReadCurrentSettings());
                    ScanAndMoveNotifications(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() => AppendLog($"[ERR] {ex.Message}"));
                }

                try { Task.Delay(_intervalMs, token).Wait(token); }
                catch (OperationCanceledException) { break; }
            }
        }

        void ScanAndMoveNotifications(CancellationToken token)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (token.IsCancellationRequested) return false;
                if (!IsWindowVisible(hWnd)) return true;

                var className = GetWindowClassNameStr(hWnd);
                if (!IsNotificationClass(className)) return true;
                if (!IsNotificationProcess(hWnd)) return true;

                GetWindowRect(hWnd, out RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                if (IsNotificationSized(w, h))
                {
                    // Google Chat 通知チェック
                    if (_googleChatEnabled && _chatService.IsAuthenticated)
                    {
                        CheckAndShowChatPopup(hWnd);
                    }

                    MoveNotificationWindow(hWnd, w, h, rect);
                }

                return true;
            }, IntPtr.Zero);

            TryFindAndMove("Windows.UI.Core.CoreWindow", "新しい通知");
            TryFindAndMove("Windows.UI.Core.CoreWindow", "New notification");
        }

        // ═══════════════════════════════════════════
        //  Google Chat 通知検出 → ポップアップ
        // ═══════════════════════════════════════════

        void CheckAndShowChatPopup(IntPtr hWnd)
        {
            try
            {
                var content = NotificationTextReader.ReadNotificationContent(hWnd);
                if (content == null || !content.IsGoogleChat) return;

                // デバウンス: 同じ送信者からの通知が短時間に連続した場合は無視
                string senderKey = content.SenderName + content.MessageText;
                if (senderKey == _lastPopupSender &&
                    (DateTime.Now - _lastPopupTime) < _popupCooldown)
                    return;

                _lastPopupSender = senderKey;
                _lastPopupTime = DateTime.Now;

                Dispatcher.BeginInvoke(async () =>
                {
                    AppendLog($"[Chat] Google Chat 通知検出: {content.SenderName}");
                    await ShowChatPopupAsync(content);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => AppendLog($"[Chat] 通知読取エラー: {ex.Message}"));
            }
        }

        async Task ShowChatPopupAsync(NotificationContent content)
        {
            var popup = new ChatPopupWindow(_chatService);

            // まず通知内容だけで即時表示
            popup.ShowNotificationContent(content);
            popup.Show();

            // バックグラウンドでAPIから詳細取得を試みる
            try
            {
                string senderHint = content.SenderName;
                if (string.IsNullOrEmpty(senderHint))
                    senderHint = content.Title;

                var (space, messages) = await _chatService.FindSpaceAndMessagesAsync(senderHint);

                if (space != null && messages.Count > 0)
                {
                    popup.SetMessages(space, messages);
                    AppendLog($"[Chat] スペース特定: {space.DisplayName} ({messages.Count}件)");
                }
                else
                {
                    AppendLog("[Chat] スペースの特定に失敗 (通知内容のみ表示)");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Chat] メッセージ取得エラー: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        //  通知ウィンドウの移動ロジック
        // ═══════════════════════════════════════════

        void TryFindAndMove(string className, string windowName)
        {
            IntPtr hWnd = FindWindow(className, windowName);
            if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd))
            {
                GetWindowRect(hWnd, out RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                MoveNotificationWindow(hWnd, w, h, rect);
            }
        }

        bool IsNotificationClass(string className)
        {
            foreach (var n in NotificationClassNames)
                if (className.Contains(n)) return true;
            return false;
        }

        bool IsNotificationProcess(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                var proc = Process.GetProcessById((int)pid);
                foreach (var n in NotificationProcessNames)
                    if (proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        bool IsNotificationSized(int w, int h)
        {
            int sw = GetSystemMetrics(SM_CXSCREEN);
            int sh = GetSystemMetrics(SM_CYSCREEN);
            if (w > sw * 0.5 || h > sh * 0.5) return false;
            if (w < 50 || h < 30) return false;
            return (w >= 150 && w <= 600 && h >= 40 && h <= 500);
        }

        void MoveNotificationWindow(IntPtr hWnd, int origW, int origH, RECT currentRect)
        {
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);
            int margin = 12;

            int finalW = _resizeEnabled ? _notifyWidth : origW;
            int finalH = _resizeEnabled ? _notifyHeight : origH;

            CalculatePosition(screenW, screenH, finalW, finalH, margin,
                out int targetX, out int targetY);

            if (currentRect.Left == targetX && currentRect.Top == targetY &&
                origW == finalW && origH == finalH)
                return;

            uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
            if (!_resizeEnabled) flags |= SWP_NOSIZE;

            SetWindowPos(hWnd, IntPtr.Zero, targetX, targetY, finalW, finalH, flags);

            Dispatcher.BeginInvoke(() =>
                AppendLog($"通知を移動 → ({targetX},{targetY}) [{finalW}x{finalH}]"));
        }

        void CalculatePosition(int screenW, int screenH, int winW, int winH, int margin,
            out int x, out int y)
        {
            int taskbar = 48;
            switch (_preset)
            {
                case PositionPreset.TopLeft:
                    x = margin; y = margin; break;
                case PositionPreset.TopCenter:
                    x = (screenW - winW) / 2; y = margin; break;
                case PositionPreset.TopRight:
                    x = screenW - winW - margin; y = margin; break;
                case PositionPreset.BottomLeft:
                    x = margin; y = screenH - winH - margin - taskbar; break;
                case PositionPreset.BottomCenter:
                    x = (screenW - winW) / 2; y = screenH - winH - margin - taskbar; break;
                case PositionPreset.BottomRight:
                    x = screenW - winW - margin; y = screenH - winH - margin - taskbar; break;
                case PositionPreset.Custom:
                    x = _customX; y = _customY; break;
                default:
                    x = screenW - winW - margin; y = margin; break;
            }
        }

        // ═══════════════════════════════════════════
        //  ユーティリティ
        // ═══════════════════════════════════════════

        static string GetWindowClassNameStr(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        void AppendLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            LogBox.AppendText(line);
            LogBox.ScrollToEnd();

            if (LogBox.Text.Length > 5000)
                LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - 3000);
        }

        // ═══════════════════════════════════════════
        //  設定の保存 / 読み込み
        // ═══════════════════════════════════════════

        class AppSettings
        {
            public string Position { get; set; } = "TopRight";
            public int CustomX { get; set; }
            public int CustomY { get; set; }
            public bool UseCustomPosition { get; set; }
            public bool ResizeEnabled { get; set; }
            public int NotifyWidth { get; set; } = 364;
            public int NotifyHeight { get; set; } = 120;
            public int IntervalMs { get; set; } = 50;
            public bool GoogleChatEnabled { get; set; }
        }

        void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var settings = new AppSettings
                {
                    Position = _preset.ToString(),
                    CustomX = int.TryParse(TxtCustomX?.Text, out var cx) ? cx : 0,
                    CustomY = int.TryParse(TxtCustomY?.Text, out var cy) ? cy : 0,
                    UseCustomPosition = ChkCustomPosition?.IsChecked == true,
                    ResizeEnabled = ChkResizeNotify?.IsChecked == true,
                    NotifyWidth = int.TryParse(TxtWidth?.Text, out var w) ? w : 364,
                    NotifyHeight = int.TryParse(TxtHeight?.Text, out var h) ? h : 120,
                    IntervalMs = _intervalMs,
                    GoogleChatEnabled = ChkGoogleChat?.IsChecked == true,
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;

                var json = File.ReadAllText(_settingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s == null) return;

                if (Enum.TryParse<PositionPreset>(s.Position, out var preset))
                {
                    _preset = preset;
                    switch (preset)
                    {
                        case PositionPreset.TopLeft: RbTopLeft.IsChecked = true; break;
                        case PositionPreset.TopCenter: RbTopCenter.IsChecked = true; break;
                        case PositionPreset.TopRight: RbTopRight.IsChecked = true; break;
                        case PositionPreset.BottomLeft: RbBottomLeft.IsChecked = true; break;
                        case PositionPreset.BottomCenter: RbBottomCenter.IsChecked = true; break;
                        case PositionPreset.BottomRight: RbBottomRight.IsChecked = true; break;
                    }
                }

                ChkCustomPosition.IsChecked = s.UseCustomPosition;
                TxtCustomX.Text = s.CustomX.ToString();
                TxtCustomY.Text = s.CustomY.ToString();

                ChkResizeNotify.IsChecked = s.ResizeEnabled;
                TxtWidth.Text = s.NotifyWidth.ToString();
                TxtHeight.Text = s.NotifyHeight.ToString();

                _intervalMs = s.IntervalMs;
                SliderInterval.Value = s.IntervalMs;

                ChkGoogleChat.IsChecked = s.GoogleChatEnabled;
                _googleChatEnabled = s.GoogleChatEnabled;
            }
            catch { }
        }
    }
}
