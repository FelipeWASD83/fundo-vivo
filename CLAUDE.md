# CLAUDE.md: fundo-vivo

Papel de parede animado leve para o Windows 11 (vídeo, imagem, web), no lugar do Wallpaper Engine para esses três
tipos. Criado em 02/11/2026. Código em inglês; interface, documentação, changelog e commits em português.
Versão: `<Version>` do `src/FundoVivo/FundoVivo.csproj`, igual ao topo do `CHANGELOG.md`.

A pesquisa que decidiu a tecnologia, com as medições: `docs/pesquisa-wallpaper-animado-leve.md`.

## 1. Como roda

```
atalho / FundoVivo.exe (sem argumento) ─► sobe o motor (FundoVivo.exe --engine) se não estiver rodando ─► abre a janela (WPF)
motor: janela escondida "FundoVivoEngine" ─► uma WallpaperWindow por monitor, filha do WorkerW ─► conteúdo Video | Image | Web
janela ─► grava %APPDATA%\FundoVivo\config.json ─► WM_COPYDATA {"cmd":"reload"|"settings"|"quit"} ─► motor
motor ─► %LOCALAPPDATA%\FundoVivo\state.json (estado de cada monitor) ─► janela lê a cada 1,5 s
```

- **Dois processos de propósito**: o motor nunca carrega WPF; a janela de ajustes só existe enquanto está aberta.
- **Onde o wallpaper mora** (`Engine/Desktop.cs`): no 24H2 em diante o `Progman` tem `WS_EX_NOREDIRECTIONBITMAP` e o
  `WorkerW` é filho dele; antes, é o `WorkerW` de topo logo atrás do que tem o `SHELLDLL_DefView`. A janela do
  wallpaper vai dentro desse `WorkerW`, por cima de tudo que já estiver lá (o papel de parede do Windows ou o
  Wallpaper Engine). Conferido no PC dele (build 26300): é o mesmo lugar onde o Wallpaper Engine pendura a janela.
- **Vídeo** (`VideoWallpaper`): `Windows.Media.Playback.MediaPlayer` com `GetSurface(compositor)` num
  `DesktopWindowTarget` do Windows.UI.Composition. `CommandManager.IsEnabled = false` tira o wallpaper do painel de
  mídia do Windows e das teclas de mídia. **Não trocar por VLC, mpv ou MediaElement** (pesquisa: mais pesado, ou DLL
  nativa sem assinatura que o Smart App Control barra).
- **Imagem** (`ImageWallpaper`): decodifica pelo `BitmapDecoder` do WinRT já no tamanho do monitor (com o corte do
  Preencher) e sobe uma vez numa drawing surface; o D3D11 só é criado quando aparece a primeira imagem. GIF animado
  mostra só o primeiro quadro.
- **Web** (`WebWallpaper`): WebView2, um nome de host virtual por wallpaper (o `localStorage` não se mistura),
  `web-shim.js` injetado antes da página recria a API do Wallpaper Engine: `wallpaperPropertyListener`
  (`applyUserProperties`, `applyGeneralProperties` com o fps do monitor, `setPaused`) e
  `wallpaperRegisterAudioListener` (128 valores: 64 faixas do canal esquerdo e 64 do direito, ~30 por segundo).
  Os listeners de mídia (capa, título da música) são aceitos e ficam mudos. Pausar = `setPaused(true)`, controller
  invisível, `TrySuspendAsync` e memória em `Low`.
- **Áudio** (`AudioSpectrum`): loopback WASAPI por COM direto, sem NAudio; só roda enquanto um wallpaper web pediu
  áudio, não está pausado e a opção está ligada.
- **Mouse** (`EngineHost.OnRawInput`): raw input com `RIDEV_INPUTSINK`, ligado só quando há wallpaper web; se o que
  está embaixo do cursor é a própria área de trabalho, repassa para o `Chrome_RenderWidgetHostHWND` do WebView2.
