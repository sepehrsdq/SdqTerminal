using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SdqTerminal;

public enum MacroMode { Interactive, Timed }
public enum MacroStepKind { Legacy, Text, Key }
public enum MacroWait { Legacy, Prompt, Delay, Immediate }
public enum MacroSensitivity { Auto, Sensitive, Normal }

public sealed class MacroStep
{
    public string Input { get; set; } = "";
    public string ExpectedPrompt { get; set; } = "";
    public int DelayMilliseconds { get; set; }
    public MacroStepKind Kind { get; set; }
    public MacroWait Wait { get; set; }
    public string KeyName { get; set; } = "";
    public MacroSensitivity Sensitivity { get; set; }
    public bool DetectedSensitive { get; set; }
    public string DetectionReason { get; set; } = "";
    public bool AskAtRuntime { get; set; }
    public string VariableName { get; set; } = "";
    public string SubmitSuffix { get; set; } = "";
    public bool RuntimeSubmissionConfigured { get; set; }
    public int Group { get; set; }
    [JsonIgnore] public bool IsSensitive => Sensitivity == MacroSensitivity.Sensitive || Sensitivity == MacroSensitivity.Auto && DetectedSensitive;
    [JsonIgnore] public string InputSummary => Kind == MacroStepKind.Key ? KeyName : AskAtRuntime ? "Ask when running" : $"{Input.Length} characters (hidden)";
    [JsonIgnore] public string Description => $"{(Kind == MacroStepKind.Key ? "Key" : "Input")} · {InputSummary}" + (IsSensitive ? " · Sensitive" : "") + (VariableName.Length > 0 ? " · " + VariableName : "");
}

public sealed class TerminalMacro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public MacroMode Mode { get; set; }
    public int Shortcut { get; set; }
    public List<MacroStep> Steps { get; set; } = [];

    public void Validate()
    {
        if (FormatVersion is < 1 or > 2 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 ||
            !Enum.IsDefined(Mode) || Shortcut is < 0 or > 9 || Steps.Count is < 1 or > 10000 ||
            Steps.Sum(s => (long)s.Input.Length + s.ExpectedPrompt.Length) > 1_000_000)
            throw new InvalidDataException("Invalid macro or recording is too large.");
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if ((!step.AskAtRuntime && step.Input.Length == 0) || step.DelayMilliseconds is < 0 or > 86400000 ||
                !Enum.IsDefined(step.Kind) || !Enum.IsDefined(step.Wait) || !Enum.IsDefined(step.Sensitivity) ||
                step.VariableName.Length > 80 || step.SubmitSuffix is not ("" or "\r" or "\n" or "\r\n"))
                throw new InvalidDataException("Invalid macro step.");
            if ((step.Wait == MacroWait.Prompt || step.Wait == MacroWait.Legacy && Mode == MacroMode.Interactive && i > 0) && string.IsNullOrWhiteSpace(step.ExpectedPrompt))
                throw new InvalidDataException($"Step {i + 1} needs a prompt to wait for. Edit the prompt before saving.");
        }
    }
}

// Owned by the UI thread. Only actual user input is recorded, never terminal protocol replies.
public sealed class MacroRecorder(MacroMode mode, bool detectSensitive = true)
{
    private readonly object _sync = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastInput;
    private MacroStep? _pending;
    private int _group = 1;
    private bool _newSubmission = true;
    private readonly Dictionary<int, (string Input, bool Echoed, bool OutputSeen, string Baseline)> _echo = [];
    public TerminalMacro Macro { get; } = new() { Mode = mode, FormatVersion = 2 };

    public void Record(string input, string prompt)
        => RecordInput(input, prompt, "", false);

    public void RecordKey(string input, string prompt, string keyName, bool independent)
        => RecordInput(input, prompt, keyName, independent);

