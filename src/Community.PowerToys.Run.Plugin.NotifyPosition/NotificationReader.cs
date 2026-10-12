using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

public class NotificationButtonInfo
{
    public string Name { get; set; } = string.Empty;
    public AutomationElement Element { get; set; } = null!;
}

public class ParsedNotification
{
    public string AppName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<NotificationButtonInfo> Buttons { get; set; } = new();
    public AutomationElement? RootElement { get; set; }
}

public static class NotificationReader
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public static ParsedNotification? ReadFromHandle(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return null;

            var result = new ParsedNotification
            {
                RootElement = element
            };

            // 1. Collect all texts
            var texts = new List<string>();
            CollectTexts(element, texts, 0);

            if (texts.Count >= 3)
            {
                result.AppName = texts[0];
                result.Title = texts[1];
                result.Body = string.Join("\n", texts.GetRange(2, texts.Count - 2));
            }
            else if (texts.Count == 2)
            {
                result.AppName = texts[0];
                result.Title = texts[1];
            }
            else if (texts.Count == 1)
            {
                result.Title = texts[0];
            }
            else
            {
                result.Title = GetWindowTitle(hWnd);
            }

            if (string.IsNullOrWhiteSpace(result.AppName))
            {
                result.AppName = "システム通知";
            }

            // 2. Collect Action Buttons
            try
            {
                var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttonElements = element.FindAll(TreeScope.Descendants, buttonCondition);

                foreach (AutomationElement btn in buttonElements)
                {
                    try
                    {
                        string btnName = btn.Current.Name ?? string.Empty;
                        // Filter out empty or generic close buttons (unless it's an explicit action)
                        if (!string.IsNullOrWhiteSpace(btnName) && 
                            !btnName.Equals("閉じる", StringComparison.OrdinalIgnoreCase) &&
                            !btnName.Equals("Close", StringComparison.OrdinalIgnoreCase) &&
                            !btnName.Equals("Dismiss", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Buttons.Add(new NotificationButtonInfo
                            {
                                Name = btnName,
                                Element = btn
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }
        catch
        {
            return null;
        }
    }

    public static void InvokeButton(AutomationElement btnElement)
    {
        try
        {
            if (btnElement.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern))
            {
                ((InvokePattern)pattern).Invoke();
            }
        }
        catch { }
    }

    public static void InvokeDefault(AutomationElement? rootElement)
    {
        try
        {
            if (rootElement != null && rootElement.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern))
            {
                ((InvokePattern)pattern).Invoke();
            }
        }
        catch { }
    }

    private static void CollectTexts(AutomationElement parent, List<string> texts, int depth)
    {
        if (depth > 8) return;

        try
        {
            var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                try
                {
                    // Avoid picking text from buttons as body text
                    if (child.Current.ControlType == ControlType.Button)
                        continue;

                    string name = child.Current.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name) && !texts.Contains(name))
                    {
                        texts.Add(name);
                    }

                    if (child.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
                    {
                        string val = ((ValuePattern)pattern).Current.Value ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(val) && !texts.Contains(val))
                        {
                            texts.Add(val);
                        }
                    }

                    CollectTexts(child, texts, depth + 1);
                }
                catch { }
            }
        }
        catch { }
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len == 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