- **Pausa** (`PauseMonitor`): a cada 1 s e logo depois de troca de janela em primeiro plano, minimizar ou fim de
  arrastar. Janela visível que cobre o monitor inteiro = tela cheia; que cobre a área de trabalho = coberto. Ignora
  sobreposições que deixam o clique passar (GeForce, Steam, Discord), janelas de ferramenta, translúcidas e as do
  shell. `SHQueryUserNotificationState` (jogo D3D, apresentação) também conta como tela cheia. Por padrão, tela cheia
  num monitor pausa todos (a 3060 dele é o gargalo nos jogos).
- **Explorer reiniciado**: a mensagem `TaskbarCreated` recria o ícone da bandeja e refaz os wallpapers.
- **Monitores** (`Core/Monitors.cs`): o id estável é o caminho do monitor do `QueryDisplayConfig`
  (`\\?\DISPLAY#AUS342B#...`), não o `\\.\DISPLAYn`, que muda com a ordem dos cabos.

- **Tema** (`Settings/ThemeManager.cs`, 2.1.0): a cara do pack de programas dele para o Windows, igual à da `ilha`
  (a pílula e o menu estilo Central de Controle, **não** a janela de configurações dela, que ele acha feia) e à do
  `relogio-mond`: escuro em preto puro com os cinzas de sistema da Apple (`#1C1C1E`, `#2C2C2E`), texto branco a
  100/58/35%, cores de estado de sistema (verde `#30D158`, laranja `#FF9F0A`, vermelho `#FF453A`); claro em `#F2F2F7`.
  Blocos com raio 22 a 26 e sem borda, controles em pílula (**raio = meia altura pelo conversor `Pill`; nunca `CornerRadius="999"`**: o WPF não limita o raio como o CSS e desenha uma elipse esticada em volta do controle, o que ele viu e odiou na 2.1.0) (o escolhido é branco com texto preto, como os blocos
  ativos da ilha), botões e campos preenchidos sem borda. O bloco dos monitores (`Stage*`) é escuro nos dois temas.
  Títulos e rótulos em **Quicksand** (a fonte do relógio, OFL, em `src/FundoVivo/Fonts` com a licença; recurso
  `Brand`), corpo em Segoe UI Variable. Mexeu num dos três? Os outros dois acompanham. As paletas ficam
  nos recursos da aplicação; tudo no XAML usa os pincéis como `DynamicResource`, então trocar repinta a janela aberta.
  O que é montado em código (cartões, mapa, painel) é refeito no evento `ThemeManager.Changed`. Padrão: seguir o
  Windows (`AppsUseLightTheme`), acompanhando a troca enquanto a janela está aberta. Pincel novo entra nas duas
  paletas e sempre como `DynamicResource`. `FUNDOVIVO_THEME=light|dark` força um nas fotos de teste, e
  `--screenshot ... theme dark` testa a troca ao vivo.

- **Tags** (`Core/Tags.cs`): as do Wallpaper Engine (`tags` do `project.json`, vocabulário fixo em inglês) aparecem
  traduzidas; as do usuário ficam em `UserItem.Tags`. Tudo se compara por `Tags.Key` (sem maiúscula nem acento), e
  `Tags.Canonical` faz a tag digitada pegar a grafia que já existe. `contentrating` vira Livre/Questionável/Adulto. No
  filtro, tags marcadas valem juntas (E) e classificações marcadas valem qualquer uma (OU).
- **Limite de fps** (`AppConfig.FpsLimit`, 30/60/120/144/160, padrão 60; `EffectiveFps` = o menor entre ele e a taxa
  do monitor): o motor passa para o web em `applyGeneralProperties` **e** o `web-shim.js` segura o
  `requestAnimationFrame` da página nesse ritmo, porque só 4 dos 23 web da biblioteca dele leem o fps da API e 13
  desenham por rAF. O valor inicial vai escrito no lugar do marcador `/*FPS*/0` do shim, então vale desde o primeiro
  quadro. CSS e laços por timer não obedecem (dito na interface). Vídeo toca no fps do arquivo; o painel lê
  `System.Video.FrameRate`. Medido: 60 → 60 quadros/s nos dois monitores; 160 → 161 no de 250 Hz e 145 no de 144 Hz.

