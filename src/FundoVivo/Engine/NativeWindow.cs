using System.Runtime.InteropServices;
using FundoVivo.Native;

namespace FundoVivo.Engine;

/// <summary>Minimal Win32 window: registers its class once and routes messages to an overridable handler.</summary>
public abstract class NativeWindow : IDisposable
{
    static readonly Dictionary<IntPtr, NativeWindow> Live = new();
    static readonly HashSet<string> Registered = new();
    static readonly WndProcDelegate SharedProc = Dispatch;
    static NativeWindow _creating;

    public IntPtr Handle { get; private set; }

    protected void Create(string className, uint exStyle, uint style, int x, int y, int w, int h, IntPtr parent)
    {
        if (Registered.Add(className))
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedProc),
                hInstance = Win32.GetModuleHandleW(null),
                hCursor = Win32.LoadCursorW(IntPtr.Zero, new IntPtr(32512)),
                lpszClassName = className,
            };
            if (Win32.RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
        }
        _creating = this;
        Handle = Win32.CreateWindowExW(exStyle, className, "", style, x, y, w, h, parent, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        _creating = null;
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        Live[Handle] = this;
    }

    static IntPtr Dispatch(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (!Live.TryGetValue(hwnd, out var window))
        {
            window = _creating;
            if (window == null) return Win32.DefWindowProcW(hwnd, msg, w, l);
            window.Handle = hwnd;
            Live[hwnd] = window;
        }
        try
        {
            return window.WndProc(msg, w, l);
        }
        catch (Exception e)
        {
            Log.Write($"WndProc {msg:X}: {e}");
            return Win32.DefWindowProcW(hwnd, msg, w, l);
        }
        finally
        {
            if (msg == Win32.WM_DESTROY) Live.Remove(hwnd);
        }
    }

    protected virtual IntPtr WndProc(uint msg, IntPtr w, IntPtr l) => Win32.DefWindowProcW(Handle, msg, w, l);

    public virtual void Dispose()
    {
        if (Handle != IntPtr.Zero && Win32.IsWindow(Handle)) Win32.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }
}
