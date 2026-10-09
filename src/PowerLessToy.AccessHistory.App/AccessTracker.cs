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

    // 记录已经处理过的窗口句柄和路径，避免重复记录
    private readonly Dictionary<long, string> _lastWindowPaths = new();

    public AccessTracker(DatabaseManager db)
    {
        _db = db;
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();

        StartExplorerPolling(_cts.Token);
        StartRecentFilesWatcher();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        
        _recentWatcher?.Dispose();
        _recentWatcher = null;
    }

    private SourceType DetermineSourceType(string path)
    {
        if (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
        {
            return SourceType.WSL;
        }
        if (path.StartsWith(@"\\"))
        {
            return SourceType.SMB;
        }
        return SourceType.Local;
    }

    private void StartExplorerPolling(CancellationToken token)
    {
        Task.Run(async () =>
        {
            Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
            if (shellAppType == null) return;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    dynamic shell = Activator.CreateInstance(shellAppType);
                    dynamic windows = shell.Windows();

                    var currentWindowHwnds = new HashSet<long>();

                    for (int i = 0; i < windows.Count; i++)
                    {
                        try
                        {
                            var window = windows.Item(i);
                            if (window == null) continue;

                            long hwnd = window.HWND;
                            currentWindowHwnds.Add(hwnd);

                            // 只处理文件浏览器
                            string name = window.Name;
                            if (name.Contains("Internet Explorer")) continue;

                            var document = window.Document;
                            if (document != null)
                            {
                                var folder = document.Folder;
                                if (folder != null)
                                {
                                    var self = folder.Self;
                                    if (self != null)
                                    {
                                        string path = self.Path;
                                        if (!string.IsNullOrEmpty(path) && path.Contains("\\"))
                                        {
                                            if (!_lastWindowPaths.TryGetValue(hwnd, out string? lastPath) || lastPath != path)
                                            {
                                                _lastWindowPaths[hwnd] = path;
                                                string folderName = Path.GetFileName(path);
                                                if (string.IsNullOrEmpty(folderName)) folderName = path;

                                                _db.RecordAccess(path, folderName, DetermineSourceType(path));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { } // Ignore COM errors for individual windows
                    }

                    // 清理已经关闭的窗口
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
                        _lastWindowPaths.Remove(key);
                    }
                }
                catch { }

                await Task.Delay(2000, token); // Poll every 2 seconds
            }
        }, token);
    }

    private void StartRecentFilesWatcher()
    {
        string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
        if (!Directory.Exists(recentPath)) return;

        _recentWatcher = new FileSystemWatcher(recentPath, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = true
        };

        _recentWatcher.Created += OnRecentFileChanged;
        _recentWatcher.Changed += OnRecentFileChanged;
        _recentWatcher.EnableRaisingEvents = true;
    }

    private void OnRecentFileChanged(object sender, FileSystemEventArgs e)
    {
        // Add a small delay to avoid file lock issues when Windows is writing the .lnk
        Task.Run(async () =>
        {
            await Task.Delay(500); 
            try
            {
                Type? wscriptType = Type.GetTypeFromProgID("WScript.Shell");
                if (wscriptType == null) return;

                dynamic wscript = Activator.CreateInstance(wscriptType);
                dynamic shortcut = wscript.CreateShortcut(e.FullPath);
                string targetPath = shortcut.TargetPath;

                if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    string fileName = Path.GetFileName(targetPath);
                    if (string.IsNullOrEmpty(fileName)) fileName = targetPath;

                    _db.RecordAccess(targetPath, fileName, DetermineSourceType(targetPath));
                }
            }
            catch { }
        });
    }
}
