# Fundo Vivo: tudo que tem no app (versão 2.7.0)

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

- Filtros **Todos**, **Vídeo**, **Web**, **Imagem** e **Cena**, e a busca pelo nome (ignora acento e maiúscula).
- **Tags**, embaixo dos filtros de tipo: as do Wallpaper Engine, em português (Anime, Jogo, Natureza, Paisagem...), e
  as suas, cada uma com quantos wallpapers tem. Marcando várias, aparece só o que tem **todas**. Depois vem a
  **Classificação** (Livre, Questionável, Adulto), lida do próprio wallpaper; ali vale **qualquer uma** das marcadas.
  Tudo se combina com o tipo e com a busca, e **Limpar** desmarca.
- **Adicionar arquivo** põe na biblioteca vídeos (mp4, m4v, mov, webm, mkv, wmv, avi), imagens (jpg, png, bmp, gif,
  webp, tif, jxr, heic, avif) ou páginas HTML seus. O arquivo não é copiado: fica onde está.
- Cada cartão mostra a miniatura, o nome, o tipo, de onde vem (Wallpaper Engine ou seu arquivo) e em quais monitores
  ele está. O do monitor selecionado ganha a borda escura.
- **Clique num cartão** e ele vai para o monitor selecionado na hora.
- **Botão direito num cartão:** **Abrir a pasta** e, nos seus arquivos, **Editar tags** e **Tirar da biblioteca**
  (o arquivo não é apagado). No editor de tags, enquanto você digita aparecem as que já existem, e uma tag que só
  muda na maiúscula ou no acento vira a que já existe ("anime" vira "Anime").
- Passando o mouse no nome do cartão aparecem as tags e a classificação dele.
- A linha de cima conta quantos wallpapers tem e quantos do tipo cena ficaram de fora (veja "O que não faz").

### Painel do monitor selecionado (à direita)

- **Embaixo do nome**, em quantos fps o wallpaper roda e por quê: o limite dos Ajustes ou o máximo do monitor. No
  vídeo, o fps do próprio arquivo, que o limite não muda.
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

### Playlist do monitor

- **Botão direito num wallpaper > Pôr na playlist deste monitor.** O que já está no monitor entra junto, em primeiro.
  Só entra o que já toca naquele monitor: cena e Vivo que ainda não viraram vídeo para ele pedem um clique antes.
- Com dois ou mais, o monitor troca sozinho: **a cada 5 min, 15 min, 30 min, 1 h ou 2 h**, **em ordem** ou
  **aleatório**. Só troca enquanto está tocando, então nunca carrega nada no meio de um jogo. A troca é seca, sem
  transição.
- No painel: **Tirar** cada item, **Trocar sozinho** para desligar, **Próximo agora**, e **Pôr o atual na playlist**.
- Escolher um wallpaper à mão no monitor recomeça a contagem.

### Ajustes

**Quando ninguém vê o wallpaper**
- **Janela maximizada cobrindo o monitor:** Pausar (padrão) ou Continuar.
- **Jogo ou programa em tela cheia:** Pausar (padrão), **Tirar da memória** ou Continuar. Tirar da memória fecha o
  wallpaper durante o jogo e libera RAM e memória da placa de vídeo; ele volta sozinho em menos de um segundo quando
  você sai.
- **Tela cheia num monitor pausa os outros também** (ligado por padrão), pensando no jogo: a placa de vídeo fica toda
  para ele.
- Com a tela bloqueada, tudo pausa.
- **Programas que pausam tudo:** uma lista de programas que, enquanto estiverem abertos, pausam os wallpapers ou
  tiram eles da memória, mesmo sem tela cheia (After Effects, Premiere, Blender, ComfyUI, que disputam a placa). Digite
  o nome do programa ou use **Dos abertos**. Tirar da memória ganha de pausar quando dois estão abertos.

**Wallpapers web**
- **O mouse funciona nos wallpapers interativos:** movimento e clique na área de trabalho vazia chegam no wallpaper.
- **Reagir ao som que o PC está tocando:** para os visualizadores de música. Só escuta enquanto um wallpaper que pede
  som está na tela e não está pausado.
- **Quadros por segundo:** 30, 60 (padrão), 120, 144 ou 160 para o que desenha ao vivo (os wallpapers web). Nunca passa
  da taxa do monitor: 160 no monitor em pé de 144 Hz roda a 144. Vídeo toca no fps em que foi gravado. Página que
  anima por CSS ou por relógio próprio não obedece; as que desenham do jeito normal (a maioria) obedecem.

**Biblioteca:** **Mostrar os wallpapers do Wallpaper Engine** liga ou desliga a leitura da pasta do Steam.

**Início:** **Abrir junto com o Windows** (desligado por padrão).

**Aparência:** **Seguir o Windows** (padrão), **Claro** ou **Escuro**. Troca na hora, a barra de título acompanha, e
no modo Seguir o Windows a janela muda junto se você trocar o tema do Windows.

**Atualização:** mostra a versão. **Procurar atualização** pergunta ao GitHub; se tiver versão nova, aparece o que
mudou e o botão **Instalar**. O campo de token só é necessário para repositório privado (o do Fundo Vivo é público).

