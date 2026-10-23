using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SdqTerminal;

// Opt-in: exercises actual WSL startup through MainWindow/ConPTY without changing
// the installed distributions, default Linux shell, or saved application settings.
internal static class WslLaunchProbe
{
    public static void Run(string distribution)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var profile = Enum.Parse(typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!, "Wsl");
        var window = new MainWindow();
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "wsl launch 'space " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        object? Get(object owner, string name) => owner.GetType().GetProperty(name)!.GetValue(owner);
        var tabs = (IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
        var content = (FrameworkElement)window.Content;
        void Pump()
        {
            content.Measure(new Size(1800, 800)); content.Arrange(new Rect(0, 0, 1800, 800)); content.UpdateLayout();
            content.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
        string Text(object pane) => string.Concat(((TerminalBuffer)Get(pane, "Buffer")!).CaptureSnapshot().Lines.SelectMany(l => l.Runs).Select(r => r.Text));
        void AssertShell(object pane, string distro, string location)
        {
            var marker = "RTL_WSL_" + Guid.NewGuid().ToString("N");
            var expected = marker + "|" + distro + "|" + location + "|truecolor";
            ((ConPtySession)Get(pane, "Session")!).Write("printf '\\n" + marker + "|%s|%s|%s\\n' \"$WSL_DISTRO_NAME\" \"$PWD\" \"$COLORTERM\"\r");
            var timer = Stopwatch.StartNew();
            while (!Text(pane).Contains(expected))
            {
                Pump(); Thread.Sleep(20);
                if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new Exception("WSL launch did not produce " + expected + ": " + Text(pane));
            }
            if (Text(pane).Contains("Author : Sepehrsdq")) throw new Exception("WSL startup injected the introduction script");
            var directory = (Task<string?>)Call("ReadExplorerDirectoryAsync", pane)!;
            timer.Restart();
            while (!directory.IsCompleted)
            {
                Pump(); Thread.Sleep(15);
                if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("WSL Explorer-directory lookup timed out");
            }
            var expectedDirectory = Exec(distro, "wslpath", "-w", location);
            if (directory.GetAwaiter().GetResult() != expectedDirectory)
                throw new Exception("Explorer selected another WSL pane's directory: " + directory.Result + " expected " + expectedDirectory);
        }
        string Exec(string? distro, params string[] args)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (distro is not null) { info.ArgumentList.Add("-d"); info.ArgumentList.Add(distro); }
            info.ArgumentList.Add("--exec"); foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(25000)) { process.Kill(true); throw new Exception("WSL probe timed out"); }
            if (process.ExitCode != 0) throw new Exception(errors.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult().TrimEnd('\r', '\n');
        }
        try
        {
            var home = Exec(distribution, "printenv", "HOME");
            Call("CreateTerminalTab", profile, null, distribution); Pump();
            var first = Get(tabs[0]!, "ActivePane")!;
            AssertShell(first, distribution, home);
            // Changing the app's default must not affect a split of an existing profile.
            typeof(MainWindow).GetField("_defaultWslDistribution", flags)!.SetValue(window, "NotInstalled_ShouldNeverOpen");
            Call("SplitActivePane"); Pump();
            var split = Get(tabs[0]!, "ActivePane")!;
            if (ReferenceEquals(first, split) || (string?)Get(split, "WslDistribution") != distribution)
                throw new Exception("WSL split did not preserve the selected profile");
            AssertShell(split, distribution, home);
            var linuxDirectory = Exec(distribution, "wslpath", "-a", "-u", directory);
            Call("CreateTerminalTab", profile, directory, distribution); Pump();
            AssertShell(Get(tabs[1]!, "ActivePane")!, distribution, linuxDirectory);

            // The legacy generic profile can still let wsl.exe select its default.
            typeof(MainWindow).GetField("_defaultWslDistribution", flags)!.SetValue(window, null);
            var defaultDistro = Exec(null, "printenv", "WSL_DISTRO_NAME");
            var defaultHome = Exec(null, "printenv", "HOME");
            Call("CreateTerminalTab", profile, null, null); Pump();
            AssertShell(Get(tabs[2]!, "ActivePane")!, defaultDistro, defaultHome);
            ((ConPtySession)Get(split, "Session")!).Write("cd " + WslPath.Quote(linuxDirectory) + "\r");
            AssertShell(split, distribution, linuxDirectory);
            AssertShell(first, distribution, home);
            Console.WriteLine("PASS real WSL startup, paths with quotes/spaces, default distribution, split retention and live per-pane Explorer directories after cd");
        }
        finally
        {
            var panes = tabs.Cast<object>().SelectMany(t => ((IList)Get(t, "Panes")!).Cast<object>()).ToArray();
            foreach (var pane in panes) (Get(pane, "Session") as ConPtySession)?.Write("cd ~; exit\r");
            var shutdown = panes.Select(p => (Task)p.GetType().GetMethod("DisposeAsync")!.Invoke(p, null)!).ToArray();
            if (!Task.WaitAll(shutdown, TimeSpan.FromSeconds(10))) throw new Exception("WSL probe sessions did not shut down");
            window.Close();
            var cleanup = Stopwatch.StartNew();
            while (true)
            {
                try { Directory.Delete(directory); break; }
                catch (IOException) when (cleanup.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(50); }
            }
        }
    }
}
