using System;
using System.Diagnostics;
using System.Windows;
using System.Threading;

namespace PowerLessToy.AccessHistory.App;

public class Program
{
    private static Mutex? _mutex;
    private const string AppEventName = "PowerLessToy.AccessHistory.ShowEvent";

    [STAThread]
    public static void Main()
    {
        const string appName = "PowerLessToy.AccessHistory.App.SingleInstance";
        _mutex = new Mutex(true, appName, out bool createdNew);

        if (!createdNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(AppEventName);
                ev.Set();
            }
            catch
            {
                BringExistingWindowToFront();
            }
            return;
        }

        // Access tracking is owned by the PowerToys Run plugin. Keeping the UI
        // process read-only avoids counting every Explorer visit twice.
        var db = new DatabaseManager();
        var app = new Application();
        var window = new MainWindow(db);

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppEventName);
        System.Threading.Tasks.Task.Run(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                app.Dispatcher.Invoke(() =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.Focus();
                    window.SearchBox.Focus();
                });
            }
        });

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
