# Fundo Vivo: tudo que tem no app (versão 1.1.0)

Papel de parede animado para o Windows 11, leve: vídeo, imagem ou página web atrás dos ícones da área de trabalho,
um por monitor. Quando ninguém está vendo o wallpaper (um jogo em tela cheia, uma janela maximizada por cima), ele
pausa sozinho e não gasta nada.

## Como abrir

- **Menu Iniciar → Fundo Vivo.** Abre a janela e liga o programa, se ele ainda não estiver rodando.
- **Ícone perto do relógio:** um clique abre a janela. No botão direito: **Abrir o Fundo Vivo**, **Pausar tudo** (ou
  **Continuar**) e **Sair**.
- **Fechar a janela não desliga os wallpapers.** Quem toca é um programa separado, que fica no ícone do relógio. A
  janela só existe enquanto está aberta, então ela não pesa nada quando está fechada.

## A janela

No topo ficam o nome, uma pílula de estado (por exemplo "2 de 2 tocando · 150 MB de RAM", somando o programa e os
processos do Edge dos wallpapers web), o botão **Pausar tudo** (vira **Continuar**, ou **Ligar** quando o programa está
fechado) e as abas **Biblioteca** e **Ajustes**.

### Mapa dos monitores

Os seus monitores aparecem desenhados na posição e no tamanho de verdade (o ultrawide e o monitor em pé; a TV aparece
quando está ligada). Cada um mostra a miniatura do que está tocando nele, o nome, a resolução e uma bolinha de estado:

| Bolinha | Quer dizer |
|---|---|
| Verde | Tocando |
| Amarela | Pausado: tela cheia, janela na frente, tela bloqueada ou pausado por você; ou fora da memória durante um jogo |
| Vermelha | O arquivo sumiu, ou não deu para abrir (o motivo fica nos registros) |
| Cinza | Sem wallpaper animado, ou o programa está fechado |

Clique num monitor para escolher onde o próximo wallpaper vai.

### Biblioteca

- Filtros **Todos**, **Vídeo**, **Web** e **Imagem**, e a busca pelo nome (ignora acento e maiúscula).
- **Adicionar arquivo** põe na biblioteca vídeos (mp4, m4v, mov, webm, mkv, wmv, avi), imagens (jpg, png, bmp, gif,
  webp, tif, jxr, heic, avif) ou páginas HTML seus. O arquivo não é copiado: fica onde está.
- Cada cartão mostra a miniatura, o nome, o tipo, de onde vem (Wallpaper Engine ou seu arquivo) e em quais monitores
  ele está. O do monitor selecionado ganha a borda escura.
- **Clique num cartão** e ele vai para o monitor selecionado na hora.
- **Botão direito num cartão:** **Abrir a pasta** e, nos seus arquivos, **Tirar da biblioteca** (o arquivo não é
  apagado).
- A linha de cima conta quantos wallpapers tem e quantos do tipo cena ficaram de fora (veja "O que não faz").

### Painel do monitor selecionado (à direita)

- **Encaixe** (vídeo e imagem): **Preencher** (cobre a tela toda e corta o que sobra), **Ajustar** (mostra o
  wallpaper inteiro, com faixas pretas se o formato for diferente) e **Esticar** (deforma para caber).
- **Som** (vídeo e web): no vídeo é volume de 0 a 100; no web é ligado ou mudo. Começa sempre mudo.
- **Deixar mais leve** (só vídeo maior que o monitor): **Converter para este monitor** faz uma cópia do vídeo no
  tamanho exato daquela tela, com o mesmo corte do encaixe escolhido. Na tela fica igual, mas toca gastando muito
  menos. Usa o ffmpeg com a codificação da RTX 3060 (e cai para o x264 se precisar). O original nunca é mexido, e
  **Voltar ao original** apaga a cópia. A cópia vale para aquele monitor, aquele encaixe e aquele arquivo: trocar o
  encaixe volta a usar o original até converter de novo.