- **Cenas convertidas em vídeo** (1.3.0; `Core/SceneProject.cs`, `SceneConverter.cs`, `SceneJobs.cs`, `SceneCache.cs`):
  - `scene.pkg` = string `PKGVxxxx`, contagem, tabela (nome, offset, tamanho) e os arquivos. Para classificar só se lê o
    `scene.json` e os `.js` (cache em `scene-cache.json` por tamanho e data do pacote).
  - Classificação: parallax (`general.cameraparallax` e `cameraparallaxamount`), som (`supportsaudioprocessing` ou
    efeito de áudio no `scene.json`), mouse e relógio separados desde a 2.4.0 (`SceneTraits.Mouse` e `Clock`, regex nos
    scripts e no `scene.json`; 46 e 98 das dele) e som de fundo (`BackgroundSound`: objeto `sound` fora do modo
    `random`, 103). `SuggestedLoop`: o menor loop de 10 a 60 s em que cabem inteiras todas as animações de linha do
    tempo (`animation.options`: `length / fps`, `mirror` vale o dobro); 43 cenas têm. As 12 "cenas de GIF" (modelo de GIF do
    Wallpaper Engine) guardam tudo em `gifscene.pkg` com `gifscene.json`: o pacote se acha pelo nome do arquivo de
    cena do `project.json` (`SceneProject.PackageOf`). Até a 2.1.1 só se procurava `scene.pkg`, elas caíam em
    "Sem leitura" e a conversão quebrava (o texto antigo daqui dizia que convertiam; não convertiam).
  - Gravação: a cena é desempacotada em `%LOCALAPPDATA%\FundoVivo\scene-work\<job>` com `cameraparallax = false` (o
    parallax nunca vai para o vídeo; o caminho de mouse parado foi descartado porque mexeria no mouse dele), aberta
    com `wallpaper64.exe -control openWallpaper -file ... -playInWindow <título> -width -height`, a janela
    (`WPEOverlappedWallpaper`) é **só movida** para a direita da área de trabalho (o Wallpaper Engine ignora
    `-x/-y`, então ela pisca no canto por um instante; redimensionar faz ele escolher outro tamanho, e em -32000 a
    altura vira 65535) e gravada pelo `gfxcapture` do ffmpeg (`hwnd`, sem cursor) em NVENC CQ 16.
  - Limites medidos em 02/11/2026: o Windows não deixa a janela passar da altura da área de trabalho (1895 px úteis
    aqui), então o monitor em pé e a TV gravam menores e são ampliados; NVENC H.264 vai até 4096 de largura, então o
    ultrawide com sobra de parallax sai em **HEVC** (o Windows tem o decodificador). O vídeo sai no fps do Wallpaper
    Engine (o dele está em 30); o Fundo Vivo não mexe nessa configuração.
  - Loop: grava `duração + emenda` e mistura (`xfade`) os últimos segundos com os primeiros. Medido: a diferença entre
    o último e o primeiro quadro é igual à de dois quadros seguidos.
  - Parallax: sobra por lado = intensidade x 0,25 (de 2% a 15%), em `MonitorAssignment.ParallaxMargin`. O motor
    (`VideoWallpaper.SetPointer`) desloca o vídeo maior conforme o cursor naquele monitor, animado no compositor,
    no máximo ~30 alvos por segundo, só em Preencher, nunca pausado; o raw input liga só se existir vídeo assim.
    `FUNDOVIVO_TEST_LAYER=beside` põe a área de trabalho de teste logo à direita da tela (ainda composta), e o
    comando de teste `{"cmd":"pan","x":..,"y":..}` move o "cursor" sem tocar no mouse: assim o pan foi fotografado.
  - A conversão roda em `FundoVivo.exe --convert-scenes` (fila em `%LOCALAPPDATA%\FundoVivo\scene-jobs`, um mutex
    por vez), nunca no motor. Pronta, grava `ConvertedScenes` e, se pedido, o monitor, e manda `reload`.
  - **Som de fundo** (2.4.0): ao fim da gravação, o primeiro arquivo do primeiro objeto `sound` em loop sai do pacote,
    vira AAC (`.m4a` ao lado do vídeo, com o `volume` do objeto) e vai em `ConvertedScene.Audio` e
    `MonitorAssignment.Audio`. O `VideoWallpaper` toca num segundo `MediaPlayer`, só de áudio, em loop no tempo dele
    (não no do vídeo), com o volume do item. Durante a gravação esse som toca no PC (o Wallpaper Engine não é
    silenciado: mexeria na configuração dele).
  - **Opções do autor** (2.4.0): o `SceneDialog` usa o mesmo `PropertyEditor` dos web; os valores vão em
    `SceneJob.Properties` e o `SceneConverter.ApplyProperties` escreve no `project.json` da cópia. **Confirmado em
    02/11/2026**: o Wallpaper Engine usa os valores da cópia (Mario Kart DS - Credits com Show Clock e Show Date
    desligados saiu sem hora e sem data). As escolhas ficam em `Items[id].Properties` da cena, e o `--set` usa elas.
  - **Cenas de imagem animada única, direto da folha** (2.2.0; `Core/SpriteScene.cs`, `Core/WeTexture.cs`): um objeto
    `image` visível só, sem `effects`, sem partícula, texto, som, modelo ou luz, cor e alpha normais, cobrindo a
    `orthogonalprojection` inteira (ou o modelo antigo de GIF, que não guarda tamanho), e a textura do material em
    RGBA (formato 0) com a flag de animada. Marca `SceneTraits.Direct` (tag "Vira vídeo direto"; 14 das 224 dele).
    O `.tex` é lido inteiro (LZ4 de bloco escrito à mão, só o mipmap 0) e cada quadro é recortado da folha e mandado
    cru para o ffmpeg, que cobre o monitor (com a sobra do parallax, se tiver) com `neighbor` quando a textura é de
    filtro de ponto. Tempos iguais tocam no próprio ritmo (0,06 s = 16,7 fps); desiguais vão para uma grade de 60. A
    sequência se repete inteira até passar de 10 s. Tabela de quadros: 32 bytes (imagem, tempo, x, y, largura, dois
    de rotação, altura), em float, **menos a `TEXS0001`, que guarda o retângulo em int32** (Aesthetic City). Quadro
    girado ou textura em outro formato: `null`, e a cena volta para a gravação pelo Wallpaper Engine. O cache das
    cenas tem `Version` (2): tag nova na classificação = subir o número, senão as entradas antigas não mudam.
  - **GIF do usuário animado** (2.2.0, `Core/GifConverter.cs`): GIF com mais de um quadro vira vídeo ao ser posto no
    monitor (job `kind: gif`, `ConvertedScenes` com `SourceStamp`, atribuição `video` com `sceneId`). Pequeno (altura
    até metade da do monitor) é ampliado por fator inteiro com `neighbor`; grande vai para a altura do monitor. Pela
    ilha ele ainda chega como `image` e mostra o primeiro quadro.
  - **Testar conversão daqui**: os processos abertos pelo app do Claude enxergam um AppData virtual, e o Wallpaper
    Engine não acharia a cópia desempacotada. Use `FUNDOVIVO_HOME` numa pasta fora do AppData (`build\test-home`).
