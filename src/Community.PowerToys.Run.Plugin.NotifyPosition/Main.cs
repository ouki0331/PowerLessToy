using ManagedCommon;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

/// <summary>
/// Main class of this plugin that implement all used interfaces.
/// </summary>
public class Main : IPlugin, IDisposable
{
    /// <summary>
    /// ID of the plugin.
    /// </summary>
    public static string PluginID => "C78713C8DC3F4DD09967B9141F3E04EB";

    /// <summary>
    /// Name of the plugin.
    /// </summary>
    public string Name => "NotifyPosition";

    /// <summary>
    /// Description of the plugin.
    /// </summary>
    public string Description => "Moves Windows notifications to a specific corner of the screen.";

    private PluginInitContext Context { get; set; }
    private string IconPath { get; set; }
    private bool Disposed { get; set; }

    private NotificationMover _mover;

    public void Init(PluginInitContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(Context.API.GetCurrentTheme());

        _mover = new NotificationMover();
        _mover.Start();
    }

    public List<Result> Query(Query query)
    {
        var search = query.Search.ToLowerInvariant().Trim();
        var results = new List<Result>();

        if (string.IsNullOrEmpty(search))
        {
            results.Add(new Result
            {
                Title = "NotifyPosition Changer",
                SubTitle = $"Current state: {(_mover.IsMonitoring ? "Running" : "Stopped")}, Position: {_mover.Preset}",
                IcoPath = IconPath,
                Action = _ => true
            });
            return results;
        }

        if (search.StartsWith("start"))
        {
            results.Add(new Result
            {
                Title = "Start notification mover",
                SubTitle = "Starts the background task to move notifications",
                IcoPath = IconPath,
                Action = _ => { _mover.Start(); return true; }
            });
        }
        else if (search.StartsWith("stop"))
        {
            results.Add(new Result
            {
                Title = "Stop notification mover",
                SubTitle = "Stops the background task",
                IcoPath = IconPath,
                Action = _ => { _mover.Stop(); return true; }
            });
        }

        var positions = new[] { "topleft", "topcenter", "topright", "bottomleft", "bottomcenter", "bottomright" };
        foreach (var pos in positions)
        {
            if (pos.Contains(search) || search.Contains(pos))
            {
                PositionPreset preset = (PositionPreset)Enum.Parse(typeof(PositionPreset), pos, true);
                results.Add(new Result
                {
                    Title = $"Set position to {pos}",
                    SubTitle = "Changes where notifications will appear",
                    IcoPath = IconPath,
                    Action = _ => { _mover.Preset = preset; return true; }
                });
            }
        }

        return results;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed || !disposing) return;
        
        _mover?.Stop();

        if (Context?.API != null)
        {
            Context.API.ThemeChanged -= OnThemeChanged;
        }

        Disposed = true;
    }

    private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? "Images/notifyposition.light.png" : "Images/notifyposition.dark.png";

    private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);
}
