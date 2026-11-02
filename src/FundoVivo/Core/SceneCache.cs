using System.Collections.Concurrent;
using System.Text.Json;

namespace FundoVivo.Core;

/// <summary>
/// Reading 224 scene packages to classify them costs a second or two; the result is kept on disk, keyed by the
/// package's size and date, so opening the window again does not read them again.
/// </summary>
public static class SceneCache
{
    /// <summary>Raised when the classification learns something new, so the entries made before are read again.</summary>
    const int CurrentVersion = 2;

    sealed class Entry
    {
        public int Version { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string Title { get; set; }
        public string SceneFile { get; set; }
        public string Preview { get; set; }
        public List<string> Tags { get; set; }
        public string ContentRating { get; set; }
        public SceneTraits Traits { get; set; }
        public double ParallaxAmount { get; set; }
    }

    static readonly string FilePath = Path.Combine(AppPaths.Local, "scene-cache.json");
    static ConcurrentDictionary<string, Entry> _entries;
    static bool _dirty;

    static ConcurrentDictionary<string, Entry> Entries()
    {
        if (_entries != null) return _entries;
        try
        {
            if (File.Exists(FilePath))
                _entries = new ConcurrentDictionary<string, Entry>(
                    JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath), ConfigStore.Json) ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return _entries ??= new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    public static SceneProject Get(string folder)
    {
        // the package when there is one (scene.pkg, or gifscene.pkg in the GIF scenes), else the project
        string stamp = Directory.EnumerateFiles(folder, "*.pkg").FirstOrDefault() ?? Path.Combine(folder, "project.json");
        long size; DateTime modified;
        try { var fi = new FileInfo(stamp); size = fi.Length; modified = fi.LastWriteTimeUtc; } catch { return null; }
        var entries = Entries();
        if (entries.TryGetValue(folder, out var e) && e.Version == CurrentVersion && e.Size == size && e.Modified == modified)
            return new SceneProject
            {
                Folder = folder, Title = e.Title, SceneFile = e.SceneFile, Preview = e.Preview, Tags = e.Tags ?? new List<string>(),
                ContentRating = e.ContentRating, Traits = e.Traits, ParallaxAmount = e.ParallaxAmount,
            };
        var scene = SceneProject.Load(folder);
        if (scene == null) return null;
        entries[folder] = new Entry
        {
            Version = CurrentVersion, Size = size, Modified = modified, Title = scene.Title, SceneFile = scene.SceneFile, Preview = scene.Preview,
            Tags = scene.Tags.ToList(), ContentRating = scene.ContentRating, Traits = scene.Traits, ParallaxAmount = scene.ParallaxAmount,
        };
        _dirty = true;
        return scene;
    }

    /// <summary>Writes what changed; called after a library scan.</summary>
    public static void Save()
    {
        if (!_dirty || _entries == null) return;
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, ConfigStore.Json));
            File.Move(tmp, FilePath, true);
            _dirty = false;
        }
        catch (Exception ex) { Log.Write("scene cache: " + ex.Message); }
    }
}
