using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Input;
using PowerLessToy.AccessHistory.App;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.AccessHistory;

public class Main : IPlugin, IDisposable
{
    public static string PluginID => "A9E3C5B1D2F44C78B9A0E1D2F3C4B5A6";

    private PluginInitContext? _context;
    private AccessTracker? _tracker;
    private DatabaseManager? _db;

    public string Name => "Access History";
    public string Description => "Open the File/Folder Access History Dashboard";

    static Main()
    {
        RegisterAssemblyLoader();
    }

    [ModuleInitializer]
    internal static void ModuleInit()
    {
        RegisterAssemblyLoader();
    }

    private static bool _loaderRegistered;
    private static void RegisterAssemblyLoader()
    {
        if (_loaderRegistered) return;
        _loaderRegistered = true;

        AccessHistoryLogger.LogInfo("Registering assembly and native DLL loaders for AccessHistory plugin...");

        try
        {
            string? pluginDir = Path.GetDirectoryName(typeof(Main).Assembly.Location);
            AccessHistoryLogger.LogInfo($"AccessHistory Plugin directory: '{pluginDir}'");

            if (string.IsNullOrEmpty(pluginDir))
            {
                AccessHistoryLogger.LogError("Main.Assembly.Location is null or empty. Cannot determine plugin directory.");
                return;
            }

            var alc = AssemblyLoadContext.GetLoadContext(typeof(Main).Assembly);
            if (alc != null)
            {
                AccessHistoryLogger.LogInfo($"AssemblyLoadContext found: '{alc.Name}' (IsDefault={alc == AssemblyLoadContext.Default})");

                alc.Resolving += (context, assemblyName) =>
                {
                    try
                    {
                        string path = Path.Combine(pluginDir, $"{assemblyName.Name}.dll");
                        bool exists = File.Exists(path);
                        AccessHistoryLogger.LogDebug($"[ALC.Resolving] Request='{assemblyName.FullName}', Candidate='{path}', Exists={exists}");

                        if (exists)
                        {
                            var loaded = context.LoadFromAssemblyPath(path);
                            AccessHistoryLogger.LogInfo($"[ALC.Resolving SUCCESS] Loaded '{assemblyName.Name}' from '{path}'");
                            return loaded;
                        }
                    }
                    catch (Exception ex)
                    {
                        AccessHistoryLogger.LogError($"[ALC.Resolving ERROR] Failed to load assembly '{assemblyName.FullName}' from plugin directory", ex);
                    }
                    return null;
                };

                alc.ResolvingUnmanagedDll += (assembly, unmanagedDllName) =>
                {
                    try
                    {
                        string arch = RuntimeInformation.ProcessArchitecture switch
                        {
                            Architecture.Arm64 => "win-arm64",
                            Architecture.X64 => "win-x64",
                            _ => "win-x64"
                        };

                        string[] candidates =
                        [
                            Path.Combine(pluginDir, "runtimes", arch, "native", $"{unmanagedDllName}.dll"),
                            Path.Combine(pluginDir, "runtimes", arch, "native", unmanagedDllName),
                            Path.Combine(pluginDir, $"{unmanagedDllName}.dll"),
                            Path.Combine(pluginDir, unmanagedDllName)
                        ];

                        AccessHistoryLogger.LogDebug($"[ALC.ResolvingUnmanagedDll] Request='{unmanagedDllName}', TargetArch='{arch}', Candidates={candidates.Length}");

                        foreach (var candidate in candidates)
                        {
                            bool exists = File.Exists(candidate);
                            AccessHistoryLogger.LogDebug($"[ALC.ResolvingUnmanagedDll] Testing candidate: '{candidate}', Exists={exists}");

                            if (exists)
                            {
                                if (NativeLibrary.TryLoad(candidate, out var handle))
                                {
                                    AccessHistoryLogger.LogInfo($"[ALC.ResolvingUnmanagedDll SUCCESS] Loaded native library '{unmanagedDllName}' from '{candidate}'");
                                    return handle;
                                }
                                else
                                {
                                    int lastErr = Marshal.GetLastWin32Error();
                                    AccessHistoryLogger.LogWarn($"[ALC.ResolvingUnmanagedDll] NativeLibrary.TryLoad returned zero for '{candidate}'. Win32Error={lastErr}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AccessHistoryLogger.LogError($"[ALC.ResolvingUnmanagedDll ERROR] Exception resolving unmanaged DLL '{unmanagedDllName}'", ex);
                    }
                    return IntPtr.Zero;
                };
            }
            else
            {
                AccessHistoryLogger.LogWarn("AssemblyLoadContext.GetLoadContext returned null for Main assembly.");
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(asmName)) return null;

                    string path = Path.Combine(pluginDir, $"{asmName}.dll");
                    bool exists = File.Exists(path);
                    AccessHistoryLogger.LogDebug($"[AppDomain.AssemblyResolve] Request='{args.Name}', Candidate='{path}', Exists={exists}");

                    if (exists)
                    {
                        var targetAlc = alc ?? AssemblyLoadContext.Default;
                        var loaded = targetAlc.LoadFromAssemblyPath(path);
                        AccessHistoryLogger.LogInfo($"[AppDomain.AssemblyResolve SUCCESS] Loaded '{asmName}' from '{path}'");
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    AccessHistoryLogger.LogError($"[AppDomain.AssemblyResolve ERROR] Exception resolving assembly '{args.Name}'", ex);
                }
                return null;
            };

            AccessHistoryLogger.LogInfo("Assembly and native DLL loaders successfully registered.");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("RegisterAssemblyLoader encountered an unexpected top-level exception", ex);
        }
    }

    public void Init(PluginInitContext context)
    {
        _context = context;
        RegisterAssemblyLoader();

        string pluginDir = Path.GetDirectoryName(typeof(Main).Assembly.Location) ?? string.Empty;
        AccessHistoryLogger.LogEnvironment("PowerToys.Run.Plugin.AccessHistory", pluginDir);

        try
        {
            AccessHistoryLogger.LogInfo("Initializing AccessHistory DatabaseManager in plugin process...");
            _db = new DatabaseManager();
            AccessHistoryLogger.LogInfo("DatabaseManager initialized successfully in plugin process.");

            AccessHistoryLogger.LogInfo("Starting AccessTracker in plugin process...");
            _tracker = new AccessTracker(_db);
            _tracker.Start();
            AccessHistoryLogger.LogInfo("AccessTracker started successfully in plugin process.");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("AccessHistory Plugin Init failed", ex);
        }
    }

    public List<Result> Query(Query query)
    {
        var results = new List<Result>();
        results.Add(new Result
        {
            Title = "アクセス履歴パネルを開く",
            SubTitle = "Open the Access History UI",
            IcoPath = "Images\\history.dark.png",
            Action = e =>
            {
                LaunchWpfApp();
                return true;
            }
        });
        return results;
    }

    private void LaunchWpfApp()
    {
        try
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string exePath = Path.Combine(pluginDir, "PowerLessToy.AccessHistory.App.exe");

            AccessHistoryLogger.LogInfo($"LaunchWpfApp requested. PluginDir='{pluginDir}', TargetExe='{exePath}', Exists={File.Exists(exePath)}");

            if (!File.Exists(exePath))
            {
                AccessHistoryLogger.LogError($"Cannot launch WPF App because executable does not exist at: '{exePath}'");
                return;
            }

            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = pluginDir
            };

            var proc = Process.Start(startInfo);
            AccessHistoryLogger.LogInfo($"WPF App process launched. PID={proc?.Id.ToString() ?? "(unknown)"}");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Failed to launch AccessHistory WPF App executable", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            AccessHistoryLogger.LogInfo("AccessHistory plugin Dispose called. Stopping tracker...");
            _tracker?.Stop();
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Error in AccessHistory plugin Dispose", ex);
        }
    }
}
