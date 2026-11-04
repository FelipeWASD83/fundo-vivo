namespace FundoVivo.Core;

/// <summary>(key, Portuguese, English). One entry per line; plain string literals only (tools/check_strings.py reads this file).</summary>
public static class StringsCore
{
    public static readonly (string Key, string Pt, string En)[] All =
    {
        // content ratings of Wallpaper Engine projects (Tags.RatingOrder)
        ("core_rating_everyone", "Livre", "Everyone"),
        ("core_rating_questionable", "Questionável", "Questionable"),
        ("core_rating_mature", "Adulto", "Mature"),

        // classification tags of converted scenes (SceneProject.TraitTags)
        ("core_trait_lossless", "Converte sem perder nada", "Converts losslessly"),
        ("core_trait_parallax", "Parallax vira pan", "Parallax becomes pan"),
        ("core_trait_sound", "Perde reação ao som", "Loses sound reaction"),
        ("core_trait_mouse", "Perde o mouse", "Loses the mouse"),
        ("core_trait_unreadable", "Sem leitura", "Unreadable"),
        ("core_trait_direct", "Vira vídeo direto", "Becomes video directly"),
        ("core_trait_clock", "Relógio fica parado", "Clock stays frozen"),
        ("core_trait_bgsound", "Som vai junto", "Sound goes with it"),

        // why a wallpaper cannot be prepared (Placement.JobFor), shown after "Não deu para preparar esse wallpaper: "
        ("core_problem_ffmpeg", "o ffmpeg precisa estar no PATH", "ffmpeg must be on the PATH"),
        ("core_problem_we_missing", "o Wallpaper Engine não foi encontrado (é ele que desenha a cena para a gravação)", "Wallpaper Engine was not found (it draws the scene for the recording)"),
        ("core_problem_vivo_unreadable", "não deu para ler esse arquivo .vivo (veja o registro)", "that .vivo file could not be read (see the log)"),
        ("core_problem_no_conversion", "esse wallpaper não precisa de conversão", "this wallpaper does not need conversion"),
    };
}