- **Formato próprio `.vivo`** (2.0.0; `Core/Packages.cs`, `Core/PackageRenderer.cs`, `Runtime/`, `docs/formato-vivo.md`,
  `schema/vivo.schema.json`, exemplos em `examples/`):
  - É dado, não código: zip com `wallpaper.json` e assets. O runtime (`Runtime/index.html` e `vivo.js`, WebGL2) vai
    embutido no exe e é escrito ao lado do conteúdo desempacotado em `%LOCALAPPDATA%\FundoVivo\packages\<hash>`
    (um por versão do arquivo e do app; guarda contra caminho `../` no zip).
  - **O desenho é função pura do tempo** (`draw(t)`): partículas em forma fechada com número inteiro de voltas por
    loop, períodos arredondados para dividir o loop, `spin` com voltas inteiras. Nunca usar `performance.now` nem estado
    acumulado no runtime: é isso que deixa a mesma cena tocar ao vivo e ser renderizada quadro a quadro com loop perfeito.
  - Ao vivo (`mode=live`, padrão): a atribuição é `type: web` com `file` = o `.vivo` e `package` preenchido; o motor
    chama `Packages.Prepare` e abre como web (shim, fps, pausa, som e mouse de sempre). Só vai ao vivo o que tem animação
    `audio`.
  - Renderizado (`mode=render`): job `kind: package` na fila das cenas; `PackageRenderer` abre o runtime numa WebView2
    própria (perfil `webview-render`, outra janela fora da tela, `CalculateNativeWinOcclusion` desligado), passa um
    `CoreWebView2SharedBuffer`, pede cada quadro por `{type:'frame', t}` e manda os pixels (RGBA de baixo para cima,
    `vflip` no ffmpeg) direto para o NVENC. Resultado em `ConvertedScenes` com `SourceStamp` (tamanho + data do
    arquivo): mudou o `.vivo`, renderiza de novo. A atribuição é `type: video` com `sceneId` e `package`.
  - Medido em 02/11/2026: 1200 quadros 3646×1526 em 15,6 s. O grão de filme fica **parado no vídeo** (mudando a cada
    quadro, a neve saía com 61 Mbit/s; parado, 8,6), e o encoder vai com CQ 21 e teto de 30 Mbit/s (a chuva chegava
    a 60). Ao vivo no ultrawide: 59 a 60 fps.
  - `FundoVivo.exe --vivo-still <vivo> <png> <w> <h> [t] [margem]` desenha um quadro com o mesmo renderizador: é o
    jeito de testar as três proporções sem pôr nada na tela dele. Headless Edge tira a foto antes das imagens
    decodificarem (sai preto); use isso ou o MCP chrome-devtools.
