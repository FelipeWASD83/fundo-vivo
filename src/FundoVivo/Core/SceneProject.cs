using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FundoVivo.Core;

/// <summary>What a scene loses when it becomes a recorded video, read from its scene.json (and its scripts).</summary>
[Flags]
public enum SceneTraits
{
    None = 0,
    Parallax = 1,
    Sound = 2,
    /// <summary>Scripts or particles that follow the mouse: frozen in the video.</summary>
    Mouse = 4,
    Unreadable = 8,
    /// <summary>A single animated picture: becomes a video straight from its sprite sheet (SpriteScene), loop exact.</summary>
    Direct = 16,
    /// <summary>Shows the time or the date: the video keeps the moment it was recorded.</summary>
    Clock = 32,
    /// <summary>Has background sound (music, ambience): it is taken out of the package and plays along with the video.</summary>
    BackgroundSound = 64,
}

/// <summary>
/// A Wallpaper Engine scene: the project, its scene.pkg (a header, a table of files, then the files) and the parts of
/// scene.json the converter needs. Only the small entries (JSON and scripts) are read to classify; the whole package
/// is read only when unpacking it for a recording.
/// </summary>
public sealed class SceneProject
{
    public string Folder { get; init; }
    public string Title { get; init; }
    public string SceneFile { get; init; }        // entry name of the scene, usually scene.json
    public string Preview { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string ContentRating { get; init; }
    public SceneTraits Traits { get; init; }
    public double ParallaxAmount { get; init; }
    /// <summary>A loop length (10 to 60 s) that every timeline animation of the scene fits a whole number of times; 0 when there is none.</summary>
    public double SuggestedLoop { get; init; }
    /// <summary>The background sound's entry in the package (or file in the folder) and its volume, when it has one.</summary>
    public string SoundEntry { get; init; }
    public double SoundVolume { get; init; } = 1;

    public string Id => Library.IdFor(Path.Combine(Folder, "project.json"));

    /// <summary>The classification tags in the current language. The index order is fixed: ClassificationTags indexes it.</summary>
    public static string[] TraitTags => new[]
    {
        Loc.T("core_trait_lossless"), Loc.T("core_trait_parallax"), Loc.T("core_trait_sound"), Loc.T("core_trait_mouse"),
        Loc.T("core_trait_unreadable"), Loc.T("core_trait_direct"), Loc.T("core_trait_clock"), Loc.T("core_trait_bgsound"),
    };

    const SceneTraits Losses = SceneTraits.Parallax | SceneTraits.Sound | SceneTraits.Mouse | SceneTraits.Clock;

    /// <summary>The automatic tags for the library filter.</summary>
    public IEnumerable<string> ClassificationTags()
    {
        var names = TraitTags;
        if (Traits.HasFlag(SceneTraits.Unreadable)) { yield return names[4]; yield break; }
        if ((Traits & Losses) == 0) yield return names[0];
        if (Traits.HasFlag(SceneTraits.Parallax)) yield return names[1];
        if (Traits.HasFlag(SceneTraits.Sound)) yield return names[2];
        if (Traits.HasFlag(SceneTraits.Mouse)) yield return names[3];
        if (Traits.HasFlag(SceneTraits.Clock)) yield return names[6];
        if (Traits.HasFlag(SceneTraits.BackgroundSound)) yield return names[7];
        if (Traits.HasFlag(SceneTraits.Direct)) yield return names[5];
    }

    /// <summary>One entry of the package, whole.</summary>
    public byte[] ReadPackageEntry(string name)
    {
        string pkg = PackageOf(Folder, SceneFile);
        var table = ReadTable(pkg);
        if (!table.TryGetValue(name, out var e)) throw new FileNotFoundException(name);
        using var f = File.OpenRead(pkg);
        return ReadEntry(f, e);
    }

    /// <summary>The sprite sheet's entry when this scene can become a video without Wallpaper Engine, else null.</summary>
    public string SpriteTexture()
    {
        try
        {
            string pkg = PackageOf(Folder, SceneFile);
            if (!File.Exists(pkg)) return null;
            var table = ReadTable(pkg);
            using var f = File.OpenRead(pkg);
            var scene = JsonNode.Parse(Encoding.UTF8.GetString(ReadEntry(f, table[SceneFile])), documentOptions: Lenient) as JsonObject;
            return scene == null ? null : SpriteScene.Describe(n => ReadEntry(f, table[n]), (n, len) => ReadHead(f, table[n], len), scene);
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- package

    /// <summary>The table of a scene.pkg: entry name to offset and size, with offsets already absolute.</summary>
    public static Dictionary<string, (long Offset, int Size)> ReadTable(string pkg)
    {
        using var f = File.OpenRead(pkg);
        using var r = new BinaryReader(f);
        string ReadString()
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 4096) throw new InvalidDataException("invalid package");
            return Encoding.UTF8.GetString(r.ReadBytes(n));
        }
        string magic = ReadString();
        if (!magic.StartsWith("PKGV", StringComparison.Ordinal)) throw new InvalidDataException("not a scene.pkg");
        int count = r.ReadInt32();
        if (count < 0 || count > 200_000) throw new InvalidDataException("invalid package");
        var entries = new List<(string, int, int)>(count);
        for (int i = 0; i < count; i++) entries.Add((ReadString(), r.ReadInt32(), r.ReadInt32()));
        long baseOffset = f.Position;
        var table = new Dictionary<string, (long, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, off, size) in entries) table[name.Replace('\\', '/')] = (baseOffset + off, size);
        return table;
    }

