using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace FundoVivo.Settings;

/// <summary>
/// Light and dark palettes for the settings window. The brushes live in the application resources and every style
/// uses them as DynamicResource, so switching themes repaints the open window without reopening it.
/// </summary>
public static class ThemeManager
{
    // The look of his Windows pack (the ilha pill and its Control Center menu, the Relógio Mond): black, blocks and
    // controls as translucent white over it, text in white at full, 58% and 35%, and the system colours for state.
    // The monitor block is that same black-pill tile in both themes: it is where the wallpapers are shown.
    static readonly Dictionary<string, string> Stage = new()
    {
        ["Stage"] = "#1C1C1E", ["StageLine"] = "#2C2C2E", ["StageInk"] = "#FFFFFF", ["StageInk2"] = "#94FFFFFF",
    };

    static readonly Dictionary<string, string> Light = new()
    {
        ["Paper"] = "#F2F2F7", ["Surface"] = "#FFFFFF", ["Well"] = "#E5E5EA", ["Line"] = "#D1D1D6",
        ["Ink"] = "#000000", ["Ink2"] = "#6C6C70", ["Ink3"] = "#A1A1A6", ["OnInk"] = "#FFFFFF",
        ["Hover"] = "#0F000000", ["KnobOff"] = "#FFFFFF", ["KnobOn"] = "#FFFFFF",
        ["Playing"] = "#28A745", ["Paused"] = "#E08600", ["Problem"] = "#E5372D",
    };

    static readonly Dictionary<string, string> Dark = new()
    {
        ["Paper"] = "#000000", ["Surface"] = "#1C1C1E", ["Well"] = "#2C2C2E", ["Line"] = "#38383A",
        ["Ink"] = "#FFFFFF", ["Ink2"] = "#94FFFFFF", ["Ink3"] = "#59FFFFFF", ["OnInk"] = "#000000",
        ["Hover"] = "#10FFFFFF", ["KnobOff"] = "#FFFFFF", ["KnobOn"] = "#000000",
        ["Playing"] = "#30D158", ["Paused"] = "#FF9F0A", ["Problem"] = "#FF453A",
    };

    public static bool IsDark { get; private set; }
    public static event Action Changed;

    public static bool WindowsUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(AppTheme mode)
    {
        bool dark = mode == AppTheme.Dark || (mode == AppTheme.System && WindowsUsesDark());
        bool first = Application.Current.Resources["Paper"] == null;
        if (!first && dark == IsDark) return;
        IsDark = dark;
        foreach (var (key, hex) in (dark ? Dark : Light).Concat(Stage))
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        foreach (Window w in Application.Current.Windows) SetTitleBar(w);
        Changed?.Invoke();
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark title bar to match (DWMWA_USE_IMMERSIVE_DARK_MODE, Windows 11).</summary>
    public static void SetTitleBar(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
    }

    /// <summary>Follows Windows switching between light and dark while the window is open.</summary>
    public static void WatchSystem(Func<AppTheme> current)
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && current() == AppTheme.System)
                Application.Current.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
    }
}
