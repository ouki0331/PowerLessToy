using System;
using System.Text.Json.Serialization;

namespace PowerLessToy.NotifyPosition.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationMode
{
    /// <summary>
    /// Intercept and remove the native notification banner while displaying custom acrylic notification.
    /// (Note: Native banner may briefly flash on screen before OS dismisses it).
    /// </summary>
    Replace,

    /// <summary>
    /// Keep native toast banner and show custom notification concurrently (for testing and comparison).
    /// </summary>
    Mirror,

    /// <summary>
    /// Recommend user to enable Windows "Do Not Disturb" (Focus Assist).
    /// Windows suppresses native toast banners, and NotificationHost displays custom notifications cleanly.
    /// </summary>
    DoNotDisturb
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PositionPreset
{
    TopCenter,
    TopLeft,
    TopRight,
    BottomCenter,
    BottomLeft,
    BottomRight
}

public class NotifyPositionSettings
{
    public bool Enabled { get; set; } = true;
    public NotificationMode NotificationMode { get; set; } = NotificationMode.Replace;
    public PositionPreset Position { get; set; } = PositionPreset.TopCenter;
    public int MaxVisible { get; set; } = 5;
    public int AutoDismissSeconds { get; set; } = 8;
    public int StackSpacing { get; set; } = 10;
    public bool UseAcrylic { get; set; } = true;
    public bool AggregationEnabled { get; set; } = false;
    public string MonitorSelection { get; set; } = "ActiveWindow"; // ActiveWindow, Primary, Cursor
    public string LoggingLevel { get; set; } = "Info";

    public NotifyPositionSettings Clone()
    {
        return new NotifyPositionSettings
        {
            Enabled = this.Enabled,
            NotificationMode = this.NotificationMode,
            Position = this.Position,
            MaxVisible = this.MaxVisible,
            AutoDismissSeconds = this.AutoDismissSeconds,
            StackSpacing = this.StackSpacing,
            UseAcrylic = this.UseAcrylic,
            AggregationEnabled = this.AggregationEnabled,
            MonitorSelection = this.MonitorSelection,
            LoggingLevel = this.LoggingLevel
        };
    }
}