    static byte[] ReadHead(FileStream f, (long Offset, int Size) e, int length)
    {
        var buf = new byte[Math.Min(length, e.Size)];
        f.Position = e.Offset;
        f.ReadExactly(buf);
        return buf;
    }

    static byte[] ReadEntry(FileStream f, (long Offset, int Size) e)
    {
        var buf = new byte[e.Size];
        f.Position = e.Offset;
        f.ReadExactly(buf);
        return buf;
    }

    /// <summary>Writes every file of the scene into `target` (the package's entries, plus the loose files next to it).</summary>
    public void UnpackTo(string target, bool disableParallax)
    {
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        string full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        string pkg = PackageOf(Folder, SceneFile);
        if (File.Exists(pkg))
        {
            var table = ReadTable(pkg);
            using var f = File.OpenRead(pkg);
            foreach (var (name, entry) in table)
            {
                string dest = Path.GetFullPath(Path.Combine(target, name));
                if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("package with an invalid path: " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, ReadEntry(f, entry));
            }
        }
        foreach (string file in Directory.GetFiles(Folder))
        {
            string name = Path.GetFileName(file);
            if (name.Equals(Path.GetFileName(pkg), StringComparison.OrdinalIgnoreCase)) continue;
            string dest = Path.Combine(target, name);
            if (!File.Exists(dest)) File.Copy(file, dest);
        }
        // Loose project folders (the user's own Wallpaper Engine projects) keep their files in subfolders.
        if (!File.Exists(pkg))
            foreach (string dir in Directory.GetDirectories(Folder))
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));

        if (disableParallax)
        {
            string sceneJson = Path.Combine(target, SceneFile);
            var scene = JsonNode.Parse(File.ReadAllText(sceneJson), documentOptions: Lenient)?.AsObject() ?? throw new InvalidDataException("empty scene.json");
            if (scene["general"] is not JsonObject general) scene["general"] = general = new JsonObject();
            general["cameraparallax"] = false;
            File.WriteAllText(sceneJson, scene.ToJsonString());
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    internal static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    // ---------------------------------------------------------------- reading and classifying

    static readonly Regex MousePattern = new(
        @"cursorWorldPosition|cursorScreenPosition|cursorEnter|cursorLeave|cursorClick|cursorDown|cursorUp|cursorMove|input\.cursor|controlpointstartinglocations",
        RegexOptions.Compiled);
    static readonly Regex ClockPattern = new(@"new Date\(|engine\.timeOfDay|getHours|getMinutes|toLocaleTimeString|toLocaleDateString", RegexOptions.Compiled);

    /// <summary>Reads a scene project folder; null when the folder is not a scene.</summary>
    public static SceneProject Load(string folder)
    {
        string pj = Path.Combine(folder, "project.json");
        if (!File.Exists(pj)) return null;
        JsonObject project;
        try { project = JsonNode.Parse(File.ReadAllText(pj), documentOptions: Lenient)?.AsObject(); }
        catch { return null; }
        if (project == null || project["type"]?.ToString()?.Trim().ToLowerInvariant() != "scene") return null;

        string sceneFile = project["file"]?.ToString() is { Length: > 0 } fn ? fn.Replace('\\', '/') : "scene.json";
        string preview = project["preview"]?.ToString() is { Length: > 0 } pv && File.Exists(Path.Combine(folder, pv)) ? Path.Combine(folder, pv) : null;
        var tags = (project["tags"] as JsonArray)?.Select(n => n?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
        bool supportsAudio = project["general"]?["supportsaudioprocessing"]?.GetValueKind() == JsonValueKind.True;
        string title = project["title"]?.ToString() is { } t && !string.IsNullOrWhiteSpace(t) ? t.Trim() : Path.GetFileName(folder);

        SceneTraits traits = SceneTraits.None;
        double amount = 0, loop = 0, soundVolume = 1;
        string soundEntry = null;
        try
        {
            var (sceneText, scripts) = ReadSceneText(folder, sceneFile);
            var scene = JsonNode.Parse(sceneText, documentOptions: Lenient)?.AsObject() ?? throw new InvalidDataException();
            var general = scene["general"] as JsonObject;
            if (Truthy(general?["cameraparallax"]))
            {
                amount = Number(general?["cameraparallaxamount"], 0.5);
                if (amount > 0.001) traits |= SceneTraits.Parallax;
            }
            if (supportsAudio || sceneText.Contains("audioprocessing", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(sceneText, @"audio_?bars|audiovisual|audio_response|""audio""", RegexOptions.IgnoreCase))
                traits |= SceneTraits.Sound;
            if (MousePattern.IsMatch(scripts) || MousePattern.IsMatch(sceneText)) traits |= SceneTraits.Mouse;
            if (ClockPattern.IsMatch(scripts) || ClockPattern.IsMatch(sceneText)) traits |= SceneTraits.Clock;
            (soundEntry, soundVolume) = BackgroundSound(scene);
            if (soundEntry != null) traits |= SceneTraits.BackgroundSound;
            loop = LoopFor(scene);
        }
        catch { traits = SceneTraits.Unreadable; }

        var probe = new SceneProject { Folder = folder, SceneFile = sceneFile };
        if (!traits.HasFlag(SceneTraits.Unreadable) && probe.SpriteTexture() != null) traits |= SceneTraits.Direct;
        return new SceneProject
        {
            Folder = folder, Title = title, SceneFile = sceneFile, Preview = preview, Tags = tags,
            ContentRating = project["contentrating"]?.ToString(), Traits = traits, ParallaxAmount = amount,
            SuggestedLoop = loop, SoundEntry = soundEntry, SoundVolume = soundVolume,
        };
    }

    /// <summary>The first sound object that plays in a loop (the random mode, with its gaps, is left out).</summary>
    static (string entry, double volume) BackgroundSound(JsonObject scene)
    {
        foreach (var o in (scene["objects"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (o["sound"] is not JsonArray files || files.Count == 0) continue;
            if (string.Equals(o["playbackmode"]?.ToString(), "random", StringComparison.OrdinalIgnoreCase)) continue;
            string first = files[0]?.ToString();
            if (!string.IsNullOrWhiteSpace(first)) return (first.Replace('\\', '/'), Math.Clamp(Number(o["volume"], 1), 0, 1));
        }
        return (null, 1);
    }

    /// <summary>
    /// Timeline animations declare their length (frames at an fps; "mirror" goes there and back). When there are
    /// some, the shortest loop of 10 to 60 s that all of them fit a whole number of times; else 0.
    /// </summary>
    static double LoopFor(JsonObject scene)
    {
        var periods = new HashSet<long>();   // in milliseconds
        void Walk(JsonNode n)
        {
            if (n is JsonObject o)
            {
                if (o["animation"] is JsonObject a && a["options"] is JsonObject opt)
                {
                    double fps = Number(opt["fps"], 0), length = Number(opt["length"], 0);
                    if (fps > 0 && length > 0)
                    {
                        double seconds = length / fps * (string.Equals(opt["mode"]?.ToString(), "mirror", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                        periods.Add((long)Math.Round(seconds * 1000));
                    }
                }
                foreach (var (_, child) in o) Walk(child);
            }
            else if (n is JsonArray arr) foreach (var child in arr) Walk(child);
        }
        Walk(scene);
        periods.RemoveWhere(p => p <= 0);
        if (periods.Count == 0) return 0;
        long lcm = 1;
        foreach (long p in periods)
        {
            lcm = lcm / Gcd(lcm, p) * p;
            if (lcm > 60_000) return 0;
        }
        long loop = lcm * (long)Math.Ceiling(10_000.0 / lcm);
        return loop <= 60_000 ? loop / 1000.0 : 0;
    }

    static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }

    /// <summary>
    /// The package next to the project: named after the scene file (gifscene.json lives in gifscene.pkg, in the GIF
    /// scenes made with Wallpaper Engine's template), scene.pkg otherwise.
    /// </summary>
    public static string PackageOf(string folder, string sceneFile)
    {
        string named = Path.Combine(folder, Path.ChangeExtension(Path.GetFileName(sceneFile ?? "scene.json"), ".pkg"));
        return File.Exists(named) ? named : Path.Combine(folder, "scene.pkg");
    }

    /// <summary>scene.json and every script, from the package (only those entries) or from the loose folder.</summary>
    static (string scene, string scripts) ReadSceneText(string folder, string sceneFile)
    {
        string pkg = PackageOf(folder, sceneFile);
        var scripts = new StringBuilder();
        if (File.Exists(pkg))
        {
            var table = ReadTable(pkg);
            using var f = File.OpenRead(pkg);
            if (!table.TryGetValue(sceneFile, out var entry)) throw new FileNotFoundException(sceneFile);
            string scene = Encoding.UTF8.GetString(ReadEntry(f, entry));
            foreach (var (name, e) in table)
                if (name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && e.Size < 2_000_000) scripts.Append(Encoding.UTF8.GetString(ReadEntry(f, e)));
            return (scene, scripts.ToString());
        }
        string path = Path.Combine(folder, sceneFile);
        foreach (string js in Directory.EnumerateFiles(folder, "*.js", SearchOption.AllDirectories)) scripts.Append(File.ReadAllText(js));
        return (File.ReadAllText(path), scripts.ToString());
    }

    static bool Truthy(JsonNode n)
    {
        if (n is JsonObject o) n = o["value"];
        return n?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => n.GetValue<double>() != 0,
            JsonValueKind.String => n.ToString() is "true" or "1",
            _ => false,
        };
    }

    static double Number(JsonNode n, double fallback)
    {
        if (n is JsonObject o) n = o["value"];
        try { return n?.GetValueKind() == JsonValueKind.Number ? n.GetValue<double>() : fallback; } catch { return fallback; }
    }
}