**Programa:** **Abrir pasta de registros** e **Fechar o Fundo Vivo** (desliga os wallpapers e devolve o papel de
parede de antes).

## Comandos (atalho de teclado, Stream Deck)

| Comando | O que faz |
|---|---|
| `FundoVivo.exe --pause` / `--resume` / `--toggle-pause` | pausa, continua ou alterna tudo |
| `FundoVivo.exe --next` | próximo da playlist em todos os monitores que têm uma |
| `FundoVivo.exe --next 2` | só no monitor 2 (contando da esquerda), ou `principal`, ou parte do nome |
| `FundoVivo.exe --set principal "Neve nas montanhas"` | põe esse wallpaper (nome ou arquivo) no monitor, como um clique; se precisar virar vídeo antes, faz e põe |

Não abre janela nem mostra texto; o que aconteceu fica em `%LOCALAPPDATA%\FundoVivo\logs\cli.log`.

## Cenas do Wallpaper Engine

As cenas (224 na sua biblioteca) aparecem com o tipo **Cena**. O Fundo Vivo não desenha cena: ele **converte** a cena
num vídeo em loop para o monitor escolhido, e quem desenha durante a gravação é o próprio Wallpaper Engine.

- **Clique numa cena** com um monitor selecionado. Se ela já foi convertida para o tamanho daquele monitor, vai direto
  para ele. Se não, abre a janela de conversão, com o que a cena perde, a **duração do loop** (10 a 60 s, padrão 20) e
  a **emenda** (quantos segundos do fim se misturam com o começo, padrão 1,5).
- **Durante a gravação**, a janela do Wallpaper Engine aparece por um instante no canto da tela e vai para fora dela;
  dá para continuar usando o PC. Não abra jogo em tela cheia até acabar (o Wallpaper Engine pausa). A barra em cima
  da Biblioteca mostra o progresso e tem **Cancelar**; dá para fechar a janela, a conversão continua sozinha. Pronta,
  a cena vai para o monitor. Uma cena de 20 s leva uns 30 a 40 s.
- **Tags automáticas** para o filtro: **Converte sem perder nada** (86 das suas), **Parallax vira pan** (40), **Perde
  reação ao som** (95), **Perde o mouse** (46), **Relógio fica parado** (98), **Som vai junto** (103) e **Sem leitura** (cena cujo arquivo não deu para ler; as 12 cenas de GIF
  caíam aqui até a 2.1.2 e agora são lidas e convertem). Uma cena pode ter várias.
- **Som:** o que estiver tocando no PC durante a gravação fica gravado na animação (para as cenas que reagem ao
  som). Converta com o som parado.
- **Som de fundo vai junto:** a música ou o ambiente da cena sai do pacote e toca em loop junto do vídeo, no volume do
  painel do monitor (começa mudo). Ele toca no próprio tempo, então a música não corta a cada volta do vídeo. Durante a
  gravação ele toca no PC.
- **Opções da cena:** a janela de conversão mostra as opções do autor (cor, personagem, mostrar relógio e data,
  filtros) e o vídeo sai com elas. Bom para esconder um relógio que ficaria parado. Ficam guardadas para a próxima.
- **Duração sugerida:** quando a cena diz quanto duram as animações dela, a duração já vem numa medida que fecha com
  elas.
- **Parallax:** o vídeo sai um pouco maior que a tela (até 15% de cada lado) e anda com o mouse. É uma aproximação: o
  vídeo inteiro anda junto, sem a profundidade de cada camada. Só em **Preencher**.
- **Imagem animada só (tag Vira vídeo direto):** as cenas que são uma imagem animada e nada mais (as 12 de GIF, o
  Pixel Cyberpunk Coffee, o buh - the cat) não passam pelo Wallpaper Engine: o Fundo Vivo lê a folha de quadros da
  cena e monta o vídeo direto, em poucos segundos, com o loop exato dela. Não pede duração nem emenda.
- **Fps:** o vídeo sai no limite de fps do seu Wallpaper Engine (hoje 30). Para cenas a 60, suba o limite nos ajustes
  dele antes de converter.
- **Botão direito numa cena:** **Converter de novo para este monitor** e **Apagar as conversões desta cena**.
- **Limites:** o Windows não deixa a janela passar da altura da área de trabalho, então no monitor em pé e na TV 4K a
  cena é gravada um pouco menor e ampliada (no monitor em pé, ~1% sem parallax e até ~14% com o parallax mais forte).
  O ultrawide com sobra de parallax sai em HEVC, que o Windows toca.

## Wallpapers Vivo (o formato do app)

O `.vivo` é o formato próprio do Fundo Vivo: um arquivo que descreve o wallpaper em camadas (imagem, gradiente, forma,
partículas de chuva, neve, poeira e vaga-lumes), efeitos (desfoque, cor, grão, brilho, raios de luz), animações em loop
e parallax. Como fazer um, com todos os campos: [`formato-vivo.md`](formato-vivo.md).

