using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.Listeners;

public class WindowScanNotificationFallback : INotificationSource
{
    public event Action<NotificationModel>? NotificationReceived;
    public string SourceName => "WindowScan (Fallback)";
    public bool IsRunning { get; private set; }
    public string StatusDetails { get; private set; } = "Inactive";

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private static readonly string[] NotificationClassNames =
    {
        "Windows.UI.Core.CoreWindow",
        "Windows.UI.Composition.DesktopWindowContentBridge",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow"
    };

    private static readonly string[] NotificationProcessNames =
    {
        "ShellExperienceHost",
        "explorer",
        "SystemSettingsBroker"
    };

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private readonly HashSet<IntPtr> _handledWindows = new();
    private readonly object _lock = new();

    public int IntervalMs { get; set; } = 80;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusDetails = "Running window polling loop";
        _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
        NotifyPositionLogger.Info($"WindowScanNotificationFallback started with interval {IntervalMs}ms", nameof(WindowScanNotificationFallback));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        IsRunning = false;
        StatusDetails = "Stopped";
        NotifyPositionLogger.Info("WindowScanNotificationFallback stopped", nameof(WindowScanNotificationFallback));
    }

    private void MonitorLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ScanWindows(token);
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error("Exception in ScanWindows loop", ex, nameof(WindowScanNotificationFallback));
            }

            try
            {
                Task.Delay(IntervalMs, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception delayEx)
            {
                NotifyPositionLogger.Warn($"Delay interrupted: {delayEx.Message}", delayEx, nameof(WindowScanNotificationFallback));
                break;
            }
        }
    }

    private void ScanWindows(CancellationToken token)
    {
        var visibleHandles = new HashSet<IntPtr>();

        EnumWindows((hWnd, lParam) =>
        {
            if (token.IsCancellationRequested) return false;

            try
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                string className = sb.ToString();

                bool isCandidateClass = false;
                foreach (var n in NotificationClassNames)
                {
                    if (className.Contains(n, StringComparison.OrdinalIgnoreCase))
                    {
                        isCandidateClass = true;
                        break;
                    }
                }

                if (!isCandidateClass) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                string processName = "Unknown";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch (Exception pEx)
                {
                    NotifyPositionLogger.Debug($"Failed to query process for PID {pid}: {pEx.Message}", nameof(WindowScanNotificationFallback));
                    return true;
                }

                bool isCandidateProcess = false;
                foreach (var n in NotificationProcessNames)
                {
                    if (processName.Equals(n, StringComparison.OrdinalIgnoreCase))
                    {
                        isCandidateProcess = true;
                        break;
                    }
                }

                if (!isCandidateProcess) return true;

                GetWindowRect(hWnd, out RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                int sw = GetSystemMetrics(SM_CXSCREEN);
                int sh = GetSystemMetrics(SM_CYSCREEN);

                string debugWindowInfo = $"HWND=0x{hWnd.ToInt64():X8}, Class='{className}', Process='{processName}', PID={pid}, Rect=({rect.Left},{rect.Top} {w}x{h})";

                if (w > sw * 0.6 || h > sh * 0.6)
                {
                    NotifyPositionLogger.Debug($"Candidate rejected (too large for toast banner): {debugWindowInfo}", nameof(WindowScanNotificationFallback));
                    return true;
                }

                if (w < 60 || h < 30)
                {
                    NotifyPositionLogger.Debug($"Candidate rejected (too small): {debugWindowInfo}", nameof(WindowScanNotificationFallback));
                    return true;
                }

                if (w >= 120 && w <= 700 && h >= 40 && h <= 550)
                {
                    visibleHandles.Add(hWnd);
                    NotifyPositionLogger.Info($"Candidate accepted as toast window: {debugWindowInfo}", nameof(WindowScanNotificationFallback));
                    HandleCandidateWindow(hWnd, debugWindowInfo);
                }
                else
                {
                    NotifyPositionLogger.Debug($"Candidate rejected (dimension out of bounds {w}x{h}): {debugWindowInfo}", nameof(WindowScanNotificationFallback));
                }
            }
            catch (Exception enumEx)
            {
                NotifyPositionLogger.Error($"Error evaluating window HWND 0x{hWnd.ToInt64():X8}", enumEx, nameof(WindowScanNotificationFallback));
            }

            return true;
        }, IntPtr.Zero);

        lock (_lock)
        {
            _handledWindows.RemoveWhere(h => !visibleHandles.Contains(h) && !IsWindowVisible(h));
        }
    }

    private void HandleCandidateWindow(IntPtr hWnd, string windowInfo)
    {
        lock (_lock)
        {
            if (_handledWindows.Contains(hWnd))
            {
                // Already processed; in Replace mode, ensure it stays suppressed
                if (SettingsManager.Current.NotificationMode == NotificationMode.Replace)
                {
                    SetWindowPos(hWnd, IntPtr.Zero, -9999, -9999, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
                }
                return;
            }

            _handledWindows.Add(hWnd);
        }

        // IMPORTANT REQUIREMENT: Read content BEFORE pushing window off-screen!
        Task.Run(() =>
        {
            try
            {
                NotifyPositionLogger.Info($"Attempting to read content from candidate: {windowInfo}", nameof(WindowScanNotificationFallback));
                var model = ReadFromWindow(hWnd);

                if (model != null && (!string.IsNullOrWhiteSpace(model.Title) || !string.IsNullOrWhiteSpace(model.Body)))
                {
                    NotifyPositionLogger.Info($"Successfully parsed notification from window: App='{model.AppName}', Title='{model.Title}', Body='{model.Body}'", nameof(WindowScanNotificationFallback));

                    // Only now, if in Replace mode, hide native window
                    if (SettingsManager.Current.NotificationMode == NotificationMode.Replace)
                    {
                        bool moved = SetWindowPos(hWnd, IntPtr.Zero, -9999, -9999, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
                        NotifyPositionLogger.Info($"Moved native toast window off-screen: result={moved}", nameof(WindowScanNotificationFallback));
                    }

                    NotificationReceived?.Invoke(model);
                }
                else
                {
                    NotifyPositionLogger.Warn($"Failed to extract title/body from candidate: {windowInfo}. Native toast left visible.", null, nameof(WindowScanNotificationFallback));
                }
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error($"Exception while reading from candidate {windowInfo}", ex, nameof(WindowScanNotificationFallback));
            }
        });
    }

    private NotificationModel? ReadFromWindow(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null)
            {
                NotifyPositionLogger.Warn($"AutomationElement.FromHandle returned null for HWND 0x{hWnd.ToInt64():X8}", null, nameof(WindowScanNotificationFallback));
                return null;
            }

            var texts = new List<string>();
            CollectTexts(element, texts, 0);

            string appName = "システム通知";
            string title = string.Empty;
            string body = string.Empty;

            if (texts.Count >= 3)
            {
                appName = texts[0];
                title = texts[1];
                body = string.Join("\n", texts.GetRange(2, texts.Count - 2));
            }
            else if (texts.Count == 2)
            {
                appName = texts[0];
                title = texts[1];
            }
            else if (texts.Count == 1)
            {
                title = texts[0];
            }

            var model = new NotificationModel
            {
                NativeWindowHandle = hWnd,
                AppName = appName,
                Title = title,
                Body = body,
                SourceType = "WindowScan",
                Timestamp = DateTime.Now
            };

            // Query Action Buttons
            try
            {
                var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttonElements = element.FindAll(TreeScope.Descendants, buttonCondition);

                foreach (AutomationElement btn in buttonElements)
                {
                    try
                    {
                        string btnName = btn.Current.Name ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(btnName) &&
                            !btnName.Equals("閉じる", StringComparison.OrdinalIgnoreCase) &&
                            !btnName.Equals("Close", StringComparison.OrdinalIgnoreCase) &&
                            !btnName.Equals("Dismiss", StringComparison.OrdinalIgnoreCase))
                        {
                            var buttonElement = btn;
                            model.Buttons.Add(new NotificationActionButton
                            {
                                Text = btnName,
                                ActionCallback = () =>
                                {
                                    try
                                    {
                                        if (buttonElement.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern))
                                        {
                                            ((InvokePattern)pattern).Invoke();
                                            NotifyPositionLogger.Info($"Invoked native button '{btnName}' on HWND 0x{hWnd.ToInt64():X8}", nameof(WindowScanNotificationFallback));
                                        }
                                    }
                                    catch (Exception bEx)
                                    {
                                        NotifyPositionLogger.Error($"Failed to invoke button '{btnName}'", bEx, nameof(WindowScanNotificationFallback));
                                    }
                                }
                            });
                        }
                    }
                    catch (Exception itemEx)
                    {
                        NotifyPositionLogger.Debug($"Failed to inspect button item: {itemEx.Message}", nameof(WindowScanNotificationFallback));
                    }
                }
            }
            catch (Exception btnScanEx)
            {
                NotifyPositionLogger.Warn($"Failed to scan buttons via UIAutomation: {btnScanEx.Message}", btnScanEx, nameof(WindowScanNotificationFallback));
            }

            return model;
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error($"ReadFromWindow failed for HWND 0x{hWnd.ToInt64():X8}", ex, nameof(WindowScanNotificationFallback));
            return null;
        }
    }

    private static void CollectTexts(AutomationElement parent, List<string> texts, int depth)
    {
        if (depth > 6) return;

        try
        {
            var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                try
                {
                    if (child.Current.ControlType == ControlType.Button)
                        continue;

                    string name = child.Current.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name) && !texts.Contains(name))
                    {
                        texts.Add(name);
                    }

                    CollectTexts(child, texts, depth + 1);
                }
                catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
