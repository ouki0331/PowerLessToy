using System;
using System.Collections.Generic;

namespace PowerLessToy.NotifyPosition.Core;

public class NotificationActionButton
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public Action? ActionCallback { get; set; }
}

public class NotificationModel
{
    public string UniqueId { get; set; } = Guid.NewGuid().ToString("N");
    public uint UserNotificationId { get; set; }
    public IntPtr NativeWindowHandle { get; set; } = IntPtr.Zero;
    public string AppName { get; set; } = "通知";
    public string AppId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourceType { get; set; } = "Unknown"; // WinRt, WindowScan, Test
    public byte[]? AppLogoBytes { get; set; }
    public string? AppLogoPath { get; set; }
    public List<NotificationActionButton> Buttons { get; set; } = new();
    public Action? OnClick { get; set; }
    public Action? OnDismiss { get; set; }

    public string DeduplicationKey => $"{AppName}|{Title}|{Body}".Trim();
}
