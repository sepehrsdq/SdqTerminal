using System.Windows;
using System.Windows.Media;

namespace SdqTerminal;

public enum AppThemeKind
{
    Dark,
    Light
}

public static class AppTheme
{
    private static readonly TerminalColor[] LightForegrounds =
    [
        new(36, 41, 47),
        new(207, 34, 46),
        new(17, 99, 41),
        new(125, 78, 0),
        new(5, 80, 174),
        new(130, 80, 223),
        new(27, 124, 131),
        new(87, 96, 106),
        new(110, 119, 129),
        new(164, 14, 38),
        new(26, 127, 55),
        new(99, 60, 1),
        new(9, 105, 218),
        new(130, 80, 223),
        new(49, 146, 170),
        new(36, 41, 47)
    ];

    // Applications pick colors assuming a dark screen, so explicit
    // backgrounds become light tints that keep the remapped text readable.
    private static readonly TerminalColor[] LightBackgrounds =
    [
        new(246, 248, 250),
        new(255, 235, 233),
        new(218, 251, 225),
        new(255, 248, 197),
        new(221, 244, 255),
        new(251, 239, 255),
        new(216, 243, 246),
        new(208, 215, 222),
        new(208, 215, 222),
        new(255, 206, 203),
        new(172, 238, 187),
        new(255, 236, 153),
        new(182, 227, 255),
        new(236, 215, 255),
        new(179, 233, 239),
        new(234, 238, 242)
    ];

    public static AppThemeKind Current { get; private set; } = AppThemeKind.Dark;

    // The selected color scheme, or null for the built-in colors of the theme.
    public static ColorScheme? Scheme { get; private set; }

    public static bool IsLight => Current == AppThemeKind.Light;

    public static TerminalColor DefaultForeground =>
        Scheme?.DefaultForeground ?? (IsLight ? new(36, 41, 47) : new(230, 230, 230));

    public static TerminalColor DefaultBackground =>
        Scheme?.DefaultBackground ?? (IsLight ? new(255, 255, 255) : new(12, 12, 12));

    public static TerminalColor MapForeground(TerminalColor color, int? paletteIndex = null) =>
        paletteIndex is not (>= 0 and < 16) ? color
        : Scheme is not null ? Scheme.Text(paletteIndex.Value)
        : IsLight ? LightForegrounds[paletteIndex.Value]
        : color;

    public static TerminalColor MapBackground(TerminalColor color, int? paletteIndex = null) =>
        paletteIndex is not (>= 0 and < 16) ? color
        : Scheme is not null ? Scheme.Fill(paletteIndex.Value)
        : IsLight ? LightBackgrounds[paletteIndex.Value]
        : color;

    internal static TerminalColor DefaultAnsiForeground(AppThemeKind theme, int index) =>
        theme == AppThemeKind.Light ? LightForegrounds[index] : ColorScheme.Find("Campbell")!.Text(index);

