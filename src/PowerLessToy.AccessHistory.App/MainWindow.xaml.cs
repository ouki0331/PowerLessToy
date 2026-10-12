using System;
using System.Diagnostics;
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
        LocalList.ItemsSource = _db.GetTopRecordsForMain(SourceType.Local);
        SmbList.ItemsSource = _db.GetTopRecordsForMain(SourceType.SMB);
        WslList.ItemsSource = _db.GetTopRecordsForMain(SourceType.WSL);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
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
            Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true });
            this.Hide();
        }
        catch { }
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
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
            fMask = SEE_MASK_INVOKEIDLIST
        };
        ShellExecuteEx(ref info);
    }
}
