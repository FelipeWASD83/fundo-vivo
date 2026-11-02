using System.Text;
using FundoVivo.Core;
using FundoVivo.Native;

namespace FundoVivo.Engine;

/// <summary>Finds the window that sits between the static wallpaper and the desktop icons.</summary>
public static class Desktop
{
    public static IntPtr Progman => Win32.FindWindowW("Progman", null);

    /// <summary>
    /// Since Windows 11 24H2 the icon layer (SHELLDLL_DefView) and a WorkerW are children of Progman, and Progman
    /// has WS_EX_NOREDIRECTIONBITMAP. Before that, Progman spawns a top-level WorkerW right behind the one holding
    /// the icons. Either way the answer is that WorkerW, and our windows go inside it.
    /// </summary>
    public static IntPtr FindWallpaperLayer()
    {
        if (AppPaths.TestMode && Environment.GetEnvironmentVariable("FUNDOVIVO_TEST_LAYER") is "offscreen" or "beside")
            return (_testLayer ??= new TestLayer(Environment.GetEnvironmentVariable("FUNDOVIVO_TEST_LAYER") == "beside")).Handle;
        IntPtr progman = Progman;
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        // 0x052C asks Explorer to create the WorkerW; harmless when it already exists.
        Win32.SendMessageTimeoutW(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), Win32.SMTO_NORMAL, 1000, out _);
        Win32.SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, Win32.SMTO_NORMAL, 1000, out _);

        if (IsRaisedDesktop(progman))
        {
            IntPtr child = Win32.FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
            if (child != IntPtr.Zero)
            {
                EnsureWorkerWAtBottom(progman, child);
                return child;
            }
        }

        IntPtr found = IntPtr.Zero;
        Win32.EnumWindows((top, _) =>
        {
            if (Win32.FindWindowExW(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                IntPtr behind = Win32.FindWindowExW(IntPtr.Zero, top, "WorkerW", null);
                if (behind != IntPtr.Zero) { found = behind; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static TestLayer _testLayer;

    /// <summary>
    /// For measurements without touching the real desktop: a stand-in layer outside every monitor. "offscreen" puts it
    /// far away (Windows does not even compose it); "beside" puts it just right of the desktop, where it is still
    /// composed, so Windows.Graphics.Capture can take pictures of it. The child positions match the real desktop's.
    /// </summary>
    sealed class TestLayer : NativeWindow
    {
        public TestLayer(bool beside)
        {
            int x = -32000, y = -32000, w = 16000, h = 8000;
            if (beside)
            {
                w = Win32.GetSystemMetrics(78); h = Win32.GetSystemMetrics(79);
                x = Win32.GetSystemMetrics(76) + w + 160; y = Win32.GetSystemMetrics(77);
            }
            Create("FundoVivoTestLayer", Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, Win32.WS_POPUP | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN,
                x, y, w, h, IntPtr.Zero);
        }
    }

    public static bool IsRaisedDesktop(IntPtr progman) => (Win32.ExStyle(progman) & Win32.WS_EX_NOREDIRECTIONBITMAP) != 0;

    static void EnsureWorkerWAtBottom(IntPtr progman, IntPtr workerW)
    {
        // The icon layer must stay above the WorkerW, or the wallpaper would cover the icons.
        IntPtr last = IntPtr.Zero;
        Win32.EnumChildWindows(progman, (c, _) => { if (Win32.GetParent(c) == progman) last = c; return true; }, IntPtr.Zero);
        if (last != workerW)
            Win32.SetWindowPos(workerW, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Screen rectangle converted to the client coordinates of the layer window.</summary>
    public static RECT ToLayer(IntPtr layer, RECT screen)
    {
        if (_testLayer != null && layer == _testLayer.Handle)
        {
            // The stand-in layer stands for the whole desktop: its top left is the desktop's top left.
            int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77);
            return new RECT(screen.Left - vx, screen.Top - vy, screen.Right - vx, screen.Bottom - vy);
        }
        var r = screen;
        Win32.MapWindowPoints(IntPtr.Zero, layer, ref r, 2);
        return r;
    }

    /// <summary>True when the window under the cursor is the desktop itself (icons, empty area, wallpaper).</summary>
    public static bool IsDesktopWindow(IntPtr hwnd)
    {
        for (int i = 0; i < 4 && hwnd != IntPtr.Zero; i++)
        {
            string cls = Win32.ClassName(hwnd);
            if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32" or WallpaperWindow.ClassName) return true;
            hwnd = Win32.GetParent(hwnd);
        }
        return false;
    }

    /// <summary>Makes Explorer repaint its own wallpaper where ours used to be.</summary>
    public static void Refresh(IntPtr layer)
    {
        if (layer != IntPtr.Zero && Win32.IsWindow(layer))
            Win32.RedrawWindow(layer, IntPtr.Zero, IntPtr.Zero, Win32.RDW_INVALIDATE | Win32.RDW_ERASE | Win32.RDW_ALLCHILDREN | Win32.RDW_UPDATENOW);
        if (!IsRaisedDesktop(Progman))
        {
            var sb = new StringBuilder(520);
            if (Win32.SystemParametersInfoW(Win32.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                Win32.SystemParametersInfoW(Win32.SPI_SETDESKWALLPAPER, 0, sb.ToString(), 0);
        }
    }

    public static string Describe()
    {
        IntPtr progman = Progman;
        var sb = new StringBuilder();
        sb.Append($"Progman {progman:X} raised={IsRaisedDesktop(progman)}");
        Win32.EnumChildWindows(progman, (c, _) =>
        {
            if (Win32.GetParent(c) == progman) sb.Append($" | {Win32.ClassName(c)} {c:X}");
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }
}
