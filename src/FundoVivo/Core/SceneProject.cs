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
    MouseOrClock = 4,
    Unreadable = 8,
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

    public string Id => Library.IdFor(Path.Combine(Folder, "project.json"));

    public static readonly string[] TraitTags = { "Converte sem perder nada", "Parallax vira pan", "Perde reação ao som", "Perde mouse/relógio", "Sem leitura" };

    /// <summary>The automatic tags for the library filter.</summary>
    public IEnumerable<string> ClassificationTags()
    {
        if (Traits.HasFlag(SceneTraits.Unreadable)) { yield return TraitTags[4]; yield break; }
        if ((Traits & (SceneTraits.Parallax | SceneTraits.Sound | SceneTraits.MouseOrClock)) == 0) yield return TraitTags[0];
        if (Traits.HasFlag(SceneTraits.Parallax)) yield return TraitTags[1];
        if (Traits.HasFlag(SceneTraits.Sound)) yield return TraitTags[2];
        if (Traits.HasFlag(SceneTraits.MouseOrClock)) yield return TraitTags[3];
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
            if (n < 0 || n > 4096) throw new InvalidDataException("pacote inválido");
            return Encoding.UTF8.GetString(r.ReadBytes(n));
        }
        string magic = ReadString();
        if (!magic.StartsWith("PKGV", StringComparison.Ordinal)) throw new InvalidDataException("não é um scene.pkg");
        int count = r.ReadInt32();
        if (count < 0 || count > 200_000) throw new InvalidDataException("pacote inválido");
        var entries = new List<(string, int, int)>(count);
        for (int i = 0; i < count; i++) entries.Add((ReadString(), r.ReadInt32(), r.ReadInt32()));
        long baseOffset = f.Position;
        var table = new Dictionary<string, (long, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, off, size) in entries) table[name.Replace('\\', '/')] = (baseOffset + off, size);
        return table;
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
        string pkg = Path.Combine(Folder, "scene.pkg");
        if (File.Exists(pkg))
        {
            var table = ReadTable(pkg);
            using var f = File.OpenRead(pkg);
            foreach (var (name, entry) in table)
            {
                string dest = Path.GetFullPath(Path.Combine(target, name));
                if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("pacote com caminho inválido: " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, ReadEntry(f, entry));
            }
        }
        foreach (string file in Directory.GetFiles(Folder))
        {
            string name = Path.GetFileName(file);
            if (name.Equals("scene.pkg", StringComparison.OrdinalIgnoreCase)) continue;
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
            var scene = JsonNode.Parse(File.ReadAllText(sceneJson), documentOptions: Lenient)?.AsObject() ?? throw new InvalidDataException("scene.json vazio");
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

    static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    // ---------------------------------------------------------------- reading and classifying

    static readonly Regex MouseOrClockPattern = new(
        @"cursorWorldPosition|cursorScreenPosition|cursorEnter|cursorLeave|cursorClick|cursorDown|cursorUp|cursorMove|input\.cursor|new Date\(|engine\.timeOfDay|getHours|getMinutes|toLocaleTimeString",
        RegexOptions.Compiled);

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
        double amount = 0;
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
            if (MouseOrClockPattern.IsMatch(scripts) || MouseOrClockPattern.IsMatch(sceneText)) traits |= SceneTraits.MouseOrClock;
        }
        catch { traits = SceneTraits.Unreadable; }

        return new SceneProject
        {
            Folder = folder, Title = title, SceneFile = sceneFile, Preview = preview, Tags = tags,
            ContentRating = project["contentrating"]?.ToString(), Traits = traits, ParallaxAmount = amount,
        };
    }

    /// <summary>scene.json and every script, from the package (only those entries) or from the loose folder.</summary>
    static (string scene, string scripts) ReadSceneText(string folder, string sceneFile)
    {
        string pkg = Path.Combine(folder, "scene.pkg");
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
