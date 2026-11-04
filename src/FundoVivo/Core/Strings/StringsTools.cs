namespace FundoVivo.Core;

/// <summary>(key, Portuguese, English). One entry per line; plain string literals only (tools/check_strings.py reads this file).</summary>
public static class StringsTools
{
    public static readonly (string Key, string Pt, string En)[] All =
    {
        // shared by the converters and the conversion queue
        ("tool_ffmpeg_missing", "O ffmpeg não foi encontrado no PATH.", "ffmpeg was not found in PATH."),
        ("tool_done", "Pronto", "Done"),
        ("tool_canceled", "Cancelada", "Canceled"),
        ("tool_starting", "Começando", "Starting"),
        // video
        ("tool_ffmpeg_failed_convert", "O ffmpeg não conseguiu converter: {0}", "ffmpeg could not convert the video: {0}"),
        // GIF
        ("tool_gif_reading", "Lendo o GIF", "Reading the GIF"),
        ("tool_gif_unreadable", "Não deu para ler o GIF.", "Could not read the GIF."),
        ("tool_gif_converting", "Convertendo o GIF", "Converting the GIF"),
        ("tool_gif_ffmpeg_failed", "O ffmpeg não converteu o GIF: {0}", "ffmpeg could not convert the GIF: {0}"),
        // Wallpaper Engine scene
        ("tool_scene_not_scene", "Essa pasta não é uma cena do Wallpaper Engine.", "This folder is not a Wallpaper Engine scene."),
        ("tool_we_missing", "O Wallpaper Engine não foi encontrado neste PC.", "Wallpaper Engine was not found on this PC."),
        ("tool_scene_preparing", "Preparando a cena", "Preparing the scene"),
        ("tool_scene_opening", "Abrindo no Wallpaper Engine", "Opening in Wallpaper Engine"),
        ("tool_we_no_window", "O Wallpaper Engine não abriu a janela da cena.", "Wallpaper Engine did not open the scene window."),
        ("tool_we_window_size", "O Wallpaper Engine abriu a janela com {0}x{1} em vez de {2}x{3}.", "Wallpaper Engine opened the window at {0}x{1} instead of {2}x{3}."),
        ("tool_scene_loading", "Esperando a cena carregar", "Waiting for the scene to load"),
        ("tool_scene_recording", "Gravando", "Recording"),
        ("tool_scene_record_failed", "A gravação falhou: {0}", "The recording failed: {0}"),
        ("tool_scene_looping", "Emendando o loop", "Joining the loop"),
        ("tool_scene_loop_failed", "Não deu para fechar o loop: {0}", "Could not close the loop: {0}"),
        // animated sprite sheet
        ("tool_sprite_reading", "Lendo a folha de quadros", "Reading the sprite sheet"),
        ("tool_sprite_building", "Montando o vídeo", "Building the video"),
        ("tool_sprite_ffmpeg_failed", "O ffmpeg não conseguiu montar o vídeo: {0}", "ffmpeg could not build the video: {0}"),
        // .vivo package
        ("tool_vivo_unreadable", "Não deu para ler o arquivo .vivo.", "Could not read the .vivo file."),
        ("tool_vivo_opening", "Abrindo o wallpaper", "Opening the wallpaper"),
        ("tool_vivo_rendering", "Renderizando", "Rendering"),
        ("tool_vivo_ffmpeg_failed", "O ffmpeg não conseguiu gravar o vídeo: {0}", "ffmpeg could not record the video: {0}"),
        ("tool_vivo_page_error", "O wallpaper deu erro: {0}", "The wallpaper reported an error: {0}"),
        ("tool_vivo_page_timeout", "O wallpaper não respondeu ({0}).", "The wallpaper did not respond ({0})."),
        // command line (lines in logs\cli.log)
        ("tool_cli_monitor_not_found", "monitor não encontrado: {0}", "monitor not found: {0}"),
        ("tool_cli_usage", "uso: --set <monitor> <arquivo ou título>", "usage: --set <monitor> <file or title>"),
        ("tool_cli_unknown_command", "comando desconhecido: {0}", "unknown command: {0}"),
        ("tool_cli_not_running", "o {0} não está rodando", "{0} is not running"),
        ("tool_cli_file_type", "tipo de arquivo que o {0} não toca: {1}", "file type that {0} cannot play: {1}"),
        ("tool_cli_nothing_found", "nada na biblioteca com esse nome ou arquivo: {0}", "nothing in the library with that name or file: {0}"),
        // updater
        ("tool_upd_none_private", "O GitHub não mostrou nenhuma versão publicada. Se o repositório é privado, guarde um token de leitura abaixo.", "GitHub did not show any published release. If the repository is private, save a read token below."),
        ("tool_upd_none", "Ainda não existe nenhuma versão publicada (ou o token não tem acesso ao repositório).", "There is no published release yet (or the token has no access to the repository)."),
        ("tool_upd_http", "O GitHub respondeu {0}.", "GitHub responded with {0}."),
        ("tool_upd_offline", "Sem conexão com o GitHub ({0}).", "Could not reach GitHub ({0})."),
        ("tool_upd_dev", "Esta é a cópia de desenvolvimento (tem .git ao lado). Atualize com git pull e ferramentas\\compilar.ps1.", "This is the development copy (it has .git next to it). Update with git pull and ferramentas\\compilar.ps1."),
        ("tool_upd_no_zip", "A versão publicada não tem o pacote .zip.", "The published release has no .zip package."),
        ("tool_upd_no_sha", "O GitHub não informou o SHA-256 do pacote; por segurança, nada foi instalado.", "GitHub did not provide the package's SHA-256; for safety, nothing was installed."),
        ("tool_upd_downloading", "Baixando a versão {0}...", "Downloading version {0}..."),
        ("tool_upd_checking", "Conferindo o pacote...", "Checking the package..."),
        ("tool_upd_hash_mismatch", "O pacote baixado não confere com o SHA-256 do GitHub. Nada foi trocado; tente de novo.", "The downloaded package does not match GitHub's SHA-256. Nothing was replaced; try again."),
        ("tool_upd_bad_path", "Pacote com caminho inválido.", "The package contains an invalid path."),
        ("tool_upd_no_exe", "O pacote não tem o FundoVivo.exe.", "The package does not contain FundoVivo.exe."),
        ("tool_upd_swapping", "Trocando os arquivos e reabrindo...", "Replacing the files and reopening..."),
    };
}