- **Onde um item vai parar** (`Core/Placement.cs`, 2.3.0): `Ready` diz o que o item toca naquele monitor agora (ou
  `null` quando precisa virar vídeo antes) e `JobFor` dá o job que faz o vídeo. A janela, a playlist e o `--set` usam
  os dois; só a janela abre o diálogo de duração das cenas gravadas. Regra nova de "como um item toca" entra aqui,
  não no `SettingsWindow`.
- **Playlist por monitor** (2.3.0, `AppConfig.Playlists` por id do monitor): cada entrada é uma `MonitorAssignment`
  pronta (por isso só entra o que já toca naquele monitor). O motor (`EngineHost.RotatePlaylists`, no `Tick`) troca
  quando passou `Minutes` desde `Changed`, **só com o monitor em `playing`**; `Advance` grava no `config.json` (o mesmo
  `Mutate` de sempre) e pula arquivo que sumiu. Escolher à mão, pela janela ou pelo `--set`, zera o relógio. A troca é
  seca, sem transição.
- **Programas que pausam tudo** (2.3.0, `Pause.Programs`): nome do processo, pausar ou tirar da memória; tirar
  ganha. A lista de processos é lida no máximo a cada 2 s, só os nomes (nenhum handle no processo de ninguém, como a
  ilha faz pelo anti-cheat). Estados `paused-program` e `unloaded-program`, e o nome vai no `state.json` (`program`).
