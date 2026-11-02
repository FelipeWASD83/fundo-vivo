using System.Reflection;

namespace FundoVivo.Core;

public static class AppPaths
{
    public const string AppName = "Fundo Vivo";
    public const string Repository = "FelipeWASD83/fundo-vivo";

    // FUNDOVIVO_HOME moves every data folder somewhere else, so tests never touch the real config.
    static readonly string TestHome = Environment.GetEnvironmentVariable("FUNDOVIVO_HOME");
    public static bool TestMode => !string.IsNullOrEmpty(TestHome);

    public static string Roaming { get; } = Ensure(TestMode ? Path.Combine(TestHome, "roaming")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FundoVivo"));
    public static string Local { get; } = Ensure(TestMode ? Path.Combine(TestHome, "local")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FundoVivo"));

    public static string ConfigFile => Path.Combine(Roaming, "config.json");
    public static string SecretsFile => Path.Combine(Roaming, "secrets.bin");
    public static string StateFile => Path.Combine(Local, "state.json");
    public static string LogFolder => Ensure(Path.Combine(Local, "logs"));
    public static string ConvertedFolder => Ensure(Path.Combine(Local, "converted"));
    public static string WebViewData => Ensure(Path.Combine(Local, "webview"));
    public static string UpdateFolder => Ensure(Path.Combine(Local, "update"));

    public static string AppFolder => AppContext.BaseDirectory.TrimEnd('\\');
    public static string ExePath => Path.Combine(AppFolder, "FundoVivo.exe");

    // A development copy is the app folder sitting inside the git checkout (fundo-vivo\app).
    public static bool IsDevelopmentCopy => Directory.Exists(Path.Combine(AppFolder, "..", ".git"));

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class Log
{
    static readonly object Gate = new();
    public static string Name { get; set; } = "app";

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string file = Path.Combine(AppPaths.LogFolder, Name + ".log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 1_000_000) File.Move(file, file + ".old", true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
