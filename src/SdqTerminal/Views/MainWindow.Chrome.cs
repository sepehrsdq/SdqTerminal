using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SdqTerminal;

// macOS-style chrome: the RTL chip, the find bar, the command palette and the
// status bar live here so the window code keeps its original responsibilities.
public partial class MainWindow
{
    private enum RtlMode { Off, Smart, Row }

    private sealed record PaletteCommand(string Title, string Shortcut, Action Run);

    private List<PaletteCommand>? _paletteCommands;
    private List<PaletteCommand> _paletteResults = [];
    private DateTime _statusUpdatedAt;
    private DateTime _searchRefreshedAt;
    private string _statusDirectory = string.Empty;

    private void InitializeChrome()
    {
        UpdateRtlChip();
        SearchPlaceholder.Visibility = Visibility.Visible;
        UpdateStatusBar(force: true);
    }

    private RtlMode CurrentRtlMode =>
        RowRtlMenuItem.IsChecked ? RtlMode.Row
        : SmartRtlMenuItem.IsChecked ? RtlMode.Smart
        : RtlMode.Off;

    private void SetRtlMode(RtlMode mode)
    {
        SmartRtlMenuItem.IsChecked = mode == RtlMode.Smart;
        RowRtlMenuItem.IsChecked = mode == RtlMode.Row;
        RenderVisiblePanes();
        UpdateRtlChip();
        UpdateStatusBar(force: true);
        ActiveView.Focus();
    }