- **Opções do wallpaper** (web do Wallpaper Engine): as opções que o autor colocou (cores, sliders, listas, liga e
  desliga, texto) aparecem aqui e mudam o wallpaper na hora. **Restaurar** volta tudo ao padrão do autor. A cor abre
  o seletor de cor do Windows.
- **Voltar ao papel de parede do Windows** tira o wallpaper animado só daquele monitor.

As escolhas (encaixe, som, opções) ficam guardadas por wallpaper: se você trocar e depois voltar, elas continuam lá.

### Ajustes

**Quando ninguém vê o wallpaper**
- **Janela maximizada cobrindo o monitor:** Pausar (padrão) ou Continuar.
- **Jogo ou programa em tela cheia:** Pausar (padrão), **Tirar da memória** ou Continuar. Tirar da memória fecha o
  wallpaper durante o jogo e libera RAM e memória da placa de vídeo; ele volta sozinho em menos de um segundo quando
  você sai.
- **Tela cheia num monitor pausa os outros também** (ligado por padrão), pensando no jogo: a placa de vídeo fica toda
  para ele.
- Com a tela bloqueada, tudo pausa.

**Wallpapers web**
- **O mouse funciona nos wallpapers interativos:** movimento e clique na área de trabalho vazia chegam no wallpaper.
- **Reagir ao som que o PC está tocando:** para os visualizadores de música. Só escuta enquanto um wallpaper que pede
  som está na tela e não está pausado.

**Biblioteca:** **Mostrar os wallpapers do Wallpaper Engine** liga ou desliga a leitura da pasta do Steam.

**Início:** **Abrir junto com o Windows** (desligado por padrão).

**Aparência:** **Seguir o Windows** (padrão), **Claro** ou **Escuro**. Troca na hora, a barra de título acompanha, e
no modo Seguir o Windows a janela muda junto se você trocar o tema do Windows.

**Atualização:** mostra a versão. **Procurar atualização** pergunta ao GitHub; se tiver versão nova, aparece o que
mudou e o botão **Instalar**. O campo de token só é necessário para repositório privado (o do Fundo Vivo é público).

**Programa:** **Abrir pasta de registros** e **Fechar o Fundo Vivo** (desliga os wallpapers e devolve o papel de
parede de antes).

## O que entra na biblioteca

- **Do Wallpaper Engine:** os wallpapers de **vídeo** e **web** que você baixou na Oficina do Steam e os seus projetos
  próprios dele, em todas as bibliotecas do Steam. Lidos direto de lá, sem copiar nada. No seu PC: 71 de vídeo e 23
  web, 94 no total.
- **Seus arquivos,** pelo botão **Adicionar arquivo**.

## Os três tipos de wallpaper

- **Vídeo:** toca pelo player do próprio Windows, decodificado na placa de vídeo, em loop. Toca o que o Windows tem
  codec para tocar (H.264, que é o de todos os seus vídeos do Wallpaper Engine, e outros como HEVC e VP9 se as
  extensões estiverem instaladas). Não aparece no painel de mídia do Windows nem responde às teclas de mídia.
- **Imagem:** decodificada uma vez no tamanho exato do monitor; depois não gasta nada. GIF animado mostra só o
  primeiro quadro.
- **Web:** a página roda no WebView2 (o Edge que vem no Windows). Os wallpapers web do Wallpaper Engine recebem as
  mesmas funções que recebem lá: as opções do autor, o fps do monitor, o aviso de pausa e o som do PC em 64 faixas por
  canal, cerca de 30 vezes por segundo. Cada wallpaper tem o seu próprio armazenamento, sem misturar com os outros.

## Por que é leve (medido no seu PC, 02/11/2026)

| Situação | Memória | CPU (de um núcleo) |
|---|---|---|
| Vídeo 1080p | 145 MB | 1,4% |
| Vídeo 4K | 286 MB | 5,5% |
| Vídeo 4K convertido para o monitor em pé | 155 MB (antes 292) | decodificador da placa em 10% (antes 48%) |
| Wallpaper web | ~220 MB, somando os processos do Edge | depende da página |
| Imagem | 97 MB | 0,1% |
| Tudo pausado | igual à de antes de pausar | 0,6%, placa parada |
| Tirar da memória durante um jogo | 29 MB | 0% |

