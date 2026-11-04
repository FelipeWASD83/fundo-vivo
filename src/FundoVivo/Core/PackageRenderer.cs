using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FundoVivo.Engine;
using FundoVivo.Native;
using Microsoft.Web.WebView2.Core;

namespace FundoVivo.Core;

/// <summary>
/// Renders a .vivo into a video loop, frame by frame: the same runtime that plays it live, in a WebView2 of its own
/// (outside every monitor), is asked for each frame by its time (`mode=render`), reads the pixels into a buffer it
/// shares with this process, and the frames go straight into ffmpeg (NVENC, like the scenes). Every animation of a
/// .vivo repeats within its loop, so the frame after the last is the first again and no crossfade is needed.
/// Runs in the conversion worker (FundoVivo.exe --convert-scenes), never inside the engine.
/// </summary>
public static class PackageRenderer
{
    /// <summary>Extra picture per side for the pan, as a fraction of the monitor, from the file's parallax.</summary>
    public static double MarginFor(double parallax) => parallax <= 0 ? 0 : Math.Clamp(parallax, 0.02, 0.15);

    public static string OutputPath(string itemId, string title, string file, int w, int h, int fps) =>
        Path.Combine(Ensure(Path.Combine(AppPaths.ConvertedFolder, "vivo")),
            $"{Safe(title)}_{w}x{h}_{Hash(itemId + "|" + Packages.Stamp(file) + "|" + fps)}.mp4");

    public static SceneResult Render(SceneJob job, IProgress<(double, string)> progress, CancellationToken cancel) =>
        OnUiThread(() => RenderAsync(job, progress, cancel));

    /// <summary>One frame to a PNG (previews and tests): `FundoVivo.exe --vivo-still file.vivo out.png width height [time] [margin]`.</summary>
    public static void Still(string file, string png, int width, int height, double time, double margin, bool fakeAudio = false) =>
        OnUiThread(async () =>
        {
            await using var page = await RenderPage.OpenAsync(file, width, height, margin, CancellationToken.None, fakeAudio);
            await page.DrawAsync(time, 0, CancellationToken.None);
            page.SavePng(png);
            return 0;
        });

