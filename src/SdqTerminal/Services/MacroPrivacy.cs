using System.Text.Json;

namespace SdqTerminal;

public static class MacroPrivacy
{
    // Never mutate the editor's copy or serialize runtime answers back into it.
    public static TerminalMacro Prepare(TerminalMacro source, bool askSensitive)
    {
        var macro = JsonSerializer.Deserialize<TerminalMacro>(JsonSerializer.SerializeToUtf8Bytes(source))!;
        var secrets = new List<string>();
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            if (step.Group > 0 && step.Kind != MacroStepKind.Key)
            {
                var members = macro.Steps.Skip(i).TakeWhile(s => s.Group == step.Group && !MacroKeys.IsIndependent(s.KeyName)).ToArray();
                if (members.Any(s => s.AskAtRuntime || s.VariableName.Length > 0 || askSensitive && s.IsSensitive))
                {
                    step.AskAtRuntime = true;
                    if (members.Any(s => s.IsSensitive)) step.Sensitivity = MacroSensitivity.Sensitive;
                    if (step.VariableName.Length == 0) step.VariableName = members.FirstOrDefault(s => s.VariableName.Length > 0)?.VariableName ?? "";
                }
            }
            if (step.Kind == MacroStepKind.Key || !(step.AskAtRuntime || step.VariableName.Length > 0 || askSensitive && step.IsSensitive)) continue;
            var group = new List<MacroStep> { step };
            if (step.Group > 0)
            {
                while (i + 1 < macro.Steps.Count && macro.Steps[i + 1].Group == step.Group &&
                    !MacroKeys.IsIndependent(macro.Steps[i + 1].KeyName))
                { group.Add(macro.Steps[i + 1]); macro.Steps.RemoveAt(i + 1); }
            }
            foreach (var part in group)
            {
                var value = part.Input.TrimEnd('\r', '\n');
                if (part.Kind != MacroStepKind.Key && value.Length > 0) secrets.Add(value);
            }
            var last = group[^1].Input;
            if (!step.RuntimeSubmissionConfigured)
            {
                if (last.EndsWith("\r\n")) step.SubmitSuffix = "\r\n";
                else if (last.EndsWith('\r')) step.SubmitSuffix = "\r";
                else if (last.EndsWith('\n')) step.SubmitSuffix = "\n";
            }
            step.Input = ""; step.AskAtRuntime = true;
        }
        // Prompt matching must not persist echoed secrets either.
        foreach (var step in macro.Steps)
            foreach (var secret in secrets)
                if (step.ExpectedPrompt.Contains(secret, StringComparison.Ordinal))
                    step.ExpectedPrompt = ""; // Validation asks for a safe replacement instead of silently changing the condition.
        return macro;
    }
}
