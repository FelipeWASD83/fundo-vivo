using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FundoVivo.Native;
using Microsoft.Web.WebView2.Core;

namespace FundoVivo.Engine;

/// <summary>An HTML wallpaper in WebView2 (the Edge that ships with Windows), with Wallpaper Engine's API recreated.</summary>
public sealed class WebWallpaper : IWallpaper
{
    static Task<CoreWebView2Environment> _environment;
    static string _shim;

    readonly WallpaperWindow _window;
    readonly string _file;
    readonly string _folder;
    readonly WeProject _project;
    CoreWebView2Controller _controller;
    CoreWebView2 _web;
    ItemSettings _settings = new();
    JsonObject _sentProps;
    bool _paused, _ready, _disposed;

    public WallpaperType Type => WallpaperType.Web;
    public bool WantsAudio { get; private set; }
    public IntPtr InputWindow { get; private set; }
    public WallpaperWindow Window => _window;
    public int Fps { get; private set; } = 60;

    /// <summary>Frame-rate cap: handed to the page through applyGeneralProperties and enforced by the shim.</summary>
    public void SetFps(int fps)
    {
        if (fps == Fps) return;
        Fps = fps;
        if (_ready) Post("general", new JsonObject { ["fps"] = Fps });
    }
    public event Action AudioWantedChanged;
    /// <summary>The browser or the page process died; the engine replaces this wallpaper.</summary>
    public event Action Crashed;

    public WebWallpaper(WallpaperWindow window, string file, int fps)
    {
        _window = window;
        _file = file;
        Fps = fps;
        // Wallpaper Engine projects keep index.html next to project.json; their assets are relative to that folder.
        string dir = Path.GetDirectoryName(file);
        _project = WeProject.Load(dir);
        _folder = dir;
    }

