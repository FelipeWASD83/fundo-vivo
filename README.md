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

Embaixo dos filtros de tipo ficam as **tags** (as do Wallpaper Engine, em português, e as suas) e a
**classificação** (Livre, Questionável, Adulto). Nos seus arquivos, botão direito no cartão → **Editar tags**.

Em **Ajustes**, **Aparência** escolhe o tema da janela: Seguir o Windows (o padrão), Claro ou Escuro, e **Quadros por
segundo** limita os wallpapers web (60 por padrão; nunca passa da taxa do monitor).

Fechar a janela não desliga nada: os wallpapers continuam pelo ícone perto do relógio (clique para abrir a janela,
botão direito para pausar tudo ou sair).

## O que entra na biblioteca

- Os wallpapers de **vídeo** e **web** que você já baixou no Wallpaper Engine, lidos direto da pasta do Steam (nada é
  copiado).
- Os seus arquivos, pelo botão **Adicionar arquivo**: vídeo (mp4, webm, mkv, mov...), imagem (jpg, png, webp...) ou
  página HTML.

Os wallpapers **Vivo** são o formato próprio do app (`.vivo`): camadas, partículas, efeitos e animações descritos num
arquivo de texto, que se ajustam sozinhos a qualquer proporção de tela. Quatro exemplos vêm junto. O que não reage a
som vira vídeo em poucos segundos e gasta o que um vídeo gasta; o que reage a som toca ao vivo. Como fazer um:
[`docs/formato-vivo.md`](docs/formato-vivo.md).

As **cenas** do Wallpaper Engine aparecem com o tipo **Cena**: clicar numa delas converte ela num vídeo em loop para o
monitor escolhido. Quem desenha é o próprio Wallpaper Engine (precisa estar instalado), numa janela fora da tela; o
Fundo Vivo grava e emenda o fim com o começo. O que reage a som, mouse ou relógio fica gravado como estava; o parallax
vira um vídeo um pouco maior que a tela, que anda com o mouse.

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
