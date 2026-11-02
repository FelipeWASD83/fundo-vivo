using FundoVivo.Native;

namespace FundoVivo.Engine;

public interface IWallpaper : IDisposable
{
    Task StartAsync();
    void SetPaused(bool paused);
    void Resize(int width, int height);
    void Apply(ItemSettings settings);
    /// <summary>Approximate cost to report on screen; video and image are cheap, web is a browser.</summary>
    WallpaperType Type { get; }
}

/// <summary>A child of the desktop's WorkerW covering exactly one monitor.</summary>
public sealed class WallpaperWindow : NativeWindow
{
    public const string ClassName = "FundoVivoWallpaper";

    public MonitorInfo Monitor { get; private set; }
    public IWallpaper Content { get; set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public WallpaperWindow(IntPtr layer, MonitorInfo monitor)
    {
        Monitor = monitor;
        var r = Desktop.ToLayer(layer, monitor.Bounds);
        Width = r.Width;
        Height = r.Height;
        Create(ClassName, Win32.WS_EX_NOACTIVATE, Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS,
            r.Left, r.Top, r.Width, r.Height, layer);
        // Above whatever else lives in the WorkerW (the Windows wallpaper, or another wallpaper program).
        Win32.SetWindowPos(Handle, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void Move(IntPtr layer, MonitorInfo monitor)
    {
        Monitor = monitor;
        var r = Desktop.ToLayer(layer, monitor.Bounds);
        Win32.SetWindowPos(Handle, Win32.HWND_TOP, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOACTIVATE);
        if (r.Width != Width || r.Height != Height)
        {
            Width = r.Width;
            Height = r.Height;
            Content?.Resize(Width, Height);
        }
    }

    protected override IntPtr WndProc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case Win32.WM_ERASEBKGND: return new IntPtr(1);
            case Win32.WM_MOUSEACTIVATE: return new IntPtr(Win32.MA_NOACTIVATE);
        }
        return base.WndProc(msg, w, l);
    }

    public override void Dispose()
    {
        try { Content?.Dispose(); } catch (Exception e) { Log.Write("dispose wallpaper: " + e.Message); }
        Content = null;
        base.Dispose();
    }
}
