using System;
using System.Diagnostics;
using System.Windows;
using System.Threading;

namespace PowerLessToy.AccessHistory.App;

public class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main()
    {
        const string appName = "PowerLessToy.AccessHistory.App.SingleInstance";
        _mutex = new Mutex(true, appName, out bool createdNew);

        if (!createdNew)
        {
            // App is already running. For simplicity, just exit. 
            // In a real app we'd use NamedPipes to tell the existing instance to show its window.
            // But since PowerToys will just launch this, we can let the user keep the window open, 
            // or we can use Win32 API to find the window and bring it to front.
            BringExistingWindowToFront();
            return;
        }

        var db = new DatabaseManager();
        var tracker = new AccessTracker(db);
        tracker.Start();

        var app = new Application();
        var window = new MainWindow(db);
        app.Run(window);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static void BringExistingWindowToFront()
    {
        var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            if (process.Id != currentProcess.Id && process.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(process.MainWindowHandle);
                break;
            }
        }
    }
}
