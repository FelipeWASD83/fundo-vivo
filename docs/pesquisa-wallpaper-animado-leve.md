# Pesquisa: wallpaper animado leve para Windows 11
Data: 2026-11-02 · Projeto: fundo-vivo

## Pergunta
Com que tecnologia fazer um programa de papel de parede animado (vídeo, imagem e página web) que gaste bem menos que o Wallpaper Engine no PC do Felipe, rodando no Windows 11 build 26300 com o Smart App Control ligado.

## Recomendação
C# com .NET 10, sem biblioteca de vídeo de terceiros. O vídeo toca pelo player do próprio Windows (Media Foundation, decodificação na placa de vídeo) desenhado direto numa janela pendurada atrás dos ícones, do jeito novo do 24H2. Página web usa o WebView2 (o Edge embutido, que já vem no Windows). A janela de configurações é WPF e só existe enquanto está aberta. Motivo: é o caminho com menos peso que foi medido aqui, não depende de DLL nativa que o Smart App Control possa barrar e não traz 150 MB de VLC junto.

## Candidatos avaliados

**Player do Windows (Windows.Media.Playback + Composition), em .NET 10.** Já vem no sistema, decodifica H.264 na RTX 3060, faz loop sozinho. Testado aqui (números abaixo). Contra: só toca o que o Windows tem codec (H.264 e HEVC sim; os 71 vídeos da biblioteca dele são todos H.264, então isso não pesa). Escolhido.

**LibVLCSharp (VLC embutido).** Toca qualquer coisa, mas leva uns 150 MB de DLL e plugin junto, e cada DLL nativa nova é um risco com o Smart App Control. Não ganha nada para H.264. Descartado.

**mpv.** Leve e ótimo, mas as builds para Windows não são assinadas; pela regra da pasta, DLL nativa sem reputação morre no Smart App Control. Descartado.

**WPF MediaElement.** Usa o caminho antigo do Windows Media Player, copia quadro pela CPU. Pior que o primeiro em tudo. Descartado.

**Electron ou Python.** Electron é um Chromium inteiro só para existir; Python precisaria de um player nativo do mesmo jeito. Nenhum dos dois é "leve". Descartados.

## O que eu testei

Protótipo descartável em .NET 10 (scratchpad, fora da pasta), janela fora da tela para não mexer no papel de parede dele, vídeos da própria biblioteca do Wallpaper Engine, medido por 12 segundos depois de 8 de aquecimento:

| O que | CPU (de um núcleo) | RAM (working set) | Placa de vídeo |
|---|---|---|---|
| Programa sem vídeo | 0% | 57 MB | nada |
| Vídeo 1080p (Fire loop) | 2,2% | 162 MB (78 MB de VRAM) | decodificador 3% |
| Vídeo 4K 60 fps (Makoto Yuki) | 7,2% | 335 MB | decodificador 29% |
| Wallpaper Engine agora (cena pausada + 1 web) | 2,5% | 458 MB somando os 7 processos | nada (pausado) |

Outras coisas confirmadas no PC dele:
- O `.exe` do .NET recém-compilado rodou sem o Smart App Control barrar.
- A estrutura das janelas da área de trabalho é a nova do 24H2: o `Progman` tem o estilo `WS_EX_NOREDIRECTIONBITMAP`, o `WorkerW` é filho do `Progman`, e o Wallpaper Engine pendura a janela dele dentro desse `WorkerW`.
- A biblioteca dele tem 224 wallpapers de cena, 71 de vídeo (todos `.mp4` H.264: 19 em 1080p, 14 em 4K) e 23 web.

Não testei: WebView2 tocando um wallpaper web, nem a janela de verdade atrás dos ícones (as duas coisas mexeriam na tela dele enquanto ele usa o PC).

## Riscos e o que pode dar errado

- **Cena não entra.** O formato `scene.pkg` é do Wallpaper Engine (texturas próprias, shaders próprios, partículas). O único leitor aberto, o linux-wallpaperengine, só roda no Linux e cobre parte das cenas. Portar isso seria outro projeto, bem maior. Como 224 dos 318 wallpapers dele são cena, o Wallpaper Engine continua necessário para esses.
- **Web não fica muito mais leve.** O WebView2 é Chromium, igual ao que o Wallpaper Engine usa; o ganho aqui vem de pausar e de não carregar nada além da página. Estimativa, não medido.
- **Vídeo 4K continua pesando** (335 MB) porque o decodificador guarda vários quadros 4K. Num monitor ultrawide, um vídeo 4K 16:9 é cortado de qualquer jeito, então converter para o tamanho do monitor cortaria memória e trabalho da placa; isso seria um extra.
- **O Windows pode mudar a estrutura das janelas de novo** numa atualização (já mudou no 24H2). O código precisa detectar os dois jeitos, como o Lively faz.
- **Wallpaper web que depende de recurso do Wallpaper Engine** (`wallpaperPropertyListener`, visualizador de áudio) precisa que o programa imite essas funções; sem isso, alguns ficam parados ou sem cor.
- Custo de sair: baixo. É um programa isolado, não mexe em nada do sistema; fechar o programa devolve o papel de parede normal.

## Confirmado / inferência / incerto
Confirmado: números da tabela; .NET passa no Smart App Control; estrutura 24H2 das janelas no PC dele; tipos e codecs da biblioteca; a API web (`wallpaperPropertyListener.applyUserProperties`, `wallpaperRegisterAudioListener`) pela documentação oficial.
Inferência: o WebView2 deve gastar parecido com o Chromium do Wallpaper Engine; boa parte dos 100 a 140 MB extras do vídeo é do driver da NVIDIA e da fila de quadros do decodificador, e qualquer player com aceleração na placa paga isso.
Incerto: o formato exato dos dados de áudio que os wallpapers web esperam (a documentação oficial não fixa o tamanho; um projeto de terceiros fala em 128 valores, 64 por canal); quanto o Wallpaper Engine gasta tocando o mesmo vídeo (não troquei o wallpaper dele para medir).

## Fontes
- Lively Wallpaper, código aberto (rocksdanister/lively, via DeepWiki): como acha `Progman`/`WorkerW` antes e depois do 24H2, a mensagem `0x052C`, pausa por tela cheia com `SHQueryUserNotificationState` e janela cobrindo o monitor, repasse do mouse para wallpaper web.
- [Notas do Lively 2.2.0.0](https://newreleases.io/project/github/lively-community/lively/release/v2.2.0.0): reescreveu a parte de área de trabalho por causa do 24H2.
- [Lively 2.1.0.6 com suporte ao 24H2 (Deskmodder)](https://www.deskmodder.de/blog/author/moinmoin/page/133).
- [Issue 2506 do Lively](https://github.com/rocksdanister/lively/issues/2506) e [resposta na Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/8001413): o 24H2 quebrou wallpaper animado em apps antigos.
- [Wallpaper Engine: propriedades de usuário em wallpaper web](https://docs.wallpaperengine.io/en/web/customization/properties.html) e [visualizador de áudio](https://docs.wallpaperengine.io/en/web/audio/visualizer.html): a API que os wallpapers web chamam.
- [Wiki do Open Wallpaper Engine para Mac](https://github.com/deepratna-awale/open-wallpaper-engine-mac/wiki/Web-Wallpapers) e [wiki do Sucrose](https://github.com/Taiizor/Sucrose/wiki/Create-Compatibility): como outros programas imitam essa API.
