using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FundoVivo.Core;

public sealed class LibraryItem
{
    public string Id { get; init; }
    public string Title { get; init; }
    public WallpaperType Type { get; init; }
    public string File { get; init; }
    public string Folder { get; init; }
    public string Preview { get; init; }
    public bool FromWallpaperEngine { get; init; }
    public bool HasProperties { get; init; }
    public string ContentRating { get; init; }
    public DateTime Date { get; init; }
    /// <summary>As shown: Wallpaper Engine tags in Portuguese (as in the file in English), the user's own as typed.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    /// <summary>The rating label of the language at scan time (Livre/Questionável/Adulto or Everyone/Questionable/Mature), or null when the project does not say.</summary>
    public string Rating { get; init; }
    /// <summary>Scenes only: what the video loses, and how strong the parallax is.</summary>
    public SceneTraits Traits { get; init; }
    public double ParallaxAmount { get; init; }
    /// <summary>Scenes only: a loop length that fits every timeline animation of the scene, 0 when there is none.</summary>
    public double SuggestedLoop { get; init; }
    /// <summary>.vivo only: shipped with the app, and whether it plays live (reacts to sound) instead of as a video.</summary>
    public bool IsExample { get; init; }
    public bool Realtime { get; init; }
    public bool ReactsToSound { get; init; }
}

public sealed class LibraryScan
{
    public List<LibraryItem> Items { get; } = new();
    public int Scenes { get; set; }
    public int SkippedOther { get; set; }
    public bool FoundWallpaperEngine { get; set; }
}