Para comparar: o Wallpaper Engine estava gastando uns 390 MB só com um wallpaper web.

## O que não faz (de propósito ou por limite)

- **Wallpapers de cena do Wallpaper Engine** (224 dos seus) não funcionam. O formato é próprio dele: partículas,
  scripts, modelos 3D, animação de boneco e dezenas de efeitos que só o programa dele desenha. Para esses, continue
  usando o Wallpaper Engine.
- **Nos wallpapers web**, ficam de fora: a música tocando no Windows (título, capa, tempo), as opções de arquivo e
  pasta, o arquivo aleatório de uma pasta e esconder opções conforme outras (todas aparecem sempre).
- **Os dois juntos no mesmo monitor:** o Fundo Vivo desenha por cima do Wallpaper Engine, mas não fecha o dele. Se os
  dois estiverem rodando no mesmo monitor, o do Wallpaper Engine continua gastando escondido embaixo. Melhor deixar
  cada um no seu monitor, ou tirar o do Wallpaper Engine daquele monitor.
- Não otimizado para celular nem para PC fraco: foi feito e medido no seu.

## Atualização

O botão **Procurar atualização** busca a versão mais nova nas releases do GitHub. Antes de trocar qualquer coisa, ele
confere o pacote pelo SHA-256 que o GitHub informa. A versão atual fica guardada e volta sozinha se a nova não abrir
em 25 segundos. A configuração e os vídeos convertidos ficam fora da pasta do programa e nunca são tocados.

Na sua cópia, que é a de desenvolvimento (a pasta tem git), o botão não instala: ali a atualização é pelo git e pelo
`ferramentas\compilar.ps1`. Ainda não existe nenhuma release publicada; para lançar uma, rode o
`ferramentas\publicar-versao.ps1`.

## Onde ficam as coisas

| O quê | Onde |
|---|---|
| Programa | `fundo-vivo\app\` (gerado pelo `ferramentas\compilar.ps1`) |
| Configuração | `%APPDATA%\FundoVivo\config.json` |
| Token do GitHub (se usar) | `%APPDATA%\FundoVivo\secrets.bin`, protegido pelo Windows para o seu usuário |
| Vídeos convertidos | `%LOCALAPPDATA%\FundoVivo\converted\` |
| Registros | `%LOCALAPPDATA%\FundoVivo\logs\` (`engine.log` e `settings.log`) |
| Dados do Edge dos wallpapers web | `%LOCALAPPDATA%\FundoVivo\webview\` |
| Atalho | Menu Iniciar, "Fundo Vivo" |

O programa não manda nada para a internet, a não ser quando você clica em **Procurar atualização** (pergunta ao
GitHub) e o que os próprios wallpapers web carregarem.

## Testado até agora

Na sua área de trabalho de verdade:
- vídeo preenchendo o ultrawide atrás dos ícones e do Relógio Mond;
- pausa só no monitor coberto pela janela maximizada;
- tela cheia pausando os dois monitores e voltando ao fechar;
- **Tirar da memória** caindo para 29 MB;
- o clique chegando no wallpaper web;
- o Wallpaper Engine voltando a aparecer ao fechar.

Os dois temas foram conferidos em fotos da janela, incluindo a troca ao vivo.

**Não testado:** o Explorer reiniciando com o Fundo Vivo ligado. O código para isso existe, mas derrubar o Explorer
podia quebrar o Relógio Mond e o `ilha`.

## Para mexer no código

Está tudo no `CLAUDE.md` da pasta: como o programa é dividido, por que cada escolha foi feita, como testar sem mexer
na sua tela e a armadilha do Smart App Control. Resumo: compile sempre pelo `ferramentas\compilar.ps1`, nunca com
`dotnet build` solto. O Windows deste PC às vezes barra a DLL recém-compilada, e o script recompila com um carimbo novo
até ela passar.

O repositório é público: https://github.com/FelipeWASD83/fundo-vivo (licença MIT).