- **Linha de comando** (2.3.0, `Core/Cli.cs`): `--pause`, `--resume`, `--toggle-pause` e `--next [monitor]` viram
  comandos `pause` e `next` no `WM_COPYDATA`; `--set <monitor> <arquivo ou nome>` resolve como um clique (põe na
  biblioteca o arquivo que não estiver lá). Sem console: código de saída 0, 2 (fazendo o vídeo antes) ou 1, e o
  registro em `logs\cli.log`. O monitor é `principal`, número da esquerda para a direita ou parte do nome.
- **Contrato com a ilha** (módulo `fundo` do projeto `ilha`, desde 02/11/2026): ela lê a biblioteca e escreve no
  `config.json` daqui (`monitors` → `itemId`, `type`, `file`, `title`, `monitorName`, e o comando `reload` na janela
  `FundoVivoEngine`). **Não renomear esses campos nem o comando sem atualizar `ilha\src\Ilha\Modulos\Fundo\FundoVivoBridge.cs`**:
  ela quebraria calada. Campos novos são seguros (ela preserva o JSON inteiro). Ela não lista cenas. Por isso o `.vivo`
  nunca aparece como `type` novo no `monitors`: vai como `web` ou `video`, com o campo novo `package`. Mas a ilha
  lista os `userItems` e copia o `type` deles: um `.vivo` escolhido por lá chega como `type: package`, que o motor
  abre pelo caminho web (toca ao vivo, sem virar vídeo). Funciona; não tirar esse caminho do `EngineHost.Load`.

## 2. Comandos

