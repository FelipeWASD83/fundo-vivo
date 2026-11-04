namespace FundoVivo.Core;

/// <summary>(key, Portuguese, English). One entry per line; plain string literals only (tools/check_strings.py reads this file).</summary>
public static class StringsDialogs
{
    public static readonly (string Key, string Pt, string En)[] All =
    {
        ("dlg_cancel", "Cancelar", "Cancel"),
        ("dlg_save", "Salvar", "Save"),
        ("dlg_scene_title", "Converter cena", "Convert scene"),
        ("dlg_scene_intro", "Vira um vídeo em loop para {0} ({1}×{2}), a {3} fps (o limite de fps do seu Wallpaper Engine; para 60, suba o limite lá antes).", "It becomes a looping video for {0} ({1}×{2}), at {3} fps (your Wallpaper Engine's fps limit; to get 60, raise that limit there first)."),
        ("dlg_scene_note_unreadable", "Não deu para ler o que esta cena usa. Ela converte, mas sem a classificação.", "Could not read what this scene uses. It converts, but without the classification."),
        ("dlg_scene_note_lossless", "Converte sem perder nada: a cena não usa mouse, relógio, reação ao som nem parallax.", "Converts without losing anything: the scene uses no mouse, clock, sound reaction or parallax."),
        ("dlg_scene_note_parallax", "Parallax vira pan: o vídeo sai um pouco maior que a tela e anda com o mouse. É uma aproximação: o vídeo inteiro anda junto, sem a profundidade de cada camada.", "Parallax becomes a pan: the video comes out a little larger than the screen and moves with the mouse. It is an approximation: the whole video moves together, without the depth of each layer."),
        ("dlg_scene_note_sound", "Perde a reação ao som. E o que estiver tocando no PC durante a gravação fica gravado na animação: converta com o som parado.", "It loses the reaction to sound. And whatever is playing on the PC during the recording gets baked into the animation: convert with the sound stopped."),
        ("dlg_scene_note_mouse", "Perde o mouse: o que segue o cursor fica parado.", "It loses the mouse: anything that follows the cursor stays still."),
        ("dlg_scene_note_clock", "O relógio fica parado na hora da gravação. Se a cena tiver uma opção para esconder o relógio, ela está aqui embaixo.", "The clock stays frozen at the time of the recording. If the scene has an option to hide the clock, it is below."),
        ("dlg_scene_note_bgsound", "O som de fundo vai junto: ele sai do pacote e toca em loop com o vídeo, no volume do painel (começa mudo).", "The background sound comes along: it is taken from the package and plays in a loop with the video, at the volume set in the panel (it starts muted)."),
        ("dlg_scene_note_loop", "As animações da cena se repetem a cada pouco; um loop de {0} s fecha certinho com elas (por isso a duração já vem nele).", "The scene's animations repeat every so often; a {0} s loop fits them exactly (that is why the length is already set to it)."),
        ("dlg_scene_what_changes", "O QUE MUDA", "WHAT CHANGES"),
        ("dlg_scene_loop_length", "DURAÇÃO DO LOOP", "LOOP LENGTH"),
        ("dlg_scene_seam", "EMENDA (MISTURA DO FIM COM O COMEÇO)", "SEAM (BLENDS THE END INTO THE START)"),
        ("dlg_scene_options", "OPÇÕES DA CENA", "SCENE OPTIONS"),
        ("dlg_scene_recording", "Durante a gravação, a janela do Wallpaper Engine aparece por um instante no canto da tela e sai para fora dela; dá para continuar usando o PC. Não abra jogo em tela cheia até terminar: o Wallpaper Engine pausa e a gravação sai parada.", "During the recording, the Wallpaper Engine window shows up for a moment in the corner of the screen and then moves off it; you can keep using the PC. Do not open a fullscreen game until it finishes: Wallpaper Engine pauses and the recording comes out frozen."),
        ("dlg_scene_convert", "Converter", "Convert"),
        ("dlg_tags_title", "Tags de {0}", "Tags for {0}"),
        ("dlg_tags_hint", "Digite e aperte Enter. As tags que já existem aparecem como sugestão.", "Type and press Enter. Tags that already exist show up as suggestions."),
        ("dlg_tags_none", "Nenhuma tag ainda.", "No tags yet."),
        ("dlg_tags_remove", "Tirar", "Remove"),
        ("dlg_prop_pick_color", "Escolher cor", "Choose color"),
    };
}
