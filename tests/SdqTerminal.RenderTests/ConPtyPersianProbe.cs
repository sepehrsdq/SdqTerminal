using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SdqTerminal;

internal static class ConPtyPersianProbe
{
    private const string Phrase = "پروژهٔ قدیمی را از محدودهٔ تغییرات نگه می‌دارم.";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WriteConsole(IntPtr handle, string text, uint count, out uint written, IntPtr reserved);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    public static void Child()
    {
        var handle = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1) || !SetConsoleMode(handle, 7)) throw new Win32Exception();
        try
        {
            var args = Environment.GetCommandLineArgs();
            var shellLaunch = args.Contains("--shell-launch");
            var launch = Array.IndexOf(args, "--codex-launch");
            if (launch >= 0 || shellLaunch)
            {
                var input = CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                try
                {
                    SetHandleInformation(input, 1, 1); SetHandleInformation(handle, 1, 1);
                    SetStdHandle(-10, input); SetStdHandle(-11, handle); SetStdHandle(-12, handle);
                    var info = shellLaunch
                        ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/D /Q /V:OFF /K \"prompt $G\"") { UseShellExecute = false }
                        : new ProcessStartInfo(args[launch + 1], "--no-alt-screen") { UseShellExecute = false, WorkingDirectory = args[launch + 2] };
                    info.Environment["TERM"] = "xterm-256color";
                    using var process = Process.Start(info)!;
                    process.WaitForExit();
                    return;
                }
                finally { CloseHandle(input); }
            }
            void Write(string text)
            {
                if (!WriteConsole(handle, text, (uint)text.Length, out var written, IntPtr.Zero) || written != text.Length)
                    throw new Win32Exception();
            }
            Write("\x1b[2J\x1b[H" + Phrase);
            Thread.Sleep(100);
            // A Codex/Ratatui-style diff: combining hamza occupies no extra cell.
            // With legacy conhost this overwrites the space after پروژهٔ.
            Write("\x1b[1;7Hقدیمی");
            Write("\x1b[2;1H\x1b[31mمی‌دارم\x1b[0m فاصله");
            Thread.Sleep(100);
            // ZWNJ also must not shift the position of the following word.
            Write("\x1b[2;8Hفاصله\x1b[4;1HENV:" + Environment.GetEnvironmentVariable("COLORTERM") + "|" + Environment.GetEnvironmentVariable("TERM_PROGRAM") + "|" + Environment.GetEnvironmentVariable(ShellPath.PercentVariable) + "\x1b[3;1HREADY");
        }
        finally { CloseHandle(handle); }
    }

    public static TerminalSnapshot Run()
    {
        var parentColor = Environment.GetEnvironmentVariable("COLORTERM");
        var parentWslEnv = Environment.GetEnvironmentVariable("WSLENV");
        using var session = new ConPtySession(110, 20);
        using var ready = new ManualResetEventSlim();
        var output = new StringBuilder();
        var buffer = new TerminalBuffer(110, 20);
        var reader = Task.Run(() =>
        {
            var bytes = new byte[8192]; var chars = new char[8192];
            var decoder = Encoding.UTF8.GetDecoder();
            try
            {
                int count;
                while ((count = session.Read(bytes)) > 0)
                {
                    var chunk = new string(chars, 0, decoder.GetChars(bytes, 0, count, chars, 0));
                    lock (output) output.Append(chunk);
                    var snapshot = buffer.Process(chunk);
                    foreach (var response in snapshot.Responses) session.Write(response);
                    if (snapshot.Lines.Any(line => string.Concat(line.Runs.Select(run => run.Text)).Contains("READY"))) ready.Set();
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
        var args = Environment.GetCommandLineArgs();
        var codex = Array.IndexOf(args, "--codex-probe");
        string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
        var command = "dotnet " + Quote(typeof(ConPtyPersianProbe).Assembly.Location) + " --conpty-child";
        if (codex >= 0)
        {
            if (args.Length <= codex + 2) throw new ArgumentException("--codex-probe requires an executable and working directory");
            command += " --codex-launch " + Quote(args[codex + 1]) + " " + Quote(args[codex + 2]);
        }
        session.Start(command);
        var completed = true;
        if (codex >= 0)
        {
            Thread.Sleep(6000);
            // Paste a draft only. Never send Enter or submit work to the agent.
            session.Write("\x1b[200~" + Phrase + " برای تغییرات کلاینت اسکرچ هم همچنان از پروژه farsiscratch استفاده می‌کنم.\x1b[201~");
            Thread.Sleep(2000);
            session.Write("\x03"); Thread.Sleep(500); session.Write("\x03"); Thread.Sleep(500);
        }
        else
        {
            completed = ready.Wait(TimeSpan.FromSeconds(10));
            // Force a repaint from the host cell buffer, as scrolling/resizing a TUI does.
            session.Resize(109, 20); Thread.Sleep(150);
            session.Resize(110, 20); Thread.Sleep(150);
        }
        session.Dispose();
        if (!reader.Wait(TimeSpan.FromSeconds(5))) throw new Exception("ConPTY reader did not shut down");
        string raw; lock (output) raw = output.ToString();
        Directory.CreateDirectory("artifacts/render-checks");
        File.WriteAllText("artifacts/render-checks/conpty-persian-raw.txt", raw);
        var result = buffer.CaptureSnapshot();
        var text = string.Join("\n", result.Lines.Select(line => string.Concat(line.Runs.Select(run => run.Text))));
        if (codex < 0 && (!text.Contains("ENV:truecolor|SdqTerminal|%") || Environment.GetEnvironmentVariable("COLORTERM") != parentColor || Environment.GetEnvironmentVariable("WSLENV") != parentWslEnv))
            throw new Exception("Child terminal environment was not isolated and advertised correctly");
        if (!completed || !text.Contains("پروژهٔ قدیمی") || !text.Contains("محدودهٔ تغییرات"))
            throw new Exception("ConPTY corrupted Persian marks or word separators: " + text);
        if (codex < 0 && !text.Contains("می‌دارم فاصله")) throw new Exception("ZWNJ changed the native cursor column: " + text);
        Console.WriteLine(codex >= 0 ? "PASS installed Codex draft redraw (no prompt submitted)" : "PASS native ConPTY Persian marks, ZWNJ, styled cursor redraw and clean shutdown");
        return result;
    }
}
