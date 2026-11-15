using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PowerLessToy.NotifyPosition.Core;

public static class NotifyPositionLogger
{
    private static readonly object _lock = new();
    private static string? _logDirectory;
    private static string? _currentLogFile;
    private static bool _initialized;

    public static string LogDirectory
    {
        get
        {
            EnsureInitialized();
            return _logDirectory ?? Path.Combine(Path.GetTempPath(), "PowerLessToy", "NotifyPosition", "Logs");
        }
    }

    public static string CurrentLogFile
    {
        get
        {
            EnsureInitialized();
            return _currentLogFile ?? string.Empty;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            string preferredDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerLessToy",
                "NotifyPosition",
                "Logs"
            );

            try
            {
                if (!Directory.Exists(preferredDir))
                {
                    Directory.CreateDirectory(preferredDir);
                }
                _logDirectory = preferredDir;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[NotifyPositionLogger] Failed to create primary log dir '{preferredDir}': {ex.Message}");
                string fallbackDir = Path.Combine(Path.GetTempPath(), "PowerLessToy", "NotifyPosition", "Logs");
                try
                {
                    if (!Directory.Exists(fallbackDir))
                    {
                        Directory.CreateDirectory(fallbackDir);
                    }
                    _logDirectory = fallbackDir;
                }
                catch (Exception fallbackEx)
                {
                    Trace.WriteLine($"[NotifyPositionLogger] Failed to create fallback log dir '{fallbackDir}': {fallbackEx.Message}");
                    _logDirectory = Path.GetTempPath();
                }
            }

            try
            {
                string processName = "UnknownProcess";
                int pid = 0;
                try
                {
                    using var currentProc = Process.GetCurrentProcess();
                    processName = currentProc.ProcessName;
                    pid = currentProc.Id;
                }
                catch { }

                string dateStr = DateTime.Now.ToString("yyyyMMdd");
                string fileName = $"notifyposition-{dateStr}-{processName}-{pid}.log";
                _currentLogFile = Path.Combine(_logDirectory, fileName);
            }
            catch
            {
                _currentLogFile = Path.Combine(_logDirectory, $"notifyposition-{DateTime.Now:yyyyMMdd}.log");
            }

            _initialized = true;
        }
    }

    public static void Info(string message, string caller = "")
    {
        Write("INFO", message, null, caller);
    }

    public static void Debug(string message, string caller = "")
    {
        Write("DEBUG", message, null, caller);
    }

    public static void Warn(string message, Exception? ex = null, string caller = "")
    {
        Write("WARN", message, ex, caller);
    }

    public static void Error(string message, Exception? ex = null, string caller = "")
    {
        Write("ERROR", message, ex, caller);
    }

    private static void Write(string level, string message, Exception? ex, string caller)
    {
        EnsureInitialized();

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string processName = "Process";
        int pid = 0;
        try
        {
            using var proc = Process.GetCurrentProcess();
            processName = proc.ProcessName;
            pid = proc.Id;
        }
        catch { }

        int tid = Environment.CurrentManagedThreadId;
        string callerPrefix = string.IsNullOrEmpty(caller) ? "" : $" [{caller}]";

        var sb = new StringBuilder();
        sb.Append($"{timestamp} [{level}] [{processName}:{pid}] [TID:{tid}]{callerPrefix} {message}");

        if (ex != null)
        {
            sb.AppendLine();
            AppendException(sb, ex, 1);
        }

        string logLine = sb.ToString();

        lock (_lock)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentLogFile))
                {
                    File.AppendAllText(_currentLogFile, logLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception writeEx)
            {
                Trace.WriteLine($"[NotifyPositionLogger] Disk write failed: {writeEx.Message}. Entry was: {logLine}");
            }
        }

        try
        {
            Trace.WriteLine(logLine);
        }
        catch { }
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        string indent = new(' ', depth * 2);
        sb.AppendLine($"{indent}Exception Type: {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message: {ex.Message}");
        sb.AppendLine($"{indent}HResult: 0x{ex.HResult:X8} ({ex.HResult})");
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine($"{indent}StackTrace:");
            foreach (var line in ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine($"{indent}  {line.Trim()}");
            }
        }

        if (ex.InnerException != null && depth < 5)
        {
            sb.AppendLine($"{indent}--- Inner Exception ---");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    public static void LogEnvironment(string contextName)
    {
        try
        {
            string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            string osVersion = RuntimeInformation.OSDescription;
            string framework = RuntimeInformation.FrameworkDescription;
            string processArch = RuntimeInformation.ProcessArchitecture.ToString();
            string osArch = RuntimeInformation.OSArchitecture.ToString();
            bool is64Bit = Environment.Is64BitProcess;
            string procPath = Environment.ProcessPath ?? "Unknown";
            string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerLessToy",
                "NotifyPosition",
                "settings.json"
            );

            Info($"=== {contextName} Diagnostics Initialized ===", nameof(LogEnvironment));
            Info($"AppVersion: {appVersion} | OS: {osVersion} | Runtime: {framework}", nameof(LogEnvironment));
            Info($"ProcessArch: {processArch} | OSArch: {osArch} | Is64Bit: {is64Bit}", nameof(LogEnvironment));
            Info($"ProcessPath: {procPath}", nameof(LogEnvironment));
            Info($"LogDirectory: {LogDirectory} (Active Log: {CurrentLogFile})", nameof(LogEnvironment));
            Info($"SettingsPath: {settingsPath} (Exists: {File.Exists(settingsPath)})", nameof(LogEnvironment));
            Info($"User: {user}", nameof(LogEnvironment));
        }
        catch (Exception ex)
        {
            Error("Failed to complete environment diagnostics inventory", ex, nameof(LogEnvironment));
        }
    }
}
