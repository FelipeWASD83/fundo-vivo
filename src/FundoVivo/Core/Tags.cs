using System.Globalization;
using System.Text;

namespace FundoVivo.Core;

/// <summary>
/// Tags as the library shows them: Wallpaper Engine's fixed English tags through the pt/en tables (late_tag_*), the
/// user's own tags as typed,
/// and one key per tag that ignores case and accents, so "anime", "Anime" and "ÁNIME" are the same tag.
/// </summary>
public static class Tags
{
        static readonly Dictionary<string, Func<string>> WallpaperEngineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Anime"] = () => "Anime", ["CGI"] = () => "CGI", ["Cyberpunk"] = () => "Cyberpunk", ["Medieval"] = () => "Medieval",
        ["Memes"] = () => "Memes", ["MMD"] = () => "MMD", ["Pixel art"] = () => "Pixel art",
        ["Abstract"] = () => Loc.T("late_tag_abstract"), ["Animal"] = () => Loc.T("late_tag_animal"),
        ["Cartoon"] = () => Loc.T("late_tag_cartoon"), ["Fantasy"] = () => Loc.T("late_tag_fantasy"),
        ["Game"] = () => Loc.T("late_tag_game"), ["Girls"] = () => Loc.T("late_tag_girls"), ["Guys"] = () => Loc.T("late_tag_guys"),
        ["Landscape"] = () => Loc.T("late_tag_landscape"), ["Music"] = () => Loc.T("late_tag_music"),
        ["Nature"] = () => Loc.T("late_tag_nature"), ["Relaxing"] = () => Loc.T("late_tag_relaxing"),
        ["Retro"] = () => Loc.T("late_tag_retro"), ["Sci-Fi"] = () => Loc.T("late_tag_scifi"),
        ["Sports"] = () => Loc.T("late_tag_sports"), ["Technology"] = () => Loc.T("late_tag_technology"),
        ["Television"] = () => Loc.T("late_tag_television"), ["Vehicle"] = () => Loc.T("late_tag_vehicle"),
        ["Unspecified"] = () => Loc.T("late_tag_unspecified"),
    };

    /// <summary>Wallpaper Engine's content ratings, as the position in <see cref="RatingOrder"/>.</summary>
    static readonly Dictionary<string, int> Ratings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Everyone"] = 0, ["Questionable"] = 1, ["Mature"] = 2,
    };

    /// <summary>The rating labels of the current language, in the order the rating chips appear in.</summary>
    public static string[] RatingOrder => new[] { Loc.T("core_rating_everyone"), Loc.T("core_rating_questionable"), Loc.T("core_rating_mature") };

    /// <summary>The tag as shown in the current language; tags that are not in the table stay as the project file has them.</summary>
    public static string FromWallpaperEngine(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        // In English the tags show exactly as the project file has them; Portuguese gets the translated names.
        if (Loc.English) return tag.Trim();
        return WallpaperEngineNames.TryGetValue(tag.Trim(), out var name) ? name() : tag.Trim();
    }

    /// <summary>The rating label in the current language, or null when the project does not say.</summary>
    public static string Rating(string contentRating) =>
        contentRating != null && Ratings.TryGetValue(contentRating.Trim(), out int i) ? RatingOrder[i] : null;

    public static string Key(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        var decomposed = tag.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// What gets saved for a typed tag: the spelling that already exists in the library when there is one, so a
    /// second "anime" becomes the existing "Anime" instead of a new tag.
    /// </summary>
    public static string Canonical(string typed, IEnumerable<string> existing)
    {
        string trimmed = typed?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        string key = Key(trimmed);
        return existing.FirstOrDefault(e => Key(e) == key) ?? trimmed;
    }
}
