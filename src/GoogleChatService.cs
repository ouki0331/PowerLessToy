using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.HangoutsChat.v1;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace NotifyPositionChanger
{
    /// <summary>
    /// Google Chat API との連携を管理するサービスクラス。
    /// OAuth 2.0 認証 → スペース一覧 → メッセージ取得 → 返信送信
    /// </summary>
    public class GoogleChatService : IDisposable
    {
        HangoutsChatService? _chatService;
        UserCredential? _credential;
        bool _isAuthenticated;

        // credentials.json の保存先
        readonly string _credentialsDir;
        readonly string _credentialsPath;
        readonly string _tokenDir;

        public bool IsAuthenticated => _isAuthenticated;

        public event Action<string>? OnLog;

        public GoogleChatService()
        {
            _credentialsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NotifyPositionChanger");
            _credentialsPath = Path.Combine(_credentialsDir, "credentials.json");
            _tokenDir = Path.Combine(_credentialsDir, "token");
        }

        /// <summary>
        /// credentials.json が配置済みかチェック
        /// </summary>
        public bool HasCredentialsFile()
        {
            return File.Exists(_credentialsPath);
        }

        /// <summary>
        /// credentials.json をコピー配置する
        /// </summary>
        public void SetCredentialsFile(string sourcePath)
        {
            if (!Directory.Exists(_credentialsDir))
                Directory.CreateDirectory(_credentialsDir);

            File.Copy(sourcePath, _credentialsPath, overwrite: true);
            Log("credentials.json を配置しました");
        }

        public string GetCredentialsPath() => _credentialsPath;

        /// <summary>
        /// OAuth 2.0 認証を実行（ブラウザが開く）
        /// </summary>
        public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
        {
            try
            {
                if (!HasCredentialsFile())
                {
                    Log("credentials.json が見つかりません");
                    return false;
                }

                using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read);

                _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[]
                    {
                        "https://www.googleapis.com/auth/chat.spaces.readonly",
                        "https://www.googleapis.com/auth/chat.messages",
                        "https://www.googleapis.com/auth/chat.messages.create",
                        "https://www.googleapis.com/auth/chat.messages.readonly",
                    },
                    "user",
                    ct,
                    new FileDataStore(_tokenDir, true)
                );

                _chatService = new HangoutsChatService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = _credential,
                    ApplicationName = "NotifyPositionChanger"
                });

                _isAuthenticated = true;
                Log("Google Chat 認証成功");
                return true;
            }
            catch (Exception ex)
            {
                Log($"認証エラー: {ex.Message}");
                _isAuthenticated = false;
                return false;
            }
        }

        /// <summary>
        /// 認証トークンを削除してログアウト
        /// </summary>
        public void Logout()
        {
            try
            {
                if (Directory.Exists(_tokenDir))
                    Directory.Delete(_tokenDir, true);
            }
            catch { }
            _chatService?.Dispose();
            _chatService = null;
            _credential = null;
            _isAuthenticated = false;
            Log("ログアウトしました");
        }

        /// <summary>
        /// スペース (チャットルーム / DM) の一覧を取得
        /// </summary>
        public async Task<List<SpaceInfo>> GetSpacesAsync()
        {
            if (_chatService == null) return new List<SpaceInfo>();

            var result = new List<SpaceInfo>();
            try
            {
                var request = _chatService.Spaces.List();
                request.PageSize = 100;

                var response = await request.ExecuteAsync();
                if (response.Spaces != null)
                {
                    foreach (var space in response.Spaces)
                    {
                        result.Add(new SpaceInfo
                        {
                            Name = space.Name ?? "",
                            DisplayName = space.DisplayName ?? "(DM)",
                            Type = space.Type ?? "UNKNOWN",
                            SingleUserBotDm = space.SingleUserBotDm ?? false,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"スペース取得エラー: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 特定スペースの最新メッセージを取得
        /// </summary>
        public async Task<List<ChatMessageInfo>> GetRecentMessagesAsync(string spaceName, int count = 10)
        {
            if (_chatService == null) return new List<ChatMessageInfo>();

            var result = new List<ChatMessageInfo>();
            try
            {
                var request = _chatService.Spaces.Messages.List(spaceName);
                request.PageSize = count;
                request.OrderBy = "createTime desc";

                var response = await request.ExecuteAsync();
                if (response.Messages != null)
                {
                    foreach (var msg in response.Messages.Reverse())
                    {
                        result.Add(new ChatMessageInfo
                        {
                            Name = msg.Name ?? "",
                            SenderName = msg.Sender?.DisplayName ?? "不明",
                            Text = msg.Text ?? "",
                            CreateTime = msg.CreateTimeDateTimeOffset?.ToString("o") ?? "",
                            ThreadName = msg.Thread?.Name ?? "",
                            SpaceName = spaceName,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"メッセージ取得エラー: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// メッセージを送信 (返信)
        /// threadName を指定するとスレッド返信になる
        /// </summary>
        public async Task<bool> SendMessageAsync(string spaceName, string text, string? threadName = null)
        {
            if (_chatService == null) return false;

            try
            {
                var message = new Message
                {
                    Text = text
                };

                if (!string.IsNullOrEmpty(threadName))
                {
                    message.Thread = new Google.Apis.HangoutsChat.v1.Data.Thread { Name = threadName };
                }

                var request = _chatService.Spaces.Messages.Create(message, spaceName);

                await request.ExecuteAsync();
                Log($"返信を送信しました → {spaceName}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"送信エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通知テキストからスペースを推定して最新メッセージを取得する
        /// </summary>
        public async Task<(SpaceInfo? space, List<ChatMessageInfo> messages)>
            FindSpaceAndMessagesAsync(string senderHint)
        {
            var spaces = await GetSpacesAsync();

            // 送信者名でマッチ試行
            // 通知には「名前: メッセージ」形式が多い
            foreach (var space in spaces)
            {
                if (!string.IsNullOrEmpty(space.DisplayName) &&
                    senderHint.Contains(space.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    var msgs = await GetRecentMessagesAsync(space.Name, 5);
                    return (space, msgs);
                }
            }

            // DM空間を全部チェックして直近メッセージの送信者名で照合
            foreach (var space in spaces.Where(s => s.Type == "DIRECT_MESSAGE"))
            {
                var msgs = await GetRecentMessagesAsync(space.Name, 3);
                if (msgs.Any(m => senderHint.Contains(m.SenderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (space, msgs);
                }
            }

            // 見つからない場合、全スペースの最新メッセージを返す
            if (spaces.Count > 0)
            {
                var firstSpace = spaces[0];
                var msgs = await GetRecentMessagesAsync(firstSpace.Name, 5);
                return (firstSpace, msgs);
            }

            return (null, new List<ChatMessageInfo>());
        }

        void Log(string msg) => OnLog?.Invoke(msg);

        public void Dispose()
        {
            _chatService?.Dispose();
        }
    }

    // ═══════════════════════════════════════════
    //  データモデル
    // ═══════════════════════════════════════════

    public class SpaceInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Type { get; set; } = "";
        public bool SingleUserBotDm { get; set; }
    }

    public class ChatMessageInfo
    {
        public string Name { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string Text { get; set; } = "";
        public string CreateTime { get; set; } = "";
        public string ThreadName { get; set; } = "";
        public string SpaceName { get; set; } = "";
    }
}
