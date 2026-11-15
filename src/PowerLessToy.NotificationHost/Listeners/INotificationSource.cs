using System;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.Listeners;

public interface INotificationSource : IDisposable
{
    event Action<NotificationModel>? NotificationReceived;
    string SourceName { get; }
    bool IsRunning { get; }
    string StatusDetails { get; }
    void Start();
    void Stop();
}
