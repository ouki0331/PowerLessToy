using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NotifyPositionChanger
{
    public partial class ChatPopupWindow : Window
    {
        readonly GoogleChatService _chatService;
        string _spaceName = "";
        string _threadName = "";

        public ChatPopupWindow(GoogleChatService chatService)
        {
            InitializeComponent();
            _chatService = chatService;
            ReplyTextBox.Focus();
        }

        // ═══════════════════════════════════════════
        //  公開メソッド: データをセット
        // ═══════════════════════════════════════════

        /// <summary>
        /// 通知内容だけでポップアップ表示 (API取得前の即時表示用)
        /// </summary>
        public void ShowNotificationContent(NotificationContent content)
        {
            HeaderSender.Text = !string.IsNullOrEmpty(content.SenderName)
                ? content.SenderName : content.Title;
            HeaderSpace.Text = content.AppName;

            AddMessageBubble(
                content.SenderName ?? "送信者",
                !string.IsNullOrEmpty(content.MessageText) ? content.MessageText : content.Body,
                DateTime.Now.ToString("HH:mm"),
                isOther: true
            );
        }

        /// <summary>
        /// APIから取得したメッセージ履歴をセット
        /// </summary>
        public void SetMessages(SpaceInfo space, List<ChatMessageInfo> messages)
        {
            _spaceName = space.Name;
            HeaderSender.Text = space.DisplayName;
            HeaderSpace.Text = space.Type == "DIRECT_MESSAGE" ? "ダイレクトメッセージ" : "スペース";

            MessagePanel.Children.Clear();

            foreach (var msg in messages)
            {
                if (string.IsNullOrEmpty(_threadName) && !string.IsNullOrEmpty(msg.ThreadName))
                    _threadName = msg.ThreadName;

                string time = "";
                if (DateTime.TryParse(msg.CreateTime, out var dt))
                    time = dt.ToLocalTime().ToString("HH:mm");

                AddMessageBubble(msg.SenderName, msg.Text, time, isOther: true);
            }

            ScrollToBottom();
        }

        /// <summary>
        /// スペース名を直接セット (通知テキストからスペースを特定できた場合)
        /// </summary>
        public void SetSpaceName(string spaceName)
        {
            _spaceName = spaceName;
        }

        public void SetThreadName(string threadName)
        {
            _threadName = threadName;
        }

        // ═══════════════════════════════════════════
        //  メッセージバブルUI
        // ═══════════════════════════════════════════

        void AddMessageBubble(string sender, string text, string time, bool isOther)
        {
            var bubble = new Border
            {
                Background = isOther
                    ? (Brush)FindResource("SurfaceBrush")
                    : (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(
                    isOther ? 4 : 12,
                    isOther ? 12 : 4,
                    12, 12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(
                    isOther ? 0 : 60, 4,
                    isOther ? 60 : 0, 4),
                HorizontalAlignment = isOther ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                MaxWidth = 350,
            };

            var stack = new StackPanel();

            // 送信者名
            if (isOther)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = sender,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("HighlightBrush"),
                    Margin = new Thickness(0, 0, 0, 2),
                });
            }

            // メッセージ本文
            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = (Brush)FindResource("TextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });

            // 時刻
            if (!string.IsNullOrEmpty(time))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = time,
                    FontSize = 9,
                    Foreground = (Brush)FindResource("SubTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            bubble.Child = stack;
            MessagePanel.Children.Add(bubble);
        }

        void ScrollToBottom()
        {
            MessageScroll.ScrollToEnd();
        }

        // ═══════════════════════════════════════════
        //  返信送信
        // ═══════════════════════════════════════════

        async void Send_Click(object sender, RoutedEventArgs e)
        {
            await SendReplyAsync();
        }

        void ReplyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                _ = SendReplyAsync();
            }
        }

        async Task SendReplyAsync()
        {
            string text = ReplyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (string.IsNullOrEmpty(_spaceName))
            {
                MessageBox.Show("送信先のスペースが特定できません。\n設定画面でGoogle Chat認証を行ってください。",
                    "送信エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 送信中UI
            SendButton.IsEnabled = false;
            ReplyTextBox.IsEnabled = false;
            SendingOverlay.Visibility = Visibility.Visible;

            try
            {
                bool success = await _chatService.SendMessageAsync(
                    _spaceName, text,
                    string.IsNullOrEmpty(_threadName) ? null : _threadName);

                if (success)
                {
                    // 送信したメッセージをUIに追加
                    AddMessageBubble("自分", text, DateTime.Now.ToString("HH:mm"), isOther: false);
                    ReplyTextBox.Text = "";
                    ScrollToBottom();
                }
                else
                {
                    MessageBox.Show("メッセージの送信に失敗しました。", "送信エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"送信エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SendButton.IsEnabled = true;
                ReplyTextBox.IsEnabled = true;
                SendingOverlay.Visibility = Visibility.Collapsed;
                ReplyTextBox.Focus();
            }
        }

        // ═══════════════════════════════════════════
        //  ウィンドウ操作
        // ═══════════════════════════════════════════

        void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
