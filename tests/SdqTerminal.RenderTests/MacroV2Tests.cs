using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using SdqTerminal;

internal static class MacroV2Tests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task Run()
    {
        var recorder = new MacroRecorder(MacroMode.Interactive);
        recorder.Record("run\r", ">");
        await Task.Delay(60);
        recorder.RecordKey("\x03", "working...", "Ctrl+C", true);
        recorder.Record("next\r", "SHELL>");
        var macro = recorder.Finish(); macro.Name = "Interrupt and continue"; macro.Validate();
        Check(macro.Steps.Count == 3 && macro.Steps[1].Kind == MacroStepKind.Key && macro.Steps[1].Wait == MacroWait.Delay && macro.Steps[1].DelayMilliseconds >= 50,
            "Ctrl+C must be an independently timed key.");
        Check(macro.Steps[2].Wait == MacroWait.Prompt && macro.Steps[2].ExpectedPrompt == "SHELL>", "Post-interrupt input must wait for the new prompt.");
        var partial = new MacroRecorder(MacroMode.Interactive);
        partial.Record("unfinished", ">"); partial.RecordKey("\x03", ">unfinished", "Ctrl+C", true); partial.Record("fresh\r", ">");
        Check(partial.Macro.Steps.Select(s => s.Input).SequenceEqual(new[] { "unfinished", "\x03", "fresh\r" }), "Ctrl+C merged partial and next input.");
        var editing = new MacroRecorder(MacroMode.Interactive);
        editing.Record("abc", "Password:"); editing.RecordKey("\x7f", "Password:", "Backspace", false); editing.Record("d\r", "Password:");
        Check(editing.Macro.Steps[1].Kind == MacroStepKind.Key && editing.Macro.Steps[2].Wait == MacroWait.Immediate, "Editing keys should not require another prompt.");
        var sensitive = editing.Finish(); sensitive.Name = "No stored secret";
        var safe = MacroPrivacy.Prepare(sensitive, true);
        Check(safe.Steps.Count == 1 && safe.Steps[0].Input == "" && safe.Steps[0].AskAtRuntime && safe.Steps[0].SubmitSuffix == "\r", "Sensitive edited submission was not collapsed to a runtime value.");
        safe.Validate();
        var echo = new MacroRecorder(MacroMode.Interactive); var buffer = new TerminalBuffer(80, 10);
        echo.Record("normal", ">"); await Task.Delay(20); echo.Observe(buffer.Process(">normal")); echo.Record("\r", ">normal");
        Check(!echo.Finish().Steps[0].IsSensitive, "Delayed but confirmed echo marked as sensitive.");
        var hidden = new MacroRecorder(MacroMode.Interactive); hidden.Record("hidden-value\r", "Value:");
        Check(hidden.Finish().Steps[0].IsSensitive, "Unconfirmed echo must be conservative.");
        hidden.Macro.Steps[0].Sensitivity = MacroSensitivity.Normal;
        Check(!hidden.Macro.Steps[0].IsSensitive, "Manual normal override ignored.");
        var masked = new MacroRecorder(MacroMode.Interactive); masked.Record("secret", "Input:"); masked.Observe(new TerminalBuffer(80, 10).Process("Input: ******"));
        Check(masked.Finish().Steps[0].DetectionReason == "Masked input detected", "Masked input was not detected.");
        Check(!MacroKeys.ShouldRecord("Ctrl+C", new() { RecordControlKeys = false }) && MacroKeys.ShouldRecord("Enter", new() { RecordNavigationKeys = false }), "Key group switches broke submission keys.");
        Check(!MacroKeys.ShouldRecord("Ctrl+Shift+L", new() { ExcludedKeys = "shift+ctrl+l; F5" }) && !MacroKeys.TryParse("Wrong+F5", out _, out _), "Key exclusion normalization failed.");

        var playback = new MacroPlayback(); playback.Pause(); var sent = new List<string>();
        var timed = new TerminalMacro { Name = "Step", Steps = [new() { Input = "a", Wait = MacroWait.Delay, DelayMilliseconds = 80 }, new() { Input = "b", Wait = MacroWait.Immediate }] };
        var task = playback.RunAsync(timed, sent.Add, 2, (_, _) => { }, CancellationToken.None);
        await Task.Delay(120); Check(sent.Count == 0, "Paused playback sent input.");
        playback.Step(); await Until(() => sent.Count == 1);
        await Task.Delay(80); Check(playback.IsPaused && sent.Count == 1, "Single-step did not pause after one action.");
        playback.Resume(); await task; Check(sent.Count == 2, "Resume did not finish.");
        var closing = new MacroPlayback();
        var longDelay = new TerminalMacro { Name = "Closed delay", Steps = [new() { Input = "never", Wait = MacroWait.Delay, DelayMilliseconds = 60000 }] };
        var closingTask = closing.RunAsync(longDelay, _ => throw new Exception("Closed delayed target received input."), 1, (_, _) => { }, CancellationToken.None);
        closing.Close();
        try { await closingTask.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Closed terminal did not stop timed replay."); }
        catch (IOException) { }
        var retry = new MacroPlayback(); var conditions = new TerminalBuffer(80, 10); int retries = 0; sent.Clear();
        var conditional = new TerminalMacro { Name = "Recover", Steps = [new() { Input = "start", Wait = MacroWait.Immediate }, new() { Input = "value", Wait = MacroWait.Prompt, ExpectedPrompt = "Old:" }] };
        await retry.RunAsync(conditional, sent.Add, 0, (_, _) => { }, CancellationToken.None, (_, step) =>
        { retries++; step.ExpectedPrompt = "New:"; retry.Observe(conditions.Process("New:")); return Task.FromResult(true); });
        Check(retries == 1 && sent.SequenceEqual(new[] { "start", "value" }), "Retry resent a previous command or skipped the condition.");
        sent.Clear();
        await new MacroPlayback().RunAsync(safe, sent.Add, 1, (_, _) => { }, CancellationToken.None,
            requestInput: _ => Task.FromResult<string?>("runtime-secret"));
        Check(sent.SequenceEqual(new[] { "runtime-secret\r" }) && safe.Steps[0].Input == "", "Runtime answer was not sent correctly or mutated the stored model.");
        var noSubmit = JsonSerializer.Deserialize<TerminalMacro>(JsonSerializer.Serialize(safe))!;
        noSubmit.Steps[0].RuntimeSubmissionConfigured = true; noSubmit.Steps[0].SubmitSuffix = "";
        Check(MacroPrivacy.Prepare(noSubmit, true).Steps[0].SubmitSuffix == "", "Explicit runtime submission setting was overwritten.");
        try
        {
            await new MacroPlayback().RunAsync(safe, _ => throw new Exception("Cancelled answer was sent."), 1, (_, _) => { }, CancellationToken.None,
                requestInput: _ => Task.FromResult<string?>(null));
            throw new Exception("Cancel did not stop runtime prompt.");
        }
        catch (OperationCanceledException) { }

        var recheck = new MacroPlayback(); var recheckBuffer = new TerminalBuffer(80, 10);
        recheck.Observe(recheckBuffer.Process("Password:"));
        var recheckMacro = new TerminalMacro { Name = "Recheck", Steps = [new() { AskAtRuntime = true, Wait = MacroWait.Prompt, ExpectedPrompt = "Password:", Sensitivity = MacroSensitivity.Sensitive }] };
        var asked = false;
        try
        {
            await recheck.RunAsync(recheckMacro, _ => throw new Exception("Secret sent after server changed its prompt."), 0, (_, _) => { }, CancellationToken.None,
                recover: (_, _) => Task.FromResult(false), requestInput: _ =>
                { asked = true; recheck.Observe(recheckBuffer.Process("\r\nDisconnected")); return Task.FromResult<string?>("must-not-send"); });
            throw new Exception("Changed prompt should not accept runtime input.");
        }
        catch (OperationCanceledException) { }
        Check(asked, "First-step prompt was not accepted from the current screen.");

        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "macro-tests", Guid.NewGuid().ToString("N"));
        var store = new MacroStore(directory); store.Save(sensitive, null, true);
        var path = Directory.GetFiles(directory).Single();
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        using (var envelope = JsonDocument.Parse(clear))
        {
            Check(envelope.RootElement.GetProperty("Version").GetInt32() == 2, "New storage version missing.");
            var payload = envelope.RootElement.GetProperty("Payload").GetBytesFromBase64();
            Check(!Encoding.UTF8.GetString(payload).Contains("abc") && !Encoding.UTF8.GetString(payload).Contains("runtime-secret"), "Sensitive values persisted inside the encrypted payload.");
        }
        Check(store.Load(sensitive.Id, null).Steps[0].AskAtRuntime, "Runtime placeholder lost on load.");
        var legacy = new TerminalMacro { Id = sensitive.Id, Name = "Legacy", Steps = [new() { Input = "old\x03\r" }] };
        var oldEnvelope = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Entry = new MacroEntry(legacy.Id, legacy.Name, legacy.Mode, 0, false),
            Payload = JsonSerializer.SerializeToUtf8Bytes(legacy), Salt = Array.Empty<byte>(), Nonce = Array.Empty<byte>(), Tag = Array.Empty<byte>() });
        File.WriteAllBytes(path, ProtectedData.Protect(oldEnvelope, null, DataProtectionScope.CurrentUser));
        var loaded = store.Load(legacy.Id, null);
        Check(loaded.FormatVersion == 1 && loaded.Steps[0].Input == "old\x03\r" && loaded.Steps[0].Wait == MacroWait.Legacy, "Version 1 recording was reinterpreted.");
        store.Delete(sensitive.Id); Directory.Delete(directory);
        Console.WriteLine("PASS macro v2 key boundaries, sensitive detection, runtime redaction, pause/step/recovery, exclusions and v1 compatibility");
    }
    private static async Task Until(Func<bool> predicate)
    { for (var i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(20); } throw new Exception("Macro v2 wait timed out."); }
}
