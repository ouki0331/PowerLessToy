using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PowerLessToy.NotifyPosition.Core;
using PowerLessToy.NotifyPosition.Listeners;
using PowerLessToy.NotifyPosition.UI;

namespace PowerLessToy.NotifyPosition;

public static class Program
{
    private static Mutex? _instanceMutex;
    private static NotificationStackManager? _stackManager;
    private static WinRtNotificationListener? _winRtListener;
    private static WindowScanNotificationFallback? _fallbackScanner;
    private static IpcServer? _ipcServer;

    [STAThread]
    public static void Main(string[] args)
    {
        bool isNewInstance = false;
        try
        {
            _instanceMutex = new Mutex(true, IpcConstants.MutexName, out isNewInstance);
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Failed to acquire mutex", ex, nameof(Program));
        }

        // If another instance is running, forward command and exit
        if (!isNewInstance)
        {
            string commandToSend = ParseArgsToIpcCommand(args);
            if (!string.IsNullOrEmpty(commandToSend))
            {
                if (IpcClient.SendCommand(commandToSend, out string resp))
                {
                    Console.WriteLine(resp);
                }
                else
                {
                    Console.WriteLine("Failed to contact running NotificationHost instance.");
                }
            }
            else
            {
                Console.WriteLine("PowerLessToy.NotificationHost is already running.");
            }
            return;
        }

        // First instance: start host
        NotifyPositionLogger.LogEnvironment("PowerLessToy.NotificationHost");
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            NotifyPositionLogger.Error("Unhandled AppDomain Exception in NotificationHost", e.ExceptionObject as Exception, nameof(Program));
        };

        var app = new NotificationHostApp();
        app.Startup += (s, e) =>
        {
            InitializeHost(args);
        };

        app.Exit += (s, e) =>
        {
            ShutdownHost();
        };

