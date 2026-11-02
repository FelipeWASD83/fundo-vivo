using System.Runtime.InteropServices;
using FundoVivo.Native;

namespace FundoVivo.Engine;

/// <summary>The notification-area icon, drawn with Shell_NotifyIcon directly (no WinForms in the engine).</summary>
public sealed class TrayIcon : IDisposable
{
    public const uint CallbackMessage = Win32.WM_APP + 1;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    readonly IntPtr _owner;
    readonly IntPtr _icon;
    string _tip = AppPaths.AppName;
    bool _added;

    public TrayIcon(IntPtr owner)
    {
        _owner = owner;
        // LoadIconMetric picks the size that matches the tray at the current DPI; 32512 is the app icon group.
        if (LoadIconMetric(Win32.GetModuleHandleW(null), new IntPtr(32512), 0, out _icon) != 0 || _icon == IntPtr.Zero)
            Win32.ExtractIconExW(Environment.ProcessPath ?? AppPaths.ExePath, 0, out _, out _icon, 1);
        Add();
    }

    [DllImport("comctl32.dll")] static extern int LoadIconMetric(IntPtr inst, IntPtr name, int metric, out IntPtr icon);

    public void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Win32.Shell_NotifyIconW(NIM_ADD, ref data);
    }

    public void SetTip(string tip)
    {
        if (tip == _tip) return;
        _tip = tip.Length > 120 ? tip[..120] : tip;
        var data = Data(NIF_TIP);
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _owner,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>Shows the menu at the cursor and returns the chosen id, or 0.</summary>
    public int ShowMenu(params (int id, string text, bool enabled)[] items)
    {
        IntPtr menu = Win32.CreatePopupMenu();
        try
        {
            foreach (var (id, text, enabled) in items)
            {
                if (id == 0) Win32.AppendMenuW(menu, 0x800 /* separator */, UIntPtr.Zero, null);
                else Win32.AppendMenuW(menu, enabled ? 0u : 0x1u /* grayed */, (UIntPtr)id, text);
            }
            Win32.GetCursorPos(out var p);
            // Without this the menu does not close when clicking elsewhere (documented TrackPopupMenu quirk).
            Win32.SetForegroundWindow(_owner);
            const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;
            return Win32.TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, _owner, IntPtr.Zero);
        }
        finally { Win32.DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Win32.Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
    }
}
