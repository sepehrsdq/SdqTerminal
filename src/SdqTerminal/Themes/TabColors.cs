using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SdqTerminal;

internal static class TabColors
{
    public static readonly (string Name, Color Color)[] Palette =
    [
        ("Red", Color.FromRgb(197, 15, 31)), ("Orange", Color.FromRgb(202, 80, 16)),
        ("Yellow", Color.FromRgb(249, 241, 165)), ("Green", Color.FromRgb(19, 161, 14)),
        ("Teal", Color.FromRgb(0, 153, 153)), ("Blue", Color.FromRgb(0, 120, 212)),
        ("Purple", Color.FromRgb(136, 23, 152)), ("Pink", Color.FromRgb(234, 0, 94)),
        ("Black", Colors.Black), ("Gray", Color.FromRgb(118, 118, 118)),
        ("Silver", Color.FromRgb(204, 204, 204)), ("White", Colors.White)
    ];

    public static Color TextColor(Color background)
    {
        static double Linear(byte value)
        {
            var s = value / 255.0;
            return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
        return (luminance + .05) / .05 >= 1.05 / (luminance + .05) ? Colors.Black : Colors.White;
    }

    // The Windows color dialog supplies full RGB selection and custom color slots.
    private static readonly int[] CustomColors = Enumerable.Repeat(0xFFFFFF, 16).ToArray();
    public static Color? ChooseCustom(Window owner, Color initial)
    {
        var colors = Marshal.AllocHGlobal(16 * sizeof(int));
        try
        {
            Marshal.Copy(CustomColors, 0, colors, 16);
            var dialog = new ChooseColorData
            {
                Size = Marshal.SizeOf<ChooseColorData>(), Owner = new WindowInteropHelper(owner).EnsureHandle(),
                Result = (uint)(initial.R | initial.G << 8 | initial.B << 16), CustomColors = colors,
                Flags = 0x1 | 0x2 // CC_RGBINIT | CC_FULLOPEN
            };
            if (!ChooseColor(ref dialog)) return null;
            Marshal.Copy(colors, CustomColors, 0, 16);
            return Color.FromRgb((byte)dialog.Result, (byte)(dialog.Result >> 8), (byte)(dialog.Result >> 16));
        }
        finally { Marshal.FreeHGlobal(colors); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public int Size;
        public IntPtr Owner, Instance;
        public uint Result;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CustomData, Hook, TemplateName;
    }
    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorData data);
}
