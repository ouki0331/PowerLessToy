using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.AccessHistory;

public class Main : IPlugin
{
    private PluginInitContext? _context;
    private PowerLessToy.AccessHistory.App.AccessTracker? _tracker;
    private PowerLessToy.AccessHistory.App.DatabaseManager? _db;

    public string Name => "Access History";
    public string Description => "Open the File/Folder Access History Dashboard";

    public void Init(PluginInitContext context)
    {
        _context = context;

        // Automatically start the background tracker when PowerToys starts
        _db = new PowerLessToy.AccessHistory.App.DatabaseManager();
        _tracker = new PowerLessToy.AccessHistory.App.AccessTracker(_db);
        _tracker.Start();
    }

    public List<Result> Query(Query query)
    {
        var results = new List<Result>();
        results.Add(new Result
        {
            Title = "アクセス履歴パネルを開く",
            SubTitle = "Open the Access History UI",
            IcoPath = "Images\\accesshistory.dark.png",
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
}