public static class Library
{
    public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".wmv", ".avi" };
    public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".jxr", ".heic", ".avif" };
    public static readonly string[] WebExtensions = { ".html", ".htm" };

    public static string IdFor(string file) => Path.GetFullPath(file).ToLowerInvariant();

    public static WallpaperType? TypeForFile(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (VideoExtensions.Contains(ext)) return WallpaperType.Video;
        if (ImageExtensions.Contains(ext)) return WallpaperType.Image;
        if (WebExtensions.Contains(ext)) return WallpaperType.Web;
        if (ext == Packages.Extension) return WallpaperType.Package;
        return null;
    }

    public static LibraryScan Scan(AppConfig config)
    {
        var scan = new LibraryScan();
        if (config.ScanWallpaperEngine)
        {
            foreach (string root in WallpaperEngineFolders())
            {
                scan.FoundWallpaperEngine = true;
                foreach (string dir in SafeDirs(root)) AddProject(scan, dir);
            }
        }
        foreach (string file in Packages.Examples()) AddPackage(scan, file, null);
        foreach (var u in config.UserItems)
        {
            if (!System.IO.File.Exists(u.File)) continue;
            if (u.Type == WallpaperType.Package) { AddPackage(scan, u.File, u); continue; }
            scan.Items.Add(new LibraryItem
            {
                Id = u.Id, Title = u.Title, Type = u.Type, File = u.File, Folder = Path.GetDirectoryName(u.File),
                Preview = u.Type == WallpaperType.Image ? u.File : null, Date = u.Added,
                Tags = (u.Tags ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            });
        }
        SceneCache.Save();
        return scan;
    }

    static void AddProject(LibraryScan scan, string dir)
    {
        var project = WeProject.Load(dir);
        if (project == null) return;
        WallpaperType type;
        switch (project.Type)
        {
            case "video": type = WallpaperType.Video; break;
            case "web": type = WallpaperType.Web; break;
            case "scene": AddScene(scan, dir); return;
            default: scan.SkippedOther++; return;
        }
        string file = Path.Combine(dir, project.File ?? "");
        if (!System.IO.File.Exists(file)) { scan.SkippedOther++; return; }
        string preview = project.Preview == null ? null : Path.Combine(dir, project.Preview);
        scan.Items.Add(new LibraryItem
        {
            Id = IdFor(file), Title = project.Title, Type = type, File = file, Folder = dir,
            Preview = preview != null && System.IO.File.Exists(preview) ? preview : null,
            FromWallpaperEngine = true, HasProperties = project.Properties.Count > 0, ContentRating = project.ContentRating,
            Date = Directory.GetLastWriteTime(dir),
            Tags = project.Tags.Select(FundoVivo.Core.Tags.FromWallpaperEngine).Where(t => t != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Rating = FundoVivo.Core.Tags.Rating(project.ContentRating),
        });
    }

    static void AddScene(LibraryScan scan, string dir)
    {
        var scene = SceneCache.Get(dir);
        if (scene == null) { scan.SkippedOther++; return; }
        scan.Scenes++;
        var tags = scene.Tags.Select(FundoVivo.Core.Tags.FromWallpaperEngine).Where(t => t != null)
            .Concat(scene.ClassificationTags()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        scan.Items.Add(new LibraryItem
        {
            Id = scene.Id, Title = scene.Title, Type = WallpaperType.Scene, File = Path.Combine(dir, "project.json"), Folder = dir,
            Preview = scene.Preview, FromWallpaperEngine = true, ContentRating = scene.ContentRating,
            Date = Directory.GetLastWriteTime(dir), Tags = tags, Rating = FundoVivo.Core.Tags.Rating(scene.ContentRating),
            Traits = scene.Traits, ParallaxAmount = scene.ParallaxAmount, SuggestedLoop = scene.SuggestedLoop,
        });
    }

    /// <summary>A .vivo: the app's examples (user == null) or one the user added, whose tags are the ones they kept.</summary>
    static void AddPackage(LibraryScan scan, string file, UserItem user)
    {
        var info = Packages.Inspect(file);
        if (info == null) { scan.SkippedOther++; return; }
        var tags = user == null ? info.Tags : (IEnumerable<string>)(user.Tags ?? new List<string>());
        scan.Items.Add(new LibraryItem
        {
            Id = user?.Id ?? IdFor(file), Title = user?.Title ?? info.Title, Type = WallpaperType.Package, File = file,
            Folder = Path.GetDirectoryName(file), Preview = info.Preview, Date = user?.Added ?? System.IO.File.GetLastWriteTime(file),
            Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(), IsExample = user == null, Realtime = info.Realtime, ReactsToSound = info.ReactsToSound,
            ParallaxAmount = info.Parallax,
        });
    }

    static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); } catch { return Array.Empty<string>(); }
    }

    /// <summary>Workshop downloads and the user's own Wallpaper Engine projects, across every Steam library.</summary>
    public static IEnumerable<string> WallpaperEngineFolders()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string lib in SteamLibraries())
        {
            foreach (string sub in new[] { @"steamapps\workshop\content\431960", @"steamapps\common\wallpaper_engine\projects\myprojects" })
            {
                string dir = Path.Combine(lib, sub);
                if (Directory.Exists(dir) && seen.Add(Path.GetFullPath(dir))) yield return dir;
            }
        }
    }

    static IEnumerable<string> SteamLibraries()
    {
        string steam = null;
        try { steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; } catch { }
        steam ??= @"C:\Program Files (x86)\Steam";
        steam = steam.Replace('/', '\\');
        var libs = new List<string> { steam };
        string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        try
        {
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        return libs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>The parts of a Wallpaper Engine project.json this app uses.</summary>
public sealed class WeProject
{
    public string Folder { get; init; }
    public string Title { get; init; }
    public string Type { get; init; }
    public string File { get; init; }
    public string Preview { get; init; }
    public string ContentRating { get; init; }
    public bool SupportsAudio { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public JsonObject Properties { get; init; } = new();
    public Dictionary<string, string> Strings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static WeProject Load(string folder)
    {
        string path = Path.Combine(folder, "project.json");
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var root = JsonNode.Parse(System.IO.File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            })?.AsObject();
            if (root == null) return null;
            var general = root["general"] as JsonObject;
            var props = general?["properties"] as JsonObject ?? new JsonObject();
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (general?["localization"] is JsonObject loc)
            {
                // The interface language when the author shipped it, the other one otherwise (later entries win).
                foreach (string lang in Loc.English ? new[] { "pt-br", "en-us" } : new[] { "en-us", "pt-br" })
                    if (loc[lang] is JsonObject table)
                        foreach (var kv in table) strings[kv.Key] = kv.Value?.ToString() ?? "";
            }
            string title = root["title"]?.ToString();
            return new WeProject
            {
                Folder = folder,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(folder) : title.Trim(),
                Type = root["type"]?.ToString()?.Trim().ToLowerInvariant(),
                File = root["file"]?.ToString(),
                Preview = root["preview"]?.ToString(),
                ContentRating = root["contentrating"]?.ToString(),
                SupportsAudio = general?["supportsaudioprocessing"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
                Tags = (root["tags"] as JsonArray)?.Select(n => n?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(),
                Properties = (JsonObject)props.DeepClone(),
                Strings = strings,
            };
        }
        catch (Exception e)
        {
            Log.Write($"project.json unreadable in {folder}: {e.Message}");
            return null;
        }
    }

    /// <summary>Wallpaper Engine's own property labels (pt/en tables, key prefix late_prop_), for the ui_ keys projects use without shipping the text.</summary>
    static string BuiltIn(string raw) => raw.ToLowerInvariant() switch
    {
        "ui_browse_properties_scheme_color" => Loc.T("late_prop_scheme_color"),
        "ui_browse_properties_alignment" => Loc.T("late_prop_alignment"),
        "ui_browse_properties_alignment_center" => Loc.T("late_prop_alignment_center"),
        "ui_browse_properties_alignment_left" => Loc.T("late_prop_alignment_left"),
        "ui_browse_properties_alignment_right" => Loc.T("late_prop_alignment_right"),
        "ui_browse_properties_alignment_top" => Loc.T("late_prop_alignment_top"),
        "ui_browse_properties_alignment_bottom" => Loc.T("late_prop_alignment_bottom"),
        "ui_browse_properties_background_color" => Loc.T("late_prop_background_color"),
        "ui_browse_properties_image" => Loc.T("late_prop_image"),
        "ui_browse_properties_volume" => Loc.T("late_prop_volume"),
        "ui_browse_properties_playback_rate" => Loc.T("late_prop_playback_rate"),
        _ => null,
    };

    /// <summary>Wallpaper Engine labels are often localization keys or carry HTML; this returns plain text.</summary>
    public string Text(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (Strings.TryGetValue(raw, out var t)) raw = t;
        else if (raw.StartsWith("ui_", StringComparison.OrdinalIgnoreCase))
        {
            // Keys from Wallpaper Engine's own interface, which projects reference without shipping the text.
            if (BuiltIn(raw) is string known) return known;
            string rest = Regex.Replace(raw, "^ui_(browse_properties_|editor_properties_)?", "", RegexOptions.IgnoreCase).Replace('_', ' ');
            return rest.Length == 0 ? raw : char.ToUpper(rest[0]) + rest[1..];
        }
        raw = Regex.Replace(raw, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(raw).Trim();
    }

    /// <summary>The values handed to applyUserProperties: the project defaults with the user's changes on top.</summary>
    public JsonObject UserProperties(ItemSettings settings)
    {
        var result = new JsonObject();
        foreach (var kv in Properties)
        {
            if (kv.Value is not JsonObject p) continue;
            var entry = new JsonObject();
            if (p["type"] != null) entry["type"] = p["type"].DeepClone();
            JsonNode value = settings != null && settings.Properties.TryGetValue(kv.Key, out var v) ? v : p["value"];
            entry["value"] = value?.DeepClone();
            result[kv.Key] = entry;
        }
        return result;
    }
}
