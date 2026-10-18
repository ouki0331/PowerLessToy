using ManagedCommon;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

/// <summary>
/// Main class of this plugin that implement all used interfaces.
/// </summary>
public class Main : IPlugin, IDisposable
{
    /// <summary>
    /// ID of the plugin.
    /// </summary>
    public static string PluginID => "C78713C8DC3F4DD09967B9141F3E04EB";

    /// <summary>
    /// Name of the plugin.
    /// </summary>
    public string Name => "NotifyPosition 2.0";

    /// <summary>
    /// Description of the plugin.
    /// </summary>
    public string Description => "Customizes Windows notifications into modern card alerts with action proxying.";

    private PluginInitContext Context { get; set; }
    private string IconPath { get; set; }
    private bool Disposed { get; set; }

    private NotificationMover _mover;
    private VirtualDesktopLoopManager? _loopManager;

    public void Init(PluginInitContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(Context.API.GetCurrentTheme());

        _mover = new NotificationMover();
        _mover.Start();

        _loopManager = new VirtualDesktopLoopManager();
        _loopManager.Start();
    }

    public List<Result> Query(Query query)
    {
        var search = query.Search.ToLowerInvariant().Trim();
        var results = new List<Result>();

        if (string.IsNullOrEmpty(search))
        {
            results.Add(new Result
            {
                Title = "NotifyPosition 2.0",
                SubTitle = $"Current state: {(_mover.IsMonitoring ? "Active" : "Stopped")}, Position: {_mover.Preset}. Type 'test' to preview.",
                IcoPath = IconPath,
                Action = _ => true
            });
            int vdCount = VirtualDesktopLoopManager.GetDesktopCount();
            int vdCurrent = VirtualDesktopLoopManager.GetCurrentDesktopIndex() + 1;
            bool isVdRunning = _loopManager?.IsRunning ?? false;
            results.Add(new Result
            {
                Title = $"仮想デスクトップ循環切替 (Virtual Desktop Loop): {(isVdRunning ? "稼働中 (Active)" : "停止中 (Stopped)")}",
                SubTitle = $"現在: デスクトップ {vdCurrent} / 全 {vdCount} 画面 | Win + Ctrl + 左右 で端から端へループ",
                IcoPath = IconPath,
                Action = _ =>
                {
                    if (isVdRunning) _loopManager?.Stop();
                    else _loopManager?.Start();
                    return true;
                }
            });
            results.Add(new Result
            {
                Title = "テスト通知を表示 (Preview Approval Notification)",
                SubTitle = "承認・却下ボタン付きのカスタム通知プレビューを表示します",
                IcoPath = IconPath,
                Action = _ =>
                {
                    ShowTestNotification();
                    return true;
                }
            });
            return results;
        }

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

        if (search.StartsWith("test"))
        {
            results.Add(new Result
            {
                Title = "テスト通知を表示 (Preview Approval Notification)",
                SubTitle = "承認・却下ボタン付きのカスタム通知プレビューを表示します",
                IcoPath = IconPath,
                Action = _ =>
                {
                    ShowTestNotification();
                    return true;
                }
            });
            return results;
        }

        if (search.StartsWith("start"))
        {
            results.Add(new Result
            {
                Title = "Start notification mover",
                SubTitle = "Starts the background task to move notifications",
                IcoPath = IconPath,
                Action = _ => { _mover.Start(); return true; }
            });
        }
        else if (search.StartsWith("stop"))
        {
            results.Add(new Result
            {
                Title = "Stop notification mover",
                SubTitle = "Stops the background task",
                IcoPath = IconPath,
                Action = _ => { _mover.Stop(); return true; }
            });
        }

        var positions = new[] { "topleft", "topcenter", "topright", "bottomleft", "bottomcenter", "bottomright" };
        foreach (var pos in positions)
        {
            if (pos.Contains(search) || search.Contains(pos))
            {
                PositionPreset preset = (PositionPreset)Enum.Parse(typeof(PositionPreset), pos, true);
                results.Add(new Result
                {
                    Title = $"Set position to {pos}",
                    SubTitle = "Changes where notifications will appear",
                    IcoPath = IconPath,
                    Action = _ => { _mover.Preset = preset; return true; }
                });
            }
        }

        return results;
    }

    private void ShowTestNotification()
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            var testNotification = new ParsedNotification
            {
                AppName = "稟議・ワークフロー",
                Title = "有給休暇の申請 (開発部 田中 太郎)",
                Body = "2026/10/16 (金) の休暇申請が届いています。内容を確認の上、承認または却下を選択してください。",
                Buttons = new List<NotificationButtonInfo>
                {
                    new NotificationButtonInfo { Name = "同意する (Approve)" },
                    new NotificationButtonInfo { Name = "却下する (Reject)" }
                }
            };
            var win = new CustomToastWindow(testNotification, _mover.Preset);
            win.Show();
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed || !disposing) return;
        
        _mover?.Stop();
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