    static Task<CoreWebView2Environment> Environment()
    {
        // A failed creation is not kept forever: the next web wallpaper tries again.
        if (_environment is { IsFaulted: true } or { IsCanceled: true }) _environment = null;
        return _environment ??= CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData, new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-features=msSmartScreenProtection,msEdgeTranslate",
        });
    }

    static string Shim()
    {
        if (_shim != null) return _shim;
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web-shim.js");
        using var r = new StreamReader(s);
        return _shim = r.ReadToEnd();
    }

    public async Task StartAsync()
    {
        var env = await Environment();
        if (_disposed) return;
        _controller = await env.CreateCoreWebView2ControllerAsync(_window.Handle);
        if (_disposed) { _controller.Close(); return; }
        _controller.DefaultBackgroundColor = System.Drawing.Color.Black;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, _window.Width, _window.Height);
        _web = _controller.CoreWebView2;
        var s = _web.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;

        // One host name per wallpaper so their localStorage never mixes.
        string host = "w" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(_folder.ToLowerInvariant())))[..12].ToLowerInvariant() + ".fundovivo";
        _web.SetVirtualHostNameToFolderMapping(host, _folder, CoreWebView2HostResourceAccessKind.Allow);
        await _web.AddScriptToExecuteOnDocumentCreatedAsync(Shim().Replace("/*FPS*/0", Fps.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _web.WebMessageReceived += OnMessage;
        _web.NewWindowRequested += (_, e) => e.Handled = true;
        _web.NavigationCompleted += (_, _) => FindInputWindow();
        _web.ProcessFailed += (_, e) =>
        {
            Log.Write($"web process failed ({e.ProcessFailedKind}) in {Path.GetFileName(_folder)}");
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                Crashed?.Invoke();
        };
        ApplyMute();
        string relative = Path.GetRelativePath(_folder, _file).Replace('\\', '/');
        _web.Navigate($"https://{host}/{Uri.EscapeDataString(relative).Replace("%2F", "/")}");
        if (_paused) SetPaused(true);
    }

    void FindInputWindow()
    {
        Win32.EnumChildWindows(_window.Handle, (c, _) =>
        {
            if (Win32.ClassName(c) == "Chrome_RenderWidgetHostHWND") { InputWindow = c; return false; }
            return true;
        }, IntPtr.Zero);
    }

    void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string type;
        try { type = JsonNode.Parse(e.WebMessageAsJson)?["type"]?.ToString(); } catch { return; }
        switch (type)
        {
            case "ready":
                _ready = true;
                FindInputWindow();
                Post("general", new JsonObject { ["fps"] = Fps });
                _sentProps = _project?.UserProperties(_settings) ?? new JsonObject();
                Post("props", _sentProps.DeepClone());
                if (_paused) Post("paused", true);
                Log.Write($"web ready: {Path.GetFileName(_folder)}, {_sentProps.Count} options sent");
                if (AppPaths.TestMode) _ = ReportStatsAsync();
                break;
            case "audio-wanted":
                if (!WantsAudio) { WantsAudio = true; AudioWantedChanged?.Invoke(); }
                break;
        }
    }

    // twice, 8 s apart: the frames between the two are the steady rate, without the page's start-up in them
    async Task ReportStatsAsync()
    {
        for (int round = 1; round <= 2; round++)
        {
            await Task.Delay(8000);
            if (_web == null) return;
            try
            {
                string stats = await _web.ExecuteScriptAsync("JSON.stringify(window.__fundoVivo)");
                Log.Write($"web stats{(round == 2 ? "2" : "")} {Path.GetFileName(_folder)}: {stats} input={(InputWindow != IntPtr.Zero)}");
            }
            catch (Exception e) { Log.Write("web stats: " + e.Message); }
        }
    }

    void Post(string type, JsonNode data)
    {
        if (_web == null) return;
        var message = new JsonObject { ["type"] = type, ["data"] = data };
        try { _web.PostWebMessageAsJson(message.ToJsonString()); } catch (Exception ex) { Log.Write("post to web: " + ex.Message); }
    }

    /// <summary>128 values: 64 bands of the left channel then 64 of the right, low to high, the layout Wallpaper Engine uses.</summary>
    public void PostAudio(string json)
    {
        if (_web == null || _paused || !_ready) return;
        try { _web.PostWebMessageAsJson("{\"type\":\"audio\",\"data\":" + json + "}"); } catch { }
    }

    public void Apply(ItemSettings s)
    {
        _settings = s;
        ApplyMute();
        if (!_ready || _project == null) return;
        var now = _project.UserProperties(s);
        var changed = new JsonObject();
        foreach (var kv in now)
        {
            string before = _sentProps?[kv.Key]?["value"]?.ToJsonString();
            string after = kv.Value?["value"]?.ToJsonString();
            if (before != after) changed[kv.Key] = kv.Value.DeepClone();
        }
        _sentProps = now;
        if (changed.Count > 0) Post("props", changed);
    }

    void ApplyMute()
    {
        if (_web != null) _web.IsMuted = _settings.Volume <= 0;
    }

    public void Resize(int width, int height)
    {
        if (_controller != null) _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
    }

    public async void SetPaused(bool paused)
    {
        _paused = paused;
        if (_controller == null) return;
        try
        {
            if (paused)
            {
                Post("paused", true);
                _controller.IsVisible = false;
                // Hidden pages are throttled; suspending stops their timers and lets Edge trim memory.
                await _web.TrySuspendAsync();
                if (_web == null) return;
                if (!_paused)
                {
                    // Resumed while the suspend was still running (a quick alt-tab): undo it.
                    if (_web.IsSuspended) _web.Resume();
                    _controller.IsVisible = true;
                    Post("paused", false);
                    return;
                }
                _web.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            else
            {
                _web.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                _controller.IsVisible = true;
                if (_web.IsSuspended) _web.Resume();
                Post("paused", false);
            }
        }
        catch (Exception e) { Log.Write("web pause: " + e.Message); }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_web != null) _web.WebMessageReceived -= OnMessage;
        try { _controller?.Close(); } catch { }
        _controller = null;
        _web = null;
    }

    public static string SerializeBands(float[] bands)
    {
        var sb = new System.Text.StringBuilder(bands.Length * 6 + 2);
        sb.Append('[');
        for (int i = 0; i < bands.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bands[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
