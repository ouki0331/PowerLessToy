using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PowerLessToy.AccessHistory.App;

public class AccessTracker
{
    private readonly DatabaseManager _db;
    private CancellationTokenSource? _cts;
    private FileSystemWatcher? _recentWatcher;

    // 処理済みのウィンドウハンドルとパスを記録し、重複記録を避ける
    private readonly Dictionary<long, string> _lastWindowPaths = new();
    private long _lastHeartbeatTick;

    public AccessTracker(DatabaseManager db)
    {
        _db = db;
    }

    public void Start()
    {
        if (_cts != null)
        {
            AccessHistoryLogger.LogWarn("AccessTracker.Start called, but tracker is already running.");
            return;
        }

        AccessHistoryLogger.LogInfo("Starting AccessTracker (Explorer polling & Recent files watcher)...");
        _cts = new CancellationTokenSource();

        StartExplorerPolling(_cts.Token);
        StartRecentFilesWatcher();
    }

    public void Stop()
    {
        AccessHistoryLogger.LogInfo("Stopping AccessTracker...");
        _cts?.Cancel();
        _cts = null;

        try
        {
            if (_recentWatcher != null)
            {
                _recentWatcher.EnableRaisingEvents = false;
                _recentWatcher.Dispose();
                _recentWatcher = null;
                AccessHistoryLogger.LogInfo("Recent files FileSystemWatcher disposed.");
            }
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Error while disposing Recent files watcher", ex);
        }

        AccessHistoryLogger.LogInfo("AccessTracker stopped.");
    }

