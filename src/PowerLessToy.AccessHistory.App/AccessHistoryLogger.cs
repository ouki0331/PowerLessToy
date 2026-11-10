using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PowerLessToy.AccessHistory.App;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// 独立したゼロ依存ファイルロガー (Zero-dependency file logger).
/// PowerToys 内部プロセスと独立 WPF アプリの双方で競合なく動作し、
/// %LOCALAPPDATA%\PowerLessToy\AccessHistory\Logs\ 配下に
/// accesshistory-{yyyyMMdd}-{ProcessName}-{PID}.log を安全に出力します。
/// </summary>
public static class AccessHistoryLogger
{
    private static readonly object _fileLock = new();
    private static readonly string _processName;
    private static readonly int _processId;
    private static bool _envLogged;

    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PowerLessToy", "AccessHistory", "Logs");

    static AccessHistoryLogger()
    {
        try
        {
            using var cur = Process.GetCurrentProcess();
            _processName = cur.ProcessName;
            _processId = cur.Id;
        }
        catch
        {
            _processName = "UnknownProcess";
            _processId = 0;
        }

        try
        {
            EnsureDirectoryExists();
        }
        catch
        {
            // Directory creation error will be logged on first WriteLog call
        }
    }

    public static string GetCurrentLogFilePath()
    {
        string dateStr = DateTime.Now.ToString("yyyyMMdd");
        string safeProcName = SanitizeFileName(_processName);
        return Path.Combine(LogsDirectory, $"accesshistory-{dateStr}-{safeProcName}-{_processId}.log");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }

    private static void EnsureDirectoryExists()
    {
        string dir = LogsDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static void LogInfo(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        WriteLog(LogLevel.Info, message, null, member, file, line);
    }

    public static void LogDebug(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        WriteLog(LogLevel.Debug, message, null, member, file, line);
    }

    public static void LogWarn(string message, Exception? ex = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        WriteLog(LogLevel.Warn, message, ex, member, file, line);
    }

    public static void LogError(string message, Exception? ex = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        WriteLog(LogLevel.Error, message, ex, member, file, line);
    }

    public static void WriteLog(LogLevel level, string message, Exception? ex,
        string member, string file, int line)
    {
        try
        {
            string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warn => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "INFO "
            };

            int tid = Environment.CurrentManagedThreadId;
            string callerFile = string.IsNullOrEmpty(file) ? "Unknown" : Path.GetFileNameWithoutExtension(file);
            string location = $"{callerFile}.{member}:{line}";

            var sb = new StringBuilder();
            sb.Append($"{timeStr} [{levelStr}] [{_processName}:{_processId}] [TID:{tid}] [{location}] {message}");
            sb.AppendLine();

            if (ex != null)
            {
                sb.AppendLine(FormatException(ex));
            }

            string entry = sb.ToString();

            lock (_fileLock)
            {
                EnsureDirectoryExists();
                string path = GetCurrentLogFilePath();
                File.AppendAllText(path, entry, Encoding.UTF8);
            }

            Debug.Write(entry);
        }
        catch (Exception writeEx)
        {
            try
            {
                Trace.WriteLine($"[AccessHistoryLogger FALLBACK] Write failed: {writeEx.Message}. Original message: {message}");
            }
            catch { }
        }
    }

    public static string FormatException(Exception ex)
    {
        if (ex == null) return string.Empty;

        var sb = new StringBuilder();
        int depth = 0;
        Exception? current = ex;

        while (current != null)
        {
            string prefix = depth == 0 ? "  [Exception Details]" : $"  [InnerException #{depth}]";
            sb.AppendLine(prefix);
            sb.AppendLine($"    Type           : {current.GetType().FullName}");
            sb.AppendLine($"    Message        : {current.Message}");
            sb.AppendLine($"    HResult        : 0x{current.HResult:X8} ({current.HResult})");
            sb.AppendLine($"    Source         : {current.Source ?? "(none)"}");
            if (current.TargetSite != null)
            {
                sb.AppendLine($"    TargetSite     : {current.TargetSite}");
            }

            if (!string.IsNullOrEmpty(current.StackTrace))
            {
                sb.AppendLine("    StackTrace     :");
                var lines = current.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var stLine in lines)
                {
                    sb.AppendLine($"      {stLine.Trim()}");
                }
            }

            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// アプリケーション起動時にシステムおよび実行環境のインベントリ情報を記録します。
    /// </summary>
    public static void LogEnvironment(string componentName, string? pluginDir = null)
    {
        lock (_fileLock)
        {
            if (_envLogged) return;
            _envLogged = true;
        }

        try
        {
            var curProc = Process.GetCurrentProcess();
            string? procPath = null;
            try { procPath = curProc.MainModule?.FileName ?? Environment.ProcessPath; } catch { procPath = Environment.ProcessPath; }

            string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            string osVersion = Environment.OSVersion.VersionString;
            string osDesc = RuntimeInformation.OSDescription;
            string netRuntime = RuntimeInformation.FrameworkDescription;
            string netVersion = Environment.Version.ToString();
            string procArch = RuntimeInformation.ProcessArchitecture.ToString();
            string osArch = RuntimeInformation.OSArchitecture.ToString();
            bool is64BitProc = Environment.Is64BitProcess;
            bool is64BitOs = Environment.Is64BitOperatingSystem;

            string currentUser = $"{Environment.UserDomainName}\\{Environment.UserName}";
            string dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerLessToy", "AccessHistory");
            string dbPath = Path.Combine(dbDir, "history.db");
            bool dbExists = File.Exists(dbPath);

            string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
            bool recentExists = Directory.Exists(recentPath);

            string logPath = GetCurrentLogFilePath();

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($" PowerLessToy AccessHistory - Environment Inventory [{componentName}]");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"  Component Name         : {componentName}");
            sb.AppendLine($"  App Assembly Version   : {appVersion}");
            sb.AppendLine($"  Current Process        : {_processName} (PID: {_processId})");
            sb.AppendLine($"  Process Main Module    : {procPath ?? "(unavailable)"}");
            sb.AppendLine($"  Process Working Dir    : {Environment.CurrentDirectory}");
            sb.AppendLine($"  Plugin Directory       : {pluginDir ?? "(none / standalone app)"}");
            sb.AppendLine($"  Log File Target        : {logPath}");
            sb.AppendLine($"  Log Directory          : {LogsDirectory}");
            sb.AppendLine($"  Current User           : {currentUser}");
            sb.AppendLine($"  OS Version String      : {osVersion}");
            sb.AppendLine($"  OS Description         : {osDesc}");
            sb.AppendLine($"  OS Architecture        : {osArch}");
            sb.AppendLine($"  Process Architecture   : {procArch}");
            sb.AppendLine($"  Is 64-bit Process      : {is64BitProc}");
            sb.AppendLine($"  Is 64-bit OS           : {is64BitOs}");
            sb.AppendLine($"  .NET Version           : {netVersion}");
            sb.AppendLine($"  .NET FrameworkDesc     : {netRuntime}");
            sb.AppendLine($"  CLR Version            : {Environment.Version}");
            sb.AppendLine($"  Database Path          : {dbPath} (Exists: {dbExists})");
            sb.AppendLine($"  Recent Folder Path     : {recentPath} (Exists: {recentExists})");
            sb.AppendLine("================================================================================");

            LogInfo(sb.ToString(), member: "LogEnvironment", file: nameof(AccessHistoryLogger), line: 0);
        }
        catch (Exception ex)
        {
            LogError("Failed to collect or write environment inventory log", ex,
                member: "LogEnvironment", file: nameof(AccessHistoryLogger), line: 0);
        }
    }
}
