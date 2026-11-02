using System.Runtime.InteropServices;
using FundoVivo.Native;

namespace FundoVivo.Core;

public sealed class MonitorInfo
{
    public string Id { get; init; }          // stable across reboots: the monitor's device path
    public string GdiName { get; init; }     // \\.\DISPLAY1, changes with plug order
    public string FriendlyName { get; init; }
    public RECT Bounds { get; init; }
    public RECT WorkArea { get; init; }
    public bool Primary { get; init; }
    public double RefreshRate { get; init; }
    public IntPtr Handle { get; init; }

    public string Label => string.IsNullOrWhiteSpace(FriendlyName) ? GdiName.Replace(@"\\.\", "") : FriendlyName;
}

public static class Monitors
{
    public static List<MonitorInfo> Enumerate()
    {
        var names = DisplayConfigNames();
        var result = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!Win32.GetMonitorInfoW(h, ref info)) return true;
            names.TryGetValue(info.szDevice, out var dc);
            result.Add(new MonitorInfo
            {
                Id = dc.Path ?? info.szDevice,
                GdiName = info.szDevice,
                FriendlyName = dc.Friendly,
                Bounds = info.rcMonitor,
                WorkArea = info.rcWork,
                Primary = (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
                RefreshRate = dc.Refresh,
                Handle = h,
            });
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    // ---- QueryDisplayConfig: the only API that gives the monitor's real name and a stable id ----

    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    struct PathSourceInfo { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct PathTargetInfo
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation;
        public int scaling; public uint refreshNum, refreshDen; public int scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo { public PathSourceInfo source; public PathTargetInfo target; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    struct ModeInfo { public int infoType; public uint id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfoHeader { public int type; public int size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SourceDeviceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetDeviceName
    {
        public DeviceInfoHeader header; public uint flags; public int outputTechnology; public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths, ref uint numModes, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName info);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName info);

    static Dictionary<string, (string Path, string Friendly, double Refresh)> DisplayConfigNames()
    {
        var map = new Dictionary<string, (string, string, double)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const uint QDC_ONLY_ACTIVE_PATHS = 2;
            const int ERROR_INSUFFICIENT_BUFFER = 122;
            PathInfo[] paths = null;
            uint np = 0, nm = 0;
            // The layout can change between the two calls (a monitor waking up); ask again instead of falling back
            // to \\.\DISPLAYn names, which would not match the saved monitors.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return map;
                paths = new PathInfo[np];
                var modes = new ModeInfo[nm];
                int r = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (r == 0) break;
                if (r != ERROR_INSUFFICIENT_BUFFER || attempt == 4) return map;
                Thread.Sleep(100);
            }
            for (int i = 0; i < np; i++)
            {
                var p = paths[i];
                var src = new SourceDeviceName { header = { type = 1, size = Marshal.SizeOf<SourceDeviceName>(), adapterId = p.source.adapterId, id = p.source.id } };
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                var tgt = new TargetDeviceName { header = { type = 2, size = Marshal.SizeOf<TargetDeviceName>(), adapterId = p.target.adapterId, id = p.target.id } };
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;
                double hz = p.target.refreshDen == 0 ? 0 : (double)p.target.refreshNum / p.target.refreshDen;
                map[src.viewGdiDeviceName] = (string.IsNullOrEmpty(tgt.monitorDevicePath) ? null : tgt.monitorDevicePath, tgt.monitorFriendlyDeviceName, hz);
            }
        }
        catch (Exception e) { Log.Write("DisplayConfig failed: " + e.Message); }
        return map;
    }
}
