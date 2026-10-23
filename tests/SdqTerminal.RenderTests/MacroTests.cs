using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using SdqTerminal;

internal static class MacroTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    public static async Task Run()
    {
        var buffer = new TerminalBuffer(80, 5);
        buffer.Process(string.Concat(Enumerable.Repeat("history\r\n", 20)));
        Check(MacroRecorder.Prompt(buffer.Process("\x1b[32mUsername: \x1b[0m")) == "Username:", "Prompt must use absolute cursor row, exclude ANSI and history.");
        var recorder = new MacroRecorder(MacroMode.Interactive);
        recorder.Record("connect", "PS>"); recorder.Record("\r", "PS>connect");
        recorder.Record("test-user", "Username:"); recorder.Record("\r", "Username:test-user");
        recorder.Record("test-secret\r", "Password:");
        var macro = recorder.Macro; macro.Name = "Test login";
        macro.Validate();
        Check(macro.Steps.Count == 3 && macro.Steps[1].ExpectedPrompt == "Username:" && macro.Steps[2].Input == "test-secret\r", "Recorder must group typing and keep prompt before echo.");

        var playback = new MacroPlayback();
        var sent = new List<string>();
        var output = new TerminalBuffer(80, 8);
        var run = playback.RunAsync(macro, input => sent.Add(input), 2, (_, _) => { }, CancellationToken.None);
        await Task.Delay(75);
        Check(sent.Count == 1, "Interactive playback must wait for a prompt.");
        playback.Observe(output.Process("wrong prompt:")); await Task.Delay(75);
        Check(sent.Count == 1, "Unrelated output must not advance a macro.");
        playback.Observe(output.Process("\r\nUser")); await Task.Delay(50);
        Check(sent.Count == 1, "Partial prompt must not advance.");
        playback.Observe(output.Process("name: "));
        await WaitFor(() => sent.Count == 2);
        playback.Observe(output.Process("\r\nPassword: "));
        await run;
        Check(sent.SequenceEqual(new[] { "connect\r", "test-user\r", "test-secret\r" }), "Interactive sequence changed.");

        var stale = new MacroPlayback(); stale.Observe(output.Process("\r\nUsername: "));
        using (var cancel = new CancellationTokenSource())
        {
            var count = 0;
            var pending = stale.RunAsync(macro, _ => count++, 2, (_, _) => { }, cancel.Token);
            await Task.Delay(75); Check(count == 1, "Old screen content must never trigger a subsequent step.");
            cancel.Cancel();
            try { await pending; throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            Check(count == 1, "Cancelled macro sent more input.");
        }
        try { await new MacroPlayback().RunAsync(macro, _ => { }, 0, (_, _) => { }, CancellationToken.None); throw new Exception("Timeout ignored."); }
        catch (TimeoutException) { }
        var closed = new MacroPlayback(); closed.Close();
        try { await closed.RunAsync(macro, _ => throw new Exception("Sent to closed terminal."), 1, (_, _) => { }, CancellationToken.None); throw new Exception("Close ignored."); }
        catch (IOException) { }

        var timed = new TerminalMacro { Name = "Timed", Mode = MacroMode.Timed, Steps = [new() { Input = "a", DelayMilliseconds = 60 }, new() { Input = "b", DelayMilliseconds = 60 }] };
        var clock = Stopwatch.StartNew();
        await new MacroPlayback().RunAsync(timed, _ => { }, 1, (_, _) => { }, CancellationToken.None);
        Check(clock.ElapsedMilliseconds >= 110, "Timed delays were not respected.");

        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "macro-tests", Guid.NewGuid().ToString("N"));
        var store = new MacroStore(directory);
        store.Save(macro, "independent-password");
        Check(store.List().Single().PasswordProtected, "Password metadata lost.");
        var disk = File.ReadAllBytes(Directory.GetFiles(directory, "*.rtmacro").Single());
        Check(!Encoding.UTF8.GetString(disk).Contains("test-secret"), "Secret stored in plaintext.");
        Check(store.Load(macro.Id, "independent-password").Steps[2].Input == "test-secret\r", "Encrypted roundtrip changed input.");
        try { store.Load(macro.Id, "wrong"); throw new Exception("Wrong password accepted."); } catch (CryptographicException) { }
        store.Save(macro, ""); Check(!store.List().Single().PasswordProtected && store.Load(macro.Id, null).Name == macro.Name, "DPAPI-only roundtrip failed.");
        macro.Shortcut = 2; store.Save(macro, ""); timed.Shortcut = 2;
        try { store.Save(timed, ""); throw new Exception("Duplicate shortcut accepted."); } catch (InvalidOperationException) { }
        var path = Directory.GetFiles(directory, "*.rtmacro").Single(); disk = File.ReadAllBytes(path); disk[^1] ^= 1; File.WriteAllBytes(path, disk);
        try { store.Load(macro.Id, null); throw new Exception("Tampered storage accepted."); } catch (CryptographicException) { }
        store.Delete(macro.Id); Check(store.List().Count == 0, "Delete failed.");
        Directory.Delete(directory);
        await LiveLogin();
        await LiveInterrupt();
        Console.WriteLine("PASS macro recording, prompt matching, stale output, cancellation, timeout, timed mode, encrypted storage, passwords and tampering");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static void Child()
    {
        var input = CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        var output = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        SetStdHandle(-10, input); SetStdHandle(-11, output); SetConsoleMode(output, 7);
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        if (Environment.GetCommandLineArgs().Contains("--interrupt"))
        {
            SetConsoleMode(input, 7);
            using var interrupted = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; interrupted.Set(); };
            Console.Write("READY>");
            if (Console.ReadLine() != "stream") return;
            while (!interrupted.Wait(30)) Console.Write("\rSTREAMING...");
            Console.Write("\r\nSHELL>");
            Console.WriteLine(Console.ReadLine() == "exit" ? "INTERRUPT_OK" : "INTERRUPT_FAILED");
            return;
        }
        Console.Write("READY>");
        var command = Console.ReadLine();
        Console.Write("\x1b[32mUser"); Thread.Sleep(80); Console.Write("name: \x1b[0m");
        var name = Console.ReadLine();
        SetConsoleMode(input, 3); // Line + processed input, deliberately disable password echo.
        Console.Write("Password: ");
        var password = Console.ReadLine();
        Console.WriteLine(command == "begin" && name == "test-user" && password == "test-secret" ? "\r\nLOGIN_OK" : "\r\nLOGIN_FAILED");
    }

    private static async Task LiveLogin()
    {
        using var session = new ConPtySession(80, 12);
        var buffer = new TerminalBuffer(80, 12);
        var playback = new MacroPlayback();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Start(ShellPath.WindowsArgument(Environment.ProcessPath!) + " --macro-child", Environment.CurrentDirectory);
        var reader = Task.Run(() =>
        {
            var bytes = new byte[8192]; var decoder = Encoding.UTF8.GetDecoder(); var chars = new char[8192];
            try
            {
                int count;
                while ((count = session.Read(bytes)) > 0)
                {
                    var snapshot = buffer.Process(new string(chars, 0, decoder.GetChars(bytes, 0, count, chars, 0)));
                    playback.Observe(snapshot);
                    foreach (var reply in snapshot.Responses) session.Write(reply);
                    var text = string.Concat(snapshot.Lines.SelectMany(l => l.Runs).Select(r => r.Text));
                    if (text.Contains("READY>")) ready.TrySetResult();
                    if (text.Contains("LOGIN_OK")) done.TrySetResult();
                }
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            finally { playback.Close(); }
        });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var macro = new TerminalMacro { Name = "Live login", Steps = [
            new() { Input = "begin\r" }, new() { Input = "test-user\r", ExpectedPrompt = "Username:" },
            new() { Input = "test-secret\r", ExpectedPrompt = "Password:" }] };
        await playback.RunAsync(macro, session.Write, 5, (_, _) => { }, CancellationToken.None);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var result = string.Concat(buffer.CaptureSnapshot().Lines.SelectMany(l => l.Runs).Select(r => r.Text));
        Check(!result.Contains("test-secret"), "Password unexpectedly echoed in live test.");
        session.Dispose();
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS real ConPTY interactive login with delayed ANSI prompt and hidden password");
    }

    private static async Task LiveInterrupt()
    {
        using var session = new ConPtySession(80, 12);
        var buffer = new TerminalBuffer(80, 12); var playback = new MacroPlayback();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Start(ShellPath.WindowsArgument(Environment.ProcessPath!) + " --macro-child --interrupt", Environment.CurrentDirectory);
        var reader = Task.Run(() =>
        {
            var bytes = new byte[8192]; var decoder = Encoding.UTF8.GetDecoder(); var chars = new char[8192];
            try
            {
                int count;
                while ((count = session.Read(bytes)) > 0)
                {
                    var snapshot = buffer.Process(new string(chars, 0, decoder.GetChars(bytes, 0, count, chars, 0)));
                    playback.Observe(snapshot);
                    foreach (var reply in snapshot.Responses) session.Write(reply);
                    var text = string.Concat(snapshot.Lines.SelectMany(l => l.Runs).Select(r => r.Text));
                    if (text.Contains("READY>")) ready.TrySetResult();
                    if (text.Contains("INTERRUPT_OK")) done.TrySetResult();
                }
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            finally { playback.Close(); }
        });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var recorder = new MacroRecorder(MacroMode.Interactive);
        recorder.Record("stream\r", "READY>");
        await Task.Delay(500);
        recorder.RecordKey("\x03", "STREAMING...", "Ctrl+C", true);
        recorder.Record("exit\r", "SHELL>");
        var macro = recorder.Finish(); macro.Name = "Live interrupt";
        await playback.RunAsync(macro, session.Write, 5, (_, _) => { }, CancellationToken.None);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.Dispose(); await reader.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS real ConPTY Ctrl+C interrupts continuous output and waits for the next shell prompt");
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(20); }
        throw new Exception("Macro did not advance.");
    }
}
