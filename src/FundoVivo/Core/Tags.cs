using System.Globalization;
using System.Text;

namespace FundoVivo.Core;

/// <summary>
/// Tags as the library shows them: Wallpaper Engine's fixed English tags translated, the user's own tags as typed,
/// and one key per tag that ignores case and accents, so "anime", "Anime" and "ÁNIME" are the same tag.
/// </summary>
public static class Tags
{
    static readonly Dictionary<string, string> WallpaperEngineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abstract"] = "Abstrato", ["Animal"] = "Animais", ["Anime"] = "Anime", ["Cartoon"] = "Desenho", ["CGI"] = "CGI",
        ["Cyberpunk"] = "Cyberpunk", ["Fantasy"] = "Fantasia", ["Game"] = "Jogo", ["Girls"] = "Garotas", ["Guys"] = "Garotos",
        ["Landscape"] = "Paisagem", ["Medieval"] = "Medieval", ["Memes"] = "Memes", ["MMD"] = "MMD", ["Music"] = "Música",
        ["Nature"] = "Natureza", ["Pixel art"] = "Pixel art", ["Relaxing"] = "Relaxante", ["Retro"] = "Retrô",
        ["Sci-Fi"] = "Ficção científica", ["Sports"] = "Esportes", ["Technology"] = "Tecnologia", ["Television"] = "TV",
        ["Vehicle"] = "Veículos", ["Unspecified"] = "Sem categoria",
    };

    static readonly Dictionary<string, string> Ratings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Everyone"] = "Livre", ["Questionable"] = "Questionável", ["Mature"] = "Adulto",
    };

    /// <summary>The order the rating chips appear in.</summary>
    public static readonly string[] RatingOrder = { "Livre", "Questionável", "Adulto" };

    public static string FromWallpaperEngine(string tag) =>
        string.IsNullOrWhiteSpace(tag) ? null : WallpaperEngineNames.TryGetValue(tag.Trim(), out var pt) ? pt : tag.Trim();

    public static string Rating(string contentRating) =>
        contentRating != null && Ratings.TryGetValue(contentRating.Trim(), out var pt) ? pt : null;

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
