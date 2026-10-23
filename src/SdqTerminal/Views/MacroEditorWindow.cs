using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SdqTerminal;

public sealed class MacroEditorWindow : Window
{
    public string Password { get; private set; } = "";
    public MacroEditorWindow(TerminalMacro macro, string initialPassword = "", bool askSensitive = false,
        IReadOnlyCollection<int>? occupiedShortcuts = null)
    {
        Title = "Edit macro"; Width = 900; Height = 760; MinWidth = 760; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(244,245,247)); Foreground = new SolidColorBrush(Color.FromRgb(24,32,51));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Macro", FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0,10,0,4) });
        var name = new TextBox { Text = macro.Name, MaxLength = 80 }; heading.Children.Add(name);
        var nameError = Error(); heading.Children.Add(nameError);
        heading.Children.Add(new TextBlock { Text = $"{macro.Mode} · Select a numbered step to review its input and condition.", Margin = new Thickness(0,8,0,10) });
        var security = new StackPanel();
        var protection = new Expander { Header = "Macro protection and shortcut", Content = security, Margin = new Thickness(0,0,0,10) };
        heading.Children.Add(protection);
        security.Children.Add(new TextBlock { Text = "Independent file password (optional)", Margin = new Thickness(0,6,0,4) });
        var password = new PasswordBox { Password = initialPassword }; security.Children.Add(password);
        security.Children.Add(new TextBlock { Text = "Confirm password", Margin = new Thickness(0,6,0,4) });
        var confirm = new PasswordBox { Password = initialPassword }; security.Children.Add(confirm);
        var passwordError = Error(); security.Children.Add(passwordError);
        security.Children.Add(new TextBlock { Text = "Ctrl+Shift+Alt + number", Margin = new Thickness(0,6,0,4) });
        var shortcut = new ComboBox { ItemsSource = new[] { "None", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, SelectedIndex = macro.Shortcut }; security.Children.Add(shortcut);
        var shortcutError = Error(); security.Children.Add(shortcutError);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var error = Error(); bottom.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) }; bottom.Children.Add(buttons);
        var save = Button("Save"); save.IsDefault = true; buttons.Children.Add(save);
        var cancel = Button("Cancel"); cancel.IsCancel = true; buttons.Children.Add(cancel);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) }); body.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0,0,14,0) }; body.Children.Add(left);
        var actions = new WrapPanel { Margin = new Thickness(0,8,0,0) }; DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch }; left.Children.Add(list);
        var details = new StackPanel { Margin = new Thickness(4,0,4,0) };
        var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        void Label(string text) => details.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0,8,0,4) });
        var stepTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold }; details.Children.Add(stepTitle);
        Label("Input (hidden until you choose to edit)");
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; details.Children.Add(summary);
        var reveal = Button("Reveal / edit input"); details.Children.Add(reveal);
        var input = new TextBox { AcceptsReturn = true, MinHeight = 70, MaxHeight = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed }; details.Children.Add(input);
        var key = new ComboBox { IsEditable = true, ItemsSource = new[] { "Ctrl+C", "Ctrl+D", "Ctrl+Z", "Enter", "Escape", "Tab", "Back", "Up", "Down", "Left", "Right", "F1", "F5", "F12" } }; details.Children.Add(key);
        Label("Run this step");
        var condition = new ComboBox { ItemsSource = new[] { "Original recording behavior", "Wait for prompt", "After a delay", "Immediately" } }; details.Children.Add(condition);
        Label("Expected prompt (logical terminal text)");
        var prompt = new TextBox(); details.Children.Add(prompt);
        Label("Delay in milliseconds");
        var delay = new TextBox(); details.Children.Add(delay);
        Label("Sensitivity");
        var sensitivity = new ComboBox { ItemsSource = Enum.GetValues<MacroSensitivity>() }; details.Children.Add(sensitivity);
        var reason = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,8) }; details.Children.Add(reason);
        var advanced = new StackPanel(); details.Children.Add(new Expander { Header = "Advanced: runtime input / variable", Content = advanced });
        var ask = new CheckBox { Content = "Ask for a value when this step runs", Margin = new Thickness(0,10,0,6) }; advanced.Children.Add(ask);
        advanced.Children.Add(new TextBlock { Text = "Optional variable / input label" });
        var variable = new TextBox { MaxLength = 80, Margin = new Thickness(0,4,0,6) }; advanced.Children.Add(variable);
        var submit = new CheckBox { Content = "Send Enter after the runtime answer" }; advanced.Children.Add(submit);
        advanced.Children.Add(new TextBlock { Text = "The whole input of this step becomes the variable. Other text is never interpreted as a template.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) });
        details.Children.Add(new TextBlock { Text = askSensitive ? "Sensitive values will be removed on save and requested when running." : "Sensitive values are stored encrypted. Enable asking at runtime to avoid storing this value.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,8) });
        MacroStep? selected = null; bool refreshing = false;
        bool Commit()
        {
            error.Text = "";
            if (selected is null) return true;
            if (!int.TryParse(delay.Text, out var milliseconds) || milliseconds is < 0 or > 86400000)
            { error.Text = "Delay must be a number between 0 and 86400000."; return false; }
            if (selected.Kind == MacroStepKind.Key && !MacroKeys.TryParse(key.Text, out _, out _))
            { error.Text = "Choose a valid terminal key combination."; return false; }
            if (input.Visibility == Visibility.Visible) selected.Input = input.Text;
            selected.Wait = (MacroWait)condition.SelectedIndex; selected.ExpectedPrompt = prompt.Text;
            selected.DelayMilliseconds = milliseconds; selected.Sensitivity = (MacroSensitivity)sensitivity.SelectedItem;
            selected.KeyName = selected.Kind == MacroStepKind.Key ? key.Text : "";
            selected.AskAtRuntime = selected.Kind != MacroStepKind.Key && ask.IsChecked == true;
            selected.VariableName = selected.Kind != MacroStepKind.Key ? variable.Text.Trim() : "";
            selected.SubmitSuffix = submit.IsChecked == true ? "\r" : "";
            selected.RuntimeSubmissionConfigured = true;
            return true;
        }
        void Display(int index)
        {
            selected = index >= 0 && index < macro.Steps.Count ? macro.Steps[index] : null;
            details.IsEnabled = selected is not null;
            if (selected is null) return;
            stepTitle.Text = $"Step {index + 1} · {(selected.Kind == MacroStepKind.Key ? "Key" : "Input")}"; summary.Text = selected.InputSummary;
            input.Clear(); input.Visibility = Visibility.Collapsed;
            reveal.Visibility = selected.Kind == MacroStepKind.Key ? Visibility.Collapsed : Visibility.Visible;
            key.Visibility = selected.Kind == MacroStepKind.Key ? Visibility.Visible : Visibility.Collapsed; key.Text = selected.KeyName;
            condition.SelectedIndex = (int)selected.Wait; prompt.Text = selected.ExpectedPrompt; delay.Text = selected.DelayMilliseconds.ToString();
            sensitivity.SelectedItem = selected.Sensitivity;
            reason.Text = selected.DetectionReason.Length > 0 ? "Automatic detection: " + selected.DetectionReason : "No automatic sensitivity signal. You can mark this input manually.";
            ask.IsChecked = selected.AskAtRuntime; variable.Text = selected.VariableName;
            submit.IsChecked = selected.RuntimeSubmissionConfigured ? selected.SubmitSuffix.Contains('\r') : selected.SubmitSuffix.Contains('\r') || selected.Input.EndsWith('\r');
            advanced.IsEnabled = sensitivity.IsEnabled = selected.Kind != MacroStepKind.Key;
        }
        void Refresh(int index)
        {
            refreshing = true;
            list.ItemsSource = macro.Steps.Select((s, i) => $"{i + 1}. {s.Description}").ToArray();
            list.SelectedIndex = Math.Clamp(index, -1, macro.Steps.Count - 1);
            refreshing = false; Display(list.SelectedIndex);
        }
        list.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            if (!Commit()) { refreshing = true; list.SelectedIndex = selected is null ? -1 : macro.Steps.IndexOf(selected); refreshing = false; return; }
            Refresh(list.SelectedIndex);
        };
        reveal.Click += (_, _) => { if (selected is null) return; input.Text = selected.Input; input.Visibility = Visibility.Visible; input.Focus(); };
        condition.SelectionChanged += (_, _) =>
        { prompt.IsEnabled = condition.SelectedIndex is 0 or 1; delay.IsEnabled = condition.SelectedIndex is 0 or 2; };
        foreach (var label in new[] { "+ Text", "+ Key", "Duplicate", "Delete", "↑", "↓" })
        {
            var button = Button(label); actions.Children.Add(button);
            button.Click += (_, _) =>
            {
                if (!Commit()) return;
                var index = list.SelectedIndex;
                if (label is "+ Text" or "+ Key") { macro.Steps.Add(new() { Kind = label == "+ Key" ? MacroStepKind.Key : MacroStepKind.Text, KeyName = label == "+ Key" ? "Enter" : "", Input = "\r", Wait = MacroWait.Immediate }); Refresh(macro.Steps.Count - 1); return; }
                if (index < 0) return;
                if (label == "Duplicate") { var copy = JsonSerializer.Deserialize<MacroStep>(JsonSerializer.Serialize(macro.Steps[index]))!; copy.Group = 0; macro.Steps.Insert(index + 1, copy); index++; }
                else if (label == "Delete") { macro.Steps.RemoveAt(index); index = Math.Min(index, macro.Steps.Count - 1); }
                else { var next = index + (label == "↑" ? -1 : 1); if (next >= 0 && next < macro.Steps.Count) { (macro.Steps[index], macro.Steps[next]) = (macro.Steps[next], macro.Steps[index]); index = next; } }
                selected = null; Refresh(index);
            };
        }
        save.Click += (_, _) =>
        {
            nameError.Text = passwordError.Text = shortcutError.Text = "";
            if (!Commit()) return;
            if (string.IsNullOrWhiteSpace(name.Text)) { nameError.Text = "Enter a name."; return; }
            if (password.Password != confirm.Password) { protection.IsExpanded = true; passwordError.Text = "Passwords do not match."; return; }
            if (shortcut.SelectedIndex > 0 && occupiedShortcuts?.Contains(shortcut.SelectedIndex) == true)
            { protection.IsExpanded = true; shortcutError.Text = "This shortcut belongs to another macro."; return; }
            macro.Name = name.Text.Trim(); macro.Shortcut = shortcut.SelectedIndex;
            try { MacroPrivacy.Prepare(macro, askSensitive).Validate(); }
            catch (Exception ex) { error.Text = ex.Message; return; }
            Password = password.Password; DialogResult = true;
        };
        Refresh(macro.Steps.Count > 0 ? 0 : -1);
    }

    private static TextBlock Error() => new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,3) };
    private static Button Button(string label) => new() { Content = label, Padding = new Thickness(10,5,10,5), Margin = new Thickness(0,0,6,4), MinWidth = 40 };
    public static string? Unlock(Window owner, string name) => MacroInteractionWindow.Request(owner, name + " — file password", true);
}
