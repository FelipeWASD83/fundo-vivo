using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FundoVivo.Core;

/// <summary>
/// Re-encodes a video at the monitor's exact size (and crop, for Fill). A 4K video on a 3440x1440 ultrawide is
/// cropped anyway, so the smaller copy looks the same and costs a fraction to decode. The original is never touched.
/// </summary>
public static class VideoConverter
{
    public static string FindFfmpeg()
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    public static string OutputPath(string source, int w, int h, FitMode fit)
    {
        string key = $"{source.ToLowerInvariant()}|{AppConfig.SafeLength(source)}|{w}x{h}|{fit}";
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(AppPaths.ConvertedFolder, $"{Path.GetFileNameWithoutExtension(source)}_{w}x{h}_{hash}.mp4");
    }

    /// <summary>Runs ffmpeg; progress goes 0..1. Tries the RTX encoder first and falls back to x264.</summary>
    public static async Task<string> ConvertAsync(string source, int w, int h, FitMode fit, double durationSeconds, IProgress<double> progress, CancellationToken cancel)
    {
        string ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException(Loc.T("tool_ffmpeg_missing"));
        string output = OutputPath(source, w, h, fit);
        string temp = output + ".part.mp4";
        string filter = fit switch
        {
            FitMode.Fill => $"scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h}",
            FitMode.Fit => $"scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,pad=ceil(iw/2)*2:ceil(ih/2)*2",
            _ => $"scale={w}:{h}:flags=lanczos",
        };
        string[] encoders =
        {
            "-c:v h264_nvenc -preset p6 -tune hq -rc vbr -cq 19 -b:v 0 -profile:v high",
            "-c:v libx264 -preset medium -crf 18 -profile:v high",
        };
        string lastError = "";
        foreach (string enc in encoders)
        {
            cancel.ThrowIfCancellationRequested();
            // AAC because copying the sound fails for WMA and Vorbis, which mp4 cannot carry.
            string args = $"-y -hide_banner -nostats -loglevel error -progress pipe:1 -i \"{source}\" -map 0:v:0 -map 0:a:0? -vf \"{filter}\" {enc} -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \"{temp}\"";
            try
            {
                var (ok, err) = await RunAsync(ffmpeg, args, durationSeconds, progress, cancel);
                if (ok && File.Exists(temp))
                {
                    File.Move(temp, output, true);
                    return output;
                }
                lastError = err;
            }
            finally
            {
                // A cancelled or failed run leaves no half file behind.
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        throw new InvalidOperationException(Loc.T("tool_ffmpeg_failed_convert", lastError.Trim()));
    }

    static async Task<(bool ok, string error)> RunAsync(string exe, string args, double duration, IProgress<double> progress, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        p.PriorityClass = ProcessPriorityClass.BelowNormal;
        var errors = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
        string line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
        {
            if (line.StartsWith("out_time_us=") && duration > 0 &&
                long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long us))
                progress?.Report(Math.Clamp(us / 1e6 / duration, 0, 1));
        }
        await p.WaitForExitAsync();
        cancel.ThrowIfCancellationRequested();
        return (p.ExitCode == 0, errors.ToString());
    }
}
