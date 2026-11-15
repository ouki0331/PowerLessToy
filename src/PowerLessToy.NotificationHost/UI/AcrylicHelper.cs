using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.UI;

public static class AcrylicHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    public static void ApplyWindowStylesAndBackdrop(Window window, bool enableAcrylic)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;
        if (hWnd == IntPtr.Zero) return;

        try
        {
            // Set ToolWindow, NoActivate, Topmost
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);

            int darkMode = 1;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            if (enableAcrylic)
            {
                ApplyAcrylicBackdrop(hWnd);
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error($"Failed applying DWM attributes to HWND 0x{hWnd.ToInt64():X8}", ex, nameof(AcrylicHelper));
        }
    }

    private static void ApplyAcrylicBackdrop(IntPtr hWnd)
    {
        // Try Windows 11 22H2+ DWMWA_SYSTEMBACKDROP_TYPE
        try
        {
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (hr == 0)
            {
                NotifyPositionLogger.Debug($"Applied DWMWA_SYSTEMBACKDROP_TYPE (Acrylic) to HWND 0x{hWnd.ToInt64():X8}", nameof(AcrylicHelper));
                return;
            }
        }
        catch { }

        // Fallback: SetWindowCompositionAttribute (AccentPolicy)
        try
        {
            var policy = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)0x991E1E22)
            };

            int size = Marshal.SizeOf(policy);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hWnd, ref data);
                NotifyPositionLogger.Debug($"Applied SetWindowCompositionAttribute acrylic fallback to HWND 0x{hWnd.ToInt64():X8}", nameof(AcrylicHelper));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Warn($"AccentPolicy fallback threw: {ex.Message}", ex, nameof(AcrylicHelper));
        }
    }
}
