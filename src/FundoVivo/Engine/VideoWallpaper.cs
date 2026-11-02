using System.Numerics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Composition;

namespace FundoVivo.Engine;

/// <summary>Windows' own player (Media Foundation, decoded on the GPU) drawn into a composition surface.</summary>
public sealed class VideoWallpaper : IWallpaper
{
    readonly CompositionHost _host;
    readonly WallpaperWindow _window;
    readonly string _file;
    Windows.UI.Composition.Desktop.DesktopWindowTarget _target;
    MediaPlayer _player;
    MediaSource _source;
    MediaPlayerSurface _surface;
    SpriteVisual _background, _video;
    CompositionSurfaceBrush _brush;
    bool _paused;
    uint _videoW, _videoH;
    FitMode _fit = FitMode.Fill;

    public WallpaperType Type => WallpaperType.Video;

    public VideoWallpaper(CompositionHost host, WallpaperWindow window, string file)
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
        _video = c.CreateSpriteVisual();
        root.Children.InsertAtBottom(_background);
        root.Children.InsertAtTop(_video);
        _target.Root = root;

        _player = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = true, IsMuted = true, RealTimePlayback = false };
        // Keeps the wallpaper out of the Windows media flyout and away from the media keys.
        _player.CommandManager.IsEnabled = false;
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.MediaFailed += (_, e) => Log.Write($"video failed ({e.Error}): {e.ErrorMessage} {_file}");
        // Size the surface once the video's own size is known (the event comes on a worker thread).
        _player.PlaybackSession.NaturalVideoSizeChanged += (s, _) =>
        {
            uint w = s.NaturalVideoWidth, h = s.NaturalVideoHeight;
            _host.Queue.TryEnqueue(() => { _videoW = w; _videoH = h; UpdateSurfaceSize(); });
        };
        _source = MediaSource.CreateFromUri(new Uri(_file));
        _player.Source = _source;
        _player.SetSurfaceSize(new Windows.Foundation.Size(_window.Width, _window.Height));
        _surface = _player.GetSurface(c);
        _brush = c.CreateSurfaceBrush(_surface.CompositionSurface);
        _brush.HorizontalAlignmentRatio = 0.5f;
        _brush.VerticalAlignmentRatio = 0.5f;
        _video.Brush = _brush;
        Resize(_window.Width, _window.Height);
        if (!_paused) _player.Play();
        return Task.CompletedTask;
    }

    public void Apply(ItemSettings s)
    {
        if (_brush != null)
            _brush.Stretch = s.Fit switch { FitMode.Fit => CompositionStretch.Uniform, FitMode.Stretch => CompositionStretch.Fill, _ => CompositionStretch.UniformToFill };
        if (_fit != s.Fit) { _fit = s.Fit; UpdateSurfaceSize(); }
        if (_player != null)
        {
            _player.IsMuted = s.Volume <= 0;
            _player.Volume = Math.Clamp(s.Volume / 100.0, 0, 1);
        }
    }

    public void Resize(int width, int height)
    {
        if (_video == null) return;
        var size = new Vector2(width, height);
        _background.Size = size;
        _video.Size = size;
        UpdateSurfaceSize();
    }

    /// <summary>
    /// The player letterboxes the video inside its surface, so the surface must have the video's own shape;
    /// the brush then fills, fits or stretches it. Sized to what the monitor needs, never bigger than the video.
    /// </summary>
    void UpdateSurfaceSize()
    {
        if (_player == null) return;
        int w = _window.Width, h = _window.Height;
        if (_videoW == 0 || _videoH == 0) { _player.SetSurfaceSize(new Windows.Foundation.Size(w, h)); return; }
        double sx = (double)w / _videoW, sy = (double)h / _videoH;
        double scale = Math.Min(1.0, _fit == FitMode.Fit ? Math.Min(sx, sy) : Math.Max(sx, sy));
        _player.SetSurfaceSize(new Windows.Foundation.Size(Math.Max(1, Math.Round(_videoW * scale)), Math.Max(1, Math.Round(_videoH * scale))));
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (_player == null) return;
        if (paused) _player.Pause(); else _player.Play();
    }

    public void Dispose()
    {
        if (_player != null)
        {
            _player.Pause();
            _player.Source = null;
            _player.Dispose();
            _player = null;
        }
        // The source owns the decoder and its frame pool; without this the memory stays until the GC runs.
        _source?.Dispose();
        _source = null;
        _surface?.Dispose();
        _brush?.Dispose();
        _video?.Dispose();
        _background?.Dispose();
        _target?.Dispose();
        _surface = null;
    }
}
