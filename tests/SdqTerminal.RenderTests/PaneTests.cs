using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SdqTerminal;

internal static partial class Program
{
    private static void CheckPanes()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var window = new MainWindow();
        var panesType = typeof(MainWindow).GetNestedType("TerminalPane", BindingFlags.NonPublic)!;
        var tabType = typeof(MainWindow).GetNestedType("TerminalTab", BindingFlags.NonPublic)!;
        var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
        var tabs = (IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethods(flags | BindingFlags.DeclaredOnly).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(window, args);
        object? Get(object owner, string property) => owner.GetType().GetProperty(property)!.GetValue(owner);
        void Set(object owner, string property, object? value) => owner.GetType().GetProperty(property)!.SetValue(owner, value);
        Call("ApplyFontSettings",new TerminalFontSettings("Consolas",18,false,false));
        object NewTab()
        {
            var pane = Call("CreatePane", Enum.ToObject(profileType, 0))!;
            var tab = Activator.CreateInstance(tabType, pane)!;
            tabs.Add(tab); return tab;
        }
        var root = (FrameworkElement)window.Content;
        void Layout(double width)
        {
            for (var i = 0; i < 3; i++)
            {
                root.Measure(new Size(width, 620)); root.Arrange(new Rect(0, 0, width, 620)); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
        }
        var first = NewTab(); Call("ShowTab", first); Layout(980);
        var second = NewTab(); Call("ShowTab", second); Layout(980);
        var firstPane = Get(first, "ActivePane")!;
        var secondPane = Get(second, "ActivePane")!;
        var firstView = (TerminalView)Get(firstPane, "View")!;
        var secondView = (TerminalView)Get(secondPane, "View")!;
        var buffer = new TerminalBuffer(80, 16);
        var snapshot = buffer.Process("\x1b[?25loutput retained متن فارسی");
        Set(firstPane, "Buffer", buffer); Call("Render", firstPane, snapshot);
        firstView.SelectAll(); var selected = firstView.GetSelectedText();
        if (!(bool)Call("TryJoinTab", first, false)!) throw new Exception("Two panes should fit in a normal window");
        Layout(980);
        if (!((MenuItem)window.FindName("MovePaneMenuItem")).IsEnabled) throw new Exception("Move pane command stayed disabled after a split");
        var joinedBitmap = new RenderTargetBitmap(980,620,96,96,PixelFormats.Pbgra32); joinedBitmap.Render(root);
        var joinedPng = new PngBitmapEncoder(); joinedPng.Frames.Add(BitmapFrame.Create(joinedBitmap));
        using(var stream = File.Create("artifacts/render-checks/joined-panes.png")) joinedPng.Save(stream);
        if (tabs.Count != 1 || ((IList)Get(second, "Panes")!).Count != 2 || !ReferenceEquals(Get(firstPane,"Buffer"),buffer))
            throw new Exception("Join lost the original pane or buffer");
        if (firstView.GetSelectedText() != selected || !ReferenceEquals(((Border)Get(firstPane,"Frame")!).Child,firstView))
            throw new Exception("Join lost selection or view ownership");
        var third = NewTab();
        var thirdPane = Get(third,"ActivePane")!;
        var count = ((IList)Get(second,"Panes")!).Count;
        Layout(480);
        if ((bool)Call("TryJoinTab",third,false)!) throw new Exception("An oversized join was accepted");
        if (tabs.Count != 2 || ((IList)Get(second,"Panes")!).Count != count || !ReferenceEquals(Get(third,"ActivePane"),thirdPane))
            throw new Exception("A rejected join changed tabs or sessions");
        var scroll = (ScrollViewer)window.FindName("PaneScroller");
        if (scroll.ScrollableWidth <= 0) throw new Exception("Narrow-window horizontal fallback is unavailable");
        scroll.ScrollToRightEnd(); Layout(480);
        if (scroll.HorizontalOffset <= 0) throw new Exception("Overflowed panes cannot be reached");
        Layout(980);
        Call("ActivatePane",firstPane);
        Call("MovePaneToNewTab",firstPane); Layout(980);
        if (tabs.Count != 3 || ((IList)Get(second,"Panes")!).Count != 1 || !ReferenceEquals(Get(firstPane,"View"),firstView))
            throw new Exception("Moving a joined pane to its own tab lost state");
        // Reusing the original XAML view after closing it must detach its old Border.
        var originalTab = tabs.Cast<object>().Single(t => ((IList)Get(t,"Panes")!).Cast<object>().Any(p => ReferenceEquals(Get(p,"View"),window.FindName("TerminalTextBox"))));
        Call("CloseTab",originalTab);
        var reused = NewTab(); Call("ShowTab",reused); Layout(980);
        if (!ReferenceEquals(Get(Get(reused,"ActivePane")!,"View"),window.FindName("TerminalTextBox")))
            throw new Exception("Template view was not safely reused");
        var modifiers = ModifierKeys.Control | ModifierKeys.Shift;
        var enabled = new PaneShortcutSettings();
        if (PaneShortcuts.Match(Key.W,ModifierKeys.Control,enabled) != PaneShortcutAction.None ||
            PaneShortcuts.Match(Key.Right,ModifierKeys.Control | ModifierKeys.Alt,enabled) != PaneShortcutAction.None ||
            PaneShortcuts.Match(Key.W,modifiers,enabled) != PaneShortcutAction.Close ||
            PaneShortcuts.Match(Key.D,modifiers,enabled) != PaneShortcutAction.Split ||
            PaneShortcuts.Match(Key.Left,modifiers,enabled) != PaneShortcutAction.Previous ||
            PaneShortcuts.Match(Key.Right,modifiers,enabled) != PaneShortcutAction.Next)
            throw new Exception("Pane shortcuts consume the wrong keys");
        foreach (var key in new[] {Key.W,Key.D,Key.Left,Key.Right})
            if (PaneShortcuts.Match(key,modifiers,new(false,false,false)) != PaneShortcutAction.None)
                throw new Exception("Disabled pane shortcut was still consumed");
        if (PaneShortcuts.Match(Key.W,modifiers,enabled with {Close=false}) != PaneShortcutAction.None ||
            PaneShortcuts.Match(Key.D,modifiers,enabled with {Split=false}) != PaneShortcutAction.None ||
            PaneShortcuts.Match(Key.Left,modifiers,enabled with {Navigate=false}) != PaneShortcutAction.None)
            throw new Exception("Individual shortcut toggles do not work");
        var settings = new FontSettingsWindow("Consolas",14,FontWeights.Normal,FontStyles.Normal,2000,HtmlPreviewScope.CurrentPage,new(false,true,false));
        if (((CheckBox)settings.FindName("SplitShortcutCheckBox")).IsChecked != false ||
            ((CheckBox)settings.FindName("NavigateShortcutCheckBox")).IsChecked != true ||
            ((CheckBox)settings.FindName("CloseShortcutCheckBox")).IsChecked != false)
            throw new Exception("Settings do not reflect shortcut preferences");
        var settingsRoot = (System.Windows.Controls.Panel)settings.Content;
        settingsRoot.Background = settings.Background;
        settingsRoot.Measure(new Size(620,700)); settingsRoot.Arrange(new Rect(0,0,620,700)); settingsRoot.UpdateLayout();
        var bitmap = new RenderTargetBitmap(620,700,96,96,PixelFormats.Pbgra32); bitmap.Render(settingsRoot);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create("artifacts/render-checks/pane-settings.png")) png.Save(stream);
        settings.Close(); window.Close();
        Console.WriteLine("PASS pane joins preserve views/buffers/selection, oversized joins are atomic, narrow windows scroll, detach/reuse and exact configurable shortcuts");
    }
}
