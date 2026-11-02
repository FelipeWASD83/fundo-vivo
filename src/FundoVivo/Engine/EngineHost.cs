using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FundoVivo.Native;

namespace FundoVivo.Engine;

/// <summary>
/// The background part: a hidden window on the UI thread that owns the tray icon, one wallpaper window per
/// monitor, the pause rules, the audio for web wallpapers and the commands sent by the settings window.
/// </summary>
public sealed class EngineHost : NativeWindow
{
    public const string WindowClass = "FundoVivoEngine";

    sealed class Slot
    {
        public MonitorInfo Monitor;
        public MonitorAssignment Assignment;
        public string File;
        public WallpaperWindow Window;
        public bool Paused, Unloaded, Failed;
        public DateTime RetryAt;
        public string State = "starting";
    }

    readonly CompositionHost _composition;
    readonly Dictionary<string, Slot> _slots = new();
    readonly uint _taskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
    readonly uint _pid = (uint)Environment.ProcessId;
    readonly WinEventProc _winEvent;
    readonly List<IntPtr> _hooks = new();
    AppConfig _config = new();
    List<MonitorInfo> _monitors = new();
    IntPtr _layer;
    TrayIcon _tray;
    AudioSpectrum _audio;
    Windows.System.DispatcherQueueTimer _tick, _soon, _rebuildTimer, _releaseTimer;
    bool _locked, _rawInputOn, _exiting;
    string _lastState;
    POINT _lastMouse;
    DateTime _lastClickLog;

    public EngineHost()
    {
        _composition = new CompositionHost();
        Create(WindowClass, Win32.WS_EX_TOOLWINDOW, Win32.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero);
        _tray = AppPaths.TestMode ? null : new TrayIcon(Handle);
        Win32.WTSRegisterSessionNotification(Handle, 0);
        _winEvent = OnWinEvent;
        const uint OUTOFCONTEXT = 0, SKIPOWNPROCESS = 2;
        foreach (var (min, max) in new[] { (0x0003u, 0x0003u) /* foreground */, (0x0016u, 0x0017u) /* minimize */, (0x000Bu, 0x000Bu) /* move/size end */ })
            _hooks.Add(Win32.SetWinEventHook(min, max, IntPtr.Zero, _winEvent, 0, 0, OUTOFCONTEXT | SKIPOWNPROCESS));

        _audio = new AudioSpectrum(bands =>
        {
            string json = WebWallpaper.SerializeBands(bands);
            _composition.Queue.TryEnqueue(() =>
            {
                foreach (var s in _slots.Values)
                    if (!s.Paused && s.Window?.Content is WebWallpaper w && w.WantsAudio) w.PostAudio(json);
            });
        });

        _tick = _composition.Queue.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(1000);
        // An exception escaping a WinRT timer callback would take the whole process down; log it and carry on.
        _tick.Tick += (_, _) => Safe(Tick);
        _soon = _composition.Queue.CreateTimer();
        _soon.Interval = TimeSpan.FromMilliseconds(150);
        _soon.IsRepeating = false;
        _soon.Tick += (_, _) => Safe(Tick);
        _rebuildTimer = _composition.Queue.CreateTimer();
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += (_, _) => Safe(() => Rebuild(true));

        Safe(() => Rebuild(true));
        _tick.Start();
        Updater.ConfirmStarted();
        Log.Write($"engine {AppPaths.Version} started; {Desktop.Describe()}");
    }

    public static void Run()
    {
        var host = new EngineHost();
        while (Win32.GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
        GC.KeepAlive(host);
    }

    static void Safe(Action action)
    {
        try { action(); }
        catch (Exception e) { Log.Write("engine error: " + e); }
    }

    // ---------------------------------------------------------------- wallpapers

    void ScheduleRebuild(int ms)
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _rebuildTimer.Start();
    }

