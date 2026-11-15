using ManagedCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Wox.Plugin;
using PowerLessToy.NotifyPosition.Core;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

public class Main : IPlugin, IDisposable
{
    public static string PluginID => "C78713C8DC3F4DD09967B9141F3E04EB";

    public string Name => "NotifyPosition 2.0";

    public string Description => "Windows 通知を macOS 風のアクリル通知へ置換・スタック表示・位置調整します。";

    private PluginInitContext Context { get; set; } = null!;
    private string IconPath { get; set; } = string.Empty;
    private bool Disposed { get; set; }

    private VirtualDesktopLoopManager? _loopManager;

    public void Init(PluginInitContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(Context.API.GetCurrentTheme());

        NotifyPositionLogger.LogEnvironment("PowerToys.Plugin.NotifyPosition");

        // 1. Start VirtualDesktopLoop (kept intact and unimpacted)
        try
        {
            _loopManager = new VirtualDesktopLoopManager();
            _loopManager.Start();
            NotifyPositionLogger.Info("VirtualDesktopLoopManager started", nameof(Init));
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Failed to start VirtualDesktopLoopManager", ex, nameof(Init));
        }

        // 2. Ensure NotificationHost is running in background (non-blocking)
        Task.Run(() =>
        {
            try
            {
                HostController.EnsureHostRunning();
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error("HostController.EnsureHostRunning failed", ex, nameof(Init));
            }
        });
    }

    public List<Result> Query(Query query)
    {
        var search = query.Search.ToLowerInvariant().Trim();
        var results = new List<Result>();

        // 1. Virtual Desktop Loop query bypass
        if (search.StartsWith("vdesk") || search.StartsWith("loop") || search.StartsWith("desk"))
        {
            int vdCount = VirtualDesktopLoopManager.GetDesktopCount();
            int vdCurrent = VirtualDesktopLoopManager.GetCurrentDesktopIndex() + 1;
            bool isVdRunning = _loopManager?.IsRunning ?? false;
            results.Add(new Result
            {
                Title = $"仮想デスクトップ循環切替: {(isVdRunning ? "有効 (ON)" : "無効 (OFF)")}",
                SubTitle = $"現在: デスクトップ {vdCurrent} / 全 {vdCount} 画面 | クリックで切り替え (Win+Ctrl+左右で循環)",
                IcoPath = IconPath,
                Action = _ =>
                {
                    if (isVdRunning) _loopManager?.Stop();
                    else _loopManager?.Start();
                    return true;
                }
            });
            return results;
        }

        var settings = SettingsManager.Current;
        bool isHostAlive = IpcClient.IsHostRunning();

        // 2. Empty query: Main Dashboard
        if (string.IsNullOrEmpty(search))
        {
            results.Add(new Result
            {
                Title = $"NotifyPosition 2.0: {(isHostAlive ? "稼働中 (Active)" : "停止中 (Stopped)")}",
                SubTitle = $"モード: {settings.NotificationMode} | 位置: {settings.Position} | 最大スタック: {settings.MaxVisible} 件",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdSettings, out _);
                    return true;
                }
            });

