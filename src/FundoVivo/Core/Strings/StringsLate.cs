namespace FundoVivo.Core;

/// <summary>(key, Portuguese, English). One entry per line; plain string literals only (tools/check_strings.py reads this file).</summary>
public static class StringsLate
{
    public static readonly (string Key, string Pt, string En)[] All =
    {
        // Wallpaper Engine's fixed tags (Tags.FromWallpaperEngine); tags that read the same in both languages are not listed
        ("late_tag_abstract", "Abstrato", "Abstract"),
        ("late_tag_animal", "Animais", "Animal"),
        ("late_tag_cartoon", "Desenho", "Cartoon"),
        ("late_tag_fantasy", "Fantasia", "Fantasy"),
        ("late_tag_game", "Jogo", "Game"),
        ("late_tag_girls", "Garotas", "Girls"),
        ("late_tag_guys", "Garotos", "Guys"),
        ("late_tag_landscape", "Paisagem", "Landscape"),
        ("late_tag_music", "Música", "Music"),
        ("late_tag_nature", "Natureza", "Nature"),
        ("late_tag_relaxing", "Relaxante", "Relaxing"),
        ("late_tag_retro", "Retrô", "Retro"),
        ("late_tag_scifi", "Ficção científica", "Sci-Fi"),
        ("late_tag_sports", "Esportes", "Sports"),
        ("late_tag_technology", "Tecnologia", "Technology"),
        ("late_tag_television", "TV", "Television"),
        ("late_tag_vehicle", "Veículos", "Vehicle"),
        ("late_tag_unspecified", "Sem categoria", "Unspecified"),

        // Wallpaper Engine's own property labels, for the ui_ keys projects use without shipping the text (ProjectInfo.Text)
        ("late_prop_scheme_color", "Cor do tema", "Scheme color"),
        ("late_prop_alignment", "Alinhamento", "Alignment"),
        ("late_prop_alignment_center", "Centro", "Center"),
        ("late_prop_alignment_left", "Esquerda", "Left"),
        ("late_prop_alignment_right", "Direita", "Right"),
        ("late_prop_alignment_top", "Topo", "Top"),
        ("late_prop_alignment_bottom", "Embaixo", "Bottom"),
        ("late_prop_background_color", "Cor de fundo", "Background color"),
        ("late_prop_image", "Imagem", "Image"),
        ("late_prop_volume", "Volume", "Volume"),
        ("late_prop_playback_rate", "Velocidade", "Speed"),
    };
}