    void Rebuild(bool findLayer)
    {
        if (_exiting) return;
        _config = ConfigStore.Load();
        if (findLayer || _layer == IntPtr.Zero || !Win32.IsWindow(_layer))
        {
            IntPtr layer = Desktop.FindWallpaperLayer();
            if (layer == IntPtr.Zero)
            {
                Log.Write("desktop layer not found yet; retrying");
                ScheduleRebuild(2000);
                return;
            }
            if (layer != _layer)
            {
                foreach (var s in _slots.Values) s.Window?.Dispose();
                _slots.Clear();
                _layer = layer;
            }
        }
        _monitors = Monitors.Enumerate();

        foreach (var id in _slots.Keys.ToList())
        {
            var s = _slots[id];
            var m = _monitors.FirstOrDefault(x => x.Id == id);
            if (m == null || !_config.Monitors.ContainsKey(id))
            {
                s.Window?.Dispose();
                _slots.Remove(id);
                ReleaseMemory();
            }
        }

        foreach (var m in _monitors)
        {
            if (!_config.Monitors.TryGetValue(m.Id, out var a) || a?.File == null) continue;
            string file = _config.PlayableFile(a, m.Bounds.Width, m.Bounds.Height);
            if (_slots.TryGetValue(m.Id, out var s) && s.File == file && s.Assignment.Type == a.Type
                && (s.Unloaded || (s.Window != null && Win32.IsWindow(s.Window.Handle))))
            {
                // Same wallpaper: just follow the monitor. An unloaded one stays unloaded (a game changing the
                // display mode must not bring a whole browser back up mid-game); Tick reloads it when it is visible.
                s.Monitor = m;
                s.Assignment = a;
                s.Window?.Move(_layer, m);
                s.Window?.Content?.Apply(_config.SettingsFor(a.ItemId));
                continue;
            }
            if (s?.Window != null) { s.Window.Dispose(); ReleaseMemory(); }
            s = new Slot { Monitor = m, Assignment = a, File = file };
            _slots[m.Id] = s;
            Load(s);
        }
        Desktop.Refresh(_layer);
        UpdateRawInput();
        Tick();
    }

