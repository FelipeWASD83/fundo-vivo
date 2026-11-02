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
    // Same paper-and-ink idea in both: the dark one is a warm charcoal, not pure black, so the wallpaper
    // thumbnails stay the brightest thing on screen.
    static readonly Dictionary<string, string> Light = new()
    {
        ["Paper"] = "#F3F1EC", ["Surface"] = "#FFFFFF", ["Well"] = "#E8E5DE", ["Line"] = "#D9D5CC",
        ["Ink"] = "#1D1B18", ["Ink2"] = "#5E5951", ["Ink3"] = "#8C867C", ["OnInk"] = "#FFFFFF",
        ["Hover"] = "#0F1D1B18", ["KnobOff"] = "#FFFFFF", ["KnobOn"] = "#FFFFFF",
        ["Playing"] = "#2F7D4F", ["Paused"] = "#B7791F", ["Problem"] = "#B3412E",
    };

    static readonly Dictionary<string, string> Dark = new()
    {
        ["Paper"] = "#161513", ["Surface"] = "#201F1C", ["Well"] = "#2B2925", ["Line"] = "#3A3732",
        ["Ink"] = "#EDE9E2", ["Ink2"] = "#B5AFA5", ["Ink3"] = "#8A847A", ["OnInk"] = "#161513",
        ["Hover"] = "#14FFFFFF", ["KnobOff"] = "#B5AFA5", ["KnobOn"] = "#161513",
        ["Playing"] = "#5CB37F", ["Paused"] = "#D9A441", ["Problem"] = "#E07A63",
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
        foreach (var (key, hex) in dark ? Dark : Light)
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