        app.Run();
    }

    private static void InitializeHost(string[] args)
    {
        try
        {
            _stackManager = new NotificationStackManager();

            // Setup listeners
            _winRtListener = new WinRtNotificationListener();
            _winRtListener.NotificationReceived += OnNotificationReceived;
            _winRtListener.Start();

            // Setup fallback window scanner
            _fallbackScanner = new WindowScanNotificationFallback();
            _fallbackScanner.NotificationReceived += OnNotificationReceived;
            _fallbackScanner.Start();

            // Setup IPC server
            _ipcServer = new IpcServer(HandleIpcCommand);
            _ipcServer.Start();

            NotifyPositionLogger.Info("NotificationHost initialized successfully", nameof(Program));

            // Process startup command if any
            string startupCmd = ParseArgsToIpcCommand(args);
            if (!string.IsNullOrEmpty(startupCmd))
            {
                HandleIpcCommand(startupCmd);
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Error during NotificationHost initialization", ex, nameof(Program));
        }
    }

    private static void OnNotificationReceived(NotificationModel model)
    {
        NotifyPositionLogger.Info($"NotificationHost received notification: Source={model.SourceType}, App='{model.AppName}', Title='{model.Title}'", nameof(Program));
        _stackManager?.EnqueueNotification(model);
    }

    private static string HandleIpcCommand(string command)
    {
        string cmdUpper = command.ToUpperInvariant().Trim();

        if (cmdUpper == IpcConstants.CmdPing)
        {
            return IpcConstants.RespPong;
        }

        if (cmdUpper == IpcConstants.CmdTest || cmdUpper == "TEST 1" || cmdUpper == "TEST:1")
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                _stackManager?.EnqueueNotification(CreateSampleNotification(1, 1));
            });
            return "OK: Test notification displayed";
        }

        if (cmdUpper == IpcConstants.CmdTest5 || cmdUpper == "TEST:5" || cmdUpper.StartsWith("TEST "))
        {
            int count = 5;
            var parts = command.Split(' ', ':');
            if (parts.Length > 1 && int.TryParse(parts[1], out int parsed)) count = parsed;

            Application.Current?.Dispatcher?.BeginInvoke(async () =>
            {
                for (int i = 1; i <= count; i++)
                {
                    _stackManager?.EnqueueNotification(CreateSampleNotification(i, count));
                    await Task.Delay(250); // slight delay to show queue & enter animations
                }
            });
            return $"OK: {count} test notifications queued";
        }

        if (cmdUpper == IpcConstants.CmdSettings)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var win = new SettingsWindow(model => _stackManager?.EnqueueNotification(model));
                win.Show();
                win.Activate();
            });
            return "OK: Settings window opened";
        }

        if (cmdUpper == IpcConstants.CmdStart)
        {
            SettingsManager.Update(s => s.Enabled = true);
            _winRtListener?.Start();
            _fallbackScanner?.Start();
            return "OK: Notifications started";
        }

        if (cmdUpper == IpcConstants.CmdStop)
        {
            SettingsManager.Update(s => s.Enabled = false);
            _winRtListener?.Stop();
            _fallbackScanner?.Stop();
            _stackManager?.DismissAll();
            return "OK: Notifications stopped";
        }

        if (cmdUpper == IpcConstants.CmdStatus)
        {
            var s = SettingsManager.Current;
            var statusObj = new
            {
                Enabled = s.Enabled,
                Mode = s.NotificationMode.ToString(),
                Position = s.Position.ToString(),
                WinRtSource = _winRtListener?.SourceName,
                WinRtStatus = _winRtListener?.StatusDetails,
                WinRtAccess = _winRtListener?.AccessStatus.ToString(),
                FallbackRunning = _fallbackScanner?.IsRunning,
                ActiveCount = _stackManager?.ActiveCount ?? 0,
                QueueCount = _stackManager?.QueueCount ?? 0
            };
            return JsonSerializer.Serialize(statusObj);
        }

        if (command.StartsWith(IpcConstants.CmdSetMode, StringComparison.OrdinalIgnoreCase))
        {
            string modeStr = command.Substring(IpcConstants.CmdSetMode.Length).Trim();
            if (Enum.TryParse<NotificationMode>(modeStr, true, out var mode))
            {
                SettingsManager.Update(s => s.NotificationMode = mode);
                return $"OK: NotificationMode set to {mode}";
            }
            return $"ERR: Unknown mode '{modeStr}'";
        }

        if (command.StartsWith(IpcConstants.CmdSetPosition, StringComparison.OrdinalIgnoreCase))
        {
            string posStr = command.Substring(IpcConstants.CmdSetPosition.Length).Trim();
            if (Enum.TryParse<PositionPreset>(posStr, true, out var pos))
            {
                SettingsManager.Update(s => s.Position = pos);
                return $"OK: Position set to {pos}";
            }
            return $"ERR: Unknown position '{posStr}'";
        }

        if (cmdUpper == "EXIT" || cmdUpper == "QUIT")
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                Application.Current.Shutdown();
            });
            return "OK: Shutting down";
        }

        return "ERR: Unknown command";
    }

    private static NotificationModel CreateSampleNotification(int index, int total)
    {
        if (total == 1)
        {
            return new NotificationModel
            {
                AppName = "稟議・ワークフロー",
                Title = "有給休暇の申請 (開発部 田中 太郎)",
                Body = "2026/11/20 (金) の休暇申請が届いています。内容を確認の上、承認または却下を選択してください。",
                SourceType = "Test",
                Buttons = new()
                {
                    new NotificationActionButton { Text = "同意する (Approve)", IsPrimary = true },
                    new NotificationActionButton { Text = "却下する (Reject)" }
                }
            };
        }

        return new NotificationModel
        {
            AppName = $"通知 #{index}",
            Title = $"スタックテスト ({index}/{total})",
            Body = $"垂直スタック・自動補位アニメーション・マウスホバー一時停止のテストメッセージです [ID: {index}]",
            SourceType = "Test"
        };
    }

    private static string ParseArgsToIpcCommand(string[] args)
    {
        if (args.Length == 0) return string.Empty;

        string first = args[0].ToLowerInvariant();
        if (first == "--test" || first == "-t" || first == "test")
        {
            if (args.Length > 1 && int.TryParse(args[1], out int count))
            {
                return $"TEST {count}";
            }
            return "TEST";
        }

        if (first == "--test-5" || first == "test5" || (first == "test" && args.Length > 1 && args[1] == "5"))
        {
            return "TEST 5";
        }

        if (first == "--settings" || first == "-s" || first == "settings")
        {
            return "SETTINGS";
        }

        if (first == "--status" || first == "status")
        {
            return "STATUS";
        }

        if (first == "--start" || first == "start")
        {
            return "START";
        }

        if (first == "--stop" || first == "stop")
        {
            return "STOP";
        }

        if (first == "--exit" || first == "exit")
        {
            return "EXIT";
        }

        return string.Join(" ", args);
    }

    private static void ShutdownHost()
    {
        NotifyPositionLogger.Info("NotificationHost shutting down", nameof(Program));
        _winRtListener?.Dispose();
        _fallbackScanner?.Dispose();
        _ipcServer?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
    }
}
