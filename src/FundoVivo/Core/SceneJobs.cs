using System.Diagnostics;
using System.Text.Json;

namespace FundoVivo.Core;

public sealed class SceneJobStatus
{
    public string JobId { get; set; }
    public string SceneTitle { get; set; }
    public string MonitorName { get; set; }
    public string State { get; set; } = "idle";   // running, done, error, canceled, idle
    public double Progress { get; set; }
    public string Message { get; set; }
    public int Queued { get; set; }
    public DateTime Updated { get; set; } = DateTime.Now;
}

/// <summary>
/// The scene conversion queue, shared by the settings window (adds jobs, shows progress, asks to cancel) and the
/// worker process `FundoVivo.exe --convert-scenes` (one at a time, keeps going when the window closes).
/// Everything is files in %LOCALAPPDATA%\FundoVivo\scene-jobs, guarded by a named mutex.
/// </summary>
public static class SceneJobs
{
    static string Folder => Ensure(Path.Combine(AppPaths.Local, "scene-jobs"));
    static string QueueFile => Path.Combine(Folder, "queue.json");
    static string StatusFile => Path.Combine(Folder, "status.json");
    static string CancelFile(string id) => Path.Combine(Folder, "cancel-" + id);
    const string QueueMutex = @"Local\FundoVivoSceneQueue";
    const string WorkerMutex = @"Local\FundoVivoSceneWorker";

    static string Ensure(string d) { Directory.CreateDirectory(d); return d; }

    static T Locked<T>(Func<T> action)
    {
        using var m = new Mutex(false, QueueMutex);
        try { m.WaitOne(5000); } catch (AbandonedMutexException) { }
        try { return action(); } finally { try { m.ReleaseMutex(); } catch { } }
    }

    static List<SceneJob> ReadQueue()
    {
        try { return File.Exists(QueueFile) ? JsonSerializer.Deserialize<List<SceneJob>>(File.ReadAllText(QueueFile), ConfigStore.Json) ?? new() : new(); }
        catch { return new(); }
    }

    static void WriteQueue(List<SceneJob> q) => WriteAtomic(QueueFile, JsonSerializer.Serialize(q, ConfigStore.Json));

    static void WriteAtomic(string path, string text)
    {
        string tmp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    public static IReadOnlyList<SceneJob> Pending() => Locked(ReadQueue);

    /// <summary>Adds a job and makes sure the worker is running.</summary>
    public static void Enqueue(SceneJob job)
    {
        Locked(() => { var q = ReadQueue(); q.Add(job); WriteQueue(q); return 0; });
        if (!WorkerRunning) Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--convert-scenes") { UseShellExecute = false });
    }

    public static bool WorkerRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(WorkerMutex, out var m)) return false;
            m.Dispose();
            return true;
        }
    }

    /// <summary>Stops the running job (and drops it); a queued one is just removed.</summary>
    public static void Cancel(string jobId)
    {
        bool queued = Locked(() =>
        {
            var q = ReadQueue();
            int removed = q.RemoveAll(j => j.Id == jobId);
            if (removed > 0) WriteQueue(q);
            return removed > 0;
        });
        if (!queued) File.WriteAllText(CancelFile(jobId), "");
    }

    public static SceneJobStatus Status()
    {
        try { return File.Exists(StatusFile) ? JsonSerializer.Deserialize<SceneJobStatus>(File.ReadAllText(StatusFile), ConfigStore.Json) : null; }
        catch { return null; }
    }

    static void SetStatus(SceneJobStatus s)
    {
        s.Updated = DateTime.Now;
        s.Queued = Pending().Count;
        try { WriteAtomic(StatusFile, JsonSerializer.Serialize(s, ConfigStore.Json)); } catch { }
    }

    /// <summary>The worker: takes jobs until the queue is empty. Only one runs at a time.</summary>
    public static int RunWorker()
    {
        using var single = new Mutex(true, WorkerMutex, out bool first);
        if (!first) return 0;
        while (true)
        {
            var job = Locked(() =>
            {
                var q = ReadQueue();
                if (q.Count == 0) return null;
                var j = q[0];
                q.RemoveAt(0);
                WriteQueue(q);
                return j;
            });
            if (job == null) return 0;
            Run(job);
        }
    }

    static void Run(SceneJob job)
    {
        var status = new SceneJobStatus { JobId = job.Id, SceneTitle = job.SceneTitle, MonitorName = job.MonitorName, State = "running", Message = "Começando" };
        SetStatus(status);
        using var cts = new CancellationTokenSource();
        using var watch = new Timer(_ => { if (File.Exists(CancelFile(job.Id))) cts.Cancel(); }, null, 500, 500);
        var last = DateTime.MinValue;
        var progress = new SyncProgress<(double p, string msg)>(x =>
        {
            status.Progress = x.p;
            status.Message = x.msg;
            if ((DateTime.Now - last).TotalMilliseconds > 300 || x.p >= 1) { last = DateTime.Now; SetStatus(status); }
        });
        try
        {
            var result = SceneConverter.ConvertAsync(job, progress, cts.Token).GetAwaiter().GetResult();
            var scene = SceneProject.Load(job.SceneFolder);
            ConfigStore.Mutate(c =>
            {
                c.ConvertedScenes.RemoveAll(x => x.SceneId == scene.Id && x.MonitorWidth == job.Width && x.MonitorHeight == job.Height);
                c.ConvertedScenes.Add(new ConvertedScene
                {
                    SceneId = scene.Id, SceneFolder = job.SceneFolder, Title = scene.Title, MonitorWidth = job.Width, MonitorHeight = job.Height,
                    Output = result.Output, VideoWidth = result.Width, VideoHeight = result.Height, Margin = result.Margin, Fps = result.Fps,
                    Duration = job.Duration, Created = DateTime.Now,
                });
                if (job.Assign && job.MonitorId != null)
                    c.Monitors[job.MonitorId] = new MonitorAssignment
                    {
                        ItemId = scene.Id, Type = WallpaperType.Video, File = result.Output, Title = scene.Title, MonitorName = job.MonitorName,
                        SceneId = scene.Id, ParallaxMargin = result.Margin,
                    };
            });
            if (job.Assign && !Engine.EngineHost.Send("{\"cmd\":\"reload\"}"))
                Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--engine") { UseShellExecute = false });
            status.State = "done";
            status.Progress = 1;
            status.Message = "Pronto";
            Log.Write($"scene converted: {scene.Title} {job.Width}x{job.Height} -> {result.Output} ({result.Width}x{result.Height}, margin {result.Margin:0.###}, {result.Fps} fps)");
        }
        catch (OperationCanceledException)
        {
            status.State = "canceled";
            status.Message = "Cancelada";
        }
        catch (Exception e)
        {
            status.State = "error";
            status.Message = e.Message;
            Log.Write($"scene conversion failed ({job.SceneTitle}): {e}");
        }
        finally
        {
            try { File.Delete(CancelFile(job.Id)); } catch { }
            SetStatus(status);
        }
    }

    /// <summary>Progress that reports on the calling thread (the worker has no UI thread to post to).</summary>
    sealed class SyncProgress<T> : IProgress<T>
    {
        readonly Action<T> _report;
        public SyncProgress(Action<T> report) { _report = report; }
        public void Report(T value) => _report(value);
    }
}
