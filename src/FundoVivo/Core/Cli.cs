using System.Diagnostics;
using System.Text.Json.Nodes;
using FundoVivo.Engine;

namespace FundoVivo.Core;

/// <summary>
/// Command line for keyboard shortcuts and the Stream Deck, like Wallpaper Engine's -control:
///   FundoVivo.exe --pause | --resume | --toggle-pause
///   FundoVivo.exe --next [monitor]                  next playlist entry (every monitor with a playlist, or one)
///   FundoVivo.exe --set &lt;monitor&gt; &lt;file or title&gt;   puts it on that monitor, as a click in the library would
/// A monitor is "principal" (or "primary"), its number counted from the left (1, 2...) or part of its name. The app has no console:
/// the outcome is the exit code (0 done, 2 a video is being made first, 1 failed) and a line in logs\cli.log.
/// </summary>
public static class Cli
{
    public static int Run(string[] args)
    {
        Log.Name = "cli";
        try
        {
            switch (args[0])
            {
                case "--pause": return Send(new JsonObject { ["cmd"] = "pause", ["value"] = true });
                case "--resume": return Send(new JsonObject { ["cmd"] = "pause", ["value"] = false });
                case "--toggle-pause": return Send(new JsonObject { ["cmd"] = "pause" });
                case "--next":
                {
                    var cmd = new JsonObject { ["cmd"] = "next" };
                    if (args.Length > 1)
                    {
                        var m = FindMonitor(args[1]);
                        if (m == null) return Fail(Loc.T("tool_cli_monitor_not_found", args[1]));
                        cmd["monitor"] = m.Id;
                    }
                    return Send(cmd);
                }
                case "--set":
                    return args.Length < 3 ? Fail(Loc.T("tool_cli_usage")) : Set(args[1], string.Join(' ', args.Skip(2)));
            }
        }
        catch (Exception e) { return Fail(e.ToString()); }
        return Fail(Loc.T("tool_cli_unknown_command", args[0]));
    }

    static int Send(JsonObject cmd)
    {
        if (EngineHost.Send(cmd.ToJsonString())) { Log.Write("sent " + cmd.ToJsonString()); return 0; }
        return Fail(Loc.T("tool_cli_not_running", Loc.AppName));
    }

    static int Fail(string why)
    {
        Log.Write("failed: " + why);
        return 1;
    }

    public static MonitorInfo FindMonitor(string arg)
    {
        var all = Monitors.Enumerate().OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
        if (arg.Equals("principal", StringComparison.OrdinalIgnoreCase) || arg.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return all.FirstOrDefault(m => m.Primary);
        if (int.TryParse(arg, out int n)) return n >= 1 && n <= all.Count ? all[n - 1] : null;
        return all.FirstOrDefault(m => m.Id.Equals(arg, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(m => m.Label.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }

    static int Set(string monitorArg, string what)
    {
        var monitor = FindMonitor(monitorArg);
        if (monitor == null) return Fail(Loc.T("tool_cli_monitor_not_found", monitorArg));
        var config = ConfigStore.Load();
        var scan = Library.Scan(config);
        LibraryItem item = null;
        if (File.Exists(what))
        {
            string id = Library.IdFor(what);
            item = scan.Items.FirstOrDefault(i => i.Id == id || Library.IdFor(i.File) == id);
            if (item == null)
            {
                // a file that is not in the library yet comes in as one of the user's own, like Adicionar arquivo
                var type = Library.TypeForFile(what) ?? throw new InvalidOperationException(Loc.T("tool_cli_file_type", Loc.AppName, what));
                var info = type == WallpaperType.Package ? Packages.Inspect(what) : null;
                ConfigStore.Mutate(c =>
                {
                    if (c.UserItems.All(u => u.Id != id))
                        c.UserItems.Add(new UserItem { Id = id, File = Path.GetFullPath(what), Type = type, Title = info?.Title ?? Path.GetFileNameWithoutExtension(what), Added = DateTime.Now, Tags = info?.Tags.ToList() ?? new() });
                });
                config = ConfigStore.Load();
                item = Library.Scan(config).Items.FirstOrDefault(i => i.Id == id);
            }
        }
        else
            item = scan.Items.FirstOrDefault(i => i.Title.Equals(what, StringComparison.CurrentCultureIgnoreCase))
                ?? scan.Items.FirstOrDefault(i => i.Title.Contains(what, StringComparison.CurrentCultureIgnoreCase));
        if (item == null) return Fail(Loc.T("tool_cli_nothing_found", what));

        if (Placement.Ready(config, item, monitor) is { } ready)
        {
            ConfigStore.Mutate(c =>
            {
                c.Monitors[monitor.Id] = ready;
                if (c.Playlists.TryGetValue(monitor.Id, out var pl)) pl.Changed = DateTime.Now;
            });
            if (!EngineHost.Send("{\"cmd\":\"reload\"}")) Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--engine") { UseShellExecute = false });
            Log.Write($"set {item.Title} on {monitor.Label}");
            return 0;
        }
        var job = Placement.JobFor(config, item, monitor, out string problem);
        if (job == null) return Fail(problem);
        SceneJobs.Enqueue(job);
        Log.Write($"set {item.Title} on {monitor.Label}: making the video first");
        return 2;
    }
}
