using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

#region COM Interface Definitions
internal static class VirtualDesktopGuids
{
    public static readonly Guid CLSID_ImmersiveShell = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    public static readonly Guid CLSID_VirtualDesktopManagerInternal = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
internal interface IObjectArray
{
    void GetCount(out int count);
    void GetAt(int index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
internal interface IServiceProvider10
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object QueryService(ref Guid service, ref Guid riid);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")] // Windows 11 22H2 / 23H2 / 24H2
internal interface IVirtualDesktop
{
    bool IsViewVisible(object view);
    Guid GetId();
    [return: MarshalAs(UnmanagedType.HString)]
    string GetName();
    [return: MarshalAs(UnmanagedType.HString)]
    string GetWallpaperPath();
    bool IsRemote();
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")] // Windows 11 22H2 / 23H2 / 24H2
internal interface IVirtualDesktopManagerInternal
{
    int GetCount();
    void MoveViewToDesktop(object view, IVirtualDesktop desktop);
    bool CanViewMoveDesktops(object view);
    IVirtualDesktop GetCurrentDesktop();
    void GetDesktops(out IObjectArray desktops);
    [PreserveSig]
    int GetAdjacentDesktop(IVirtualDesktop from, int direction, out IVirtualDesktop desktop);
    void SwitchDesktop(IVirtualDesktop desktop);
    void SwitchDesktopAndMoveForegroundView(IVirtualDesktop desktop);
    IVirtualDesktop CreateDesktop();
    void MoveDesktop(IVirtualDesktop desktop, int nIndex);
    void RemoveDesktop(IVirtualDesktop desktop, IVirtualDesktop fallback);
    IVirtualDesktop FindDesktop(ref Guid desktopid);
    void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktop desktop, out IObjectArray unknown1, out IObjectArray unknown2);
    void SetDesktopName(IVirtualDesktop desktop, [MarshalAs(UnmanagedType.HString)] string name);
    void SetDesktopWallpaper(IVirtualDesktop desktop, [MarshalAs(UnmanagedType.HString)] string path);
    void UpdateWallpaperPathForAllDesktops([MarshalAs(UnmanagedType.HString)] string path);
    void CopyDesktopState(object pView0, object pView1);
    void CreateRemoteDesktop([MarshalAs(UnmanagedType.HString)] string path, out IVirtualDesktop desktop);
    void SwitchRemoteDesktop(IVirtualDesktop desktop, IntPtr switchtype);
    void SwitchDesktopWithAnimation(IVirtualDesktop desktop);
    void GetLastActiveDesktop(out IVirtualDesktop desktop);
    void WaitForAnimationToComplete();
}
#endregion

/// <summary>
/// Windows 仮想デスクトップ循環切替マネージャー (Virtual Desktop Loop Manager)
/// Win + Ctrl + Left / Right の端（端点）における循環（ラップアラウンド）切替を実現します。
/// </summary>
public class VirtualDesktopLoopManager : IDisposable
{
    #region Win32 P/Invoke
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_QUIT = 0x0012;

    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int SW_MINIMIZE = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    #endregion

    private static IVirtualDesktopManagerInternal? _managerInternal;
    private static bool _comInitAttempted;
    private static long _lastSwitchTick;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _disposed;

    public bool IsRunning => _hookId != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;

        EnsureComInitialized();

        _proc = HookCallback;
        var readyEvent = new ManualResetEventSlim(false);

        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr moduleHandle = curModule?.ModuleName != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;

            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
            readyEvent.Set();

            if (_hookId == IntPtr.Zero)
            {
                Debug.WriteLine($"[VirtualDesktopLoop] Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
                return;
            }

            Debug.WriteLine("[VirtualDesktopLoop] Keyboard hook successfully installed.");

            // STA ワーカースレッド用の Windows メッセージループ
            while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_QUIT) break;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        });

        _hookThread.IsBackground = true;
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        readyEvent.Wait(2000);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(1000);
            _hookThread = null;
            _hookThreadId = 0;
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        Debug.WriteLine("[VirtualDesktopLoop] Keyboard hook stopped.");
    }

