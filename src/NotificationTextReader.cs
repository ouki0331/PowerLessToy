using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace NotifyPositionChanger
{
    /// <summary>
    /// Windows 11 のトースト通知からテキストを読み取るクラス。
    /// UIAutomation を使用して通知内容を取得する。
    /// </summary>
    public static class NotificationTextReader
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);

        [DllImport("user32.dll")]
        static extern int GetWindowTextLength(IntPtr hWnd);

        /// <summary>
        /// 通知ウィンドウのハンドルからテキスト内容を読み取る
        /// </summary>
        public static NotificationContent? ReadNotificationContent(IntPtr hWnd)
        {
            try
            {
                // ウィンドウタイトルを取得
                string title = GetWindowTitle(hWnd);

                // UIAutomation でテキスト要素を列挙
                var element = AutomationElement.FromHandle(hWnd);
                if (element == null) return null;

                var texts = new List<string>();
                CollectTextElements(element, texts, 0);

                if (texts.Count == 0 && string.IsNullOrEmpty(title))
                    return null;

                return ParseNotificationTexts(title, texts);
            }
            catch
            {
                // UIAutomation が使えない場合はウィンドウタイトルだけ返す
                string title = GetWindowTitle(hWnd);
                if (string.IsNullOrEmpty(title)) return null;

                return new NotificationContent
                {
                    AppName = "不明",
                    Title = title,
                    Body = "",
                    RawTexts = new List<string> { title }
                };
            }
        }

        /// <summary>
        /// UIAutomation の子要素を再帰的に走査してテキストを収集
        /// </summary>
        static void CollectTextElements(AutomationElement parent, List<string> texts, int depth)
        {
            if (depth > 10) return; // 深すぎる再帰を防止

            try
            {
                var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    try
                    {
                        string name = child.Current.Name ?? "";
                        if (!string.IsNullOrWhiteSpace(name) && name.Length > 0)
                        {
                            // 重複チェック
                            if (!texts.Contains(name))
                                texts.Add(name);
                        }

                        // ValuePattern からもテキスト取得を試みる
                        if (child.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
                        {
                            var vp = (ValuePattern)pattern;
                            string val = vp.Current.Value ?? "";
                            if (!string.IsNullOrWhiteSpace(val) && !texts.Contains(val))
                                texts.Add(val);
                        }

                        // 子要素を再帰
                        CollectTextElements(child, texts, depth + 1);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 取得したテキスト群を通知構造にパースする
        /// </summary>
        static NotificationContent ParseNotificationTexts(string windowTitle, List<string> texts)
        {
            var content = new NotificationContent
            {
                RawTexts = texts
            };

            // 典型的な通知テキスト構造:
            // [0] アプリ名 (例: "Google Chat")
            // [1] 送信者名 or タイトル
            // [2] メッセージ本文
            // ただしアプリによって異なる

            if (texts.Count >= 3)
            {
                content.AppName = texts[0];
                content.Title = texts[1];
                content.Body = string.Join("\n", texts.GetRange(2, texts.Count - 2));
            }
            else if (texts.Count == 2)
            {
                content.AppName = texts[0];
                content.Title = texts[1];
                content.Body = "";
            }
            else if (texts.Count == 1)
            {
                content.AppName = "";
                content.Title = texts[0];
                content.Body = "";
            }
            else
            {
                content.Title = windowTitle;
            }

            // Google Chat 通知かどうか判定
            content.IsGoogleChat = IsGoogleChatNotification(content, windowTitle);

            // Google Chat の場合、送信者名を抽出
            if (content.IsGoogleChat)
            {
                ExtractGoogleChatSender(content);
            }

            return content;
        }

        static bool IsGoogleChatNotification(NotificationContent content, string windowTitle)
        {
            string combined = $"{content.AppName} {content.Title} {content.Body} {windowTitle}".ToLower();

            return combined.Contains("google chat") ||
                   combined.Contains("chat.google") ||
                   combined.Contains("hangouts") ||
                   (content.AppName.ToLower().Contains("chrome") && combined.Contains("chat")) ||
                   (content.AppName.ToLower().Contains("edge") && combined.Contains("chat"));
        }

        static void ExtractGoogleChatSender(NotificationContent content)
        {
            // Google Chat 通知は通常「送信者名」がタイトルに入る
            // 本文は "送信者名: メッセージ" or 直接メッセージ
            string text = !string.IsNullOrEmpty(content.Body) ? content.Body : content.Title;

            // "名前: メッセージ" パターン
            int colonIdx = text.IndexOf(':');
            if (colonIdx > 0 && colonIdx < text.Length - 1)
            {
                content.SenderName = text.Substring(0, colonIdx).Trim();
                content.MessageText = text.Substring(colonIdx + 1).Trim();
            }
            else
            {
                content.SenderName = content.Title;
                content.MessageText = content.Body;
            }
        }

        static string GetWindowTitle(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd);
            if (len == 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 通知から読み取った内容
    /// </summary>
    public class NotificationContent
    {
        public string AppName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public bool IsGoogleChat { get; set; }
        public string SenderName { get; set; } = "";
        public string MessageText { get; set; } = "";
        public List<string> RawTexts { get; set; } = new();
    }
}
