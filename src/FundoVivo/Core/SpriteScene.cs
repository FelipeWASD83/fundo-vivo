using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace FundoVivo.Core;

/// <summary>
/// Scenes that are a single animated picture (the GIF scenes of Wallpaper Engine's template, pixel art loops): one
/// visible image object with no effect, covering the whole scene, whose texture is a sprite sheet with the time of
/// each frame. Those become a video straight from the sheet, without opening Wallpaper Engine: no window, no
/// recording, and a loop that is exact because it is the sheet's own sequence repeated whole.
/// </summary>
public static class SpriteScene
{
    /// <summary>The loop is repeated whole until the video is at least this long (a 2 s file restarting is a visible hitch).</summary>
    const double MinimumLength = 10;

    /// <summary>The texture entry when the scene qualifies, else null. Reads only JSON and the texture's first bytes.</summary>
    public static string Describe(Func<string, byte[]> entry, Func<string, int, byte[]> head, JsonObject scene)
    {
        var objects = (scene["objects"] as JsonArray)?.OfType<JsonObject>().Where(Visible).ToList();
        if (objects is not { Count: 1 } || objects[0]["image"] is not JsonValue imagePath) return null;
        var o = objects[0];
        foreach (string other in new[] { "particle", "text", "sound", "model", "light" })
            if (o[other] != null) return null;
        if (o["effects"] is JsonArray { Count: > 0 }) return null;
        if (Number(o["alpha"], 1) < 0.999 || (Text(o["color"]) is { } c && Vec(c) is { } col && col.Any(v => Math.Abs(v - 1) > 0.001))) return null;
        if (!Covers(o, scene["general"]?["orthogonalprojection"] as JsonObject)) return null;
        try
        {
            var model = JsonNode.Parse(Encoding.UTF8.GetString(entry(imagePath.ToString())), documentOptions: SceneProject.Lenient) as JsonObject;
            string materialPath = model?["material"]?.ToString();
            var material = JsonNode.Parse(Encoding.UTF8.GetString(entry(materialPath)), documentOptions: SceneProject.Lenient) as JsonObject;
            string texture = material?["passes"]?[0]?["textures"]?[0]?.ToString();
            if (string.IsNullOrEmpty(texture)) return null;
            string texPath = $"materials/{texture}.tex";
            var (format, flags) = WeTexture.Header(head(texPath, 128));
            return format == 0 && (flags & 4) != 0 ? texPath : null;
        }
        catch { return null; }
    }

    /// <summary>The image fills the scene: centred, the size of the projection, not scaled.</summary>
    static bool Covers(JsonObject o, JsonObject projection)
    {
        double[] size = Vec(Text(o["size"])), origin = Vec(Text(o["origin"])), scale = Vec(Text(o["scale"])) ?? new double[] { 1, 1, 1 };
        if (scale.Take(2).Any(s => Math.Abs(s - 1) > 0.001)) return false;
        double pw = Number(projection?["width"], 0), ph = Number(projection?["height"], 0);
        if (pw <= 0 || ph <= 0)
            return size == null;   // the old GIF template stores neither: the picture is the scene
        if (size == null || origin == null) return false;
        return Math.Abs(size[0] - pw) < 1.5 && Math.Abs(size[1] - ph) < 1.5 && Math.Abs(origin[0] - pw / 2) < 1.5 && Math.Abs(origin[1] - ph / 2) < 1.5;
    }