    public static Color TabAccent => IsLight ? Rgb(12, 96, 92) : Rgb(120, 220, 202);
    public static Color TabIcon => IsLight ? Rgb(108, 117, 128) : Rgb(148, 156, 166);
    public static Color ActiveTabText => IsLight ? Rgb(24, 28, 33) : Rgb(244, 246, 249);
    public static Color InactiveTabText => IsLight ? Rgb(96, 105, 116) : Rgb(160, 168, 178);
    public static Color ActiveTab => IsLight ? Rgb(255, 255, 255) : Rgb(58, 59, 63);
    public static Color HoverTab => IsLight ? Rgb(235, 238, 241) : Rgb(46, 47, 51);
    public static Color TabSeparator => IsLight ? Rgb(205, 210, 216) : Rgb(58, 59, 63);
    public static Color TabBorder => IsLight ? Rgb(216, 220, 226) : Rgb(70, 71, 76);
    public static Color SearchHighlight => IsLight ? Color.FromArgb(70, 255, 176, 0) : Color.FromArgb(58, 255, 214, 10);
    public static Color SearchCurrent => IsLight ? Color.FromArgb(140, 255, 176, 0) : Color.FromArgb(120, 255, 214, 10);
    public static Color CursorFill => IsLight ? Color.FromArgb(90, 36, 41, 47) : Color.FromArgb(90, 230, 230, 230);
    public static Color ScrollTrack => IsLight ? Color.FromArgb(10, 0, 0, 0) : Color.FromArgb(14, 255, 255, 255);
    public static Color ScrollThumb => IsLight ? Color.FromArgb(105, 0, 0, 0) : Color.FromArgb(100, 255, 255, 255);
    public static Color ScrollThumbHover => IsLight ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(160, 255, 255, 255);
    public static Color ScrollThumbPressed => IsLight ? Color.FromArgb(185, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
    public static Color CursorBorder => IsLight ? Rgb(110, 119, 129) : Colors.LightGray;

    // A scheme switches to its own dark or light theme.
    public static void Apply(AppThemeKind theme, ColorScheme? scheme = null)
    {
        Current = scheme?.Theme ?? theme;
        Scheme = scheme;
        theme = Current;
        TerminalBuffer.DefaultForeground = DefaultForeground;
        TerminalBuffer.DefaultBackground = DefaultBackground;
        var light = theme == AppThemeKind.Light;
        var resources = Application.Current.Resources;

        void Set(string key, byte r, byte g, byte b, byte lr, byte lg, byte lb)
        {
            resources[key] = Frozen(light ? Rgb(lr, lg, lb) : Rgb(r, g, b));
        }

        Set("TerminalBackgroundBrush", 12, 12, 12, 255, 255, 255);
        Set("TerminalForegroundBrush", 230, 230, 230, 36, 41, 47);
        Set("TitleBarBrush", 34, 35, 38, 238, 240, 243);
        Set("ToolbarBrush", 34, 35, 38, 238, 240, 243);
        Set("ToolbarBorderBrush", 62, 64, 68, 205, 210, 215);
        Set("HeaderSeparatorBrush", 50, 51, 55, 214, 218, 224);
        Set("ChipBackgroundBrush", 52, 53, 57, 236, 239, 243);
        Set("ChipBorderBrush", 72, 73, 78, 208, 214, 221);
        Set("StatusBarBrush", 28, 29, 32, 244, 246, 249);
        Set("StatusBarForegroundBrush", 168, 176, 186, 78, 86, 96);
        Set("SearchPanelBrush", 44, 45, 49, 255, 255, 255);
        Set("SearchPanelBorderBrush", 70, 71, 76, 205, 211, 218);
        Set("PaletteBackgroundBrush", 42, 43, 47, 255, 255, 255);
        Set("PaletteListBrush", 36, 37, 40, 250, 251, 253);
        Set("PaletteSelectionBrush", 60, 61, 66, 232, 236, 241);
        Set("PaletteForegroundBrush", 234, 238, 243, 30, 34, 40);
        Set("ChromeForegroundBrush", 240, 240, 240, 31, 35, 40);
        Set("ChromeStrongForegroundBrush", 255, 255, 255, 0, 0, 0);
        Set("MutedForegroundBrush", 139, 148, 158, 87, 96, 106);
        Set("AccentBrush", 114, 214, 197, 15, 118, 110);
        Set("FocusBrush", 168, 199, 250, 11, 87, 208);
        Set("MenuBackgroundBrush", 37, 37, 38, 255, 255, 255);
        Set("MenuBorderBrush", 63, 63, 70, 208, 215, 222);
        Set("MenuHighlightBrush", 58, 58, 58, 234, 238, 242);
        Set("MenuOpenBrush", 65, 65, 65, 220, 225, 230);
        Set("SeparatorBrush", 69, 70, 74, 216, 222, 228);
        Set("CaptionHoverBrush", 64, 64, 64, 205, 210, 216);
        Set("CaptionPressedBrush", 80, 80, 80, 190, 196, 203);
        Set("ButtonPressedBrush", 70, 70, 70, 208, 215, 222);
        Set("RoundButtonHoverBrush", 64, 65, 69, 205, 210, 216);
        Set("RoundButtonPressedBrush", 81, 82, 86, 190, 196, 203);
        Set("RoundButtonFocusBrush", 69, 79, 96, 196, 214, 240);

        if (scheme is not null)
        {
            resources["TerminalBackgroundBrush"] = Frozen(ToColor(scheme.DefaultBackground));
            resources["TerminalForegroundBrush"] = Frozen(ToColor(scheme.DefaultForeground));
        }

        resources["ScrimBrush"] = Frozen(Color.FromArgb(126, 0, 0, 0));
        resources["SearchHighlightBrush"] = Frozen(SearchHighlight);
        resources["SearchCurrentBrush"] = Frozen(SearchCurrent);

        // Menus in the window share the terminal scrollbar style.
        resources["ScrollTrackBrush"] = Frozen(ScrollTrack);
        resources["ScrollThumbBrush"] = Frozen(ScrollThumb);
        resources["ScrollThumbHoverBrush"] = Frozen(ScrollThumbHover);
        resources["ScrollThumbPressedBrush"] = Frozen(ScrollThumbPressed);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ToColor(TerminalColor color) =>
        Color.FromRgb(color.Red, color.Green, color.Blue);

    private static Color Rgb(byte red, byte green, byte blue) =>
        Color.FromRgb(red, green, blue);
}
