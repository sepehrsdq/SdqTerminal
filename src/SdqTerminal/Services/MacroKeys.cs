using System.Windows.Input;

namespace SdqTerminal;

public static class MacroKeys
{
    public static string Name(Key key, ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") +
        (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") +
        (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (key == Key.Back ? "Backspace" : key == Key.Enter ? "Enter" : key.ToString());

    public static bool IsIndependent(string name) => TryParse(name, out var key, out var modifiers) &&
        (key == Key.Escape || modifiers.HasFlag(ModifierKeys.Control) && key is Key.C or Key.D or Key.Z);

    public static bool TryParse(string name, out Key key, out ModifierKeys modifiers)
    {
        modifiers = ModifierKeys.None; key = Key.None;
        var parts = name.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch { "ctrl" => ModifierKeys.Control, "shift" => ModifierKeys.Shift, "alt" => ModifierKeys.Alt, _ => ModifierKeys.None };
            if (modifier == ModifierKeys.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        return Enum.TryParse(parts[^1].Equals("Backspace", StringComparison.OrdinalIgnoreCase) ? "Back" : parts[^1], true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    public static bool ShouldRecord(string name, MacroSettings settings)
    {
        if (!TryParse(name, out var key, out var modifiers)) return true;
        if (settings.ExcludedKeys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(excluded => TryParse(excluded, out var excludedKey, out var excludedModifiers) && key == excludedKey && modifiers == excludedModifiers)) return false;
        if (key == Key.Enter) return true;
        if (modifiers.HasFlag(ModifierKeys.Control)) return settings.RecordControlKeys;
        if (key >= Key.F1 && key <= Key.F12) return settings.RecordFunctionKeys;
        return settings.RecordNavigationKeys;
    }
}