    private void UpdateRtlChip()
    {
        var mode = CurrentRtlMode;
        RtlChipText.Text = mode switch
        {
            RtlMode.Smart => "Smart RTL",
            RtlMode.Row => "Row RTL",
            _ => "RTL off"
        };
        RtlChipGlyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            mode == RtlMode.Off ? "MutedForegroundBrush" : "AccentBrush");
        RtlChipButton.ToolTip = mode switch
        {
            RtlMode.Smart => "Smart RTL: right-to-left only on lines that contain Persian or Arabic",
            RtlMode.Row => "Row RTL: the whole grid is right-to-left, including prompt glyphs",
            _ => "RTL layout is off: text keeps the terminal's own direction"
        };
    }

    private void RtlChipButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("DarkContextMenuStyle"),
            PlacementTarget = RtlChipButton,
            Placement = PlacementMode.Bottom
        };
        menu.Items.Add(CreateRtlModeItem("Smart RTL — per line", RtlMode.Smart));
        menu.Items.Add(CreateRtlModeItem("Row RTL — every line", RtlMode.Row));
        menu.Items.Add(CreateRtlModeItem("RTL off", RtlMode.Off));
        menu.Items.Add(new Separator());
        var options = new MenuItem { Header = "RTL options…" };
        options.Click += (_, _) => ShowSettings(openThemeTab: false);
        menu.Items.Add(options);
        menu.IsOpen = true;
    }

    private MenuItem CreateRtlModeItem(string header, RtlMode mode)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = CurrentRtlMode == mode
        };
        item.Click += (_, _) => SetRtlMode(mode);
        return item;
    }

    // ---------------------------------------------------------------- find bar

    private void SearchButton_Click(object sender, RoutedEventArgs e) => OpenSearch();
    private void SearchMenuItem_Click(object sender, RoutedEventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        if (PaletteOverlay.Visibility == Visibility.Visible)
            PaletteOverlay.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
        if (SearchBox.Text.Length > 0)
            RunSearch();
    }

    private void CloseSearch()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchStatusText.Text = string.Empty;
        ActiveView.ClearSearch();
        ActiveView.Focus();
    }

    private void SearchClose_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RunSearch();
    }

    private void RunSearch()
    {
        var query = SearchBox.Text;
        if (query.Length == 0)
        {
            ActiveView.ClearSearch();
            SearchStatusText.Text = string.Empty;
            SearchPreviousButton.IsEnabled = SearchNextButton.IsEnabled = false;
            return;
        }
        UpdateSearchStatus(ActiveView.Search(query));
    }

    private void UpdateSearchStatus(int count)
    {
        SearchStatusText.Text = count == 0 ? "No results" : $"{ActiveView.SearchMatchIndex}/{count}";
        SearchPreviousButton.IsEnabled = SearchNextButton.IsEnabled = count > 0;
    }

    private void StepSearch(int direction)
    {
        ActiveView.StepSearch(direction);
        UpdateSearchStatus(ActiveView.SearchMatchCount);
    }

    private void SearchNext_Click(object sender, RoutedEventArgs e) => StepSearch(1);
    private void SearchPrevious_Click(object sender, RoutedEventArgs e) => StepSearch(-1);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                StepSearch((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                break;
            case Key.Escape:
                e.Handled = true;
                CloseSearch();
                break;
        }
    }

    // Output continues to arrive while the bar is open; refresh the matches
    // without stealing the caret or jumping the scroll position.
    private void RefreshSearchIfOpen()
    {
        if (SearchPanel.Visibility != Visibility.Visible || SearchBox.Text.Length == 0)
            return;
        var now = DateTime.UtcNow;
        if ((now - _searchRefreshedAt).TotalMilliseconds < 350)
            return;
        _searchRefreshedAt = now;
        UpdateSearchStatus(ActiveView.RefreshSearch());
    }

    // ------------------------------------------------------------ status bar

    private void UpdateStatusBar(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _statusUpdatedAt).TotalMilliseconds < 350)
            return;
        _statusUpdatedAt = now;

        var pane = ActivePane;
        if (pane is null)
        {
            StatusProfileText.Text = "SdqTerminal";
            StatusDirectoryText.Text = string.Empty;
            StatusRightText.Text = string.Empty;
            return;
        }

        StatusProfileText.Text = pane.Profile switch
        {
            TerminalProfile.Wsl => pane.WslDistribution ?? "WSL",
            TerminalProfile.PowerShell => "PowerShell",
            _ => "Command Prompt"
        };
        // WSL's process directory is the Windows-side wsl.exe path, so only
        // Windows profiles can fall back to asking the shell process.
        var directory = TerminalDirectory.FromReport(
            pane.LastRenderedSnapshot?.WorkingDirectory, pane.WslDistribution)
            ?? (pane.Profile == TerminalProfile.Wsl ? null : pane.Session?.ReadWorkingDirectory())
            ?? string.Empty;
        if (!string.Equals(directory, _statusDirectory, StringComparison.Ordinal))
        {
            _statusDirectory = directory;
            StatusDirectoryText.Text = directory;
            StatusDirectoryText.ToolTip = directory.Length == 0 ? null : directory;
        }

        var panes = _activeTab?.Panes.Count ?? 1;
        var size = TerminalTextBox.FontSize;
        StatusRightText.Text = $"{size:0} pt · {panes} pane{(panes == 1 ? string.Empty : "s")}";
    }

    // -------------------------------------------------------- command palette

    private void PaletteMenuItem_Click(object sender, RoutedEventArgs e) => OpenPalette();

    private void OpenPalette()
    {
        if (SearchPanel.Visibility == Visibility.Visible)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            ActiveView.ClearSearch();
        }
        EnsurePaletteCommands();
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteSearchBox.Text = string.Empty;
        PalettePlaceholder.Visibility = Visibility.Visible;
        FilterPalette(string.Empty);
        PaletteSearchBox.Focus();
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        ActiveView.Focus();
    }

    private void PaletteOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PaletteOverlay))
        {
            e.Handled = true;
            ClosePalette();
        }
    }

    private void PaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PalettePlaceholder.Visibility = PaletteSearchBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FilterPalette(PaletteSearchBox.Text);
    }

    private void PaletteSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                ClosePalette();
                break;
            case Key.Enter:
                e.Handled = true;
                RunPaletteSelection();
                break;
            case Key.Down:
                e.Handled = true;
                MovePaletteSelection(1);
                break;
            case Key.Up:
                e.Handled = true;
                MovePaletteSelection(-1);
                break;
        }
    }

    private void MovePaletteSelection(int delta)
    {
        if (PaletteList.Items.Count == 0)
            return;
        var index = Math.Clamp(PaletteList.SelectedIndex + delta, 0, PaletteList.Items.Count - 1);
        PaletteList.SelectedIndex = index;
        PaletteList.ScrollIntoView(PaletteList.Items[index]);
    }

    private void RunPaletteSelection()
    {
        var index = PaletteList.SelectedIndex;
        if (index < 0 || index >= _paletteResults.Count)
            return;
        var action = _paletteResults[index].Run;
        PaletteOverlay.Visibility = Visibility.Collapsed;
        action();
    }

    private void PaletteList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PaletteList.SelectedIndex >= 0)
            RunPaletteSelection();
    }

    private void FilterPalette(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _paletteResults = (_paletteCommands ?? [])
            .Where(command => terms.All(term =>
                command.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                command.Shortcut.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PaletteList.Items.Clear();
        foreach (var command in _paletteResults)
            PaletteList.Items.Add(new ListBoxItem { Content = BuildPaletteRow(command) });
        if (PaletteList.Items.Count > 0)
            PaletteList.SelectedIndex = 0;
    }

    private UIElement BuildPaletteRow(PaletteCommand command)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = command.Title,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PaletteForegroundBrush");
        row.Children.Add(title);
        if (command.Shortcut.Length > 0)
        {
            var shortcut = new TextBlock
            {
                Text = command.Shortcut,
                FontSize = 11,
                Margin = new Thickness(16, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            shortcut.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundBrush");
            Grid.SetColumn(shortcut, 1);
            row.Children.Add(shortcut);
        }
        AutomationProperties.SetName(row, command.Title);
        return row;
    }

    private void EnsurePaletteCommands()
    {
        if (_paletteCommands is not null)
            return;

        _paletteCommands =
        [
            new("New terminal", "Ctrl+Shift+T", () => CreateTerminalTab(_defaultProfile)),
            new("New PowerShell terminal", "", () => CreateTerminalTab(TerminalProfile.PowerShell)),
            new("Split pane", "Ctrl+Shift+D", SplitActivePane),
            new("Close pane", "Ctrl+Shift+W", () => ClosePane(ActivePane)),
            new("Next pane", "", () => SelectRelativePane(1)),
            new("Previous pane", "", () => SelectRelativePane(-1)),
            new("Close tab", "", () => CloseTab(_activeTab)),
            new("Find in terminal", "Ctrl+F", OpenSearch),
            new("Settings", "", () => ShowSettings(openThemeTab: false)),
            new("Theme and colors", "", () => ShowSettings(openThemeTab: true)),
            new("Increase font size", "Ctrl+Plus", () => AdjustFontSize(1)),
            new("Decrease font size", "Ctrl+Minus", () => AdjustFontSize(-1)),
            new("Reset font size", "Ctrl+0", () => SetFontSize(DefaultFontSize)),
            new("Toggle light and dark appearance", "", () =>
            {
                DarkModeButton.IsChecked = !(DarkModeButton.IsChecked ?? false);
                DarkModeButton_Click(this, new RoutedEventArgs());
            }),
            new("RTL: Smart (per line)", "", () => SetRtlMode(RtlMode.Smart)),
            new("RTL: Row (every line)", "", () => SetRtlMode(RtlMode.Row)),
            new("RTL: off", "", () => SetRtlMode(RtlMode.Off)),
            new("Export session", "", () => ExportSessionMenuItem_Click(this, new RoutedEventArgs())),
            new("Open current directory in Explorer", "",
                () => OpenDirectoryMenuItem_Click(this, new RoutedEventArgs())),
            new("Preview in browser", "", () => HtmlPreviewButton_Click(this, new RoutedEventArgs())),
            new("Record macro", "", () => MacroRecord_Click(this, new RoutedEventArgs())),
            new("Manage macros", "", () => MacroManage_Click(this, new RoutedEventArgs())),
            new("Guide", "", () => GuideMenuItem_Click(this, new RoutedEventArgs())),
            new("Check for updates", "",
                () => CheckForUpdatesMenuItem_Click(this, new RoutedEventArgs())),
            new("About SdqTerminal", "", () => AboutMenuItem_Click(this, new RoutedEventArgs())),
            new("Exit", "", () => ExitMenuItem_Click(this, new RoutedEventArgs()))
        ];
    }

    // Window-level shortcuts. Returning false lets the key reach the shell.
    private bool HandleChromeShortcut(KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control && !shift && e.Key == Key.F)
        {
            OpenSearch();
            return true;
        }
        if (control && shift && e.Key == Key.P)
        {
            OpenPalette();
            return true;
        }
        if (e.Key == Key.Escape)
        {
            if (PaletteOverlay.Visibility == Visibility.Visible)
            {
                ClosePalette();
                return true;
            }
            if (SearchPanel.Visibility == Visibility.Visible)
            {
                CloseSearch();
                return true;
            }
        }
        return false;
    }
}
