using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Input;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.AccessHistory;

public class Main : IPlugin, IDisposable
{
    public static string PluginID => "A9E3C5B1D2F44C78B9A0E1D2F3C4B5A6";

    private PluginInitContext? _context;
    private PowerLessToy.AccessHistory.App.AccessTracker? _tracker;
    private PowerLessToy.AccessHistory.App.DatabaseManager? _db;

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

        try
        {
            string? pluginDir = Path.GetDirectoryName(typeof(Main).Assembly.Location);
            if (string.IsNullOrEmpty(pluginDir)) return;

            var alc = AssemblyLoadContext.GetLoadContext(typeof(Main).Assembly);
            if (alc != null)
            {
                alc.Resolving += (context, assemblyName) =>
                {
                    string path = Path.Combine(pluginDir, $"{assemblyName.Name}.dll");
                    if (File.Exists(path))
                    {
                        return context.LoadFromAssemblyPath(path);
                    }
                    return null;
                };

                alc.ResolvingUnmanagedDll += (assembly, unmanagedDllName) =>
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

                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            if (NativeLibrary.TryLoad(candidate, out var handle))
                            {
                                return handle;
                            }
                        }
                    }
                    return IntPtr.Zero;
                };
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new AssemblyName(args.Name).Name;
                    if (string.IsNullOrEmpty(asmName)) return null;

                    string path = Path.Combine(pluginDir, $"{asmName}.dll");
                    if (File.Exists(path))
                    {
                        var targetAlc = alc ?? AssemblyLoadContext.Default;
                        return targetAlc.LoadFromAssemblyPath(path);
                    }
                }
                catch { }
                return null;
            };
        }
        catch { }
    }

    public void Init(PluginInitContext context)
    {
        _context = context;
        RegisterAssemblyLoader();

        try
        {
            // Automatically start the background tracker when PowerToys starts
            _db = new PowerLessToy.AccessHistory.App.DatabaseManager();
            _tracker = new PowerLessToy.AccessHistory.App.AccessTracker(_db);
            _tracker.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccessHistory] Init failed: {ex}");
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
            // The WPF App executable will be packaged alongside this plugin DLL
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string exePath = Path.Combine(pluginDir, "PowerLessToy.AccessHistory.App.exe");

            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _tracker?.Stop();
    }
}
