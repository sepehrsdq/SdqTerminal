using Microsoft.Win32;

namespace SdqTerminal;

public sealed record MacroSettings(MacroMode Mode = MacroMode.Interactive, int TimeoutSeconds = 30, bool Shortcuts = true)
{
    public bool RecordControlKeys { get; init; } = true;
    public bool RecordNavigationKeys { get; init; } = true;
    public bool RecordFunctionKeys { get; init; } = true;
    public bool DetectSensitive { get; init; } = true;
    public bool AskSensitive { get; init; }
    public bool PlaybackShortcuts { get; init; } = true;
    public string ExcludedKeys { get; init; } = "";
    public static MacroSettings Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\SdqTerminal\Macros");
        bool Enabled(string name, bool fallback = true) => key?.GetValue(name) is int value ? value != 0 : fallback;
        return new(key?.GetValue("Mode") is int mode && mode == 1 ? MacroMode.Timed : MacroMode.Interactive,
            Math.Clamp(key?.GetValue("Timeout") is int timeout ? timeout : 30, 5, 600),
            key?.GetValue("Shortcuts") is not int shortcuts || shortcuts != 0)
        {
            RecordControlKeys = Enabled("RecordControlKeys"), RecordNavigationKeys = Enabled("RecordNavigationKeys"),
            RecordFunctionKeys = Enabled("RecordFunctionKeys"), DetectSensitive = Enabled("DetectSensitive"),
            AskSensitive = Enabled("AskSensitive", false), PlaybackShortcuts = Enabled("PlaybackShortcuts"),
            ExcludedKeys = key?.GetValue("ExcludedKeys") as string ?? ""
        };
    }
    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\SdqTerminal\Macros");
        key.SetValue("Mode", (int)Mode); key.SetValue("Timeout", TimeoutSeconds); key.SetValue("Shortcuts", Shortcuts ? 1 : 0);
        key.SetValue("RecordControlKeys", RecordControlKeys ? 1 : 0);
        key.SetValue("RecordNavigationKeys", RecordNavigationKeys ? 1 : 0);
        key.SetValue("RecordFunctionKeys", RecordFunctionKeys ? 1 : 0);
        key.SetValue("DetectSensitive", DetectSensitive ? 1 : 0);
        key.SetValue("AskSensitive", AskSensitive ? 1 : 0);
        key.SetValue("PlaybackShortcuts", PlaybackShortcuts ? 1 : 0);
        key.SetValue("ExcludedKeys", ExcludedKeys);
    }
}
