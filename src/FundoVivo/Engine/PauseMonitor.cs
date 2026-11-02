using FundoVivo.Native;

namespace FundoVivo.Engine;

public enum MonitorCover { None, Covered, Fullscreen }

/// <summary>
/// Decides, per monitor, whether the wallpaper is visible at all. A wallpaper nobody can see is paused (or
/// unloaded), which is where most of the savings come from.
/// </summary>
public static class PauseMonitor
{
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "Xaml_WindowedPopupClass", "SysShadow", "tooltips_class32", "#32768", WallpaperWindow.ClassName,
    };

    /// <summary>
    /// True when Windows itself says an exclusive-mode D3D game or a presentation owns the screen. QUNS_BUSY is left
    /// out on purpose: on this PC it is reported with no fullscreen program at all (topmost overlays such as the
    /// GeForce one seem to trigger it), and borderless games are caught by the window check anyway.
    /// </summary>
    public static bool ExclusiveFullscreen()
    {
        if (Win32.SHQueryUserNotificationState(out int s) != 0) return false;
        return s == QUNS_RUNNING_D3D_FULL_SCREEN || s == QUNS_PRESENTATION_MODE;
    }

    public static Dictionary<string, MonitorCover> Evaluate(IReadOnlyList<MonitorInfo> monitors, uint ownPid)
    {
        var result = monitors.ToDictionary(m => m.Id, _ => MonitorCover.None);
        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h) || Win32.IsIconic(h) || Win32.IsCloaked(h)) return true;
            uint ex = Win32.ExStyle(h);
            // Click-through overlays (GeForce, Steam, Discord) cover the screen but hide nothing.
            if ((ex & Win32.WS_EX_TRANSPARENT) != 0 || (ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;
            if ((ex & Win32.WS_EX_NOACTIVATE) != 0 && (ex & Win32.WS_EX_TOPMOST) != 0) return true;
            Win32.GetWindowThreadProcessId(h, out uint pid);
            if (pid == ownPid) return true;
            string cls = Win32.ClassName(h);
            if (ShellClasses.Contains(cls)) return true;
            if ((ex & Win32.WS_EX_LAYERED) != 0 && IsTranslucent(h)) return true;

            var r = Win32.FrameBounds(h);
            // A maximized window with a title bar is just covering, even where the work area is the whole monitor
            // (no taskbar on that screen, or one that auto-hides); only borderless windows count as fullscreen.
            const uint WS_CAPTION = 0x00C00000;
            uint style = Win32.Style(h);
            bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & Win32.WS_MAXIMIZE) != 0;
            foreach (var m in monitors)
            {
                if (result[m.Id] == MonitorCover.Fullscreen) continue;
                if (Covers(r, m.Bounds) && !framed) result[m.Id] = MonitorCover.Fullscreen;
                else if (Covers(r, m.Bounds)) result[m.Id] = MonitorCover.Covered;
                else if (result[m.Id] == MonitorCover.None && Covers(r, m.WorkArea)) result[m.Id] = MonitorCover.Covered;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    static bool Covers(RECT window, RECT area)
    {
        const int slack = 2;
        return window.Left <= area.Left + slack && window.Top <= area.Top + slack
            && window.Right >= area.Right - slack && window.Bottom >= area.Bottom - slack;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);

    static bool IsTranslucent(IntPtr h)
    {
        const uint LWA_ALPHA = 2;
        return GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags) && (flags & LWA_ALPHA) != 0 && alpha < 250;
    }
}
