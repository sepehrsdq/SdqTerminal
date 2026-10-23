using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SdqTerminal;

public static class MacroInteractionWindow
{
    private static (Window Window, StackPanel Panel) Create(Window owner, string title)
    {
        var window = new Window { Owner = owner, Title = title, Width = 480, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(244,245,247)), Foreground = new SolidColorBrush(Color.FromRgb(24,32,51)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        return (window, panel);
    }

    public static string? Request(Window owner, string label, bool sensitive)
    {
        var (window, panel) = Create(owner, sensitive ? "Sensitive input" : "Macro input");
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Used only for this run. Cancel stops the macro.", Margin = new Thickness(0,6,0,12), TextWrapping = TextWrapping.Wrap });
        var secret = new PasswordBox(); var text = new TextBox();
        Control field = sensitive ? secret : text; panel.Children.Add(field);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var ok = new Button { Content = "Continue", MinWidth = 90, Padding = new Thickness(8,5,8,5), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Padding = new Thickness(8,5,8,5), Margin = new Thickness(8,0,0,0), IsCancel = true };
        row.Children.Add(ok); row.Children.Add(cancel); panel.Children.Add(row);
        ok.Click += (_, _) => window.DialogResult = true; window.Loaded += (_, _) => field.Focus();
        var accepted = window.ShowDialog() == true;
        var value = sensitive ? secret.Password : text.Text;
        secret.Clear(); text.Clear();
        return accepted ? value : null;
    }

    public static string? Recover(Window owner, int number, string prompt)
    {
        var (window, panel) = Create(owner, $"Step {number} needs review");
        panel.Children.Add(new TextBlock { Text = "The expected prompt did not arrive. No further input was sent. The server may still be running.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Expected prompt (edit for this run only)", Margin = new Thickness(0,14,0,5) });
        var field = new TextBox { Text = prompt }; panel.Children.Add(field);
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0,5,0,0) }; panel.Children.Add(error);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var retry = new Button { Content = "Wait again", Padding = new Thickness(12,6,12,6), IsDefault = true };
        var stop = new Button { Content = "End run", Padding = new Thickness(12,6,12,6), Margin = new Thickness(8,0,0,0), IsCancel = true };
        row.Children.Add(retry); row.Children.Add(stop); panel.Children.Add(row);
        retry.Click += (_, _) => { if (string.IsNullOrWhiteSpace(field.Text)) error.Text = "Enter a prompt to wait for."; else window.DialogResult = true; };
        return window.ShowDialog() == true ? field.Text : null;
    }
}
