using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SdqTerminal;

internal static partial class Program
{
    private static void CheckExplorerDirectories()
    {
        var buffer = new TerminalBuffer(80, 20);
        var snapshot = buffer.Process("\x1b]9;9;\"C:\\work space\\فارسی\"\x07");
        if (snapshot.WorkingDirectory != @"C:\work space\فارسی" || snapshot.Lines.Any(l => l.Runs.Any(r => r.Text.Contains("work space"))))
            throw new Exception("Directory reports leaked into terminal output or lost the path");
        buffer.Process("\x1b]7;file://localhost/home/a%20b/\x1b");
        snapshot = buffer.Process("\\");
        if (snapshot.WorkingDirectory != "file://localhost/home/a%20b/") throw new Exception("Split OSC 7 report was not retained");
        var directoryType = typeof(MainWindow).Assembly.GetType("SdqTerminal.TerminalDirectory")!;
        string? FromReport(string report, string? distro) => (string?)directoryType.GetMethod("FromReport")!.Invoke(null, [report, distro]);
        if (FromReport(snapshot.WorkingDirectory!, "Ubuntu") != @"\\wsl.localhost\Ubuntu\home\a b\" ||
            FromReport("file:///D:/work%20space/", null) != @"D:\work space\" ||
            FromReport("relative/path", null) is not null || FromReport("https://example.com", null) is not null)
            throw new Exception("Explorer directory conversion accepted a relative/non-file path or lost a Unicode/WSL path");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow();
        var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
        var tabType = typeof(MainWindow).GetNestedType("TerminalTab", BindingFlags.NonPublic)!;
        var tabs = (IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags | BindingFlags.Static)!.Invoke(window, args);
        object? Get(object owner, string name) => owner.GetType().GetProperty(name)!.GetValue(owner);
        void Set(object owner, string name, object? value) => owner.GetType().GetProperty(name)!.SetValue(owner, value);
        var content = (FrameworkElement)window.Content;
        void Pump()
        {
            content.Measure(new Size(1400, 800)); content.Arrange(new Rect(0, 0, 1400, 800)); content.UpdateLayout();
            content.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
        string? Current(object pane) => ((Task<string?>)Call("ReadExplorerDirectoryAsync", pane)!).GetAwaiter().GetResult();
        void WaitDirectory(object pane, string path)
        {
            var watch = Stopwatch.StartNew();
            while (!string.Equals(Current(pane)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Pump(); Thread.Sleep(20);
                if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Shell did not report changed directory: " + Current(pane));
            }
        }
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "directory-probe-" + Guid.NewGuid().ToString("N"));
        var locations = new[] { Path.Combine(root, "CMD & فارسی"), Path.Combine(root, "PowerShell ' space") };
        foreach (var path in locations) Directory.CreateDirectory(path);
        try
        {
            if (window.FindName("OpenDirectoryMenuItem") is not MenuItem) throw new Exception("File menu has no Explorer command");
            var panes = new List<object>();
            for (var i = 0; i < 2; i++)
            {
                var pane = Call("CreatePane", Enum.ToObject(profileType, i))!;
                var tab = Activator.CreateInstance(tabType, pane)!; tabs.Add(tab); panes.Add(pane);
                Call("ShowTab", tab); Pump(); Call("StartPane", pane, root, i == 1);
                if (!ReferenceEquals(Call("ExplorerTargetPane"), pane)) throw new Exception("Single-pane Explorer target was incorrect");
                WaitDirectory(pane, root);
                var session = (ConPtySession)Get(pane, "Session")!;
                session.Write(i == 0 ? "cd /d " + ShellPath.Windows(locations[i], false) + "\r"
                    : "Set-Location -LiteralPath " + ShellPath.Windows(locations[i], true) + "\r");
                WaitDirectory(pane, locations[i]);
                if (i == 0 && !string.Equals(session.ReadWorkingDirectory()?.TrimEnd('\\'), locations[i].TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Native process directory fallback did not follow CMD cd: " + session.ReadWorkingDirectory());
            }
            if (!(bool)Call("TryJoinTab", tabs[0], false)!) throw new Exception("Directory test panes did not fit");
            var joined = tabs[0]!;
            foreach (var pane in panes)
            {
                Call("ActivatePane", pane);
                if (!ReferenceEquals(Call("ExplorerTargetPane"), pane)) throw new Exception("Explorer selected the wrong split pane");
                WaitDirectory(pane, locations[panes.IndexOf(pane)]);
            }
            var selected = Get(joined, "ActivePane");
            var direction = Enum.Parse(typeof(MainWindow).GetNestedType("PaneSplitDirection", BindingFlags.NonPublic)!, "SideBySide");
            var newest = Call("AddSplitPane", joined, panes[0], direction)!;
            Set(joined, "ActivePane", null);
            if (!ReferenceEquals(Call("ExplorerTargetPane"), newest)) throw new Exception("Missing selection did not fall back to the last opened pane");
            Set(joined, "ActivePane", selected);
            Console.WriteLine("PASS File Explorer menu, OSC directory reports, live CMD/PowerShell cd, Unicode/quoted paths, per-pane targeting and last-pane fallback");
        }
        finally
        {
            var shutdown = tabs.Cast<object>().SelectMany(t => ((IList)Get(t, "Panes")!).Cast<object>())
                .Select(p => (Task)p.GetType().GetMethod("DisposeAsync")!.Invoke(p, null)!).ToArray();
            Task.WaitAll(shutdown, TimeSpan.FromSeconds(10)); window.Close();
            var watch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    foreach (var path in locations) if (Directory.Exists(path)) Directory.Delete(path);
                    Directory.Delete(root); break;
                }
                catch (IOException) when (watch.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(50); }
            }
        }
    }
}
