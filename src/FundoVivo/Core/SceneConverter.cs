using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FundoVivo.Native;

namespace FundoVivo.Core;

public sealed class SceneJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SceneFolder { get; set; }
    public string SceneTitle { get; set; }
    public string MonitorId { get; set; }
    public string MonitorName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Duration { get; set; } = 20;
    public double Crossfade { get; set; } = 1.5;
    public bool Assign { get; set; } = true;
    /// <summary>"package" for a .vivo rendered frame by frame (PackageRenderer); empty for a Wallpaper Engine scene.</summary>
    public string Kind { get; set; }
    public string ItemId { get; set; }
    public string PackageFile { get; set; }
    public int Fps { get; set; } = 60;
}

public sealed class SceneResult
{
    public string Output { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Margin { get; init; }
    public int Fps { get; init; }
}

/// <summary>
/// Turns a Wallpaper Engine scene into a seamless video loop, drawn by Wallpaper Engine itself:
///   1. unpacks the scene into a work folder with the camera parallax switched off (so the mouse never ends up in
///      the recording; the parallax comes back at playback as a pan of an oversized video);
///   2. asks the running Wallpaper Engine to play that copy in its own window ("-playInWindow"), at the size the
///      monitor needs plus the parallax margin, and moves the window outside every monitor (Wallpaper Engine ignores
///      -x/-y, so it shows for a moment at the top left first). Only moved, never resized: resizing it makes
///      Wallpaper Engine pick a size of its own;
///   3. records that window with Windows.Graphics.Capture (ffmpeg's gfxcapture, no cursor) on NVENC;
///   4. cuts the loop: the recording runs `duration + crossfade`, and its last `crossfade` seconds are blended into
///      its first ones, so the end leads into the start without a visible seam.
/// Runs in its own process (FundoVivo.exe --convert-scenes), never inside the engine.
/// </summary>
public static class SceneConverter
{
    const string WindowClass = "WPEOverlappedWallpaper";
    const double Warmup = 5;   // scenes fade in and particle systems start empty

    public static string FindWallpaperEngine()
    {
        foreach (var p in Process.GetProcessesByName("wallpaper64"))
            using (p)
                try { if (p.MainModule?.FileName is { } f && File.Exists(f)) return f; } catch { }
        foreach (string dir in Library.WallpaperEngineFolders())
        {
            // ...\steamapps\workshop\content\431960 -> ...\steamapps\common\wallpaper_engine
            var steamapps = Directory.GetParent(dir)?.Parent?.Parent;
            string exe = steamapps == null ? null : Path.Combine(steamapps.FullName, "common", "wallpaper_engine", "wallpaper64.exe");
            if (exe != null && File.Exists(exe)) return exe;
        }
        return null;
    }

    /// <summary>The frame rate Wallpaper Engine draws at (its own setting); the recording follows it.</summary>
    public static int WallpaperEngineFps(string exe)
    {
        try
        {
            string cfg = Path.Combine(Path.GetDirectoryName(exe), "config.json");
            var root = JsonNode.Parse(File.ReadAllText(cfg))?.AsObject();
            foreach (var (_, user) in root ?? new JsonObject())
                if (user?["general"]?["user"]?["fps"] is JsonValue v && v.TryGetValue(out int fps)) return Math.Clamp(fps, 10, 60);
        }
        catch { }
        return 30;
    }

    public static double MarginFor(SceneProject scene) =>
        scene.Traits.HasFlag(SceneTraits.Parallax) ? Math.Clamp(scene.ParallaxAmount * 0.25, 0.02, 0.15) : 0;

    public static string OutputPath(SceneProject scene, int w, int h) =>
        Path.Combine(Ensure(Path.Combine(AppPaths.ConvertedFolder, "scenes")),
            $"{Safe(scene.Title)}_{w}x{h}_{Hash(scene.Id)}.mp4");

