namespace FundoVivo.Core;

/// <summary>
/// What a library item plays as on one monitor: the assignment when it can play there now, or the job that makes the
/// video it needs (a scene, a .vivo with no sound, an animated GIF are videos made for one monitor size). Shared by the
/// window, the playlists and `FundoVivo.exe --set`, so the three put things on screen the same way.
/// </summary>
public static class Placement
{
    /// <summary>The assignment, or null when a video has to be made for this monitor first.</summary>
    public static MonitorAssignment Ready(AppConfig c, LibraryItem item, MonitorInfo m)
    {
        ConvertedScene Made(bool stamped)
        {
            string stamp = stamped ? Packages.Stamp(item.File) : null;
            return c.ConvertedScenes.FirstOrDefault(x => x.SceneId == item.Id && x.MonitorWidth == m.Bounds.Width && x.MonitorHeight == m.Bounds.Height
                && (!stamped || x.SourceStamp == stamp) && File.Exists(x.Output));
        }
        MonitorAssignment Video(ConvertedScene done, string package = null) => new()
        {
            ItemId = item.Id, Type = WallpaperType.Video, File = done.Output, Title = item.Title, MonitorName = m.Label,
            SceneId = item.Id, ParallaxMargin = done.Margin, Package = package,
        };

        if (item.Type == WallpaperType.Scene) return Made(false) is { } scene ? Video(scene) : null;
        if (NeedsGifVideo(item)) return Made(true) is { } gif ? Video(gif) : null;
        if (item.Type == WallpaperType.Package && !item.Realtime) return Made(true) is { } vivo ? Video(vivo, item.File) : null;
        // A .vivo that reacts to sound plays live: the engine opens its runtime page like a web wallpaper.
        bool live = item.Type == WallpaperType.Package;
        return new MonitorAssignment
        {
            ItemId = item.Id, Type = live ? WallpaperType.Web : item.Type, File = item.File, Title = item.Title, MonitorName = m.Label,
            Package = live ? item.File : null,
        };
    }

    public static bool NeedsGifVideo(LibraryItem item) =>
        item.Type == WallpaperType.Image && !item.FromWallpaperEngine && GifConverter.IsAnimated(item.File);

    /// <summary>
    /// The job that makes the video, assigning it when done; null with the reason when it cannot be made. A recorded
    /// scene takes the duration and crossfade last chosen (the window asks first; the command line does not).
    /// </summary>
    public static SceneJob JobFor(AppConfig c, LibraryItem item, MonitorInfo m, out string problem)
    {
        problem = null;
        if (VideoConverter.FindFfmpeg() == null) { problem = "o ffmpeg precisa estar no PATH"; return null; }
        var job = new SceneJob
        {
            SceneTitle = item.Title, MonitorId = m.Id, MonitorName = m.Label, Width = m.Bounds.Width, Height = m.Bounds.Height, Crossfade = 0,
        };
        if (item.Type == WallpaperType.Scene)
        {
            job.SceneFolder = item.Folder;
            if (!item.Traits.HasFlag(SceneTraits.Direct))
            {
                if (SceneConverter.FindWallpaperEngine() == null) { problem = "o Wallpaper Engine não foi encontrado (é ele que desenha a cena para a gravação)"; return null; }
                job.Duration = c.SceneDuration;
                job.Crossfade = c.SceneCrossfade;
            }
            return job;
        }
        if (NeedsGifVideo(item))
        {
            job.Kind = "gif"; job.ItemId = item.Id; job.PackageFile = item.File;
            return job;
        }
        if (item.Type == WallpaperType.Package && !item.Realtime)
        {
            var info = Packages.Inspect(item.File);
            if (info == null) { problem = "não deu para ler esse arquivo .vivo (veja o registro)"; return null; }
            job.Kind = "package"; job.ItemId = item.Id; job.PackageFile = item.File; job.Duration = info.Loop;
            job.Fps = Math.Min(60, c.EffectiveFps(m.RefreshRate));
            return job;
        }
        problem = "esse wallpaper não precisa de conversão";
        return null;
    }
}
