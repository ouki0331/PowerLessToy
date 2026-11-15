using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.UI;

public class NotificationStackManager
{
    private readonly List<AcrylicNotificationWindow> _activeWindows = new();
    private readonly Queue<NotificationModel> _pendingQueue = new();
    private readonly Dictionary<string, DateTime> _dedupCache = new();
    private readonly Dictionary<string, List<DateTime>> _aggregationTracker = new();
    private readonly object _lock = new();

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    public int ActiveCount
    {
        get { lock (_lock) return _activeWindows.Count; }
    }

    public int QueueCount
    {
        get { lock (_lock) return _pendingQueue.Count; }
    }

    public void EnqueueNotification(NotificationModel notification)
    {
        if (notification == null) return;

        var settings = SettingsManager.Current;
        if (!settings.Enabled)
        {
            NotifyPositionLogger.Info("Notification ignored because NotifyPosition is currently disabled", nameof(NotificationStackManager));
            return;
        }

        lock (_lock)
        {
            // 1. De-duplication check
            string dedupKey = notification.DeduplicationKey;
            DateTime now = DateTime.Now;
            if (_dedupCache.TryGetValue(dedupKey, out DateTime lastSeen))
            {
                if ((now - lastSeen).TotalSeconds < 10)
                {
                    NotifyPositionLogger.Info($"De-duplication: Notification '{dedupKey}' suppressed (seen {(now - lastSeen).TotalSeconds:F1}s ago)", nameof(NotificationStackManager));
                    return;
                }
            }
            _dedupCache[dedupKey] = now;

            // Cleanup old dedup entries
            if (_dedupCache.Count > 500)
            {
                var expired = _dedupCache.Where(kv => (now - kv.Value).TotalMinutes > 5).Select(kv => kv.Key).ToList();
                foreach (var k in expired) _dedupCache.Remove(k);
            }

            // 2. Aggregation check
            if (settings.AggregationEnabled)
            {
                if (!_aggregationTracker.ContainsKey(notification.AppName))
                {
                    _aggregationTracker[notification.AppName] = new List<DateTime>();
                }
                var timestamps = _aggregationTracker[notification.AppName];
                timestamps.RemoveAll(t => (now - t).TotalSeconds > 10);
                timestamps.Add(now);

                if (timestamps.Count > 3)
                {
                    NotifyPositionLogger.Info($"Aggregation active for '{notification.AppName}': {timestamps.Count} messages in last 10s. Aggregating.", nameof(NotificationStackManager));
                    notification.Title = $"{notification.AppName} ({timestamps.Count} 件の新着通知)";
                    notification.Body = $"短時間に多数の通知を受信しました。\n最新: {notification.Title}";
                }
            }

            // 3. Queue or Show immediately
            _pendingQueue.Enqueue(notification);
            NotifyPositionLogger.Info($"Enqueued notification: App='{notification.AppName}', Title='{notification.Title}', QueueCount={_pendingQueue.Count}, ActiveCount={_activeWindows.Count}", nameof(NotificationStackManager));
        }

        // Process queue on UI thread
        Application.Current?.Dispatcher?.BeginInvoke(ProcessQueue);
    }

    public void ProcessQueue()
    {
        var settings = SettingsManager.Current;
        Rect workArea = GetTargetWorkArea(settings.MonitorSelection);

        lock (_lock)
        {
            while (_pendingQueue.Count > 0 && _activeWindows.Count < settings.MaxVisible)
            {
                var nextItem = _pendingQueue.Peek();

                // Check height budget
                double estimatedHeight = 110; // average card height
                double currentTotalHeight = _activeWindows.Sum(w => (w.ActualHeight > 0 ? w.ActualHeight : estimatedHeight) + settings.StackSpacing);
                if (_activeWindows.Count > 0 && (currentTotalHeight + estimatedHeight) > (workArea.Height * 0.5))
                {
                    NotifyPositionLogger.Info($"Max stack height budget reached ({currentTotalHeight:F0}px / limit {workArea.Height * 0.5:F0}px). Keeping remaining in queue.", nameof(NotificationStackManager));
                    break;
                }

                _pendingQueue.Dequeue();

                var win = new AcrylicNotificationWindow(nextItem, settings);
                win.Dismissed += OnWindowDismissed;
                _activeWindows.Add(win);

                // Initial position offscreen or calculated top
                CalculateCardTargetPosition(win, _activeWindows.Count - 1, settings, workArea, out double targetLeft, out double targetTop);
                win.Left = targetLeft;
                win.Top = targetTop;

                win.Show();
                win.AnimateEnter();

                NotifyPositionLogger.Info($"Displayed notification card index {_activeWindows.Count - 1} at ({targetLeft:F0}, {targetTop:F0})", nameof(NotificationStackManager));
            }

            // Re-layout existing windows smoothly
            RepositionActiveWindows(settings, workArea);
        }
    }