    public static async Task<SceneResult> ConvertAsync(SceneJob job, IProgress<(double, string)> progress, CancellationToken cancel)
    {
        var scene = SceneProject.Load(job.SceneFolder) ?? throw new InvalidOperationException("Essa pasta não é uma cena do Wallpaper Engine.");
        string we = FindWallpaperEngine() ?? throw new InvalidOperationException("O Wallpaper Engine não foi encontrado neste PC.");
        string ffmpeg = VideoConverter.FindFfmpeg() ?? throw new InvalidOperationException("O ffmpeg não foi encontrado no PATH.");
        int fps = WallpaperEngineFps(we);
        double margin = MarginFor(scene);
        int finalW = Even(job.Width * (1 + 2 * margin)), finalH = Even(job.Height * (1 + 2 * margin));
        var (recW, recH) = FitWindow(finalW, finalH);
        double total = job.Duration + job.Crossfade;

        string work = Path.Combine(AppPaths.Local, "scene-work", job.Id);
        string raw = Path.Combine(work, "_recording.mp4");
        string output = OutputPath(scene, job.Width, job.Height);
        string title = "FundoVivoGravacao-" + job.Id;
        IntPtr window = IntPtr.Zero;
        try
        {
            progress.Report((0.02, "Preparando a cena"));
            scene.UnpackTo(work, disableParallax: true);
            await EnsureRunningAsync(we, cancel);

            progress.Report((0.05, "Abrindo no Wallpaper Engine"));
            for (int attempt = 0; ; attempt++)
            {
                Run(we, $"-control openWallpaper -file \"{Path.Combine(work, "project.json")}\" -playInWindow \"{title}\" -width {recW} -height {recH}");
                var sw = Stopwatch.StartNew();
                while ((window = Win32.FindWindowW(WindowClass, title)) == IntPtr.Zero)
                {
                    if (sw.Elapsed.TotalSeconds > 30) throw new InvalidOperationException("O Wallpaper Engine não abriu a janela da cena.");
                    await Task.Delay(30, cancel);
                }
                MoveOffscreen(window);
                Win32.GetClientRect(window, out var client);
                if (client.Width == recW && client.Height == recH) break;
                // Windows capped the size (taller or wider than the desktop allows): open again smaller, same shape.
                Log.Write($"scene window came out {client.Width}x{client.Height} instead of {recW}x{recH}");
                Close(we, title, window);
                window = IntPtr.Zero;
                if (attempt >= 2 || client.Width <= 0 || client.Height <= 0)
                    throw new InvalidOperationException($"O Wallpaper Engine abriu a janela com {client.Width}x{client.Height} em vez de {recW}x{recH}.");
                double s = Math.Min((double)client.Width / recW, (double)client.Height / recH);
                recW = Even(recW * s) - 2;
                recH = Even(recH * s) - 2;
            }

            progress.Report((0.08, "Esperando a cena carregar"));
            await Task.Delay(TimeSpan.FromSeconds(Warmup), cancel);

            progress.Report((0.1, "Gravando"));
            string tags = "setparams=range=tv:colorspace=bt470bg:color_primaries=bt709:color_trc=iec61966-2-1";
            string recordEncoder = Encoder(recW, recH, nvenc: true, cq: 16);
            string args = $"-y -hide_banner -nostats -loglevel error -progress pipe:1 -f lavfi -i gfxcapture=hwnd={(long)window}:capture_cursor=0:max_framerate={fps},{tags} " +
                          $"-t {F(total)} {recordEncoder} -fps_mode cfr -r {fps} \"{raw}\"";
            var (ok, err) = await RunFfmpegAsync(ffmpeg, args, total, p => progress.Report((0.1 + 0.6 * p, "Gravando")), cancel);
            if (!ok || !File.Exists(raw)) throw new InvalidOperationException("A gravação falhou: " + err.Trim());
            Close(we, title, window);
            window = IntPtr.Zero;

            progress.Report((0.72, "Emendando o loop"));
            string scale = recW == finalW && recH == finalH ? "" : $",scale={finalW}:{finalH}:flags=lanczos";
            string filter = $"[0:v]split[a][b];[a]trim=start={F(job.Crossfade)}:end={F(total)},setpts=PTS-STARTPTS[body];" +
                            $"[b]trim=start=0:end={F(job.Crossfade)},setpts=PTS-STARTPTS[head];" +
                            $"[body][head]xfade=transition=fade:duration={F(job.Crossfade)}:offset={F(job.Duration - job.Crossfade)}{scale},format=yuv420p[v]";
            string temp = output + ".part.mp4";
            bool done = false;
            string lastError = "";
            foreach (bool nvenc in new[] { true, false })
            {
                string finish = $"-y -hide_banner -nostats -loglevel error -progress pipe:1 -i \"{raw}\" -filter_complex \"{filter}\" -map [v] " +
                                $"{Encoder(finalW, finalH, nvenc, cq: 19)} -r {fps} -movflags +faststart -an \"{temp}\"";
                var (fine, ferr) = await RunFfmpegAsync(ffmpeg, finish, job.Duration, p => progress.Report((0.72 + 0.27 * p, "Emendando o loop")), cancel);
                if (fine && File.Exists(temp)) { File.Move(temp, output, true); done = true; break; }
                lastError = ferr;
            }
            if (!done) throw new InvalidOperationException("Não deu para fechar o loop: " + lastError.Trim());
            progress.Report((1, "Pronto"));
            return new SceneResult { Output = output, Width = finalW, Height = finalH, Margin = margin, Fps = fps };
        }
        finally
        {
            if (window != IntPtr.Zero) Close(we, title, window);
            try { if (File.Exists(output + ".part.mp4")) File.Delete(output + ".part.mp4"); } catch { }
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (Exception e) { Log.Write("scene work folder: " + e.Message); }
        }
    }

