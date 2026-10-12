using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

public partial class CustomToastWindow : Window
{
    private readonly ParsedNotification _notification;
    private readonly DispatcherTimer _dismissTimer;
    private const int AutoDismissSeconds = 8;

    public CustomToastWindow(ParsedNotification notification, PositionPreset preset)
    {
        InitializeComponent();
        _notification = notification;

        TxtAppName.Text = string.IsNullOrEmpty(notification.AppName) ? "通知" : notification.AppName;
        TxtTitle.Text = notification.Title;
        TxtBody.Text = notification.Body;
        TxtTime.Text = DateTime.Now.ToString("HH:mm");

        BuildButtons();
        CalculatePosition(preset);

        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoDismissSeconds)
        };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            CloseWithFade();
        };
        _dismissTimer.Start();

        MouseEnter += (s, e) => _dismissTimer.Stop();
        MouseLeave += (s, e) => _dismissTimer.Start();
    }

    private void BuildButtons()
    {
        if (_notification.Buttons == null || _notification.Buttons.Count == 0)
        {
            ButtonContainer.Visibility = Visibility.Collapsed;
            return;
        }

        ButtonContainer.Visibility = Visibility.Visible;
        foreach (var btnInfo in _notification.Buttons)
        {
            var btn = new Button
            {
                Content = btnInfo.Name,
                Margin = new Thickness(6, 0, 0, 0)
            };

            // Style: Primary for positive (同意, 許可, はい, Approve, Accept, OK)
            string lowerName = btnInfo.Name.ToLower();
            if (lowerName.Contains("同意") || lowerName.Contains("許可") || lowerName.Contains("承認") ||
                lowerName.Contains("はい") || lowerName.Contains("approve") || lowerName.Contains("accept") || lowerName.Contains("ok"))
            {
                btn.Style = (Style)FindResource("ToastPrimaryButton");
            }
            else
            {
                btn.Style = (Style)FindResource("ToastActionButton");
            }

            var currentBtnInfo = btnInfo;
            btn.Click += (s, e) =>
            {
                _dismissTimer.Stop();
                NotificationReader.InvokeButton(currentBtnInfo.Element);
                CloseWithFade();
            };

            ButtonContainer.Children.Add(btn);
        }
    }

    private void CalculatePosition(PositionPreset preset)
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double margin = 20;

        // Default layout sizes
        double winWidth = 410;
        double winHeight = 160;

        switch (preset)
        {
            case PositionPreset.TopLeft:
                Left = margin;
                Top = margin;
                break;
            case PositionPreset.TopCenter:
                Left = (screenWidth - winWidth) / 2;
                Top = margin;
                break;
            case PositionPreset.TopRight:
                Left = screenWidth - winWidth - margin;
                Top = margin;
                break;
            case PositionPreset.BottomLeft:
                Left = margin;
                Top = screenHeight - winHeight - margin - 50;
                break;
            case PositionPreset.BottomCenter:
                Left = (screenWidth - winWidth) / 2;
                Top = screenHeight - winHeight - margin - 50;
                break;
            case PositionPreset.BottomRight:
            default:
                Left = screenWidth - winWidth - margin;
                Top = screenHeight - winHeight - margin - 50;
                break;
        }
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ignore if clicked on buttons
        if (e.OriginalSource is DependencyObject dep)
        {
            var parentBtn = FindParent<Button>(dep);
            if (parentBtn != null) return;
        }

        _dismissTimer.Stop();
        NotificationReader.InvokeDefault(_notification.RootElement);
        CloseWithFade();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _dismissTimer.Stop();
        CloseWithFade();
    }

    private void CloseWithFade()
    {
        try
        {
            Close();
        }
        catch { }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = child;
        while (parent != null)
        {
            if (parent is T typedParent) return typedParent;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
