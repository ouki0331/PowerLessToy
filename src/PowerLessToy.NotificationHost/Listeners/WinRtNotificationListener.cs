using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.Listeners;

public class WinRtNotificationListener : INotificationSource
{
    public event Action<NotificationModel>? NotificationReceived;
    public string SourceName => "WinRT UserNotificationListener";
    public bool IsRunning { get; private set; }
    public string StatusDetails { get; private set; } = "Not initialized";
    public UserNotificationListenerAccessStatus AccessStatus { get; private set; } = UserNotificationListenerAccessStatus.Unspecified;

    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _processedNotificationIds = new();
    private readonly object _lock = new();

    public static bool IsSupported()
    {
        try
        {
            return ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener");
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Warn($"ApiInformation check for UserNotificationListener threw: {ex.Message}", ex, nameof(WinRtNotificationListener));
            return false;
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        if (!IsSupported())
        {
            StatusDetails = "UserNotificationListener API is not supported on this Windows build";
            NotifyPositionLogger.Warn(StatusDetails, null, nameof(WinRtNotificationListener));
            return;
        }

        try
        {
            _listener = UserNotificationListener.Current;
            AccessStatus = _listener.GetAccessStatus();
            NotifyPositionLogger.Info($"Current UserNotificationListener AccessStatus: {AccessStatus}", nameof(WinRtNotificationListener));

            if (AccessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                // Request access asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        NotifyPositionLogger.Info("Requesting UserNotificationListener access...", nameof(WinRtNotificationListener));
                        var requested = await _listener.RequestAccessAsync();
                        AccessStatus = requested;
                        NotifyPositionLogger.Info($"UserNotificationListener.RequestAccessAsync result: {requested}", nameof(WinRtNotificationListener));

                        if (requested == UserNotificationListenerAccessStatus.Allowed)
                        {
                            AttachListener();
                        }
                        else
                        {
                            StatusDetails = $"Permission {requested}. Please allow notification access in Windows Settings (Privacy & security -> Notifications).";
                            NotifyPositionLogger.Warn(StatusDetails, null, nameof(WinRtNotificationListener));
                        }
                    }
                    catch (Exception reqEx)
                    {
                        StatusDetails = $"RequestAccessAsync failed: {reqEx.Message}";
                        NotifyPositionLogger.Error("Failed to request UserNotificationListener access", reqEx, nameof(WinRtNotificationListener));
                    }
                });
            }
            else
            {
                AttachListener();
            }

            IsRunning = true;
        }
        catch (Exception ex)
        {
            StatusDetails = $"Initialization failed: {ex.Message}";
            NotifyPositionLogger.Error("WinRtNotificationListener start failed", ex, nameof(WinRtNotificationListener));
        }
    }

    private void AttachListener()
    {
        if (_listener == null) return;

        try
        {
            _listener.NotificationChanged -= OnNotificationChanged;
            _listener.NotificationChanged += OnNotificationChanged;
            StatusDetails = "Listening for notifications (Allowed)";
            NotifyPositionLogger.Info("Successfully attached to UserNotificationListener.NotificationChanged event", nameof(WinRtNotificationListener));

            // Populate initial existing IDs so we do not re-alert old notifications
            Task.Run(async () =>
            {
                try
                {
                    var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                    lock (_lock)
                    {
                        foreach (var n in existing)
                        {
                            _processedNotificationIds.Add(n.Id);
                        }
                    }
                    NotifyPositionLogger.Info($"Seeded {existing.Count} existing notification IDs into cache", nameof(WinRtNotificationListener));
                }
                catch (Exception seedEx)
                {
                    NotifyPositionLogger.Warn($"Failed to seed existing notification IDs: {seedEx.Message}", seedEx, nameof(WinRtNotificationListener));
                }
            });
        }
        catch (Exception ex)
        {
            StatusDetails = $"Failed to attach event handler: {ex.Message}";
            NotifyPositionLogger.Error("Error attaching NotificationChanged event", ex, nameof(WinRtNotificationListener));
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        NotifyPositionLogger.Debug($"NotificationChanged event received: ChangeKind={args.ChangeKind}, Id={args.UserNotificationId}", nameof(WinRtNotificationListener));

        if (args.ChangeKind != UserNotificationChangedKind.Added)
        {
            return;
        }

        uint id = args.UserNotificationId;
        lock (_lock)
        {
            if (_processedNotificationIds.Contains(id))
            {
                NotifyPositionLogger.Debug($"Notification ID {id} has already been processed. Skipping.", nameof(WinRtNotificationListener));
                return;
            }
            _processedNotificationIds.Add(id);

            // Keep cache bounded
            if (_processedNotificationIds.Count > 1000)
            {
                _processedNotificationIds.Clear();
                _processedNotificationIds.Add(id);
            }
        }

        Task.Run(async () =>
        {
            try
            {
                var notification = sender.GetNotification(id);
                if (notification == null)
                {
                    NotifyPositionLogger.Warn($"sender.GetNotification({id}) returned null", null, nameof(WinRtNotificationListener));
                    return;
                }

                var model = ParseNotification(notification);
                if (model == null)
                {
                    NotifyPositionLogger.Warn($"Failed to parse notification ID {id}", null, nameof(WinRtNotificationListener));
                    return;
                }

                NotifyPositionLogger.Info($"Processing Notification: App='{model.AppName}', Title='{model.Title}', ID={id}", nameof(WinRtNotificationListener));

                // Dispatch to subscribers
                NotificationReceived?.Invoke(model);

                // Handle Replace Mode if configured
                var currentSettings = SettingsManager.Current;
                if (currentSettings.NotificationMode == NotificationMode.Replace)
                {
                    try
                    {
                        sender.RemoveNotification(id);
                        NotifyPositionLogger.Info($"Replace mode: Removed native toast banner for notification ID {id}", nameof(WinRtNotificationListener));
                    }
                    catch (Exception rmEx)
                    {
                        NotifyPositionLogger.Warn($"Failed to remove native notification ID {id}: {rmEx.Message}", rmEx, nameof(WinRtNotificationListener));
                    }
                }
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error($"Error processing NotificationChanged for ID {id}", ex, nameof(WinRtNotificationListener));
            }
        });
    }

    private NotificationModel? ParseNotification(UserNotification notification)
    {
        try
        {
            string appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "通知";
            string appId = notification.AppInfo?.Id ?? string.Empty;
            string title = string.Empty;
            string body = string.Empty;

            var toastBinding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (toastBinding != null)
            {
                var textElements = toastBinding.GetTextElements();
                if (textElements != null && textElements.Count > 0)
                {
                    title = textElements[0]?.Text ?? string.Empty;
                    if (textElements.Count > 1)
                    {
                        body = string.Join("\n", textElements.Skip(1).Select(t => t?.Text ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
            {
                title = appName;
            }

            var model = new NotificationModel
            {
                UserNotificationId = notification.Id,
                AppName = appName,
                AppId = appId,
                Title = title,
                Body = body,
                Timestamp = notification.CreationTime.DateTime,
                SourceType = "WinRT"
            };

            return model;
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error($"Exception during ParseNotification: {ex.Message}", ex, nameof(WinRtNotificationListener));
            return null;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            if (_listener != null)
            {
                _listener.NotificationChanged -= OnNotificationChanged;
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Warn($"Exception detaching listener on Stop: {ex.Message}", ex, nameof(WinRtNotificationListener));
        }

        IsRunning = false;
        StatusDetails = "Stopped";
        NotifyPositionLogger.Info("WinRtNotificationListener stopped", nameof(WinRtNotificationListener));
    }

    public void Dispose()
    {
        Stop();
    }
}
