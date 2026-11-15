using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerLessToy.NotifyPosition.Core;
using Windows.UI.Notifications.Management;

namespace PowerLessToy.NotifyPosition.UI;

public partial class SettingsWindow : Window
{
    private readonly Action<NotificationModel> _testTrigger;

    public SettingsWindow(Action<NotificationModel> testTrigger)
    {
        InitializeComponent();
        _testTrigger = testTrigger ?? throw new ArgumentNullException(nameof(testTrigger));

        LoadSettingsToUi();
        CheckPermissionStatus();
    }

    private void LoadSettingsToUi()
    {
        var s = SettingsManager.Current;

        CmbMode.SelectedIndex = (int)s.NotificationMode;
        CmbPosition.SelectedIndex = (int)s.Position;
        TxtMaxVisible.Text = s.MaxVisible.ToString();
        TxtDismissSeconds.Text = s.AutoDismissSeconds.ToString();
        ChkAcrylic.IsChecked = s.UseAcrylic;
        ChkAggregation.IsChecked = s.AggregationEnabled;

        UpdateModeDescription((NotificationMode)CmbMode.SelectedIndex);
    }

    private void CheckPermissionStatus()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var status = listener.GetAccessStatus();
            TxtPermissionStatus.Text = $"権限ステータス: {status}";

            switch (status)
            {
                case UserNotificationListenerAccessStatus.Allowed:
                    TxtPermissionStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88)); // Green
                    break;
                case UserNotificationListenerAccessStatus.Denied:
                    TxtPermissionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58)); // Red
                    break;
                case UserNotificationListenerAccessStatus.Unspecified:
                default:
                    TxtPermissionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10)); // Orange
                    break;
            }
        }
        catch (Exception ex)
        {
            TxtPermissionStatus.Text = $"ステータス取得失敗 ({ex.Message})";
            TxtPermissionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
        }
    }

    private void RequestPermission_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Open Windows Settings for notifications privacy
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:privacy-notifications",
                UseShellExecute = true
            });
            NotifyPositionLogger.Info("Opened 'ms-settings:privacy-notifications' from settings GUI", nameof(SettingsWindow));
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Failed to open Windows Settings URI", ex, nameof(SettingsWindow));
            MessageBox.Show("Windowsの設定アプリを開けませんでした。\n手動で「設定 -> プライバシーとセキュリティ -> 通知」を開いてください。", "NotifyPosition", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMode != null)
        {
            UpdateModeDescription((NotificationMode)CmbMode.SelectedIndex);
        }
    }

    private void UpdateModeDescription(NotificationMode mode)
    {
        if (TxtModeDesc == null) return;

        switch (mode)
        {
            case NotificationMode.Replace:
                TxtModeDesc.Text = "【Replace モード】原生通知を捕捉後、RemoveNotification で消去しカスタム通知を表示します。（※Windowsの仕様上、原生通知が一瞬だけフラッシュする場合があります）";
                break;
            case NotificationMode.Mirror:
                TxtModeDesc.Text = "【Mirror モード】原生通知を消去せず、そのまま残した上でカスタム通知も同時に表示します（動作テスト・比較用）。";
                break;
            case NotificationMode.DoNotDisturb:
                TxtModeDesc.Text = "【DoNotDisturb モード】Windowsの「応答不可/集中モード」をONにしてシステム横断幕を非表示にし、本ツールがカスタム通知のみを表示します（原生のチラつきが一切なくなります）。";
                break;
        }
    }

    private void TestOne_Click(object sender, RoutedEventArgs e)
    {
        var test = new NotificationModel
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
        _testTrigger(test);
    }

    private void TestFive_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 1; i <= 5; i++)
        {
            int index = i;
            var test = new NotificationModel
            {
                AppName = $"テスト通知 #{index}",
                Title = $"スタック検証通知 ({index}/5)",
                Body = $"垂直スタック・自動補位アニメーション・マウスホバー一時停止のテストメッセージです [ID: {index}]",
                SourceType = "Test"
            };
            _testTrigger(test);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = NotifyPositionLogger.LogDirectory;
            if (System.IO.Directory.Exists(dir))
            {
                Process.Start("explorer.exe", dir);
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Failed to open log folder in explorer", ex, nameof(SettingsWindow));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int maxVisible = 5;
        int.TryParse(TxtMaxVisible.Text, out maxVisible);

        int dismissSeconds = 8;
        int.TryParse(TxtDismissSeconds.Text, out dismissSeconds);

        SettingsManager.Update(s =>
        {
            s.NotificationMode = (NotificationMode)CmbMode.SelectedIndex;
            s.Position = (PositionPreset)CmbPosition.SelectedIndex;
            s.MaxVisible = maxVisible;
            s.AutoDismissSeconds = dismissSeconds;
            s.UseAcrylic = ChkAcrylic.IsChecked ?? true;
            s.AggregationEnabled = ChkAggregation.IsChecked ?? false;
        });

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
