# Fundo Vivo

Papel de parede animado para o Windows 11, no espírito do Wallpaper Engine, mas leve: vídeo, imagem ou página web
atrás dos ícones, um por monitor, e nada rodando quando ninguém está vendo.

## Como usar

1. Abra **Fundo Vivo** pelo Menu Iniciar.
2. Clique num monitor no mapa de cima.
3. Clique num wallpaper da biblioteca. Pronto, ele já está atrás dos ícones.

No painel da direita ficam as opções do monitor escolhido: encaixe (preencher, ajustar ou esticar), som, as opções
próprias dos wallpapers web e o botão **Converter para este monitor**, que aparece quando o vídeo é maior que a tela.
**Voltar ao papel de parede do Windows** tira o wallpaper animado daquele monitor.

Fechar a janela não desliga nada: os wallpapers continuam pelo ícone perto do relógio (clique para abrir a janela,
botão direito para pausar tudo ou sair).

## O que entra na biblioteca

- Os wallpapers de **vídeo** e **web** que você já baixou no Wallpaper Engine, lidos direto da pasta do Steam (nada é
  copiado).
- Os seus arquivos, pelo botão **Adicionar arquivo**: vídeo (mp4, webm, mkv, mov...), imagem (jpg, png, webp...) ou
  página HTML.

Os wallpapers do tipo **cena** do Wallpaper Engine não entram: o formato é próprio dele (texturas, shaders e partículas
que só o programa dele desenha). Para esses, continue usando o Wallpaper Engine, de preferência num monitor diferente.

## Por que é leve

- O vídeo toca pelo player do próprio Windows e é decodificado na placa de vídeo; o quadro nunca passa pela CPU.
- A imagem é decodificada uma vez, no tamanho exato do monitor, e depois não gasta nada.
- Quando uma janela maximizada ou um jogo em tela cheia cobre o monitor, o wallpaper pausa. Em **Ajustes** dá para
  escolher **Tirar da memória** durante o jogo, que libera RAM e memória da placa de vídeo.
- O conversor faz uma cópia do vídeo no tamanho do monitor (um vídeo 4K no ultrawide é cortado de qualquer jeito).
  Medido aqui: o mesmo vídeo 4K no monitor em pé caiu de 292 MB para 155 MB e de 48% para 10% do decodificador.

Medições em 02/11/2026 no PC do Felipe (Core Ultra 9 285, RTX 3060): vídeo 1080p 145 MB e 1,4% de um núcleo; vídeo 4K
286 MB; wallpaper web ~220 MB; tudo pausado 0,6% de um núcleo.

## Wallpapers web

As opções que o autor colocou no `project.json` (cores, velocidade, listas, liga e desliga) aparecem no painel e mudam
na hora. Os visualizadores de música recebem o som do PC no mesmo formato do Wallpaper Engine (64 faixas por canal,
cerca de 30 vezes por segundo), e os interativos recebem o mouse quando você clica na área de trabalho vazia. As duas
coisas podem ser desligadas em **Ajustes**.

## Atualização

Em **Ajustes**, **Procurar atualização** pergunta ao GitHub. Se o repositório for privado, guarde ali um token de
leitura (fine-grained, só este repositório, Contents: leitura). O pacote é conferido pelo SHA-256 antes de qualquer
troca, a versão atual fica guardada e volta sozinha se a nova não abrir. A sua configuração e os vídeos convertidos
ficam fora da pasta do programa e não são tocados.

## Onde ficam as coisas

- Programa: `app\` nesta pasta (gerado pelo `ferramentas\compilar.ps1`).
- Configuração: `%APPDATA%\FundoVivo\config.json`.
- Vídeos convertidos, registros e dados do Edge: `%LOCALAPPDATA%\FundoVivo\`.

## Compilar

```powershell
powershell -ExecutionPolicy Bypass -File ferramentas\compilar.ps1
```

Precisa do .NET 10 SDK. O script repete a compilação com um carimbo novo se o Smart App Control barrar a DLL (ver
`CLAUDE.md`). Para converter vídeo, o `ffmpeg` precisa estar no PATH.
