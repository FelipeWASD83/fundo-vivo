using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace FundoVivo.Core;

/// <summary>
/// An animated GIF of the user's becomes a video, so it moves (the image wallpaper shows only the first frame).
/// Small GIFs (pixel art, mostly) are enlarged by a whole factor without smoothing and the player does the rest;
/// big ones are fitted to the monitor. The sequence is repeated whole until the video is at least 10 s long, so the
/// player's restart never shows as a hitch every second. Runs in the conversion worker, like the scenes.
/// </summary>
public static class GifConverter
{
    const double MinimumLength = 10;

    public static bool IsGif(string file) => string.Equals(Path.GetExtension(file), ".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>More than one frame. Reads the frame table only, not the pictures.</summary>
    public static bool IsAnimated(string file)
    {
        if (!IsGif(file)) return false;
        try
        {
            using var s = File.OpenRead(file);
            return BitmapDecoder.Create(s, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames.Count > 1;
        }
        catch { return false; }
    }

    public static string OutputPath(string file, int w, int h) =>
        Path.Combine(Ensure(Path.Combine(AppPaths.ConvertedFolder, "gif")),
            $"{Safe(Path.GetFileNameWithoutExtension(file))}_{w}x{h}_{Hash(Path.GetFullPath(file).ToLowerInvariant() + "|" + Packages.Stamp(file))}.mp4");

    public static async Task<SceneResult> ConvertAsync(SceneJob job, IProgress<(double, string)> progress, CancellationToken cancel)
    {
        string ffmpeg = VideoConverter.FindFfmpeg() ?? throw new InvalidOperationException(Loc.T("tool_ffmpeg_missing"));
        string file = job.PackageFile;
        progress.Report((0.05, Loc.T("tool_gif_reading")));
        var (gw, gh, seconds) = Probe(ffmpeg, file);
        if (gw <= 0 || gh <= 0) throw new InvalidOperationException(Loc.T("tool_gif_unreadable"));
        int repeats = seconds > 0 ? Math.Max(1, (int)Math.Ceiling(MinimumLength / seconds)) : 1;

        string scale;
        int w, h;
        if (gh * 2 <= job.Height)
        {
            int k = Math.Max(1, job.Height / gh);
            w = Even(gw * k); h = Even(gh * k);
            scale = $"scale={w}:{h}:flags=neighbor";
        }
        else
        {
            // the monitor's height, same shape; the player fills or fits it like any video
            double k = (double)job.Height / gh;
            w = Even(gw * k); h = Even(gh * k);
            scale = $"scale={w}:{h}:flags=lanczos";
        }
        string output = OutputPath(file, job.Width, job.Height);
        string temp = output + ".part.mp4";
        string lastError = "";
        foreach (bool nvenc in new[] { true, false })
        {
            progress.Report((0.15, Loc.T("tool_gif_converting")));
            string args = $"-y -hide_banner -nostats -loglevel error -stream_loop {repeats - 1} -i \"{file}\" " +
                          $"-vf {scale},scale=out_color_matrix=bt709:out_range=tv,format=yuv420p -fps_mode vfr " +
                          $"{SceneConverter.Encoder(w, h, nvenc, cq: 19)} -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv " +
                          $"-movflags +faststart -an \"{temp}\"";
            using var p = Process.Start(new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
            using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
            string err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(CancellationToken.None);
            cancel.ThrowIfCancellationRequested();
            if (p.ExitCode == 0 && File.Exists(temp))
            {
                File.Move(temp, output, true);
                Log.Write($"gif converted: {file} {gw}x{gh} x{repeats} ({seconds:0.##} s each) -> {w}x{h}, {(nvenc ? "nvenc" : "software")}");
                progress.Report((1, Loc.T("tool_done")));
                return new SceneResult { Output = output, Width = w, Height = h, Margin = 0, Fps = 0 };
            }
            lastError = err;
            try { File.Delete(temp); } catch { }
        }
        throw new InvalidOperationException(Loc.T("tool_gif_ffmpeg_failed", lastError.Trim()));
    }

    /// <summary>Size and length (one pass of the animation) through ffprobe, next to ffmpeg.</summary>
    static (int w, int h, double seconds) Probe(string ffmpeg, string file)
    {
        string probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
        if (!File.Exists(probe)) probe = "ffprobe";
        try
        {
            using var p = Process.Start(new ProcessStartInfo(probe, $"-v error -select_streams v:0 -show_entries stream=width,height:format=duration -of default=nw=1 \"{file}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            int w = 0, h = 0; double d = 0;
            foreach (string line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "width") int.TryParse(kv[1], out w);
                else if (kv[0] == "height") int.TryParse(kv[1], out h);
                else if (kv[0] == "duration") double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            }
            return (w, h, d);
        }
        catch { return (0, 0, 0); }
    }

    static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
    static string Ensure(string d) { Directory.CreateDirectory(d); return d; }
    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..10].ToLowerInvariant();
    static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "gif") sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
        string r = sb.ToString();
        return r.Length > 40 ? r[..40] : r;
    }
}
