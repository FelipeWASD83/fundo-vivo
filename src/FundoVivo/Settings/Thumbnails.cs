using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FundoVivo.Settings;

/// <summary>Loads card pictures off the UI thread: the Wallpaper Engine preview when there is one, otherwise the Windows thumbnail.</summary>
public static class Thumbnails
{
    const int Width = 440;
    static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly BlockingCollection<(string key, string preview, string file, Action<BitmapSource> done, Dispatcher ui)> Queue = new();
    static Thread _worker;

    public static void Request(string preview, string file, Action<BitmapSource> done)
    {
        string key = preview ?? file;
        if (key == null) return;
        if (Cache.TryGetValue(key, out var hit)) { done(hit); return; }
        if (_worker == null)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "thumbnails", Priority = ThreadPriority.BelowNormal };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
        Queue.Add((key, preview, file, done, Dispatcher.CurrentDispatcher));
    }

    static void Work()
    {
        foreach (var job in Queue.GetConsumingEnumerable())
        {
            BitmapSource image = Cache.TryGetValue(job.key, out var hit) ? hit : null;
            if (image == null)
            {
                try { image = Load(job.preview, job.file); } catch (Exception e) { Log.Write($"thumbnail {job.key}: {e.Message}"); }
                if (image != null) Cache[job.key] = image;
            }
            if (image != null) job.ui.BeginInvoke(job.done, image);
        }
    }

    static BitmapSource Load(string preview, string file)
    {
        string path = preview ?? file;
        if (Library.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = Width;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch when (preview != null && file != null) { path = file; }
        }
        return ShellThumbnail(path);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap); }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bind, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    static BitmapSource ShellThumbnail(string path)
    {
        if (!File.Exists(path)) return null;
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_THUMBNAILONLY = 0x8;
            if (factory.GetImage(new SIZE { cx = Width, cy = Width * 9 / 16 }, SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out IntPtr hbmp) != 0) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DeleteObject(hbmp); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }
}
