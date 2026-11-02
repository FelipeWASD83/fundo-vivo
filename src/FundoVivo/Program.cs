using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using FundoVivo.Engine;
using FundoVivo.Native;

namespace FundoVivo;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        switch (mode)
        {
            case "--check-load": return CheckLoad();
            case "--engine": return RunEngine();
            case "--settings": return RunSettings();
            case "--quit": return EngineHost.Send("{\"cmd\":\"quit\"}") ? 0 : 1;
            case "--pause" or "--resume" or "--toggle-pause" or "--next" or "--set": return Cli.Run(args);
            case "--convert-scenes":
                // Scene conversions run here, in a process of their own: never inside the engine.
                Log.Name = "scenes";
                try { return SceneJobs.RunWorker(); }
                catch (Exception e) { Log.Write("scene worker crashed: " + e); return 1; }
            case "--vivo-still":
                // One frame of a .vivo to a PNG, drawn by the same runtime and renderer that make its video.
                Log.Name = "scenes";
                try
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    PackageRenderer.Still(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]),
                        args.Length > 5 ? double.Parse(args[5], inv) : 0, args.Length > 6 ? double.Parse(args[6], inv) : 0);
                    return 0;
                }
                catch (Exception e) { Log.Write("vivo still failed: " + e); return 1; }
            case "--screenshot":
                Log.Name = "settings";
                return new Settings.SettingsApp(args.Skip(1).ToArray()).Run();
            case "--describe":
                Log.Name = "describe";
                Log.Write(Desktop.Describe());
                foreach (var m in Monitors.Enumerate()) Log.Write($"{m.Label} {m.Bounds} {m.RefreshRate:0.#}Hz primary={m.Primary} id={m.Id}");
                return 0;
            default:
                // The shortcut: make sure the engine is running, then open the window.
                if (!EngineHost.IsRunning) StartEngineProcess();
                return RunSettings();
        }
    }

    public static void StartEngineProcess()
    {
        Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--engine") { UseShellExecute = false });
    }

    static int RunEngine()
    {
        Log.Name = "engine";
        using var mutex = new Mutex(true, @"Local\FundoVivoEngine", out bool first);
        if (!first) return 0;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("unhandled: " + e.ExceptionObject);
        try
        {
            EngineHost.Run();
        }
        catch (Exception e)
        {
            Log.Write("engine crashed: " + e);
            return 1;
        }
        return 0;
    }

    static int RunSettings()
    {
        Log.Name = "settings";
        using var mutex = new Mutex(true, @"Local\FundoVivoSettings", out bool first);
        if (!first)
        {
            IntPtr existing = Win32.FindWindowW(null, AppPaths.AppName);
            if (existing != IntPtr.Zero)
            {
                Win32.ShowWindow(existing, Win32.SW_RESTORE);
                Win32.SetForegroundWindow(existing);
            }
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("unhandled: " + e.ExceptionObject);
        var app = new Settings.SettingsApp();
        return app.Run();
    }

    /// <summary>
    /// Loads every DLL next to the exe. Smart App Control judges each new file by its hash and may refuse one;
    /// the build script uses this to know whether it has to rebuild with a new stamp.
    /// </summary>
    static int CheckLoad()
    {
        int blocked = 0;
        foreach (string dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                bool managed;
                try { AssemblyName.GetAssemblyName(dll); managed = true; } catch (BadImageFormatException) { managed = false; }
                if (managed) Assembly.LoadFrom(dll);
                else if (Path.GetFileName(dll).Equals("WebView2Loader.dll", StringComparison.OrdinalIgnoreCase)) NativeLibrary.Free(NativeLibrary.Load(dll));
            }
            catch (Exception e) when (e is FileLoadException or DllNotFoundException or BadImageFormatException)
            {
                if ((uint)e.HResult == 0x800711C7 || e.Message.Contains("Controle de Aplicativo") || e.Message.Contains("Application Control"))
                {
                    blocked++;
                    Console.Error.WriteLine("BLOCKED " + Path.GetFileName(dll));
                }
            }
        }
        return blocked == 0 ? 0 : 3;
    }
}