    private void RecordInput(string input, string prompt, string keyName, bool independent)
    {
        lock (_sync)
        {
        if (input.Length == 0) return;
        if (Macro.Steps.Count >= 10000 || Macro.Steps.Sum(s => (long)s.Input.Length) + input.Length > 1_000_000)
            throw new InvalidOperationException("Recording limit reached. Stop and save this recording.");
        var now = _clock.ElapsedMilliseconds;
        // Finish older echo probes once a later submission starts. Do not scan
        // the entire recording on every terminal output frame.
        foreach (var old in _echo.Keys.Where(g => g < _group - 1).ToArray())
        {
            if (!_echo[old].Echoed && _echo[old].Input.Length > 0 && detectSensitive) MarkGroup(old, "Input echo not confirmed — treated as sensitive");
            _echo.Remove(old);
        }
        var delay = (int)Math.Min(86400000, now - _lastInput);
        if (!_echo.ContainsKey(_group)) _echo[_group] = ("", false, false, prompt.TrimEnd());
        if (keyName.Length > 0 && input != "\r")
        {
            _pending = null;
            Macro.Steps.Add(new() { Kind = MacroStepKind.Key, Input = input, KeyName = keyName,
                Wait = Macro.Mode == MacroMode.Timed || independent ? MacroWait.Delay : MacroWait.Immediate,
                DelayMilliseconds = delay, Group = _group });
            if (independent) { FinishGroup(); _newSubmission = true; }
        }
        else if (Macro.Mode == MacroMode.Timed)
        {
            var step = new MacroStep { Kind = MacroStepKind.Text, Input = input, Wait = MacroWait.Delay, DelayMilliseconds = delay, Group = _group };
            DetectPrompt(step, prompt); Macro.Steps.Add(step);
            TrackInput(input);
            if (input.Contains('\r') || input.Contains('\n')) { FinishGroup(); _newSubmission = true; }
        }
        else
        {
            if (_pending is null)
            {
                _pending = new() { Kind = MacroStepKind.Text, Group = _group,
                    Wait = Macro.Steps.Count == 0 || !_newSubmission ? MacroWait.Immediate : MacroWait.Prompt,
                    ExpectedPrompt = Macro.Steps.Count == 0 || !_newSubmission ? "" : prompt.TrimEnd() };
                DetectPrompt(_pending, prompt);
                Macro.Steps.Add(_pending);
                _newSubmission = false;
            }
            _pending.Input += input;
            TrackInput(input);
            if (input.Contains('\r') || input.Contains('\n')) { _pending = null; FinishGroup(); _newSubmission = true; }
        }
        _lastInput = now;
        }
    }

    private void TrackInput(string input)
    {
        _echo.TryGetValue(_group, out var state);
        var added = new string(input.Where(c => !char.IsControl(c)).ToArray());
        _echo[_group] = ((state.Input ?? "") + added, added.Length == 0 && state.Echoed, state.OutputSeen, state.Baseline);
    }

