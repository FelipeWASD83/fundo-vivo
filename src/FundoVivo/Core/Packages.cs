using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FundoVivo.Core;

/// <summary>What the library needs to know about a .vivo file, read from its wallpaper.json.</summary>
public sealed class VivoInfo
{
    public string Title { get; init; }
    public string Author { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public double Loop { get; init; } = 20;
    public double Parallax { get; init; }
    /// <summary>Uses something that depends on the moment (today, only the sound): plays live instead of as a video.</summary>
    public bool Realtime { get; init; }
    public string Preview { get; init; }
}

/// <summary>
/// The app's own wallpaper format: a zip with a declarative wallpaper.json and its assets (docs/formato-vivo.md).
/// To play or render one, it is unpacked next to the runtime (Runtime/index.html and vivo.js, embedded in the exe)
/// into %LOCALAPPDATA%\FundoVivo\packages\&lt;hash&gt;, one folder per file version and app version.
/// </summary>
public static class Packages
{
    public const string Extension = ".vivo";
    static string Root => Ensure(Path.Combine(AppPaths.Local, "packages"));
    static string Previews => Ensure(Path.Combine(Root, "previews"));

    public static bool IsPackage(string file) => file != null && file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The examples that ship with the app (compilar.ps1 packs examples/ into app\examples).</summary>
    public static IEnumerable<string> Examples()
    {
        string dir = Path.Combine(AppPaths.AppFolder, "examples");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + Extension) : Array.Empty<string>();
    }

    /// <summary>Changes whenever the file does: the key of its unpacked copy and of its rendered videos.</summary>
    public static string Stamp(string file)
    {
        try
        {
            var f = new FileInfo(file);
            return $"{f.Length}-{f.LastWriteTimeUtc.Ticks}";
        }
        catch { return "missing"; }
    }

    static string Key(string file, string extra) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToLowerInvariant() + "|" + Stamp(file) + "|" + extra)))[..12].ToLowerInvariant();

    public static VivoInfo Inspect(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.GetEntry("wallpaper.json") ?? throw new InvalidDataException("sem wallpaper.json");
            JsonObject root;
            using (var s = entry.Open())
                root = JsonNode.Parse(s, documentOptions: new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                })?.AsObject() ?? throw new InvalidDataException("wallpaper.json vazio");
            if (root["format"]?.ToString() != "fundo-vivo") throw new InvalidDataException("não é um wallpaper do Fundo Vivo");

            bool realtime = (root["layers"] as JsonArray ?? new JsonArray())
                .SelectMany(l => (l?["animate"] as JsonArray)?.AsEnumerable() ?? Enumerable.Empty<JsonNode>())
                .Any(a => a?["type"]?.ToString() == "audio");
            string preview = null;
            string previewName = root["preview"]?.ToString();
            var previewEntry = previewName == null ? null : zip.GetEntry(previewName.Replace('\\', '/'));
            if (previewEntry != null)
            {
                preview = Path.Combine(Previews, Key(file, "preview") + Path.GetExtension(previewEntry.Name).ToLowerInvariant());
                if (!File.Exists(preview)) previewEntry.ExtractToFile(preview, true);
            }
            string title = root["title"]?.ToString();
            return new VivoInfo
            {
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file) : title.Trim(),
                Author = root["author"]?.ToString(),
                Tags = (root["tags"] as JsonArray)?.Select(n => n?.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>(),
                Loop = Math.Clamp(Number(root["loop"], 20), 1, 120),
                Parallax = Math.Clamp(Number(root["parallax"], 0), 0, 0.15),
                Realtime = realtime,
                Preview = preview,
            };
        }
        catch (Exception e)
        {
            Log.Write($"vivo unreadable ({file}): {e.Message}");
            return null;
        }
    }

    static double Number(JsonNode n, double fallback) =>
        n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : fallback;

    /// <summary>Unpacks the file next to the runtime (once per version) and returns the page to open.</summary>
    public static string Prepare(string file)
    {
        string key = Key(file, AppPaths.Version);
        string dir = Path.Combine(Root, key);
        string page = Path.Combine(dir, "index.html");
        if (File.Exists(Path.Combine(dir, ".ready"))) return page;

        string temp = dir + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
        Directory.CreateDirectory(temp);
        try
        {
            string full = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(file))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;   // a folder
                    string target = Path.GetFullPath(Path.Combine(temp, e.FullName));
                    // A name like ../../x must never write outside the folder.
                    if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("caminho inválido no pacote: " + e.FullName);
                    string name = Path.GetFileName(target);
                    if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase) || name.Equals("vivo.js", StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            }
            foreach (string runtime in new[] { "index.html", "vivo.js" }) File.WriteAllText(Path.Combine(temp, runtime), RuntimeFile(runtime));
            File.WriteAllText(Path.Combine(temp, ".source"), Path.GetFullPath(file));
            File.WriteAllText(Path.Combine(temp, ".ready"), "");
            try { Directory.Move(temp, dir); }
            catch (IOException) when (File.Exists(Path.Combine(dir, ".ready"))) { Directory.Delete(temp, true); }   // another process got there first
        }
        catch
        {
            try { Directory.Delete(temp, true); } catch { }
            throw;
        }
        DropOldCopies(file, key);
        return page;
    }

    /// <summary>Unpacked copies of older versions of the same file. Best effort: one in use is left for next time.</summary>
    static void DropOldCopies(string file, string keep)
    {
        string source = Path.GetFullPath(file);
        foreach (string dir in Directory.GetDirectories(Root))
        {
            string name = Path.GetFileName(dir);
            if (name == keep || name == "previews") continue;
            try
            {
                string marker = Path.Combine(dir, ".source");
                if (File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), source, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(dir, true);
            }
            catch { }
        }
    }

    static readonly Dictionary<string, string> Runtime = new();

    static string RuntimeFile(string name)
    {
        lock (Runtime)
        {
            if (Runtime.TryGetValue(name, out var text)) return text;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("runtime/" + name)
                ?? throw new InvalidOperationException("runtime ausente: " + name);
            using var r = new StreamReader(s);
            return Runtime[name] = r.ReadToEnd();
        }
    }

    static string Ensure(string d) { Directory.CreateDirectory(d); return d; }
}
