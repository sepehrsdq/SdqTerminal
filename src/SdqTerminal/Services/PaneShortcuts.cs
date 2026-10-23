using System.Windows.Input;

namespace SdqTerminal;

public sealed record PaneShortcutSettings(bool Split = true, bool Navigate = true, bool Close = true);
public enum PaneShortcutAction { None, Split, Previous, Next, Close }

public static class PaneShortcuts
{
    public static PaneShortcutAction Match(Key key, ModifierKeys modifiers, PaneShortcutSettings settings)
    {
        if (modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return PaneShortcutAction.None;
        return key switch
        {
            Key.D when settings.Split => PaneShortcutAction.Split,
            Key.Left when settings.Navigate => PaneShortcutAction.Previous,
            Key.Right when settings.Navigate => PaneShortcutAction.Next,
            Key.W when settings.Close => PaneShortcutAction.Close,
            _ => PaneShortcutAction.None
        };
    }
}
