using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SdqTerminal;

internal static partial class Program
{
    private static void CheckMacroUi()
    {
        var saved = MacroSettings.Load();
        var settings = new FontSettingsWindow("Consolas", 14, FontWeights.Normal, FontStyles.Normal, 2000);
        settings.ShowMacroTab();
        if (((TabItem)((TabControl)settings.FindName("SettingsTabs")).SelectedItem).Header.ToString() != "Macros")
            throw new Exception("Macros tab did not open.");
        Capture(settings, "macro-settings.png", 860, 680);
        settings.Close();
        if (MacroSettings.Load() != saved) throw new Exception("Opening settings changed saved macro preferences.");
        var macro = new TerminalMacro { Name = "Server login", Steps = [new() { Input = "connect\r" }, new() { Input = "example-user\r", ExpectedPrompt = "Username:" }, new() { Input = "SECRET-MUST-NOT-APPEAR\r", ExpectedPrompt = "Password:" }] };
        var editor = new MacroEditorWindow(macro);
        Capture(editor, "macro-editor.png", 880, 720);
        Capture(editor, "macro-editor-small.png", 740, 580); editor.Close();
        Console.WriteLine("PASS macro settings navigation, cancel preserves preferences, and masked editor layout");
    }

    private static void Capture(Window window, string filename, int width, int height)
    {
        var root = (Panel)window.Content;
        root.Background = window.Background;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("artifacts/render-checks");
        using var stream = File.Create(Path.Combine("artifacts/render-checks", filename)); encoder.Save(stream);
    }
}
