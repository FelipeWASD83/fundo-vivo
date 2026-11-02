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
    readonly double _margin;
    Windows.UI.Composition.Desktop.DesktopWindowTarget _target;
    MediaPlayer _player;
    MediaSource _source;
    MediaPlayerSurface _surface;
    SpriteVisual _background, _video;
    CompositionSurfaceBrush _brush;
    bool _paused;
    uint _videoW, _videoH;
    FitMode _fit = FitMode.Fill;
    Vector3 _panTarget;
    DateTime _lastPan;

    public WallpaperType Type => WallpaperType.Video;

    /// <param name="parallaxMargin">
    /// For converted scenes with parallax: the video is that much bigger than the monitor on each side (a fraction of
    /// the monitor), and the mouse pans over it. 0 for an ordinary video.
    /// </param>
    public VideoWallpaper(CompositionHost host, WallpaperWindow window, string file, double parallaxMargin = 0)
    {
        _host = host;
        _window = window;
        _file = file;
        _margin = Math.Clamp(parallaxMargin, 0, 0.3);
    }

    /// <summary>The pan only applies in Fill, where the extra picture is outside the screen and the mouse reveals it.</summary>
    public bool Pans => _margin > 0 && _fit == FitMode.Fill;

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
        if (_fit != s.Fit) { _fit = s.Fit; Resize(_window.Width, _window.Height); }
        if (_player != null)
        {
            _player.IsMuted = s.Volume <= 0;
            _player.Volume = Math.Clamp(s.Volume / 100.0, 0, 1);
        }
    }

    Vector2 VideoSize => Pans
        ? new Vector2((float)(_window.Width * (1 + 2 * _margin)), (float)(_window.Height * (1 + 2 * _margin)))
        : new Vector2(_window.Width, _window.Height);

    Vector3 CenterOffset => Pans ? new Vector3((float)(-_margin * _window.Width), (float)(-_margin * _window.Height), 0) : Vector3.Zero;

    public void Resize(int width, int height)
    {
        if (_video == null) return;
        _background.Size = new Vector2(width, height);
        _video.StopAnimation("Offset");
        _video.Size = VideoSize;
        _video.Offset = _panTarget = CenterOffset;
        UpdateSurfaceSize();
    }

    /// <summary>
    /// The cursor relative to this monitor, -1..1 on each axis (0,0 = centre). The picture moves the other way, up to
    /// the margin, and the compositor eases into each new position so the movement stays smooth.
    /// </summary>
    public void SetPointer(Vector2 normalized, bool force = false)
    {
        if (!Pans || _paused || _video == null) return;
        var n = Vector2.Clamp(normalized, -Vector2.One, Vector2.One);
        var target = CenterOffset + new Vector3((float)(-n.X * _margin * _window.Width), (float)(-n.Y * _margin * _window.Height), 0);
        // At most ~30 new targets a second; the engine sends one last forced one when the mouse stops.
        if (Vector3.Distance(target, _panTarget) < 0.5f || (!force && (DateTime.Now - _lastPan).TotalMilliseconds < 33)) return;
        _panTarget = target;
        _lastPan = DateTime.Now;
        var c = _host.Compositor;
        var anim = c.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1f, target, c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.6f), new Vector2(0.3f, 1f)));
        anim.Duration = TimeSpan.FromMilliseconds(450);
        _video.StartAnimation("Offset", anim);
    }

    /// <summary>
    /// The player letterboxes the video inside its surface, so the surface must have the video's own shape;
    /// the brush then fills, fits or stretches it. Sized to what the screen needs, never bigger than the video.
    /// </summary>
    void UpdateSurfaceSize()
    {
        if (_player == null) return;
        var size = VideoSize;
        double w = size.X, h = size.Y;
        if (_videoW == 0 || _videoH == 0) { _player.SetSurfaceSize(new Windows.Foundation.Size(w, h)); return; }
        double sx = w / _videoW, sy = h / _videoH;
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