- **Exemplos:** quatro vêm no app (filtro **Vivo**): Noite de chuva, Vaga-lumes no crepúsculo, Neve nas montanhas e
  Pulso (este reage ao som).
- **O que dá para fazer** (2.5.0): além de imagem, gradiente, formas e partículas, texto que se digita, chuva de
  código, riscos de neon, ondas recortadas, fumaça e areia em relevo, barras do som, cubo que pula com a música e
  tentáculos. O som chega em 64 faixas. Na 2.6.0: mar escuro com cristas brilhando (com areia, se quiser) e
  eletricidade que segue o mouse. Na 2.7.0: imagens que rolam, ondulam como água, ganham máscaras e luz, efeito de
  lente, fonte dentro do próprio arquivo e loop de até uma hora.
- **Qualquer tela:** as medidas são frações do lado menor da tela e cada camada se prende a um canto ou a uma borda,
  então o mesmo arquivo serve no ultrawide, no monitor em pé e na TV 4K.
- **Clique num Vivo** com um monitor selecionado. Se ele não reage a som, vira **vídeo** para aquele monitor (a barra
  em cima da Biblioteca mostra; leva uns 5 a 20 s) e vai para a tela, em loop perfeito. Com parallax, o vídeo sai um
  pouco maior e anda com o mouse, como nas cenas. Se reage a som, toca **ao vivo**, no limite de fps dos Ajustes, com
  o parallax de verdade em cada camada.
- **No vídeo, o grão de filme fica parado** (ao vivo ele mexe): grão mexendo em todo pixel deixava o vídeo sete vezes
  maior.
- **Mudou o arquivo?** No próximo clique ele vira vídeo de novo. Botão direito: **Renderizar de novo para este monitor**
  e **Apagar os vídeos feitos deste wallpaper**.

## O que entra na biblioteca

- **Os exemplos Vivo** que vêm com o app.
- **Do Wallpaper Engine:** os wallpapers de **vídeo** e **web** que você baixou na Oficina do Steam e os seus projetos
  próprios dele, em todas as bibliotecas do Steam. Lidos direto de lá, sem copiar nada. No seu PC: 71 de vídeo e 23
  web, 94 no total.
- **Seus arquivos,** pelo botão **Adicionar arquivo**: vídeo, imagem, página HTML ou `.vivo`.

## Os três tipos de wallpaper

- **Vídeo:** toca pelo player do próprio Windows, decodificado na placa de vídeo, em loop. Toca o que o Windows tem
  codec para tocar (H.264, que é o de todos os seus vídeos do Wallpaper Engine, e outros como HEVC e VP9 se as
  extensões estiverem instaladas). Não aparece no painel de mídia do Windows nem responde às teclas de mídia.
- **Imagem:** decodificada uma vez no tamanho exato do monitor; depois não gasta nada. **GIF animado** vira vídeo ao
  ser posto num monitor (a barra da Biblioteca mostra) e toca em loop; GIF pequeno, de pixel art, é ampliado sem
  borrar.
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
| Mesmo web nos dois monitores, limite 160 (160 e 144 fps) | | 51% |
| Mesmo web nos dois monitores, limite 60 (o padrão) | | 28% |
| Imagem | 97 MB | 0,1% |
| Tudo pausado | igual à de antes de pausar | 0,6%, placa parada |
| Tirar da memória durante um jogo | 29 MB | 0% |
| Cena "Pixel Cyberpunk Coffee" no próprio Wallpaper Engine (a mais sobre o que ele já gastava) | +226 MB | +6,2% |
| A mesma cena convertida, tocando no Fundo Vivo (HEVC 4472×1872, com a sobra do parallax) | 283 MB | 1,9% (decodificador da placa em 13%) |
| Vivo "Noite de chuva" ao vivo no ultrawide, 60 fps | 537 MB, somando os processos do Edge | 15% (3D da placa em 10%) |
| O mesmo Vivo virado vídeo (3646×1526, 60 fps, com a sobra do pan) | 251 MB | 3,1% (3D parado, decodificador em 25%) |
| Vivo "Pulso" ao vivo, reagindo ao som | 478 MB | 13% (3D da placa em 10%) |

No Vivo virado vídeo, a memória cai para menos da metade, a CPU para um quinto e a parte 3D da placa (a que os jogos
usam) fica parada; quem trabalha é o decodificador de vídeo, que é um pedaço separado da placa. É por isso que o app só
deixa ao vivo o que reage a som.

Na cena convertida, a CPU cai para um terço, mas a memória fica parecida: o vídeo com a sobra do parallax é bem maior
que a tela. Cena sem parallax sai do tamanho exato do monitor e gasta como um vídeo comum.

Para comparar: o Wallpaper Engine estava gastando uns 390 MB só com um wallpaper web.

## O que não faz (de propósito ou por limite)

- **Cena ao vivo:** o Fundo Vivo não desenha cena do Wallpaper Engine; ele grava a cena em vídeo (veja "Cenas do
  Wallpaper Engine"). O que reage a som, mouse ou relógio fica parado no que foi gravado.
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