    private static void EnsureComInitialized()
    {
        if (_comInitAttempted) return;
        _comInitAttempted = true;

        try
        {
            var shellType = Type.GetTypeFromCLSID(VirtualDesktopGuids.CLSID_ImmersiveShell);
            if (shellType == null) return;

            var shell = (IServiceProvider10?)Activator.CreateInstance(shellType);
            if (shell == null) return;

            var serviceGuid = VirtualDesktopGuids.CLSID_VirtualDesktopManagerInternal;
            var riid = typeof(IVirtualDesktopManagerInternal).GUID;
            _managerInternal = (IVirtualDesktopManagerInternal?)shell.QueryService(ref serviceGuid, ref riid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VirtualDesktopLoop] COM initialization failed: {ex.Message}");
            _managerInternal = null;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_LEFT || vkCode == VK_RIGHT)
            {
                bool isWinDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                // Win + Ctrl + Left / Right のみ対象（Alt や Shift が押されている場合は除外）
                if (isWinDown && isCtrlDown && !isAltDown && !isShiftDown)
                {
                    long now = Environment.TickCount64;
                    if (now - _lastSwitchTick < 280)
                    {
                        // アニメーション中の連打による重複処理を抑制
                        return (IntPtr)1;
                    }

                    if (vkCode == VK_RIGHT)
                    {
                        if (HandleSwitchRight())
                        {
                            _lastSwitchTick = now;
                            return (IntPtr)1; // 端でのループを処理したため、Windows 本体のキー処理を抑制
                        }
                    }
                    else if (vkCode == VK_LEFT)
                    {
                        if (HandleSwitchLeft())
                        {
                            _lastSwitchTick = now;
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>
    /// 右方向への切替処理。右端（最後のデスクトップ）にいる場合のみ、先頭（Desktop 0）へ循環切替。
    /// 端点以外は false を返し、Windows 標準のスムーズな画面切替に委ねます。
    /// </summary>
    private static bool HandleSwitchRight()
    {
        int count = GetDesktopCount();
        if (count <= 1) return false;

        int current = GetCurrentDesktopIndex();
        if (current >= count - 1)
        {
            // 右端に到達 -> 最初のデスクトップ (index 0) へループ
            SwitchToDesktopIndex(0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 左方向への切替処理。左端（最初のデスクトップ）にいる場合のみ、末尾（Desktop count - 1）へ循環切替。
    /// 端点以外は false を返し、Windows 標準のスムーズな画面切替に委ねます。
    /// </summary>
    private static bool HandleSwitchLeft()
    {
        int count = GetDesktopCount();
        if (count <= 1) return false;

        int current = GetCurrentDesktopIndex();
        if (current <= 0)
        {
            // 左端に到達 -> 最後のデスクトップ (index count - 1) へループ
            SwitchToDesktopIndex(count - 1);
            return true;
        }

        return false;
    }

    public static int GetDesktopCount()
    {
        EnsureComInitialized();

        if (_managerInternal != null)
        {
            try
            {
                return _managerInternal.GetCount();
            }
            catch { }
        }

        // レジストリからのフォールバック取得
        return GetDesktopCountFromRegistry();
    }

    public static int GetCurrentDesktopIndex()
    {
        EnsureComInitialized();

        if (_managerInternal != null)
        {
            try
            {
                var currentDesktop = _managerInternal.GetCurrentDesktop();
                if (currentDesktop != null)
                {
                    Guid currentId = currentDesktop.GetId();
                    _managerInternal.GetDesktops(out IObjectArray desktops);
                    desktops.GetCount(out int count);
                    var iid = typeof(IVirtualDesktop).GUID;

                    for (int i = 0; i < count; i++)
                    {
                        desktops.GetAt(i, ref iid, out object objDesktop);
                        if (objDesktop is IVirtualDesktop vd && vd.GetId() == currentId)
                        {
                            Marshal.ReleaseComObject(desktops);
                            return i;
                        }
                    }
                    Marshal.ReleaseComObject(desktops);
                }
            }
            catch { }
        }

        return GetCurrentDesktopIndexFromRegistry();
    }

    public static void SwitchToDesktopIndex(int index)
    {
        EnsureComInitialized();

        if (_managerInternal != null)
        {
            try
            {
                _managerInternal.GetDesktops(out IObjectArray desktops);
                var iid = typeof(IVirtualDesktop).GUID;
                desktops.GetAt(index, ref iid, out object objDesktop);
                Marshal.ReleaseComObject(desktops);

                if (objDesktop is IVirtualDesktop desktop)
                {
                    IntPtr hWnd = FindWindow("Shell_TrayWnd", "");
                    if (hWnd != IntPtr.Zero)
                    {
                        uint desktopThreadId = GetWindowThreadProcessId(hWnd, out _);
                        uint foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                        uint currentThreadId = GetCurrentThreadId();

                        if (desktopThreadId != 0 && foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                        {
                            AttachThreadInput(desktopThreadId, currentThreadId, true);
                            AttachThreadInput(foregroundThreadId, currentThreadId, true);
                            SetForegroundWindow(hWnd);
                            AttachThreadInput(foregroundThreadId, currentThreadId, false);
                            AttachThreadInput(desktopThreadId, currentThreadId, false);
                        }
                    }

                    _managerInternal.WaitForAnimationToComplete();
                    _managerInternal.SwitchDesktopWithAnimation(desktop);
                    _managerInternal.WaitForAnimationToComplete();

                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_MINIMIZE);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VirtualDesktopLoop] COM switch failed: {ex.Message}");
            }
        }

        // フォールバック: キーボードシミュレーションによる端から端への一括ジャンプ
        FallbackKeySwitch(index);
    }

    private static void FallbackKeySwitch(int targetIndex)
    {
        int count = GetDesktopCount();
        if (count <= 1) return;

        // targetIndex が 0 の場合: 右端から左端へ移動するため Win+Ctrl+Left を (count - 1) 回送信
        // targetIndex が count-1 の場合: 左端から右端へ移動するため Win+Ctrl+Right を (count - 1) 回送信
        byte directionKey = (byte)(targetIndex == 0 ? VK_LEFT : VK_RIGHT);
        int steps = count - 1;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);

            for (int i = 0; i < steps; i++)
            {
                keybd_event(directionKey, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                keybd_event(directionKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(50);
            }

            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        });
    }

    private static int GetDesktopCountFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (key != null)
            {
                if (key.GetValue("VirtualDesktopIDs") is byte[] bytes && bytes.Length >= 16)
                {
                    return bytes.Length / 16;
                }

                using var subKey = key.OpenSubKey("Desktops");
                if (subKey != null)
                {
                    return subKey.SubKeyCount;
                }
            }
        }
        catch { }

        return 1;
    }

    private static int GetCurrentDesktopIndexFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (key != null && key.GetValue("VirtualDesktopIDs") is byte[] allBytes && allBytes.Length >= 16)
            {
                byte[]? currentBytes = key.GetValue("CurrentVirtualDesktop") as byte[];
                if (currentBytes == null || currentBytes.Length < 16)
                {
                    // SessionInfo から取得
                    int sessionId = Process.GetCurrentProcess().SessionId;
                    using var sessionKey = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{sessionId}\VirtualDesktops");
                    currentBytes = sessionKey?.GetValue("CurrentVirtualDesktop") as byte[];
                }

                if (currentBytes != null && currentBytes.Length >= 16)
                {
                    var currentGuid = new Guid(currentBytes);
                    int count = allBytes.Length / 16;
                    for (int i = 0; i < count; i++)
                    {
                        var span = new byte[16];
                        Array.Copy(allBytes, i * 16, span, 0, 16);
                        if (new Guid(span) == currentGuid)
                        {
                            return i;
                        }
                    }
                }
            }
        }
        catch { }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
