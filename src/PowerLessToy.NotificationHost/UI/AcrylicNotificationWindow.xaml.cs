using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerLessToy.NotifyPosition.Core;

namespace PowerLessToy.NotifyPosition.UI;

public partial class AcrylicNotificationWindow : Window
{
    private readonly NotificationModel _notification;
    private readonly NotifyPositionSettings _settings;
    private readonly DispatcherTimer _dismissTimer;
    private bool _isClosing;

    public event Action<AcrylicNotificationWindow>? Dismissed;

    public NotificationModel Notification => _notification;

    public AcrylicNotificationWindow(NotificationModel notification, NotifyPositionSettings settings)
    {
        InitializeComponent();
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        TxtAppName.Text = string.IsNullOrEmpty(notification.AppName) ? "通知" : notification.AppName;
        TxtTitle.Text = notification.Title;
        TxtBody.Text = notification.Body;
        TxtTime.Text = notification.Timestamp.ToString("HH:mm");

        LoadAppIcon(notification);
        BuildButtons();

        SourceInitialized += (s, e) =>
        {
            AcrylicHelper.ApplyWindowStylesAndBackdrop(this, _settings.UseAcrylic);
        };

        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.AutoDismissSeconds)
        };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            CloseWithAnimation();
        };
        _dismissTimer.Start();

        MouseEnter += (s, e) =>
        {
            if (!_isClosing) _dismissTimer.Stop();
        };
        MouseLeave += (s, e) =>
        {
            if (!_isClosing) _dismissTimer.Start();
        };
    }

    private void LoadAppIcon(NotificationModel notification)
    {
        try
        {
            if (notification.AppLogoBytes != null && notification.AppLogoBytes.Length > 0)
            {
                using var ms = new MemoryStream(notification.AppLogoBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                ImgAppIcon.Source = bmp;
                ImgAppIcon.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(notification.AppLogoPath) && File.Exists(notification.AppLogoPath))
            {
                var bmp = new BitmapImage(new Uri(notification.AppLogoPath, UriKind.Absolute));
                ImgAppIcon.Source = bmp;
                ImgAppIcon.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Debug($"Failed to load notification app logo: {ex.Message}", nameof(AcrylicNotificationWindow));
        }
    }

    private void BuildButtons()
    {
        if (_notification.Buttons == null || _notification.Buttons.Count == 0)
        {
            ButtonContainer.Visibility = Visibility.Collapsed;
            return;
        }

        ButtonContainer.Visibility = Visibility.Visible;
        ButtonContainer.Children.Clear();

        foreach (var btnInfo in _notification.Buttons)
        {
            var btn = new Button
            {
                Content = btnInfo.Text,
                Margin = new Thickness(6, 0, 0, 0)
            };

            string lower = btnInfo.Text.ToLower();
            if (btnInfo.IsPrimary ||
                lower.Contains("同意") || lower.Contains("許可") || lower.Contains("承認") ||
                lower.Contains("はい") || lower.Contains("approve") || lower.Contains("accept") || lower.Contains("ok"))
            {
                btn.Style = (Style)FindResource("ToastPrimaryButton");
            }
            else
            {
                btn.Style = (Style)FindResource("ToastActionButton");
            }

            var callback = btnInfo.ActionCallback;
            btn.Click += (s, e) =>
            {
                _dismissTimer.Stop();
                try
                {
                    callback?.Invoke();
                }
                catch (Exception cbEx)
                {
                    NotifyPositionLogger.Error($"Error invoking action button '{btnInfo.Text}'", cbEx, nameof(AcrylicNotificationWindow));
                }
                CloseWithAnimation();
            };

            ButtonContainer.Children.Add(btn);
        }
    }

    public void AnimateEnter()
    {
        Opacity = 0;
        WindowTransform.Y = -25;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slideDown = new DoubleAnimation(-25, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeIn);
        WindowTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
    }

    public void AnimateToPosition(double targetLeft, double targetTop, double durationMs = 250)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(durationMs);

        var animLeft = new DoubleAnimation(Left, targetLeft, time) { EasingFunction = ease };
        var animTop = new DoubleAnimation(Top, targetTop, time) { EasingFunction = ease };

        BeginAnimation(LeftProperty, animLeft);
        BeginAnimation(TopProperty, animTop);
    }

    public void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;
        _dismissTimer.Stop();

        try
        {
            _notification.OnDismiss?.Invoke();
        }
        catch { }

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var slideUp = new DoubleAnimation(WindowTransform.Y, -20, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            try
            {
                Dismissed?.Invoke(this);
                Close();
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Debug($"Window close error: {ex.Message}", nameof(AcrylicNotificationWindow));
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
        WindowTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't trigger card click if button was clicked
        if (e.OriginalSource is DependencyObject dep)
        {
            var btn = FindParent<Button>(dep);
            if (btn != null) return;
        }

        _dismissTimer.Stop();
        try
        {
            _notification.OnClick?.Invoke();
        }
        catch (Exception clickEx)
        {
            NotifyPositionLogger.Error("Error executing notification card click callback", clickEx, nameof(AcrylicNotificationWindow));
        }
        CloseWithAnimation();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = child;
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
