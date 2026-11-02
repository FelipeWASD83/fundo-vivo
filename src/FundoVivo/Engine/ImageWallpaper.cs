using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Composition;

namespace FundoVivo.Engine;

/// <summary>A still image decoded once at the monitor's exact size; after that it costs nothing.</summary>
public sealed class ImageWallpaper : IWallpaper
{
    readonly CompositionHost _host;
    readonly WallpaperWindow _window;
    readonly string _file;
    Windows.UI.Composition.Desktop.DesktopWindowTarget _target;
    SpriteVisual _background, _image;
    CompositionDrawingSurface _surface;
    FitMode _fit = FitMode.Fill;
    int _drawnW, _drawnH;
    FitMode _drawnFit;
    int _generation;

    public WallpaperType Type => WallpaperType.Image;

    public ImageWallpaper(CompositionHost host, WallpaperWindow window, string file)
    {
        _host = host;
        _window = window;
        _file = file;
    }

    public Task StartAsync()
    {
        var c = _host.Compositor;
        _target = _host.CreateTarget(_window.Handle);
        var root = c.CreateContainerVisual();
        _background = c.CreateSpriteVisual();
        _background.Brush = c.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        _image = c.CreateSpriteVisual();
        root.Children.InsertAtBottom(_background);
        root.Children.InsertAtTop(_image);
        _target.Root = root;
        return RedrawAsync();
    }

    public void Apply(ItemSettings s)
    {
        _fit = s.Fit;
        if (_target != null && (_drawnFit != _fit || _drawnW != _window.Width || _drawnH != _window.Height)) _ = RedrawAsync();
    }

    public void Resize(int width, int height) => _ = RedrawAsync();

    public void SetPaused(bool paused) { }

    async Task RedrawAsync()
    {
        int generation = ++_generation;
        int w = _window.Width, h = _window.Height;
        var fit = _fit;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_file);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            uint srcW = decoder.OrientedPixelWidth, srcH = decoder.OrientedPixelHeight;

            var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
            int outW, outH;
            if (fit == FitMode.Stretch)
            {
                outW = w; outH = h;
                transform.ScaledWidth = (uint)w; transform.ScaledHeight = (uint)h;
            }
            else
            {
                double scale = fit == FitMode.Fill ? Math.Max((double)w / srcW, (double)h / srcH) : Math.Min((double)w / srcW, (double)h / srcH);
                uint sw = Math.Max(1, (uint)Math.Round(srcW * scale)), sh = Math.Max(1, (uint)Math.Round(srcH * scale));
                transform.ScaledWidth = sw; transform.ScaledHeight = sh;
                if (fit == FitMode.Fill)
                {
                    outW = (int)Math.Min(sw, (uint)w); outH = (int)Math.Min(sh, (uint)h);
                    transform.Bounds = new BitmapBounds { X = (sw - (uint)outW) / 2, Y = (sh - (uint)outH) / 2, Width = (uint)outW, Height = (uint)outH };
                }
                else { outW = (int)sw; outH = (int)sh; }
            }

            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            byte[] pixels = data.DetachPixelData();
            if (generation != _generation || _image == null) return;

            var surface = _host.CreateImageSurface(outW, outH, pixels);
            var brush = _host.Compositor.CreateSurfaceBrush(surface);
            brush.Stretch = CompositionStretch.None;
            var old = _surface;
            _surface = surface;
            _image.Brush = brush;
            _image.Size = new Vector2(outW, outH);
            _image.Offset = new Vector3((w - outW) / 2f, (h - outH) / 2f, 0);
            _background.Size = new Vector2(w, h);
            old?.Dispose();
            _drawnW = w; _drawnH = h; _drawnFit = fit;
        }
        catch (Exception e)
        {
            Log.Write($"image failed: {_file}: {e.Message}");
        }
    }

    public void Dispose()
    {
        _generation++;
        _surface?.Dispose();
        _image?.Dispose();
        _background?.Dispose();
        _target?.Dispose();
        _image = null;
    }
}