            results.Add(new Result
            {
                Title = "テスト通知 (1件プレビュー)",
                SubTitle = "有給休暇申請の承認・却下ボタン付きカスタム通知を表示します ('notify test')",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdTest, out _);
                    return true;
                }
            });

            results.Add(new Result
            {
                Title = "テスト通知 (5件連続スタック検証)",
                SubTitle = "5件の通知を連続送信し、垂直スタック・自動補位・上限5件・マウスホバー一時停止を検証します ('notify test 5')",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdTest5, out _);
                    return true;
                }
            });

            results.Add(new Result
            {
                Title = "⚙️ NotifyPosition 設定を開く",
                SubTitle = "通知権限、動作モード (Replace/Mirror/DND)、位置、アクリル効果を設定します ('notify settings')",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdSettings, out _);
                    return true;
                }
            });

            // Virtual desktop loop status
            int vdCount = VirtualDesktopLoopManager.GetDesktopCount();
            int vdCurrent = VirtualDesktopLoopManager.GetCurrentDesktopIndex() + 1;
            bool isVdRunning = _loopManager?.IsRunning ?? false;
            results.Add(new Result
            {
                Title = $"仮想デスクトップ循環切替 (Virtual Desktop Loop): {(isVdRunning ? "稼働中" : "停止中")}",
                SubTitle = $"現在: デスクトップ {vdCurrent} / 全 {vdCount} 画面 | Win + Ctrl + 左右 でループ切替",
                IcoPath = IconPath,
                Action = _ =>
                {
                    if (isVdRunning) _loopManager?.Stop();
                    else _loopManager?.Start();
                    return true;
                }
            });

            return results;
        }

        // 3. Subcommand: test / test 5
        if (search.StartsWith("test"))
        {
            if (search.Contains("5"))
            {
                results.Add(new Result
                {
                    Title = "テスト通知 (5件連続スタック検証)",
                    SubTitle = "5件の通知を連続送信し、垂直スタック・自動補位・マウスホバー一時停止を検証します",
                    IcoPath = IconPath,
                    Action = _ =>
                    {
                        HostController.SendCommand(IpcConstants.CmdTest5, out _);
                        return true;
                    }
                });
            }
            else
            {
                results.Add(new Result
                {
                    Title = "テスト通知を表示 (1件プレビュー)",
                    SubTitle = "承認・却下ボタン付きのカスタム通知プレビューを表示します",
                    IcoPath = IconPath,
                    Action = _ =>
                    {
                        HostController.SendCommand(IpcConstants.CmdTest, out _);
                        return true;
                    }
                });

                results.Add(new Result
                {
                    Title = "テスト通知 (5件連続スタック検証)",
                    SubTitle = "5件の通知を連続送信し、垂直スタック・自動補位・マウスホバー一時停止を検証します ('notify test 5')",
                    IcoPath = IconPath,
                    Action = _ =>
                    {
                        HostController.SendCommand(IpcConstants.CmdTest5, out _);
                        return true;
                    }
                });
            }
            return results;
        }

        // 4. Subcommand: settings / config
        if (search.StartsWith("setting") || search.StartsWith("config") || search.StartsWith("opt"))
        {
            results.Add(new Result
            {
                Title = "⚙️ NotifyPosition 設定ウィンドウを開く",
                SubTitle = "通知取得権限、動作モード、表示位置、アクリル効果を設定します",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdSettings, out _);
                    return true;
                }
            });
            return results;
        }

        // 5. Subcommand: start / stop
        if (search.StartsWith("start"))
        {
            results.Add(new Result
            {
                Title = "Start NotifyPosition Host",
                SubTitle = "Starts the background notification listener",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdStart, out _);
                    return true;
                }
            });
            return results;
        }

        if (search.StartsWith("stop"))
        {
            results.Add(new Result
            {
                Title = "Stop NotifyPosition Host",
                SubTitle = "Stops notification listening and dismisses active cards",
                IcoPath = IconPath,
                Action = _ =>
                {
                    HostController.SendCommand(IpcConstants.CmdStop, out _);
                    return true;
                }
            });
            return results;
        }

        // 6. Subcommand: mode
        if (search.StartsWith("mode"))
        {
            foreach (NotificationMode mode in Enum.GetValues(typeof(NotificationMode)))
            {
                string mStr = mode.ToString();
                results.Add(new Result
                {
                    Title = $"Set mode to {mStr}",
                    SubTitle = $"Switch notification mode to {mStr}",
                    IcoPath = IconPath,
                    Action = _ =>
                    {
                        HostController.SendCommand(IpcConstants.CmdSetMode + mStr, out _);
                        return true;
                    }
                });
            }
            return results;
        }

        // 7. Subcommand: position presets
        var positions = new[] { "topcenter", "topleft", "topright", "bottomcenter", "bottomleft", "bottomright" };
        foreach (var pos in positions)
        {
            if (pos.Contains(search) || search.Contains(pos))
            {
                PositionPreset preset = (PositionPreset)Enum.Parse(typeof(PositionPreset), pos, true);
                results.Add(new Result
                {
                    Title = $"Set position to {preset}",
                    SubTitle = $"Changes where notification cards appear on active monitor",
                    IcoPath = IconPath,
                    Action = _ =>
                    {
                        HostController.SendCommand(IpcConstants.CmdSetPosition + preset, out _);
                        return true;
                    }
                });
            }
        }

        return results;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed || !disposing) return;

        _loopManager?.Dispose();

        if (Context?.API != null)
        {
            Context.API.ThemeChanged -= OnThemeChanged;
        }

        Disposed = true;
    }

    private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? "Images/notifyposition.light.png" : "Images/notifyposition.dark.png";

    private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);
}
