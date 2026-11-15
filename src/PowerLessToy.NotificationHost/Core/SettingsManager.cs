using System;
using System.IO;
using System.Text.Json;

namespace PowerLessToy.NotifyPosition.Core;

public static class SettingsManager
{
    private static readonly object _syncLock = new();
    private static NotifyPositionSettings _current = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static event EventHandler<NotifyPositionSettings>? SettingsChanged;

    public static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PowerLessToy",
        "NotifyPosition",
        "settings.json"
    );

    public static NotifyPositionSettings Current
    {
        get
        {
            lock (_syncLock)
            {
                return _current.Clone();
            }
        }
    }

    static SettingsManager()
    {
        Load();
    }

    public static NotifyPositionSettings Load()
    {
        lock (_syncLock)
        {
            string path = SettingsFilePath;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<NotifyPositionSettings>(json, _jsonOptions);
                    if (loaded != null)
                    {
                        ValidateSettings(loaded);
                        _current = loaded;
                        NotifyPositionLogger.Info($"Loaded settings from '{path}'. Mode={_current.NotificationMode}, Pos={_current.Position}, Acrylic={_current.UseAcrylic}", nameof(SettingsManager));
                        return _current.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error($"Failed to load settings file '{path}', falling back to defaults", ex, nameof(SettingsManager));
            }

            _current = new NotifyPositionSettings();
            Save(_current);
            return _current.Clone();
        }
    }

    public static void Save(NotifyPositionSettings newSettings)
    {
        lock (_syncLock)
        {
            ValidateSettings(newSettings);
            _current = newSettings.Clone();

            string path = SettingsFilePath;
            try
            {
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(_current, _jsonOptions);
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);

                NotifyPositionLogger.Info($"Saved settings to '{path}'", nameof(SettingsManager));
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error($"Failed to save settings file to '{path}'", ex, nameof(SettingsManager));
            }
        }

        try
        {
            SettingsChanged?.Invoke(null, _current.Clone());
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error("Error notifying SettingsChanged subscribers", ex, nameof(SettingsManager));
        }
    }

    public static void Update(Action<NotifyPositionSettings> updateAction)
    {
        lock (_syncLock)
        {
            var clone = _current.Clone();
            updateAction(clone);
            Save(clone);
        }
    }

    private static void ValidateSettings(NotifyPositionSettings s)
    {
        if (s.MaxVisible < 1) s.MaxVisible = 1;
        if (s.MaxVisible > 10) s.MaxVisible = 10;
        if (s.AutoDismissSeconds < 2) s.AutoDismissSeconds = 2;
        if (s.AutoDismissSeconds > 60) s.AutoDismissSeconds = 60;
        if (s.StackSpacing < 0) s.StackSpacing = 0;
        if (s.StackSpacing > 50) s.StackSpacing = 50;
        if (string.IsNullOrWhiteSpace(s.MonitorSelection)) s.MonitorSelection = "ActiveWindow";
    }
}