    void Load(Slot s)
    {
        s.Unloaded = false;
        s.Failed = false;
        if (!System.IO.File.Exists(s.File))
        {
            Fail(s, null, "missing", "file missing: " + s.File);
            return;
        }
        try
        {
            var window = new WallpaperWindow(_layer, s.Monitor);
            s.Window = window;
            IWallpaper content = s.Assignment.Type switch
            {
                WallpaperType.Video => new VideoWallpaper(_composition, window, s.File),
                WallpaperType.Image => new ImageWallpaper(_composition, window, s.File),
                _ => new WebWallpaper(window, s.File, _config.EffectiveFps(s.Monitor.RefreshRate)),
            };
            window.Content = content;
            if (content is WebWallpaper web)
            {
                web.AudioWantedChanged += UpdateAudio;
                web.Crashed += () => _composition.Queue.TryEnqueue(() => Fail(s, content, "error", "web wallpaper crashed; reopening", 3));
            }
            if (s.Paused) content.SetPaused(true);
            var settings = _config.SettingsFor(s.Assignment.ItemId);
            content.StartAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Fail(s, content, "error", $"start failed on {s.Monitor.Label}: {t.Exception?.GetBaseException()}");
                    return;
                }
                if (s.Window?.Content == content) content.Apply(settings);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            content.Apply(settings);
            Log.Write($"{s.Assignment.Type} on {s.Monitor.Label} ({s.Monitor.Bounds}): {s.File}");
        }
        catch (Exception e)
        {
            Fail(s, s.Window?.Content, "error", $"load failed on {s.Monitor.Label}: {e}");
        }
    }

    /// <summary>
    /// Takes a broken wallpaper off the desktop (no empty window left behind) and schedules another try. Ignored when
    /// the slot already moved on to other content.
    /// </summary>
    void Fail(Slot s, IWallpaper content, string state, string message, int retrySeconds = 30)
    {
        if (content != null && s.Window?.Content != content) return;
        Log.Write(message);
        s.Window?.Dispose();
        s.Window = null;
        s.Failed = true;
        s.State = state;
        s.RetryAt = DateTime.Now.AddSeconds(retrySeconds);
    }

    void Unload(Slot s)
    {
        s.Window?.Dispose();
        s.Window = null;
        s.Unloaded = true;
        ReleaseMemory();
    }

    /// <summary>
    /// The player, its surfaces and the composition objects are WinRT objects whose native memory (decoder buffers,
    /// hundreds of MB for 4K) is only freed when .NET collects their wrappers. After taking a wallpaper down, collect
    /// now instead of whenever the GC feels like it, once the compositor has had a moment to drop its references.
    /// </summary>
    void ReleaseMemory()
    {
        if (_releaseTimer == null)
        {
            // Kept in a field: a timer only referenced by a local could be collected before it fires.
            _releaseTimer = _composition.Queue.CreateTimer();
            _releaseTimer.Interval = TimeSpan.FromMilliseconds(500);
            _releaseTimer.IsRepeating = false;
            _releaseTimer.Tick += (_, _) =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            };
        }
        _releaseTimer.Stop();
        _releaseTimer.Start();
    }

    // ---------------------------------------------------------------- pause rules

    void Tick()
    {
        if (_exiting || _layer == IntPtr.Zero) return;
        if (!Win32.IsWindow(_layer)) { ScheduleRebuild(500); return; }

        bool ignoreScreen = AppPaths.TestMode && Environment.GetEnvironmentVariable("FUNDOVIVO_TEST_NOPAUSE") == "1";
        var cover = ignoreScreen ? new Dictionary<string, MonitorCover>() : PauseMonitor.Evaluate(_monitors, _pid);
        bool anyFullscreen = !ignoreScreen && (PauseMonitor.ExclusiveFullscreen() || cover.Values.Any(c => c == MonitorCover.Fullscreen));
        var p = _config.Pause;

        foreach (var s in _slots.Values)
        {
            if (s.Failed)
            {
                if (DateTime.Now >= s.RetryAt) Load(s);
                if (s.Failed) continue;
            }
            cover.TryGetValue(s.Monitor.Id, out var c);
            if (p.FullscreenPausesAll && anyFullscreen) c = MonitorCover.Fullscreen;

            string state; bool pause = false, unload = false;
            if (_config.PausedByUser) { state = "paused-user"; pause = true; }
            else if (_locked) { state = "paused-locked"; pause = true; }
            else if (c == MonitorCover.Fullscreen && p.WhenFullscreen == FullscreenAction.Unload) { state = "unloaded-fullscreen"; unload = true; }
            else if (c == MonitorCover.Fullscreen && p.WhenFullscreen == FullscreenAction.Pause) { state = "paused-fullscreen"; pause = true; }
            else if (c == MonitorCover.Covered && p.WhenCovered == CoveredAction.Pause) { state = "paused-covered"; pause = true; }
            else state = "playing";

            if (unload)
            {
                if (!s.Unloaded) Unload(s);
            }
            else
            {
                if (s.Unloaded) { s.Paused = pause; Load(s); }
                if (s.Paused != pause)
                {
                    s.Paused = pause;
                    s.Window?.Content?.SetPaused(pause);
                }
            }
            s.State = state;
        }
        UpdateAudio();
        WriteState();
    }

    void UpdateAudio()
    {
        bool needed = _config.WebAudio && _slots.Values.Any(s => !s.Paused && !s.Unloaded && s.Window?.Content is WebWallpaper w && w.WantsAudio);
        if (needed && !_audio.Running) { _audio.Start(); Log.Write("listening to the PC audio for web wallpapers"); }
        else if (!needed && _audio.Running) { _audio.Stop(); Log.Write("stopped listening to audio"); }
    }

    void WriteState()
    {
        var monitors = new JsonObject();
        foreach (var s in _slots.Values)
            monitors[s.Monitor.Id] = new JsonObject { ["state"] = s.State, ["file"] = s.File, ["type"] = s.Assignment.Type.ToString().ToLowerInvariant() };
        var root = new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["version"] = AppPaths.Version,
            ["pausedByUser"] = _config.PausedByUser,
            ["audio"] = _audio.Running,
            ["monitors"] = monitors,
        };
        string text = root.ToJsonString();
        if (text == _lastState) return;
        try
        {
            // Written to a temp file and moved, so the window never reads half a file.
            string tmp = AppPaths.StateFile + ".tmp";
            System.IO.File.WriteAllText(tmp, text);
            System.IO.File.Move(tmp, AppPaths.StateFile, true);
            _lastState = text;
        }
        catch (Exception e) { Log.Write("state not written: " + e.Message); }
        int playing = _slots.Values.Count(s => s.State == "playing");
        _tray?.SetTip(_config.PausedByUser ? $"{AppPaths.AppName}: pausado" :
            _slots.Count == 0 ? $"{AppPaths.AppName}: nenhum wallpaper escolhido" : $"{AppPaths.AppName}: {playing} de {_slots.Count} tocando");
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return;
        // A burst of events (alt-tab, a game starting) becomes one evaluation shortly after.
        _soon.Stop();
        _soon.Start();
    }

    // ---------------------------------------------------------------- mouse for web wallpapers

    void UpdateRawInput()
    {
        bool wanted = _config.WebMouse && _slots.Values.Any(s => s.Assignment.Type == WallpaperType.Web);
        if (wanted == _rawInputOn) return;
        const uint RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1;
        var dev = new[] { new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = wanted ? RIDEV_INPUTSINK : RIDEV_REMOVE, hwndTarget = wanted ? Handle : IntPtr.Zero } };
        if (Win32.RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>())) _rawInputOn = wanted;
    }

    unsafe void OnRawInput(IntPtr lParam)
    {
        uint size = 0;
        uint header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        Win32.GetRawInputData(lParam, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref size, header);
        if (size == 0 || size > 256) return;
        byte* buffer = stackalloc byte[(int)size];
        if (Win32.GetRawInputData(lParam, 0x10000003, (IntPtr)buffer, ref size, header) != size) return;
        var head = *(RAWINPUTHEADER*)buffer;
        if (head.dwType != 0) return;
        var mouse = *(RAWMOUSE*)(buffer + header);

        Win32.GetCursorPos(out var pt);
        ushort buttons = mouse.usButtonFlags;
        if (buttons == 0 && pt.X == _lastMouse.X && pt.Y == _lastMouse.Y) return;
        _lastMouse = pt;

        var target = _slots.Values.FirstOrDefault(s => !s.Paused && s.Window?.Content is WebWallpaper && s.Monitor.Bounds.Contains(pt.X, pt.Y));
        if (target?.Window?.Content is not WebWallpaper web || web.InputWindow == IntPtr.Zero) return;
        if (!Desktop.IsDesktopWindow(Win32.WindowFromPoint(pt))) return;

        int x = pt.X - target.Monitor.Bounds.Left, y = pt.Y - target.Monitor.Bounds.Top;
        IntPtr pos = (IntPtr)((y << 16) | (x & 0xFFFF));
        int keys = 0;
        if (Win32.GetAsyncKeyState(0x01) < 0) keys |= 0x1;
        if (Win32.GetAsyncKeyState(0x02) < 0) keys |= 0x2;
        if (Win32.GetAsyncKeyState(0x04) < 0) keys |= 0x10;
        IntPtr input = web.InputWindow;
        Win32.PostMessageW(input, Win32.WM_MOUSEMOVE, (IntPtr)keys, pos);
        if ((buttons & 0x3F) != 0 && (DateTime.Now - _lastClickLog).TotalSeconds > 5)
        {
            _lastClickLog = DateTime.Now;
            Log.Write($"click forwarded to the web wallpaper on {target.Monitor.Label} at {x},{y}");
        }
        if ((buttons & 0x1) != 0) Win32.PostMessageW(input, Win32.WM_LBUTTONDOWN, (IntPtr)(keys | 0x1), pos);
        if ((buttons & 0x2) != 0) Win32.PostMessageW(input, Win32.WM_LBUTTONUP, (IntPtr)keys, pos);
        if ((buttons & 0x4) != 0) Win32.PostMessageW(input, Win32.WM_RBUTTONDOWN, (IntPtr)(keys | 0x2), pos);
        if ((buttons & 0x8) != 0) Win32.PostMessageW(input, Win32.WM_RBUTTONUP, (IntPtr)keys, pos);
        if ((buttons & 0x10) != 0) Win32.PostMessageW(input, Win32.WM_MBUTTONDOWN, (IntPtr)(keys | 0x10), pos);
        if ((buttons & 0x20) != 0) Win32.PostMessageW(input, Win32.WM_MBUTTONUP, (IntPtr)keys, pos);
    }

    // ---------------------------------------------------------------- window messages

    protected override unsafe IntPtr WndProc(uint msg, IntPtr w, IntPtr l)
    {
        if (msg == _taskbarCreated)
        {
            // Explorer restarted: the WorkerW and our children died with it.
            _tray?.Add();
            ScheduleRebuild(2000);
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case TrayIcon.CallbackMessage:
                int mouse = (int)(l.ToInt64() & 0xFFFF);
                if (mouse == Win32.WM_LBUTTONUP) OpenSettings();
                else if (mouse == Win32.WM_RBUTTONUP || mouse == Win32.WM_CONTEXTMENU) TrayMenu();
                return IntPtr.Zero;
            case Win32.WM_COPYDATA:
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
                string text = cds.cbData > 0 ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)cds.lpData, cds.cbData)) : "";
                _composition.Queue.TryEnqueue(() => Command(text));
                return new IntPtr(1);
            case Win32.WM_DISPLAYCHANGE:
            case Win32.WM_DPICHANGED:
                ScheduleRebuild(1200);
                break;
            case Win32.WM_SETTINGCHANGE:
                if (w.ToInt64() == 0x2F /* SPI_SETWORKAREA */) ScheduleRebuild(800);
                break;
            case Win32.WM_WTSSESSION_CHANGE:
                if (w.ToInt64() == 7) { _locked = true; Tick(); }
                else if (w.ToInt64() == 8) { _locked = false; Tick(); }
                break;
            case Win32.WM_INPUT:
                OnRawInput(l);
                break;
        }
        return base.WndProc(msg, w, l);
    }

    unsafe void Command(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            switch (node?["cmd"]?.ToString())
            {
                case "reload":
                    Rebuild(false);
                    break;
                case "settings":
                    // Fit, volume or Wallpaper Engine properties changed: apply without reloading anything.
                    _config = ConfigStore.Load();
                    foreach (var s in _slots.Values)
                    {
                        s.Window?.Content?.Apply(_config.SettingsFor(s.Assignment.ItemId));
                        if (s.Window?.Content is WebWallpaper web) web.SetFps(_config.EffectiveFps(s.Monitor.RefreshRate));
                    }
                    UpdateRawInput();
                    Tick();
                    break;
                case "quit":
                    Exit();
                    break;
            }
        }
        catch (Exception e) { Log.Write("command failed: " + e.Message); }
    }

    void TrayMenu()
    {
        bool paused = _config.PausedByUser;
        int choice = _tray.ShowMenu((1, "Abrir o Fundo Vivo", true), (2, paused ? "Continuar" : "Pausar tudo", true), (0, null, true), (3, "Sair", true));
        switch (choice)
        {
            case 1: OpenSettings(); break;
            case 2:
                _config = ConfigStore.Load();
                _config.PausedByUser = !paused;
                ConfigStore.Save(_config);
                Tick();
                break;
            case 3: Exit(); break;
        }
    }

    static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--settings") { UseShellExecute = false }); }
        catch (Exception e) { Log.Write("open settings: " + e.Message); }
    }

    void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _tick.Stop();
        _audio.Stop();
        foreach (var h in _hooks) Win32.UnhookWinEvent(h);
        foreach (var s in _slots.Values) s.Window?.Dispose();
        _slots.Clear();
        Desktop.Refresh(_layer);
        Win32.WTSUnRegisterSessionNotification(Handle);
        _tray?.Dispose();
        try { System.IO.File.Delete(AppPaths.StateFile); } catch { }
        Log.Write("engine stopped");
        Dispose();
        _composition.Dispose();
        Win32.PostQuitMessage(0);
    }

    /// <summary>Sends a command to the running engine. False when it is not running.</summary>
    public static unsafe bool Send(string json)
    {
        IntPtr target = Win32.FindWindowW(WindowClass, null);
        if (target == IntPtr.Zero) return false;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        fixed (byte* p = bytes)
        {
            var cds = new COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = bytes.Length, lpData = (IntPtr)p };
            Win32.SendMessageW(target, Win32.WM_COPYDATA, IntPtr.Zero, ref cds);
        }
        return true;
    }

    public static bool IsRunning => Win32.FindWindowW(WindowClass, null) != IntPtr.Zero;
}