    public SourceType DetermineSourceType(string path)
    {
        if (string.IsNullOrEmpty(path)) return SourceType.Local;

        if (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
        {
            return SourceType.WSL;
        }
        if (path.StartsWith(@"\\"))
        {
            return SourceType.SMB;
        }
        if (path.Length >= 2 && path[1] == ':')
        {
            try
            {
                var drive = new DriveInfo(path.Substring(0, 2));
                if (drive.DriveType == DriveType.Network)
                {
                    return SourceType.SMB;
                }
            }
            catch (Exception ex)
            {
                AccessHistoryLogger.LogDebug($"DriveInfo check failed for path '{path}': {ex.Message}");
            }
        }
        return SourceType.Local;
    }

    private void StartExplorerPolling(CancellationToken token)
    {
        var thread = new Thread(() =>
        {
            AccessHistoryLogger.LogInfo($"Explorer polling thread started. TID={Environment.CurrentManagedThreadId}, ApartmentState={Thread.CurrentThread.GetApartmentState()}");
            _lastHeartbeatTick = Environment.TickCount64;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellAppType == null)
                    {
                        AccessHistoryLogger.LogError("Type.GetTypeFromProgID('Shell.Application') returned null. Shell COM is unavailable.");
                    }
                    else
                    {
                        object? shellObj = null;
                        try
                        {
                            shellObj = Activator.CreateInstance(shellAppType);
                        }
                        catch (Exception ex)
                        {
                            AccessHistoryLogger.LogError("Failed to create 'Shell.Application' COM instance via Activator.CreateInstance", ex);
                        }

                        if (shellObj != null)
                        {
                            dynamic shell = shellObj;
                            dynamic? windows = null;
                            try
                            {
                                windows = shell.Windows();
                            }
                            catch (Exception ex)
                            {
                                AccessHistoryLogger.LogError("Failed to call shell.Windows() on Shell.Application", ex);
                            }

                            if (windows != null)
                            {
                                int count = 0;
                                try
                                {
                                    count = windows.Count;
                                }
                                catch (Exception ex)
                                {
                                    AccessHistoryLogger.LogError("Failed to read windows.Count from Shell.Windows()", ex);
                                }

                                var currentWindowHwnds = new HashSet<long>();

                                for (int i = 0; i < count; i++)
                                {
                                    try
                                    {
                                        dynamic? window = null;
                                        try
                                        {
                                            window = windows.Item(i);
                                        }
                                        catch (Exception ex)
                                        {
                                            AccessHistoryLogger.LogDebug($"Failed to get window item at index {i}: {ex.Message}");
                                            continue;
                                        }

                                        if (window == null) continue;

                                        long hwnd = 0;
                                        try { hwnd = Convert.ToInt64(window.HWND); } catch { hwnd = i; }
                                        currentWindowHwnds.Add(hwnd);

                                        string name = string.Empty;
                                        try { name = Convert.ToString(window.Name) ?? string.Empty; } catch { }

                                        // ファイルエクスプローラーのみ処理 (Internet Explorer 等は除外)
                                        if (name.Contains("Internet Explorer", StringComparison.OrdinalIgnoreCase)) continue;

                                        dynamic? document = null;
                                        try
                                        {
                                            document = window.Document;
                                        }
                                        catch (Exception ex)
                                        {
                                            AccessHistoryLogger.LogDebug($"Could not read Document for Explorer window HWND={hwnd} ('{name}'): {ex.Message}");
                                            continue;
                                        }

                                        if (document == null) continue;

                                        dynamic? folder = null;
                                        try
                                        {
                                            folder = document.Folder;
                                        }
                                        catch (Exception ex)
                                        {
                                            AccessHistoryLogger.LogDebug($"Could not read Folder from Document for window HWND={hwnd} ('{name}'): {ex.Message}");
                                            continue;
                                        }

                                        if (folder == null) continue;

                                        dynamic? self = null;
                                        try
                                        {
                                            self = folder.Self;
                                        }
                                        catch (Exception ex)
                                        {
                                            AccessHistoryLogger.LogDebug($"Could not read Self from Folder for window HWND={hwnd} ('{name}'): {ex.Message}");
                                            continue;
                                        }

                                        if (self == null) continue;

                                        string? path = null;
                                        try
                                        {
                                            path = Convert.ToString(self.Path);
                                        }
                                        catch (Exception ex)
                                        {
                                            AccessHistoryLogger.LogWarn($"Failed to read Path from folder.Self for window HWND={hwnd} ('{name}')", ex);
                                            continue;
                                        }

                                        if (!string.IsNullOrEmpty(path) && (path.Contains('\\') || path.Contains('/')))
                                        {
                                            if (!_lastWindowPaths.TryGetValue(hwnd, out string? lastPath) || lastPath != path)
                                            {
                                                _lastWindowPaths[hwnd] = path;
                                                string folderName = Path.GetFileName(path);
                                                if (string.IsNullOrEmpty(folderName)) folderName = path;

                                                SourceType srcType = DetermineSourceType(path);
                                                AccessHistoryLogger.LogInfo($"[Explorer Navigation Detected] HWND={hwnd}, Name='{name}', Path='{path}', FolderName='{folderName}', Classified={srcType}");

                                                bool success = _db.RecordAccess(path, folderName, srcType);
                                                if (!success)
                                                {
                                                    AccessHistoryLogger.LogWarn($"RecordAccess returned false for HWND={hwnd}, Path='{path}'");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        AccessHistoryLogger.LogWarn($"Unexpected error processing window item index {i}", ex);
                                    }
                                }

                                // 閉じたウィンドウのクリーンアップ
                                var keysToRemove = new List<long>();
                                foreach (var key in _lastWindowPaths.Keys)
                                {
                                    if (!currentWindowHwnds.Contains(key))
                                    {
                                        keysToRemove.Add(key);
                                    }
                                }
                                foreach (var key in keysToRemove)
                                {
                                    string closedPath = _lastWindowPaths[key];
                                    _lastWindowPaths.Remove(key);
                                    AccessHistoryLogger.LogDebug($"Explorer window HWND={key} closed (was '{closedPath}'). Untracked.");
                                }

                                // 定期ハートビートサマリー (30秒毎)
                                long nowTick = Environment.TickCount64;
                                if (nowTick - _lastHeartbeatTick >= 30000)
                                {
                                    _lastHeartbeatTick = nowTick;
                                    AccessHistoryLogger.LogInfo($"[ExplorerPollSummary] Polling healthy. Active Explorer windows: {count}, Tracked unique windows: {_lastWindowPaths.Count}.");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AccessHistoryLogger.LogError("Unexpected top-level exception in Explorer polling loop", ex);
                }

                Thread.Sleep(2000); // Poll every 2 seconds
            }

            AccessHistoryLogger.LogInfo("Explorer polling thread loop terminated.");
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void StartRecentFilesWatcher()
    {
        string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
        AccessHistoryLogger.LogInfo($"Initializing Recent files FileSystemWatcher on '{recentPath}'...");

        if (!Directory.Exists(recentPath))
        {
            AccessHistoryLogger.LogWarn($"Recent directory does not exist: '{recentPath}'. FileSystemWatcher will not start.");
            return;
        }

        try
        {
            _recentWatcher = new FileSystemWatcher(recentPath, "*.lnk")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = true
            };

            _recentWatcher.Created += OnRecentFileChanged;
            _recentWatcher.Changed += OnRecentFileChanged;
            _recentWatcher.Error += (s, e) =>
            {
                AccessHistoryLogger.LogError("FileSystemWatcher on Recent folder encountered an internal buffer error", e.GetException());
            };

            _recentWatcher.EnableRaisingEvents = true;
            AccessHistoryLogger.LogInfo($"Recent files FileSystemWatcher successfully started on '{recentPath}'.");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to start FileSystemWatcher on Recent folder: '{recentPath}'", ex);
        }
    }

    private void OnRecentFileChanged(object sender, FileSystemEventArgs e)
    {
        AccessHistoryLogger.LogDebug($"[Recent .lnk Event] ChangeType={e.ChangeType}, Path='{e.FullPath}'");

        // Windows の .lnk 書き込み完了を待つため少しディレイを置く
        Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                if (!File.Exists(e.FullPath))
                {
                    AccessHistoryLogger.LogDebug($"Recent .lnk file disappeared or inaccessible before parsing: '{e.FullPath}'");
                    return;
                }

                Type? wscriptType = Type.GetTypeFromProgID("WScript.Shell");
                if (wscriptType == null)
                {
                    AccessHistoryLogger.LogError("Type.GetTypeFromProgID('WScript.Shell') returned null. Shortcut parsing unavailable.");
                    return;
                }

                object? wscriptObj = null;
                try
                {
                    wscriptObj = Activator.CreateInstance(wscriptType);
                }
                catch (Exception ex)
                {
                    AccessHistoryLogger.LogError("Failed to create 'WScript.Shell' COM instance via Activator.CreateInstance", ex);
                    return;
                }

                if (wscriptObj == null)
                {
                    AccessHistoryLogger.LogError("'WScript.Shell' instance is null.");
                    return;
                }

                dynamic wscript = wscriptObj;
                dynamic? shortcut = null;
                try
                {
                    shortcut = wscript.CreateShortcut(e.FullPath);
                }
                catch (Exception ex)
                {
                    AccessHistoryLogger.LogWarn($"Failed to execute wscript.CreateShortcut for '{e.FullPath}'", ex);
                    return;
                }

                if (shortcut == null)
                {
                    AccessHistoryLogger.LogWarn($"wscript.CreateShortcut returned null for '{e.FullPath}'");
                    return;
                }

                string? targetPath = null;
                try
                {
                    targetPath = Convert.ToString(shortcut.TargetPath);
                }
                catch (Exception ex)
                {
                    AccessHistoryLogger.LogWarn($"Failed to read TargetPath from shortcut '{e.FullPath}'", ex);
                    return;
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    AccessHistoryLogger.LogDebug($"Shortcut '{e.FullPath}' has empty TargetPath.");
                    return;
                }

                bool fileExists = File.Exists(targetPath);
                bool dirExists = Directory.Exists(targetPath);

                if (fileExists || dirExists)
                {
                    string fileName = Path.GetFileName(targetPath);
                    if (string.IsNullOrEmpty(fileName)) fileName = targetPath;

                    SourceType srcType = DetermineSourceType(targetPath);
                    AccessHistoryLogger.LogInfo($"[Recent Shortcut Resolved] Lnk='{e.FullPath}', TargetPath='{targetPath}', FileName='{fileName}', Classified={srcType}");

                    bool success = _db.RecordAccess(targetPath, fileName, srcType);
                    if (!success)
                    {
                        AccessHistoryLogger.LogWarn($"RecordAccess returned false for recent target '{targetPath}'");
                    }
                }
                else
                {
                    AccessHistoryLogger.LogWarn($"Recent shortcut target does not exist or inaccessible on disk: '{targetPath}' (from '{e.FullPath}')");
                }
            }
            catch (Exception ex)
            {
                AccessHistoryLogger.LogError($"Unexpected exception handling Recent shortcut '{e.FullPath}'", ex);
            }
        });
    }
}