    static async Task<SceneResult> RenderAsync(SceneJob job, IProgress<(double, string)> progress, CancellationToken cancel)
    {
        string ffmpeg = VideoConverter.FindFfmpeg() ?? throw new InvalidOperationException(Loc.T("tool_ffmpeg_missing"));
        var info = Packages.Inspect(job.PackageFile) ?? throw new InvalidOperationException(Loc.T("tool_vivo_unreadable"));
        int fps = Math.Clamp(job.Fps, 10, 60);
        double margin = MarginFor(info.Parallax);
        int w = SceneConverter.Even(job.Width * (1 + 2 * margin)), h = SceneConverter.Even(job.Height * (1 + 2 * margin));
        int frames = Math.Max(1, (int)Math.Round(info.Loop * fps));
        string output = OutputPath(job.ItemId, job.SceneTitle, job.PackageFile, job.Width, job.Height, fps);
        string temp = output + ".part.mp4";

        progress.Report((0.02, Loc.T("tool_vivo_opening")));
        await using var page = await RenderPage.OpenAsync(job.PackageFile, w, h, margin, cancel);
        if (page.Warnings.Count > 0) Log.Write("vivo warnings: " + string.Join(" | ", page.Warnings));

        string lastError = "";
        foreach (bool nvenc in new[] { true, false })
        {
            string args = $"-y -hide_banner -nostats -loglevel error -f rawvideo -pix_fmt rgba -s {w}x{h} -framerate {fps} -i pipe:0 " +
                          "-vf vflip,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p " +
                          // capped: a thousand raindrops at 60 fps reached 60 Mbit/s and half the GPU's video decoder
                          $"{SceneConverter.Encoder(w, h, nvenc, cq: 21)} -maxrate 30M -bufsize 60M -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv " +
                          $"-r {fps} -movflags +faststart -an \"{temp}\"";
            var psi = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            var errors = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
            var stdin = p.StandardInput.BaseStream;
            var clock = Stopwatch.StartNew();
            bool written = true;
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    await page.DrawAsync(i / (double)fps, i, cancel);
                    page.WriteTo(stdin);
                    progress.Report((0.04 + 0.94 * (i + 1) / frames, Loc.T("tool_vivo_rendering")));
                }
                stdin.Close();
            }
            catch (IOException)
            {
                // ffmpeg quit early (most likely NVENC refused this size or this machine): try the software encoder
                written = false;
            }
            await p.WaitForExitAsync(CancellationToken.None);
            cancel.ThrowIfCancellationRequested();
            if (written && p.ExitCode == 0 && File.Exists(temp))
            {
                File.Move(temp, output, true);
                Log.Write($"vivo render: {frames} frames {w}x{h} in {clock.Elapsed.TotalSeconds:0.0} s ({frames / clock.Elapsed.TotalSeconds:0.0} fps), {(nvenc ? "nvenc" : "software")}");
                progress.Report((1, Loc.T("tool_done")));
                return new SceneResult { Output = output, Width = w, Height = h, Margin = margin, Fps = fps };
            }
            lastError = errors.ToString();
            Log.Write($"vivo render with {(nvenc ? "nvenc" : "software")} failed: {lastError.Trim()}");
            try { File.Delete(temp); } catch { }
        }
        throw new InvalidOperationException(Loc.T("tool_vivo_ffmpeg_failed", lastError.Trim()));
    }

    /// <summary>WebView2 needs a thread with a message loop; the worker has none, so this runs one for the job.</summary>
    static T OnUiThread<T>(Func<Task<T>> work)
    {
        T result = default;
        Exception error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Func<Task>(async () =>
            {
                try { result = await work(); }
                catch (Exception e) { error = e; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    static string Ensure(string d) { Directory.CreateDirectory(d); return d; }
    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..10].ToLowerInvariant();
    static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "vivo") sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
        string r = sb.ToString();
        return r.Length > 40 ? r[..40] : r;
    }

    /// <summary>The runtime page in render mode, and the buffer it draws each frame into.</summary>
    sealed class RenderPage : IAsyncDisposable
    {
        HostWindow _window;
        CoreWebView2Controller _controller;
        CoreWebView2 _web;
        CoreWebView2SharedBuffer _buffer;
        readonly int _width, _height;
        readonly Queue<JsonObject> _messages = new();
        TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Warnings { get; } = new();

        readonly bool _fakeAudio;
        RenderPage(int w, int h, bool fakeAudio) { _width = w; _height = h; _fakeAudio = fakeAudio; }

        public static async Task<RenderPage> OpenAsync(string file, int width, int height, double margin, CancellationToken cancel, bool fakeAudio = false)
        {
            var page = new RenderPage(width, height, fakeAudio);
            try
            {
                await page.StartAsync(file, margin, cancel);
                return page;
            }
            catch
            {
                await page.DisposeAsync();
                throw;
            }
        }

        async Task StartAsync(string file, double margin, CancellationToken cancel)
        {
            string folder = Path.GetDirectoryName(Packages.Prepare(file));
            _window = new HostWindow();
            // Its own profile: the engine's WebView2 runs with other switches, and two processes can only share a
            // profile with the same ones. The occlusion switch keeps Edge from treating the hidden page as covered.
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Local, "webview-render"), new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion,msSmartScreenProtection --disable-background-timer-throttling --disable-renderer-backgrounding",
            });
            _controller = await env.CreateCoreWebView2ControllerAsync(_window.Handle);
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, 64, 64);
            _web = _controller.CoreWebView2;
            _web.Settings.AreDefaultContextMenusEnabled = false;
            _web.Settings.AreDevToolsEnabled = false;
            _web.SetVirtualHostNameToFolderMapping("render.fundovivo", folder, CoreWebView2HostResourceAccessKind.Allow);
            _web.WebMessageReceived += (_, e) =>
            {
                try { if (JsonNode.Parse(e.WebMessageAsJson) is JsonObject m) { _messages.Enqueue(m); _arrived.TrySetResult(); } } catch { }
            };
            string m = margin.ToString("0.#####", CultureInfo.InvariantCulture);
            _web.Navigate($"https://render.fundovivo/index.html?mode=render&w={_width}&h={_height}&margin={m}" + (_fakeAudio ? "&fakeaudio=1" : ""));

            var ready = await NextAsync("vivo-ready", TimeSpan.FromSeconds(30), cancel);
            if (ready["warnings"] is JsonArray list) Warnings.AddRange(list.Select(n => n?.ToString()));
            _buffer = env.CreateSharedBuffer((ulong)_width * (ulong)_height * 4);
            _web.PostSharedBufferToScript(_buffer, CoreWebView2SharedBufferAccess.ReadWrite, null);
            await NextAsync("buffer-ok", TimeSpan.FromSeconds(15), cancel);
        }

        public async Task DrawAsync(double t, int i, CancellationToken cancel)
        {
            _web.PostWebMessageAsJson($"{{\"type\":\"frame\",\"t\":{t.ToString("R", CultureInfo.InvariantCulture)},\"i\":{i}}}");
            await NextAsync("frame-done", TimeSpan.FromSeconds(20), cancel);
        }

        /// <summary>The frame as the page left it: RGBA rows from the bottom up.</summary>
        public unsafe void WriteTo(Stream stream) =>
            stream.Write(new ReadOnlySpan<byte>((void*)_buffer.Buffer, checked((int)_buffer.Size)));

        public unsafe void SavePng(string png)
        {
            int stride = _width * 4;
            var bgra = new byte[stride * _height];
            var src = new ReadOnlySpan<byte>((void*)_buffer.Buffer, checked((int)_buffer.Size));
            for (int y = 0; y < _height; y++)
            {
                var row = src.Slice((_height - 1 - y) * stride, stride);
                for (int x = 0; x < stride; x += 4)
                {
                    int o = y * stride + x;
                    bgra[o] = row[x + 2]; bgra[o + 1] = row[x + 1]; bgra[o + 2] = row[x]; bgra[o + 3] = 255;
                }
            }
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(_width, _height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, stride);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png)));
            using var fs = File.Create(png);
            encoder.Save(fs);
        }

        async Task<JsonObject> NextAsync(string type, TimeSpan timeout, CancellationToken cancel)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                while (_messages.Count > 0)
                {
                    var m = _messages.Dequeue();
                    string got = m["type"]?.ToString();
                    if (got == "error") throw new InvalidOperationException(Loc.T("tool_vivo_page_error", m["message"]?.ToString() ?? ""));
                    if (got == "warning") { Warnings.Add(m["message"]?.ToString()); continue; }
                    if (got == type) return m;
                }
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) throw new TimeoutException(Loc.T("tool_vivo_page_timeout", type));
                _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_messages.Count > 0) continue;
                await Task.WhenAny(_arrived.Task, Task.Delay(left, cancel));
                cancel.ThrowIfCancellationRequested();
            }
        }

        public ValueTask DisposeAsync()
        {
            try { _controller?.Close(); } catch { }
            try { _buffer?.Dispose(); } catch { }
            _window?.Dispose();
            _controller = null;
            _web = null;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A tiny window outside every monitor to hold the WebView2; it never shows on the taskbar.</summary>
    sealed class HostWindow : NativeWindow
    {
        public HostWindow()
        {
            const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            Create("FundoVivoRender", WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WS_POPUP | WS_VISIBLE, -32000, -32000, 64, 64, IntPtr.Zero);
        }
    }
}