- Compilar e atualizar `app\`: `powershell -ExecutionPolicy Bypass -File ferramentas\compilar.ps1` (também empacota
  `examples\*` em `app\examples\*.vivo`; o Smart App Control às vezes barra o próprio `.exe`, e o script sorteia outro
  carimbo nesse caso também)
- Refazer as imagens dos exemplos: `python ferramentas\make_example_assets.py`
- Fechar o motor: `app\FundoVivo.exe --quit`
- Diagnóstico (escreve no `describe.log`): `app\FundoVivo.exe --describe`
- Publicar versão (decisão dele): subir `<Version>` e o `CHANGELOG.md`, commit, push, `ferramentas\publicar-versao.ps1`.
- Só gerar o pacote, sem GitHub: `ferramentas\publicar-versao.ps1 -SoGerar` compila, empacota em
  `build\release\fundo-vivo-<versão>.zip` (com `notas-<versão>.md` e `SHA256-<versão>.txt`; os pacotes das outras versões ficam), abre a cópia desempacotada e confere que
  o Windows deixa ela rodar. É o pacote que o instalador do pack dele usa.

## 3. Decisões que não se desfazem sem motivo

- **Smart App Control barra DLL .NET também** (02/11/2026, contra o que a nota antiga do ambiente dizia): o mesmo
  projeto passou numa compilação e foi barrado na seguinte, por hash. Por isso **só compilar pelo `compilar.ps1`**:
  cada tentativa leva um carimbo sorteado em `SourceRevisionId` (muda o hash da DLL e do .exe, que herda a versão do
  assembly), o `FundoVivo.exe --check-load` carrega cada DLL e, se o Windows barrar, o script sorteia outro. As DLLs
  de pacote (WebView2, WinRT) são assinadas pela Microsoft e passam.
- **Release**: o pacote é a pasta `build\publish` compilada aqui (o hash que passou no PC dele). O atualizador
  (`Core/Updater.cs`) exige o SHA-256 que o GitHub informa no asset, troca a pasta por um script PowerShell fora do
  app e volta para a anterior se a nova não gravar `started.txt` em 25 s. Na cópia de desenvolvimento (tem `.git` do
  lado de `app\`) ele recusa e manda usar git e `compilar.ps1`, como o Clave.
- **Cena do Wallpaper Engine não é desenhada pelo Fundo Vivo**: formato proprietário (`scene.pkg`, texturas `.tex`,
  shaders próprios). Ela entra só convertida em vídeo pelo próprio Wallpaper Engine (1.3.0). Não prometer mais que isso.
- **Configuração sempre relida antes de gravar** (`SettingsWindow.Mutate`): o motor grava o "pausar tudo" do ícone da
  bandeja no mesmo arquivo.
- **Conversão** (`Core/VideoConverter.cs`): ffmpeg do PATH, `h264_nvenc` e, se falhar, `libx264`; a cópia vai para
  `%LOCALAPPDATA%\FundoVivo\converted` e vale só para aquele tamanho de monitor, aquele encaixe e aquele tamanho de
  arquivo original. O original nunca é mexido.

## 4. Testar sem mexer na tela dele

Pendurar na área de trabalho de verdade troca o papel de parede dele enquanto roda: **só com o OK dele**. Para o
resto:

- `FUNDOVIVO_HOME=<pasta>` muda toda a configuração e os dados para `<pasta>\roaming` e `<pasta>\local`.
- `FUNDOVIVO_TEST_LAYER=offscreen` (com o `HOME` acima) usa uma janela fora de todos os monitores no lugar do
  `WorkerW`; também não cria o ícone da bandeja. Mede decodificação e renderização, não a apresentação na tela.
- `FUNDOVIVO_TEST_NOPAUSE=1` ignora a regra de pausa (senão um jogo aberto pausa tudo e a medição sai zerada).
- `FundoVivo.exe --screenshot <png> library|settings <largura> <altura> [assign <trecho da pasta>]` renderiza a janela
  num PNG fora da tela; com `assign`, põe aquele wallpaper no primeiro monitor que não é o principal, como um clique.
  Isso sobe o motor: deixe o `TEST_LAYER=offscreen` ligado junto.
- Em modo de teste o wallpaper web grava no `engine.log`, 8 s depois de abrir, os contadores do `web-shim.js`
  (`audioFrames`, `propsApplied`, `generalApplied`) e se achou a janela que recebe o mouse.
- **Armadilha**: no PowerShell 5.1, `Start-Process -Wait` espera os processos filhos também. A janela abre o motor,
  então `-Wait` nela trava até o motor fechar. Use `-PassThru` e `WaitForExit(ms)`.

## 5. Testado na área de trabalho de verdade (02/11/2026, com o OK dele)

- Vídeo preenchendo o ultrawide atrás dos ícones e do Relógio Mond, web no monitor em pé.
- Janela do Claude maximizada no monitor em pé: "pausado: janela na frente" só nele.
- Janela preta sem borda no ultrawide (como um jogo em tela cheia): os dois pausam e voltam quando ela fecha.
- "Tirar da memória": o programa inteiro cai de ~480 MB para 29 MB durante a tela cheia e volta sozinho.
- Clique numa área vazia do ultrawide chega no wallpaper web (`click forwarded` no `engine.log`).
- Ao fechar, o Wallpaper Engine volta a aparecer e não sobra processo.

Três defeitos que só apareceram assim e foram corrigidos: o `QUNS_BUSY` do Windows aparece sem programa em tela
cheia nenhum neste PC (fica fora da regra); o vídeo saía com faixas pretas em Preencher (a superfície agora tem o
formato do vídeo); e a memória do vídeo só voltava quando o GC passava (agora o `MediaSource` é fechado e o motor
coleta logo depois de tirar um wallpaper).

## 6. Pendente

- **Explorer reiniciando não foi testado**: o Relógio Mond vive pendurado no `Progman` e o `ilha` depende do
  Explorer, então derrubar o Explorer no PC dele podia quebrar os dois. O código (`TaskbarCreated`) está lá.
- Primeira release publicada em 02/11/2026, com o OK dele: **v2.1.2**. Foi publicado o zip que saiu do `-SoGerar` e foi
  testado desempacotado (o `gh release create` à mão, com as `notas.md` dele), não uma compilação nova: cada
  compilação leva outro carimbo, então o arquivo publicado tem que ser o conferido. O GitHub informa o SHA-256 do
  asset (`digest`), que é o que o atualizador exige. Repositório público desde 02/11/2026, MIT.