    private void DetectPrompt(MacroStep step, string prompt)
    {
        if (!detectSensitive) return;
        if (Regex.IsMatch(prompt, @"(?i)\b(password|passwd|passphrase|pin|otp|verification\s+code|secret|token)\b|رمز|گذرواژه|کد\s*تأیید", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        { step.DetectedSensitive = true; step.DetectionReason = "Password or secret prompt"; }
    }

    // Called from the reader thread. Raw output is never retained or logged.
    public void Observe(TerminalSnapshot snapshot)
    {
        if (!detectSensitive) return;
        lock (_sync)
        {
            var visible = snapshot.Lines.Skip(Math.Max(0, snapshot.CursorRow - 3)).Take(4)
                .Select(line => string.Concat(line.Runs.Select(r => r.Text))).ToArray();
            foreach (var group in _echo.Keys.ToArray())
            {
                var state = _echo[group];
                var echoed = state.Input.Length > 0 && state.Baseline.Length > 0 && visible.Any(line =>
                    line.StartsWith(state.Baseline, StringComparison.Ordinal) && line[state.Baseline.Length..].Contains(state.Input, StringComparison.Ordinal));
                _echo[group] = (state.Input, state.Echoed || echoed, true, state.Baseline);
                if (state.Input.Length > 0 && Regex.IsMatch(MacroRecorder.Prompt(snapshot), @"[●•*]{2,}", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                    MarkGroup(group, "Masked input detected");
            }
        }
    }

    private void MarkGroup(int group, string reason)
    {
        foreach (var step in Macro.Steps.Where(s => s.Group == group && s.Kind != MacroStepKind.Key))
        { step.DetectedSensitive = true; if (step.DetectionReason.Length == 0) step.DetectionReason = reason; }
    }
    private void FinishGroup() => _group++;

    public TerminalMacro Finish()
    {
        lock (_sync)
        {
            if (detectSensitive)
                foreach (var pair in _echo)
                    if (pair.Value.Input.Length > 0 && !pair.Value.Echoed)
                        MarkGroup(pair.Key, "Input echo not confirmed — treated as sensitive");
            return Macro;
        }
    }

    public static string Prompt(TerminalSnapshot snapshot)
    {
        var row = snapshot.CursorRow;
        if (row < 0 || row >= snapshot.Lines.Count) return "";
        var text = string.Concat(snapshot.Lines[row].Runs.Select(r => r.Text)).TrimEnd();
        // Keep the whole logical prompt when it wraps across terminal rows.
        while (row > 0 && snapshot.Lines[row].WrappedFromPrevious)
            text = string.Concat(snapshot.Lines[--row].Runs.Select(r => r.Text)) + text;
        return text.Length > 1024 ? text[^1024..] : text;
    }
}

// Output is published by the ConPTY reader; playback always targets the original pane.
public sealed class MacroPlayback
{
    private readonly object _gate = new();
    private long _generation;
    private string _prompt = "";
    private bool _closed;
    private volatile bool _paused;
    private bool _stepOnce;
    public bool IsPaused => _paused;
    public void Pause() => _paused = true;
    public void Resume() { _stepOnce = false; _paused = false; }
    public void Step() { _stepOnce = true; _paused = false; }
    private async Task Gate(CancellationToken cancellationToken)
    {
        while (_paused)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (_closed) throw new IOException("The target terminal has closed.");
            await Task.Delay(25, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) if (_closed) throw new IOException("The target terminal has closed.");
    }

    public void Observe(TerminalSnapshot snapshot)
    {
        lock (_gate) { _prompt = MacroRecorder.Prompt(snapshot); _generation++; }
    }

    public void Close() { lock (_gate) _closed = true; }

    public async Task RunAsync(TerminalMacro macro, Action<string> send, int timeoutSeconds,
        Action<int, bool> progress, CancellationToken cancellationToken,
        Func<int, MacroStep, Task<bool>>? recover = null,
        Func<MacroStep, Task<string?>>? requestInput = null,
        Func<MacroStep, string>? encodeKey = null)
    {
        macro.Validate();
        long sentAt;
        lock (_gate) sentAt = _generation;
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            await Gate(cancellationToken);
            progress(i + 1, true);
            var delayed = step.Wait == MacroWait.Delay || step.Wait == MacroWait.Legacy && macro.Mode == MacroMode.Timed;
            var waitForPrompt = step.Wait == MacroWait.Prompt || step.Wait == MacroWait.Legacy && macro.Mode == MacroMode.Interactive && i > 0;
            if (delayed)
            {
                double remaining = step.DelayMilliseconds;
                while (remaining > 0)
                {
                    await Gate(cancellationToken);
                    var tick = Stopwatch.StartNew();
                    await Task.Delay((int)Math.Min(25, Math.Ceiling(remaining)), cancellationToken);
                    remaining -= tick.Elapsed.TotalMilliseconds;
                }
            }
            async Task WaitForPrompt(bool requireFresh)
            {
                double elapsed = 0;
                while (true)
                {
                    await Gate(cancellationToken);
                    lock (_gate)
                    {
                        if (_closed) throw new IOException("The target terminal has closed.");
                        if ((!requireFresh || _generation > sentAt) && _prompt.TrimEnd().EndsWith(step.ExpectedPrompt.TrimEnd(), StringComparison.Ordinal)) break;
                    }
                    if (elapsed >= timeoutSeconds * 1000)
                    {
                        if (recover is null) throw new TimeoutException($"Step {i + 1}: prompt was not detected. Nothing further was sent.");
                        if (!await recover(i + 1, step)) throw new OperationCanceledException(cancellationToken);
                        if (string.IsNullOrWhiteSpace(step.ExpectedPrompt)) throw new InvalidDataException("A prompt is required.");
                        elapsed = 0;
                    }
                    var tick = Stopwatch.StartNew();
                    await Task.Delay(25, cancellationToken);
                    elapsed += tick.Elapsed.TotalMilliseconds;
                }
            }
            if (waitForPrompt) await WaitForPrompt(i > 0);
            var input = step.Kind == MacroStepKind.Key && encodeKey is not null ? encodeKey(step) : step.Input;
            if (step.AskAtRuntime)
            {
                if (requestInput is null) throw new InvalidOperationException("This macro needs a runtime input provider.");
                var answer = await requestInput(step);
                if (answer is null) throw new OperationCanceledException(cancellationToken);
                input = answer + step.SubmitSuffix;
                await Gate(cancellationToken);
                if (waitForPrompt) await WaitForPrompt(false);
            }
            await Gate(cancellationToken);
            if (waitForPrompt) await WaitForPrompt(false);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed) throw new IOException("The target terminal has closed.");
                sentAt = _generation;
            }
            send(input);
            if (_stepOnce) { _stepOnce = false; _paused = true; }
            progress(i + 1, false);
        }
    }
}
