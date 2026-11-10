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
        AccessHistoryLogger.LogEnvironment("PowerLessToy.AccessHistory.App");
        AccessHistoryLogger.LogInfo("WPF Application entry point reached.");

        const string appName = "PowerLessToy.AccessHistory.App.SingleInstance";
        bool createdNew = false;
        try
        {
            _mutex = new Mutex(true, appName, out createdNew);
            AccessHistoryLogger.LogDebug($"Single-instance mutex check: createdNew={createdNew}");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Exception acquiring single-instance mutex", ex);
            createdNew = true; // Fallback to allowing startup
        }

        if (!createdNew)
        {
            AccessHistoryLogger.LogInfo("Another instance of AccessHistory.App is already running. Signaling existing window to display.");
            try
            {
                using var ev = EventWaitHandle.OpenExisting(AppEventName);
                ev.Set();
                AccessHistoryLogger.LogInfo("ShowEvent signaled successfully to existing instance.");
            }
            catch (Exception ex)
            {
                AccessHistoryLogger.LogWarn("Could not signal existing ShowEvent. Trying fallback via BringExistingWindowToFront.", ex);
                BringExistingWindowToFront();
            }
            return;
        }

        try
        {
            AccessHistoryLogger.LogInfo("Initializing DatabaseManager in WPF process...");
            var db = new DatabaseManager();
            AccessHistoryLogger.LogInfo("DatabaseManager successfully created in WPF process.");

            AccessHistoryLogger.LogInfo("Starting AccessTracker in WPF process...");
            var tracker = new AccessTracker(db);
            tracker.Start();
            AccessHistoryLogger.LogInfo("AccessTracker successfully started in WPF process.");

            var app = new Application();
            var window = new MainWindow(db);

            EventWaitHandle? showEvent = null;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppEventName);
                System.Threading.Tasks.Task.Run(() =>
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        AccessHistoryLogger.LogInfo("ShowEvent signal received in background thread. Displaying MainWindow on UI thread.");
                        app.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                window.Show();
                                window.WindowState = WindowState.Normal;
                                window.Activate();
                                window.Focus();
                                window.SearchBox.Focus();
                            }
                            catch (Exception winEx)
                            {
                                AccessHistoryLogger.LogError("Error activating MainWindow from ShowEvent", winEx);
                            }
                        });
                    }
                });
            }
            catch (Exception evEx)
            {
                AccessHistoryLogger.LogError("Failed to initialize ShowEvent EventWaitHandle", evEx);
            }

            AccessHistoryLogger.LogInfo("Invoking Application.Run(window)...");
            app.Run(window);
            AccessHistoryLogger.LogInfo("WPF Application exited cleanly. Stopping tracker...");
            tracker.Stop();
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Fatal unhandled exception in AccessHistory.App Main", ex);
            MessageBox.Show($"AccessHistory encountered an unexpected error:\n\n{ex.Message}\n\nDetailed diagnostic log written to:\n{AccessHistoryLogger.GetCurrentLogFilePath()}",
                "PowerLessToy AccessHistory Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static void BringExistingWindowToFront()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                if (process.Id != currentProcess.Id && process.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(process.MainWindowHandle);
                    AccessHistoryLogger.LogInfo($"Brought existing process PID={process.Id} to foreground.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Exception in BringExistingWindowToFront", ex);
        }
    }
}
