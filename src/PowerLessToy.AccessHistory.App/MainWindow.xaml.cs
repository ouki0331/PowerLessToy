using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PowerLessToy.AccessHistory.App;

public partial class MainWindow : Window
{
    private readonly DatabaseManager _db;

    public MainWindow(DatabaseManager db)
    {
        InitializeComponent();
        _db = db;

        Width = SystemParameters.PrimaryScreenWidth * 0.8;
        Height = SystemParameters.PrimaryScreenHeight * 0.8;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

        Loaded += (s, e) =>
        {
            LoadMainLists();
            SearchBox.Focus();
        };

        Activated += (s, e) =>
        {
            LoadMainLists();
            SearchBox.Focus();
        };

        Deactivated += (s, e) =>
        {
            SearchPopup.IsOpen = false;
            this.Hide();
        };
        
        LoadMainLists();
    }

    private void LoadMainLists()
    {
        try
        {
            var local = _db.GetTopRecordsForMain(SourceType.Local);
            var smb = _db.GetTopRecordsForMain(SourceType.SMB);
            var wsl = _db.GetTopRecordsForMain(SourceType.WSL);

            LocalList.ItemsSource = local;
            SmbList.ItemsSource = smb;
            WslList.ItemsSource = wsl;

            AccessHistoryLogger.LogDebug($"MainWindow.LoadMainLists: Local={local.Count}, SMB={smb.Count}, WSL={wsl.Count}");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Failed to load records in MainWindow.LoadMainLists", ex);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                SearchPopup.IsOpen = false;
                return;
            }

            var results = _db.SearchRecords(query, 10);
            SearchResultsList.ItemsSource = results;
            SearchPopup.IsOpen = results.Count > 0;
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Exception in SearchBox_TextChanged", ex);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchPopup.IsOpen)
            {
                SearchPopup.IsOpen = false;
            }
            else
            {
                this.Hide();
            }
            e.Handled = true;
        }
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (GetRecordFromMenu(sender) is AccessRecord record)
        {
            OpenRecord(record);
        }
    }

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        if (GetRecordFromMenu(sender) is AccessRecord record)
        {
            ShowFileProperties(record.Path);
        }
    }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        if (GetRecordFromMenu(sender) is AccessRecord record)
        {
            _db.SetHiddenStatus(record.Id, true);
            LoadMainLists(); // Reload UI
        }
    }

    private AccessRecord? GetRecordFromMenu(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is AccessRecord record)
        {
            return record;
        }
        return null;
    }

    private void ItemBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Border border && border.DataContext is AccessRecord record)
        {
            OpenRecord(record);
        }
    }

    private void OpenRecord(AccessRecord record)
    {
        try
        {
            AccessHistoryLogger.LogInfo($"User requested opening record: '{record.Path}'");
            Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true });
            this.Hide();
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to open record path: '{record.Path}'", ex);
        }
    }

    private void BtnOpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string logDir = AccessHistoryLogger.LogsDirectory;
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            AccessHistoryLogger.LogInfo($"User clicked 'Open Logs'. Opening folder: '{logDir}'");
            Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError("Failed to open logs directory from UI button", ex);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    private const int SW_SHOW = 5;
    private const uint SEE_MASK_INVOKEIDLIST = 12;

    private void ShowFileProperties(string path)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                lpVerb = "properties",
                lpFile = path,
                nShow = SW_SHOW,
                fMask = SEE_MASK_INVOKEIDLIST
            };
            bool result = ShellExecuteEx(ref info);
            AccessHistoryLogger.LogInfo($"ShowFileProperties called for '{path}'. Result={result}");
        }
        catch (Exception ex)
        {
            AccessHistoryLogger.LogError($"Failed to show file properties for '{path}'", ex);
        }
    }
}