    /// <summary>The video for one monitor, or null when the sheet turns out to be something this does not read.</summary>
    public static async Task<SceneResult> ConvertAsync(SceneJob job, SceneProject scene, string texPath, IProgress<(double, string)> progress, CancellationToken cancel)
    {
        string ffmpeg = VideoConverter.FindFfmpeg() ?? throw new InvalidOperationException("O ffmpeg não foi encontrado no PATH.");
        progress.Report((0.02, "Lendo a folha de quadros"));
        WeTexture tex;
        try
        {
            byte[] bytes = scene.ReadPackageEntry(texPath);
            tex = await Task.Run(() => WeTexture.Read(bytes), cancel);
        }
        catch (NotSupportedException e)
        {
            Log.Write($"sprite scene {scene.Title}: {e.Message}; recording it in Wallpaper Engine instead");
            return null;
        }
        if (!tex.Animated) return null;
        var frames = tex.Frames;
        int fw = frames[0].Width, fh = frames[0].Height;
        if (frames.Any(f => f.Width != fw || f.Height != fh)) return null;

        // Equal frame times play at their own rate (0.06 s = 16.7 fps); uneven ones are laid on a 60 fps grid.
        bool even = frames.All(f => Math.Abs(f.Time - frames[0].Time) < 0.0005f) && frames[0].Time > 0.004f;
        double rate = even ? 1 / frames[0].Time : 60;
        var sequence = new List<int>();
        if (even) sequence.AddRange(Enumerable.Range(0, frames.Count));
        else
        {
            double t = 0;
            for (int k = 0; k < frames.Count; k++)
            {
                double next = t + Math.Max(0.001, frames[k].Time);
                int slots = (int)Math.Round(next * rate) - (int)Math.Round(t * rate);
                for (int s = 0; s < slots; s++) sequence.Add(k);
                t = next;
            }
            if (sequence.Count == 0) return null;
        }
        double loop = sequence.Count / rate;
        int repeats = Math.Max(1, (int)Math.Ceiling(MinimumLength / loop));

        double margin = SceneConverter.MarginFor(scene);
        int w = SceneConverter.Even(job.Width * (1 + 2 * margin)), h = SceneConverter.Even(job.Height * (1 + 2 * margin));
        string output = SceneConverter.OutputPath(scene, job.Width, job.Height);
        string temp = output + ".part.mp4";
        string filter = tex.PointFilter ? "neighbor" : "lanczos";
        string r = rate.ToString("0.######", CultureInfo.InvariantCulture);
        var frame = new byte[fw * fh * 4];
        string lastError = "";
        foreach (bool nvenc in new[] { true, false })
        {
            progress.Report((0.1, "Montando o vídeo"));
            // Covers the monitor like Wallpaper Engine does, cropping what is left over; pixel art stays sharp.
            string args = $"-y -hide_banner -nostats -loglevel error -f rawvideo -pix_fmt rgba -s {fw}x{fh} -framerate {r} -i pipe:0 " +
                          $"-vf scale={w}:{h}:force_original_aspect_ratio=increase:flags={filter},crop={w}:{h},scale=out_color_matrix=bt709:out_range=tv,format=yuv420p " +
                          $"{SceneConverter.Encoder(w, h, nvenc, cq: 19)} -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv " +
                          $"-r {r} -movflags +faststart -an \"{temp}\"";
            using var p = Process.Start(new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true });
            var errors = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
            bool written = true;
            try
            {
                var stdin = p.StandardInput.BaseStream;
                int total = sequence.Count * repeats, done = 0;
                for (int rep = 0; rep < repeats; rep++)
                    foreach (int k in sequence)
                    {
                        cancel.ThrowIfCancellationRequested();
                        tex.CopyFrame(frames[k], frame);
                        await stdin.WriteAsync(frame, cancel);
                        if (++done % 8 == 0) progress.Report((0.1 + 0.88 * done / total, "Montando o vídeo"));
                    }
                stdin.Close();
            }
            catch (IOException) { written = false; }
            await p.WaitForExitAsync(CancellationToken.None);
            cancel.ThrowIfCancellationRequested();
            if (written && p.ExitCode == 0 && File.Exists(temp))
            {
                File.Move(temp, output, true);
                Log.Write($"sprite scene {scene.Title}: {frames.Count} frames, loop {loop:0.###} s x{repeats} at {r} fps, {w}x{h}, {(nvenc ? "nvenc" : "software")}, {filter}");
                progress.Report((1, "Pronto"));
                return new SceneResult { Output = output, Width = w, Height = h, Margin = margin, Fps = (int)Math.Round(rate) };
            }
            lastError = errors.ToString();
            try { File.Delete(temp); } catch { }
        }
        throw new InvalidOperationException("O ffmpeg não conseguiu montar o vídeo: " + lastError.Trim());
    }

    static bool Visible(JsonObject o) => o["visible"] switch
    {
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonObject a when a["value"] is JsonValue v2 && v2.TryGetValue(out bool b2) => b2,
        _ => true,
    };

    static string Text(JsonNode n) => n is JsonObject o ? o["value"]?.ToString() : n?.ToString();

    static double[] Vec(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var v = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return v;
    }

    static double Number(JsonNode n, double fallback)
    {
        if (n is JsonObject o) n = o["value"];
        try { return n is JsonValue v && v.TryGetValue(out double d) ? d : fallback; } catch { return fallback; }
    }
}