    /// <summary>
    /// Windows keeps a window no taller (or wider) than the whole desktop allows; the scene is then drawn smaller with
    /// the same shape and scaled up at the end (on the 1080x1920 monitor that is about 1%).
    /// </summary>
    static (int w, int h) FitWindow(int w, int h)
    {
        int maxW = Win32.GetSystemMetrics(59 /* SM_CXMAXTRACK */), maxH = Win32.GetSystemMetrics(60 /* SM_CYMAXTRACK */);
        var frame = new RECT(0, 0, 100, 100);
        Win32.AdjustWindowRectEx(ref frame, 0x00CF0000 /* WS_OVERLAPPEDWINDOW */, false, 0);
        int availW = maxW - (frame.Width - 100), availH = maxH - (frame.Height - 100);
        if (availW <= 0 || availH <= 0 || (w <= availW && h <= availH)) return (w, h);
        double s = Math.Min((double)availW / w, (double)availH / h);
        return (Even(w * s) - 2, Even(h * s) - 2);
    }

    static void MoveOffscreen(IntPtr window)
    {
        // Just right of the whole desktop: far enough to be invisible, near enough that Windows leaves the size alone.
        int right = Win32.GetSystemMetrics(76 /* SM_XVIRTUALSCREEN */) + Win32.GetSystemMetrics(78 /* SM_CXVIRTUALSCREEN */);
        int top = Win32.GetSystemMetrics(77 /* SM_YVIRTUALSCREEN */);
        Win32.SetWindowPos(window, IntPtr.Zero, right + 160, top, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    static void Close(string we, string title, IntPtr window)
    {
        try { Run(we, $"-control closeWallpaper -location \"{title}\""); } catch (Exception e) { Log.Write("close scene window: " + e.Message); }
        for (int i = 0; i < 50 && Win32.IsWindow(window); i++) Thread.Sleep(100);
    }

    static async Task EnsureRunningAsync(string we, CancellationToken cancel)
    {
        if (Process.GetProcessesByName("wallpaper64").Length > 0) return;
        Process.Start(new ProcessStartInfo(we) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(we) });
        for (int i = 0; i < 100 && Win32.FindWindowW("WPETrayWindow", null) == IntPtr.Zero; i++) await Task.Delay(200, cancel);
        await Task.Delay(1500, cancel);
    }

    static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        p?.WaitForExit(15000);
    }

    /// <summary>H.264 goes up to 4096 wide on NVENC; the ultrawide with a parallax margin needs HEVC (Windows has the decoder).</summary>
    internal static string Encoder(int w, int h, bool nvenc, int cq)
    {
        bool big = w > 4096 || h > 4096;
        if (nvenc) return $"-c:v {(big ? "hevc_nvenc" : "h264_nvenc")} -preset p5 -tune hq -rc vbr -cq {cq} -b:v 0";
        return big ? $"-c:v libx265 -preset medium -crf {cq + 2} -tag:v hvc1" : $"-c:v libx264 -preset medium -crf {cq - 1}";
    }

    static async Task<(bool ok, string error)> RunFfmpegAsync(string exe, string args, double seconds, Action<double> progress, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi);
        var errors = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
        string line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long us) && seconds > 0)
                progress(Math.Clamp(us / 1e6 / seconds, 0, 1));
        await p.WaitForExitAsync();
        cancel.ThrowIfCancellationRequested();
        return (p.ExitCode == 0, errors.ToString());
    }

    internal static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Ensure(string d) { Directory.CreateDirectory(d); return d; }
    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..10].ToLowerInvariant();
    static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "cena") sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
        string r = sb.ToString();
        return r.Length > 40 ? r[..40] : r;
    }
}
