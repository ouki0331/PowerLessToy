using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

public enum PositionPreset { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

public class NotificationMover
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_ASYNCWINDOWPOS = 0x4000;
    const uint SWP_NOSIZE = 0x0001;
    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;

    public PositionPreset Preset { get; set; } = PositionPreset.TopRight;
    public int IntervalMs { get; set; } = 60;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    public bool IsMonitoring { get; private set; }

    private readonly HashSet<IntPtr> _handledWindows = new();

    static readonly string[] NotificationClassNames = { "Windows.UI.Core.CoreWindow", "Windows.UI.Composition.DesktopWindowContentBridge" };
    static readonly string[] NotificationProcessNames = { "ShellExperienceHost", "explorer" };

    public void Start()
    {
        if (IsMonitoring) return;
        _cts = new CancellationTokenSource();
        IsMonitoring = true;
        _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsMonitoring) return;
        _cts?.Cancel();
        IsMonitoring = false;
    }

    void MonitorLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { ScanAndInterceptNotifications(token); } catch { }
            try { Task.Delay(IntervalMs, token).Wait(token); } catch { break; }
        }
    }

    void ScanAndInterceptNotifications(CancellationToken token)
    {
        var visibleHandles = new HashSet<IntPtr>();

        EnumWindows((hWnd, lParam) =>
        {
            if (token.IsCancellationRequested) return false;
            if (!IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            var className = sb.ToString();

            bool isClass = false;
            foreach (var n in NotificationClassNames) if (className.Contains(n)) { isClass = true; break; }
            if (!isClass) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            try
            {
                var proc = Process.GetProcessById((int)pid);
                bool isProc = false;
                foreach (var n in NotificationProcessNames) if (proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)) { isProc = true; break; }
                if (!isProc) return true;
            }
            catch { return true; }

            GetWindowRect(hWnd, out RECT rect);
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            int sw = GetSystemMetrics(SM_CXSCREEN);
            int sh = GetSystemMetrics(SM_CYSCREEN);
            if (w > sw * 0.5 || h > sh * 0.5) return true;
            if (w < 50 || h < 30) return true;

            if (w >= 150 && w <= 600 && h >= 40 && h <= 500)
            {
                visibleHandles.Add(hWnd);
                InterceptNotification(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        // Cleanup dead handles
        _handledWindows.RemoveWhere(h => !visibleHandles.Contains(h) && !IsWindowVisible(h));
    }

    void InterceptNotification(IntPtr hWnd)
    {
        lock (_handledWindows)
        {
            if (_handledWindows.Contains(hWnd))
            {
                // Keep pushed off-screen
                SetWindowPos(hWnd, IntPtr.Zero, -9999, -9999, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
                return;
            }

            _handledWindows.Add(hWnd);
        }

        // 1. Move native toast off-screen immediately so user doesn't see the ugly default toast
        SetWindowPos(hWnd, IntPtr.Zero, -9999, -9999, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);

        // 2. Read full content + action buttons from UIAutomation
        Task.Run(() =>
        {
            var content = NotificationReader.ReadFromHandle(hWnd);
            if (content != null && (!string.IsNullOrEmpty(content.Title) || !string.IsNullOrEmpty(content.Body)))
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    try
                    {
                        var customWin = new CustomToastWindow(content, Preset);
                        customWin.Show();
                    }
                    catch { }
                });
            }
        });
    }
}
