using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SdqTerminal;

internal static class ChromeTests
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        SearchCountsAndSteps();
        FindBarAndPalette();
        Console.WriteLine("PASS find bar, search match stepping, command palette filtering, RTL chip modes and status bar");
    }

    private static void SearchCountsAndSteps()
    {
        var view = new TerminalView
        {
            Width = 800,
            Height = 420,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            Background = Brushes.Black
        };
        var buffer = new TerminalBuffer(70, 16);
        var snapshot = buffer.Process(
            "\x1b[?25lerror: first\r\nall good\r\nerror: second\r\nerror: third");
        view.Present(snapshot, true, 10.8, 25, false);
        view.Measure(new Size(800, 420));
        view.Arrange(new Rect(0, 0, 800, 420));
        view.UpdateLayout();

        if (view.Search("error") != 3)
            throw new Exception("Find did not collect every match");
        if (view.SearchMatchIndex != 1)
            throw new Exception("Find did not start on the first match");
        if (view.StepSearch(1) != 2 || view.StepSearch(-1) != 1)
            throw new Exception("Find stepping did not wrap through matches");
        if (view.Search("SECOND") != 1)
            throw new Exception("Find is not case-insensitive");
        if (view.Search("absent") != 0 || view.SearchMatchCount != 0)
            throw new Exception("Find reported a match for absent text");
        if (view.Search("first") != 1)
            throw new Exception("Find lost a match after a new query");
        view.ClearSearch();
        if (view.SearchMatchCount != 0)
            throw new Exception("Closing find left match highlighting behind");
    }

    private static void FindBarAndPalette()
    {
        var window = new MainWindow();
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1080, 680));
        root.Arrange(new Rect(0, 0, 1080, 680));
        root.UpdateLayout();

        typeof(MainWindow).GetMethod("OpenSearch", Flags)!.Invoke(window, null);
        var panel = (FrameworkElement)window.FindName("SearchPanel");
        if (panel.Visibility != Visibility.Visible)
            throw new Exception("Find bar did not open");

        var box = (TextBox)window.FindName("SearchBox");
        box.Text = "zzz-not-in-buffer";
        var status = (TextBlock)window.FindName("SearchStatusText");
        if (status.Text != "No results")
            throw new Exception("Find bar did not report an empty result");

        typeof(MainWindow).GetMethod("CloseSearch", Flags)!.Invoke(window, null);
        if (panel.Visibility != Visibility.Collapsed)
            throw new Exception("Find bar did not close");

        typeof(MainWindow).GetMethod("OpenPalette", Flags)!.Invoke(window, null);
        var overlay = (FrameworkElement)window.FindName("PaletteOverlay");
        if (overlay.Visibility != Visibility.Visible)
            throw new Exception("Command palette did not open");

        var list = (ListBox)window.FindName("PaletteList");
        var total = list.Items.Count;
        if (total < 10)
            throw new Exception("Command palette has too few commands");

        var search = (TextBox)window.FindName("PaletteSearchBox");
        search.Text = "font";
        if (list.Items.Count != 3)
            throw new Exception($"Palette filter returned {list.Items.Count} rows for 'font'");
        search.Text = string.Empty;
        if (list.Items.Count != total)
            throw new Exception("Clearing the palette filter did not restore every command");

        typeof(MainWindow).GetMethod("ClosePalette", Flags)!.Invoke(window, null);
        if (overlay.Visibility != Visibility.Collapsed)
            throw new Exception("Command palette did not close");

        var rtlMode = typeof(MainWindow).GetNestedType("RtlMode", BindingFlags.NonPublic)!;
        var setRtlMode = typeof(MainWindow).GetMethod("SetRtlMode", Flags)!;
        var chipText = (TextBlock)window.FindName("RtlChipText");
        var smartItem = (MenuItem)window.FindName("SmartRtlMenuItem");
        var rowItem = (MenuItem)window.FindName("RowRtlMenuItem");
        setRtlMode.Invoke(window, [Enum.Parse(rtlMode, "Row")]);
        if (chipText.Text != "Row RTL" || smartItem.IsChecked || !rowItem.IsChecked)
            throw new Exception("RTL chip did not switch to Row RTL");
        setRtlMode.Invoke(window, [Enum.Parse(rtlMode, "Off")]);
        if (chipText.Text != "RTL off" || smartItem.IsChecked || rowItem.IsChecked)
            throw new Exception("RTL chip did not switch RTL off");
        setRtlMode.Invoke(window, [Enum.Parse(rtlMode, "Smart")]);
        if (chipText.Text != "Smart RTL" || !smartItem.IsChecked || rowItem.IsChecked)
            throw new Exception("RTL chip did not switch back to Smart RTL");

        var tabs = (IList)typeof(MainWindow).GetField("_tabs", Flags)!.GetValue(window)!;
        var tabType = typeof(MainWindow).GetNestedType("TerminalTab", BindingFlags.NonPublic)!;
        var paneType = typeof(MainWindow).GetNestedType("TerminalPane", BindingFlags.NonPublic)!;
        var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
        var tab = Activator.CreateInstance(tabType,
            Activator.CreateInstance(paneType, 1, Enum.ToObject(profileType, 1), "PowerShell"))!;
        tabs.Add(tab);
        typeof(MainWindow).GetField("_activeTab", Flags)!.SetValue(window, tab);
        typeof(MainWindow).GetMethod("UpdateStatusBar", Flags)!.Invoke(window, [true]);
        var profile = (TextBlock)window.FindName("StatusProfileText");
        var right = (TextBlock)window.FindName("StatusRightText");
        if (profile.Text != "PowerShell" || !right.Text.Contains("1 pane"))
            throw new Exception($"Status bar shows '{profile.Text}' / '{right.Text}'");
        window.Close();
    }
}
