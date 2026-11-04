using System.Globalization;
using System.Windows.Markup;

namespace FundoVivo.Core;

/// <summary>
/// Interface language: Portuguese or English. Follows the Windows display language unless the config forces one
/// (<see cref="AppConfig.Language"/>: "auto", "pt" or "en"). FUNDOVIVO_LANG=pt|en overrides both, for tests.
/// Every user-facing text lives in the tables of Core/Strings/*.cs as (key, pt, en) and is read with <see cref="T"/>.
/// </summary>
public static class Loc
{
    public static bool English { get; private set; }

    /// <summary>"Living Wallpaper" in English, "Fundo Vivo" in Portuguese: the window title, tray and messages.</summary>
    public static string AppName => English ? "Living Wallpaper" : "Fundo Vivo";

    static readonly Dictionary<string, (string Pt, string En)> Table = new();
    static readonly List<string> Problems = new();

    static Loc()
    {
        foreach (var set in new[] { StringsSettings.All, StringsDialogs.All, StringsCore.All, StringsEngine.All, StringsTools.All, StringsLate.All })
            foreach (var (key, pt, en) in set)
                if (!Table.TryAdd(key, (pt, en))) Problems.Add("duplicate key: " + key);
        Init("auto");
    }

    /// <param name="choice">"auto" (the Windows language), "pt" or "en".</param>
    public static void Init(string choice)
    {
        string forced = Environment.GetEnvironmentVariable("FUNDOVIVO_LANG");
        if (forced is "pt" or "en") choice = forced;
        English = choice switch
        {
            "en" => true,
            "pt" => false,
            _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>The text for <paramref name="key"/> in the current language, with {0}-style arguments filled in.</summary>
    public static string T(string key, params object[] args)
    {
        if (key == "app_name") return AppName;
        if (!Table.TryGetValue(key, out var t))
        {
            Log.Write("missing string: " + key);
            return key;
        }
        string s = English ? t.En : t.Pt;
        return args.Length > 0 ? string.Format(CultureInfo.CurrentCulture, s, args) : s;
    }

    /// <summary>The same text in both languages, for code that must recognise either (e.g. a saved title).</summary>
    public static IEnumerable<string> All(string key) =>
        Table.TryGetValue(key, out var t) ? new[] { t.Pt, t.En } : Array.Empty<string>();

    /// <summary>Consistency problems of the tables: duplicate keys, empty texts, placeholders that differ between languages.</summary>
    public static List<string> Validate()
    {
        var found = new List<string>(Problems);
        foreach (var (key, (pt, en)) in Table)
        {
            if (string.IsNullOrWhiteSpace(pt) || string.IsNullOrWhiteSpace(en)) found.Add("empty text: " + key);
            var a = System.Text.RegularExpressions.Regex.Matches(pt ?? "", @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = System.Text.RegularExpressions.Regex.Matches(en ?? "", @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            if (!a.SequenceEqual(b)) found.Add("placeholders differ: " + key);
        }
        return found;
    }
}

/// <summary>XAML: <c>Text="{loc:T settings.tab_library}"</c> (xmlns:loc="clr-namespace:FundoVivo.Core").</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; }
    public TExtension() { }
    public TExtension(string key) { Key = key; }
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
