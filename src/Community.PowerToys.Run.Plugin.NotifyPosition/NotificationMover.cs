using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    public bool ResizeEnabled { get; set; } = false;
    public int NotifyWidth { get; set; } = 364;
    public int NotifyHeight { get; set; } = 120;
    public int IntervalMs { get; set; } = 50;

    private CancellationTokenSource _cts;
    private Task _monitorTask;
    public bool IsMonitoring { get; private set; }

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
            try { ScanAndMoveNotifications(token); } catch { }
            try { Task.Delay(IntervalMs, token).Wait(token); } catch { break; }
        }
    }

    void ScanAndMoveNotifications(CancellationToken token)
    {
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
            var proc = Process.GetProcessById((int)pid);
            bool isProc = false;
            foreach (var n in NotificationProcessNames) if (proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)) { isProc = true; break; }
            if (!isProc) return true;

            GetWindowRect(hWnd, out RECT rect);
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            int sw = GetSystemMetrics(SM_CXSCREEN);
            int sh = GetSystemMetrics(SM_CYSCREEN);
            if (w > sw * 0.5 || h > sh * 0.5) return true;
            if (w < 50 || h < 30) return true;
            if (w >= 150 && w <= 600 && h >= 40 && h <= 500)
            {
                MoveNotificationWindow(hWnd, w, h, rect);
            }

            return true;
        }, IntPtr.Zero);

        TryFindAndMove("Windows.UI.Core.CoreWindow", "新しい通知");
        TryFindAndMove("Windows.UI.Core.CoreWindow", "New notification");
    }

    void TryFindAndMove(string className, string windowName)
    {
        IntPtr hWnd = FindWindow(className, windowName);
        if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd))
        {
            GetWindowRect(hWnd, out RECT rect);
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            MoveNotificationWindow(hWnd, w, h, rect);
        }
    }

    void MoveNotificationWindow(IntPtr hWnd, int origW, int origH, RECT currentRect)
    {
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        int margin = 12;

        int finalW = ResizeEnabled ? NotifyWidth : origW;
        int finalH = ResizeEnabled ? NotifyHeight : origH;

        int taskbar = 48;
        int targetX, targetY;
        switch (Preset)
        {
            case PositionPreset.TopLeft: targetX = margin; targetY = margin; break;
            case PositionPreset.TopCenter: targetX = (screenW - finalW) / 2; targetY = margin; break;
            case PositionPreset.TopRight: targetX = screenW - finalW - margin; targetY = margin; break;
            case PositionPreset.BottomLeft: targetX = margin; targetY = screenH - finalH - margin - taskbar; break;
            case PositionPreset.BottomCenter: targetX = (screenW - finalW) / 2; targetY = screenH - finalH - margin - taskbar; break;
            case PositionPreset.BottomRight: targetX = screenW - finalW - margin; targetY = screenH - finalH - margin - taskbar; break;
            default: targetX = screenW - finalW - margin; targetY = margin; break;
        }

        if (currentRect.Left == targetX && currentRect.Top == targetY && origW == finalW && origH == finalH) return;

        uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
        if (!ResizeEnabled) flags |= SWP_NOSIZE;

        SetWindowPos(hWnd, IntPtr.Zero, targetX, targetY, finalW, finalH, flags);
    }
}
