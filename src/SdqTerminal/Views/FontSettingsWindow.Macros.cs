using System.Windows;
using System.Windows.Controls;

namespace SdqTerminal;

public partial class FontSettingsWindow
{
    private ComboBox _macroMode = null!;
    private TextBox _macroTimeout = null!;
    private CheckBox _macroShortcuts = null!;
    private TabItem _macroTab = null!;
    private CheckBox _controlKeys = null!, _navigationKeys = null!, _functionKeys = null!, _detectSensitive = null!, _askSensitive = null!, _playbackShortcuts = null!;
    private TextBox _excludedKeys = null!;
    private TextBlock _macroError = null!;
    public MacroSettings SelectedMacroSettings => new((MacroMode)_macroMode.SelectedIndex,
        int.TryParse(_macroTimeout.Text, out var timeout) ? Math.Clamp(timeout, 5, 600) : 30,
        _macroShortcuts.IsChecked == true)
    {
        RecordControlKeys = _controlKeys.IsChecked == true, RecordNavigationKeys = _navigationKeys.IsChecked == true,
        RecordFunctionKeys = _functionKeys.IsChecked == true, DetectSensitive = _detectSensitive.IsChecked == true,
        AskSensitive = _askSensitive.IsChecked == true, PlaybackShortcuts = _playbackShortcuts.IsChecked == true,
        ExcludedKeys = _excludedKeys.Text.Trim()
    };

    private void InitializeMacroSettings()
    {
        var settings = MacroSettings.Load();
        var panel = new StackPanel { Margin = new Thickness(16) };
        _macroTab = new TabItem { Header = "Macros", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; SettingsTabs.Items.Add(_macroTab);
        panel.Children.Add(new TextBlock { Text = "Default recording mode", Margin = new Thickness(0,0,0,6) });
        _macroMode = new ComboBox { ItemsSource = new[] { "Interactive — wait for terminal prompts", "Timed — reproduce recorded delays" }, SelectedIndex = (int)settings.Mode }; panel.Children.Add(_macroMode);
        panel.Children.Add(new TextBlock { Text = "Prompt timeout in seconds (5–600)", Margin = new Thickness(0,16,0,6) });
        _macroTimeout = new TextBox { Text = settings.TimeoutSeconds.ToString() }; panel.Children.Add(_macroTimeout);
        _macroShortcuts = new CheckBox { Content = "Enable macro shortcuts (Ctrl+Shift+Alt+1–9)", IsChecked = settings.Shortcuts, Margin = new Thickness(0,16,0,16) }; panel.Children.Add(_macroShortcuts);
        CheckBox Option(string label, bool value)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0,6,0,6) };
            panel.Children.Add(box); return box;
        }
        _playbackShortcuts = Option("Playback shortcuts: Ctrl+Shift+Alt+P (pause), Ctrl+Shift+Alt+End (stop)", settings.PlaybackShortcuts);
        panel.Children.Add(new TextBlock { Text = "Record terminal keys", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,12,0,4) });
        _controlKeys = Option("Control combinations, including Ctrl+C / Ctrl+D / Ctrl+Z", settings.RecordControlKeys);
        _navigationKeys = Option("Navigation and editing keys (arrows, Tab, Escape, Backspace…)", settings.RecordNavigationKeys);
        _functionKeys = Option("Function keys F1–F12", settings.RecordFunctionKeys);
        panel.Children.Add(new TextBlock { Text = "Exclude combinations (separate with ;), for example: Ctrl+L; F5", Margin = new Thickness(0,8,0,4) });
        _excludedKeys = new TextBox { Text = settings.ExcludedKeys }; panel.Children.Add(_excludedKeys);
        _macroError = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(_macroError);
        panel.Children.Add(new TextBlock { Text = "Sensitive inputs", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,4) });
        _detectSensitive = Option("Detect password prompts, masked input and unconfirmed input echo", settings.DetectSensitive);
        _askSensitive = Option("Do not store sensitive values; ask when running the macro", settings.AskSensitive);
        panel.Children.Add(new TextBlock { Text = "Applies when saving. Existing stored values are removed when you open and save that macro again. Detection is conservative, not guaranteed; review each step's sensitivity.\n\nFiles are encrypted for your Windows account. An independent macro password adds protection from other apps using that account. Runtime answers are never saved.\n\nInteractive text waits for prompts; independent keys use their recorded delay. The editor lets you change either condition. A missing prompt pauses for review without repeating the previous command.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
    }
    public void ShowMacroTab() => SettingsTabs.SelectedItem = _macroTab;
    private bool ValidateMacroSettings()
    {
        _macroError.Text = "";
        if (!int.TryParse(_macroTimeout.Text, out var timeout) || timeout is < 5 or > 600)
            _macroError.Text = "Prompt timeout must be between 5 and 600 seconds.";
        else if (_excludedKeys.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(k => !MacroKeys.TryParse(k, out _, out _)))
            _macroError.Text = "Use key names such as Ctrl+L; Shift+Tab; F5. Separate combinations with semicolons.";
        if (_macroError.Text.Length == 0) return true;
        ShowMacroTab(); return false;
    }
}