    private void OnWindowDismissed(AcrylicNotificationWindow window)
    {
        lock (_lock)
        {
            _activeWindows.Remove(window);
            NotifyPositionLogger.Info($"Notification card dismissed. Active remaining: {_activeWindows.Count}, Queued: {_pendingQueue.Count}", nameof(NotificationStackManager));
        }

        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            var settings = SettingsManager.Current;
            Rect workArea = GetTargetWorkArea(settings.MonitorSelection);
            lock (_lock)
            {
                RepositionActiveWindows(settings, workArea);
            }
            ProcessQueue();
        });
    }

    private void RepositionActiveWindows(NotifyPositionSettings settings, Rect workArea)
    {
        for (int i = 0; i < _activeWindows.Count; i++)
        {
            var win = _activeWindows[i];
            CalculateCardTargetPosition(win, i, settings, workArea, out double targetLeft, out double targetTop);

            if (Math.Abs(win.Left - targetLeft) > 1 || Math.Abs(win.Top - targetTop) > 1)
            {
                win.AnimateToPosition(targetLeft, targetTop, 220);
            }
        }
    }

    public static void CalculateCardTargetPosition(
        Window win,
        int stackIndex,
        NotifyPositionSettings settings,
        Rect workArea,
        out double targetLeft,
        out double targetTop)
    {
        double cardWidth = win.Width > 0 ? win.Width : 380;
        double cardHeight = win.ActualHeight > 0 ? win.ActualHeight : 105;
        double spacing = settings.StackSpacing;
        double margin = 20;

        // Calculate Y offset accumulated by prior cards
        double accumulatedY = margin;
        if (win.Tag is double customTop)
        {
            accumulatedY = customTop;
        }
        else
        {
            accumulatedY = margin + (stackIndex * (cardHeight + spacing));
        }

        switch (settings.Position)
        {
            case PositionPreset.TopCenter:
                targetLeft = workArea.Left + (workArea.Width - cardWidth) / 2;
                targetTop = workArea.Top + accumulatedY;
                break;

            case PositionPreset.TopLeft:
                targetLeft = workArea.Left + margin;
                targetTop = workArea.Top + accumulatedY;
                break;

            case PositionPreset.TopRight:
                targetLeft = workArea.Right - cardWidth - margin;
                targetTop = workArea.Top + accumulatedY;
                break;

            case PositionPreset.BottomCenter:
                targetLeft = workArea.Left + (workArea.Width - cardWidth) / 2;
                targetTop = workArea.Bottom - (margin + ((stackIndex + 1) * (cardHeight + spacing)));
                break;

            case PositionPreset.BottomLeft:
                targetLeft = workArea.Left + margin;
                targetTop = workArea.Bottom - (margin + ((stackIndex + 1) * (cardHeight + spacing)));
                break;

            case PositionPreset.BottomRight:
            default:
                targetLeft = workArea.Right - cardWidth - margin;
                targetTop = workArea.Bottom - (margin + ((stackIndex + 1) * (cardHeight + spacing)));
                break;
        }
    }

    public static Rect GetTargetWorkArea(string monitorSelection)
    {
        try
        {
            IntPtr hMonitor = IntPtr.Zero;

            if (string.Equals(monitorSelection, "Cursor", StringComparison.OrdinalIgnoreCase))
            {
                if (GetCursorPos(out POINT pt))
                {
                    hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                }
            }
            else if (string.Equals(monitorSelection, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);
                }
            }

            if (hMonitor == IntPtr.Zero)
            {
                hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
            }

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                return new Rect(
                    mi.rcWork.Left,
                    mi.rcWork.Top,
                    mi.rcWork.Right - mi.rcWork.Left,
                    mi.rcWork.Bottom - mi.rcWork.Top
                );
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Warn($"Failed to query monitor work area: {ex.Message}", ex, nameof(NotificationStackManager));
        }

        // Fallback to WPF PrimaryScreen
        return new Rect(
            0,
            0,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height
        );
    }

    public void DismissAll()
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            lock (_lock)
            {
                _pendingQueue.Clear();
                foreach (var win in _activeWindows.ToList())
                {
                    win.CloseWithAnimation();
                }
            }
        });
    }
}
