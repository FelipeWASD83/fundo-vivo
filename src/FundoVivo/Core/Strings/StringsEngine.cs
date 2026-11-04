namespace FundoVivo.Core;

/// <summary>(key, Portuguese, English). One entry per line; plain string literals only (tools/check_strings.py reads this file).</summary>
public static class StringsEngine
{
    public static readonly (string Key, string Pt, string En)[] All =
    {
        ("engine_tray_tip_paused", "{0}: pausado", "{0}: paused"),
        ("engine_tray_tip_empty", "{0}: nenhum wallpaper escolhido", "{0}: no wallpaper selected"),
        ("engine_tray_tip_playing", "{0}: {1} de {2} tocando", "{0}: {1} of {2} playing"),
        ("engine_tray_open", "Abrir o {0}", "Open {0}"),
        ("engine_tray_resume", "Continuar", "Resume"),
        ("engine_tray_pause", "Pausar tudo", "Pause all"),
        ("engine_tray_quit", "Sair", "Exit"),
    };
}
