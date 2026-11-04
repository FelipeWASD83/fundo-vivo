using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FundoVivo.Core;

public enum WallpaperType { Video, Image, Web, Scene, Package }
public enum FitMode { Fill, Fit, Stretch }
public enum CoveredAction { Pause, Keep }
public enum FullscreenAction { Pause, Unload, Keep }
public enum AppTheme { System, Light, Dark }

/// <summary>What is shown on one monitor. Everything the engine needs, so it never scans the library.</summary>
public sealed class MonitorAssignment
{
    public string ItemId { get; set; }
    public WallpaperType Type { get; set; }
    public string File { get; set; }
    public string Title { get; set; }
    public string MonitorName { get; set; }
    /// <summary>Set when the video is a converted Wallpaper Engine scene.</summary>
    public string SceneId { get; set; }
    /// <summary>Extra picture on each side, as a fraction of the monitor, that the mouse pans over (the scene's parallax).</summary>
    public double ParallaxMargin { get; set; }
    /// <summary>Set when this is a .vivo wallpaper: the video rendered from it, or (Type Web) the file played live.</summary>
    public string Package { get; set; }
    /// <summary>A converted scene's background sound, played in a loop next to the video.</summary>
    public string Audio { get; set; }
}

/// <summary>Per-wallpaper choices; kept by item so switching away and back keeps them.</summary>
public sealed class ItemSettings
{
    public FitMode Fit { get; set; } = FitMode.Fill;
    public int Volume { get; set; }                       // 0..100, 0 = muted
    public Dictionary<string, JsonNode> Properties { get; set; } = new();
}

public sealed class UserItem
{
    public string Id { get; set; }
    public string Title { get; set; }
    public WallpaperType Type { get; set; }
    public string File { get; set; }
    public DateTime Added { get; set; }
    public List<string> Tags { get; set; } = new();
}

public sealed class PauseSettings
{
    public CoveredAction WhenCovered { get; set; } = CoveredAction.Pause;
    public FullscreenAction WhenFullscreen { get; set; } = FullscreenAction.Pause;
    public bool FullscreenPausesAll { get; set; } = true;
    /// <summary>Programs that pause (or unload) every wallpaper while they are open: After Effects, ComfyUI and the like.</summary>
    public List<ProgramRule> Programs { get; set; } = new();
}

public enum ProgramAction { Pause, Unload }

public sealed class ProgramRule
{
    /// <summary>The process name, with or without ".exe" (AfterFX.exe, python.exe).</summary>
    public string Exe { get; set; }
    public ProgramAction Action { get; set; } = ProgramAction.Pause;
}

/// <summary>Wallpapers a monitor goes through on its own: each entry is ready to play on that monitor as it is.</summary>
public sealed class Playlist
{
    public List<MonitorAssignment> Entries { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public int Minutes { get; set; } = 30;
    public bool Shuffle { get; set; }
    public int Index { get; set; } = -1;
    /// <summary>When the monitor last changed by the playlist (or the entry was put there); the next change counts from it.</summary>
    public DateTime Changed { get; set; } = DateTime.Now;

    public static readonly int[] MinuteChoices = { 5, 15, 30, 60, 120 };
}

public sealed class ConvertedVideo
{
    public string Source { get; set; }
    public string Output { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public FitMode Fit { get; set; }
    public long SourceLength { get; set; }
}

/// <summary>A scene recorded, or a .vivo rendered, into a video for one monitor size.</summary>
public sealed class ConvertedScene
{
    public string SceneId { get; set; }
    public string SceneFolder { get; set; }
    public string Title { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }
    public string Output { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public double Margin { get; set; }
    public int Fps { get; set; }
    public double Duration { get; set; }
    public DateTime Created { get; set; }
    /// <summary>For a rendered .vivo: the file version it was made from (Packages.Stamp).</summary>
    public string SourceStamp { get; set; }
    /// <summary>The scene's background sound, taken out of its package (null when it has none).</summary>
    public string Audio { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public Dictionary<string, MonitorAssignment> Monitors { get; set; } = new();
    public Dictionary<string, ItemSettings> Items { get; set; } = new();
    public List<UserItem> UserItems { get; set; } = new();
    public List<ConvertedVideo> Converted { get; set; } = new();
    public List<ConvertedScene> ConvertedScenes { get; set; } = new();
    public double SceneDuration { get; set; } = 20;
    public double SceneCrossfade { get; set; } = 1.5;
    public PauseSettings Pause { get; set; } = new();
    /// <summary>By monitor id.</summary>
    public Dictionary<string, Playlist> Playlists { get; set; } = new();
    public bool WebMouse { get; set; } = true;
    public bool WebAudio { get; set; } = true;
    public bool PausedByUser { get; set; }
    public bool ScanWallpaperEngine { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>Interface language: "auto" (follows Windows), "pt" or "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Frame-rate cap for live wallpapers (web, and the app's own format later). The monitor's rate still wins when lower.</summary>
    public int FpsLimit { get; set; } = 60;

    public static readonly int[] FpsChoices = { 30, 60, 120, 144, 160 };

    /// <summary>What a live wallpaper on this monitor actually runs at.</summary>
    public int EffectiveFps(double monitorHz)
    {
        int hz = monitorHz >= 20 ? (int)Math.Round(monitorHz) : 60;
        int cap = FpsChoices.Contains(FpsLimit) ? FpsLimit : 60;
        return Math.Min(cap, hz);
    }

    public ItemSettings SettingsFor(string itemId)
    {
        if (!Items.TryGetValue(itemId, out var s)) Items[itemId] = s = new ItemSettings();
        return s;
    }

    /// <summary>The file to actually play: the converted copy when one matches this monitor and fit.</summary>
    public string PlayableFile(MonitorAssignment a, int width, int height)
    {
        if (a.Type != WallpaperType.Video) return a.File;
        var fit = SettingsFor(a.ItemId).Fit;
        long length = SafeLength(a.File);
        var c = Converted.FirstOrDefault(c => string.Equals(c.Source, a.File, StringComparison.OrdinalIgnoreCase)
            && c.Width == width && c.Height == height && c.Fit == fit && c.SourceLength == length && System.IO.File.Exists(c.Output));
        return c?.Output ?? a.File;
    }

    public static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; } catch { return -1; }
    }
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// A file held for a moment (antivirus, backup, the other process saving) is retried and then reported as an
    /// error, never turned into an empty config: callers read, change and save, and saving an empty one would wipe
    /// every choice. Only a file that is not valid JSON falls back to defaults, with a copy kept aside.
    /// </summary>
    public static AppConfig Load()
    {
        if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
        string text = null;
        for (int attempt = 0; ; attempt++)
        {
            try { text = File.ReadAllText(AppPaths.ConfigFile); break; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(50); }
            catch (FileNotFoundException) { return new AppConfig(); }
        }
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(text, Json) ?? new AppConfig();
        }
        catch (JsonException e)
        {
            Log.Write("config is not valid JSON, keeping a copy: " + e.Message);
            try { File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".broken", true); } catch { }
            return new AppConfig();
        }
    }

    /// <summary>Read, change, save: what every writer does, so nobody overwrites another's change.</summary>
    public static AppConfig Mutate(Action<AppConfig> change)
    {
        var fresh = Load();
        change(fresh);
        Save(fresh);
        return fresh;
    }

    public static void Save(AppConfig config)
    {
        // Unique temp name: the engine and the window may save at the same moment.
        string tmp = $"{AppPaths.ConfigFile}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, AppPaths.ConfigFile, true); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(50); }
            catch { try { File.Delete(tmp); } catch { } throw; }
        }
    }
}
