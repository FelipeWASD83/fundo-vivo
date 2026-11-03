# Changelog

## 2.8.0 (03/11/2026)

### Novo
- Cinco wallpapers novos vêm com o app: **Maré noturna** (mar escuro sob a lua), **Cortina boreal** (aurora de luz caindo), **Camadas do entardecer** (faixas de papel ondulando), **Faíscas** (eletricidade que segue o mouse) e **Cubo sonoro** (cubo que pula com a música dentro de um anel de barras). Agora são nove no filtro Vivo.
- Os riscos podem sortear algumas cores e espalhar pela tela, passando devagar para um sorteio novo de tempos em tempos.
- O cubo aceita a direção da luz e o tom do miolo.

### Corrigido
- A ondulação de tela inteira esticava a borda da imagem em faixas claras; agora a borda é espelhada.

## 2.7.0 (02/11/2026)

### Novo
- Imagens no `.vivo` podem rolar e se repetir, ondular como água (por um mapa de normais), ganhar máscaras que deslizam por cima (uma apaga, outra pinta) e ser iluminadas por uma luz pontual que pode seguir o som.
- Efeitos novos: ondulação de água na tela inteira e lente (barril), calculada no formato da cena original.
- Riscos em forma de pílula, com espessura e velocidade iguais para todos se quiser, ponta branca do tamanho que escolher e perfil reto.
- O cubo ganhou perspectiva e um degradê que gira por ele.
- A eletricidade ganhou o estilo "tentáculos": pontos que esticam linhas curvas até o mouse.
- Texto com fonte que vai dentro do próprio `.vivo`.
- Loop de até uma hora; acima de dois minutos o wallpaper toca sempre ao vivo.
- Grão que acompanha o brilho (o preto continua preto).

### Mudou
- A sombra da camada de onda cai como um sino, como a de papel recortado.

### Corrigido
- O grão forte mostrava listras verticais.

## 2.6.0 (02/11/2026)

### Novo
- Camada de **mar**: ondas escuras vistas de lado, com cristas enrugadas que pegam a luz, e um grão de areia opcional na superfície.
- Camada de **eletricidade**: pontinhos pela tela e raios que saem do mouse para os pontos mais perto e pulam de um para o outro. Sem mouse, a corrente passeia sozinha.
- Os riscos podem ser pintados pela posição na tela, em faixas de cor que deslizam, e alguns podem ter a ponta branca.
- O cubo gira de verdade (para os lados, para cima e para baixo) e as faces se iluminam conforme ele vira.

### Corrigido
- Animar algo que a camada deixou no padrão começava do zero. Por exemplo, as ondas de um wallpaper ficavam retas sem música tocando.

## 2.5.0 (02/11/2026)

### Novo
- O formato `.vivo` ganhou camadas novas: **texto** (com várias cores numa linha, digitando sozinho e cursor piscando), **chuva de código**, **riscos** (linhas compridas coloridas atravessando a tela), **onda** (faixa recortada que anda, com sombra), **ruído** (fumaça, areia, rocha, em relevo), **barras do som** (em linha ou em círculo), **polígono**, **cubo** isométrico que pula com o som e **tentáculos**.
- O som chega em 64 faixas: dá para escolher uma faixa (0 a 63) ou um trecho, além de graves, médios e agudos.
- Qualquer número de uma camada pode ser animado (o comprimento dos riscos, a altura da onda, o brilho do relevo), inclusive pelo som.
- Efeitos novos: **tremor** da imagem com os graves e **separação de cor**; o brilho e os raios de luz também podem crescer com o som.

## 2.4.0 (02/11/2026)

### Novo
- O som de fundo das cenas (música, ambiente; 103 das suas) vai junto: sai do pacote e toca em loop com o vídeo convertido, com o controle de volume do painel (começa mudo). Toca no próprio tempo, então uma música de 3 minutos não é cortada a cada volta do vídeo.
- As opções do autor da cena aparecem antes de converter (cor, personagem, mostrar relógio, filtros...), e o vídeo sai com elas. Dá, por exemplo, para esconder um relógio que ficaria parado na hora da gravação. As escolhas ficam guardadas para a próxima conversão.
- Quando a cena declara a duração das animações dela (43 das suas), a duração do loop já vem numa medida que fecha certinho com elas.

### Mudou
- As tags das cenas dizem melhor o que muda no vídeo: **Perde o mouse** e **Relógio fica parado** separados (antes era uma só), e **Som vai junto** para as que têm som de fundo.

## 2.3.0 (02/11/2026)

### Novo
- Playlist por monitor: botão direito num wallpaper, **Pôr na playlist deste monitor**, e ele passa a trocar sozinho a cada 5 min, 15 min, 30 min, 1 h ou 2 h, em ordem ou aleatório. Só troca enquanto o monitor está tocando, nunca no meio de um jogo. No painel do monitor dá para tirar itens, desligar e pedir o próximo na hora.
- Programas que pausam tudo: em Ajustes, uma lista de programas (After Effects, Blender, ComfyUI...) que, enquanto estiverem abertos, pausam os wallpapers ou tiram eles da memória, mesmo sem tela cheia. **Dos abertos** escolhe da lista do que está rodando.
- Comandos para atalho de teclado ou Stream Deck: `FundoVivo.exe --pause`, `--resume`, `--toggle-pause`, `--next [monitor]` e `--set <monitor> <arquivo ou nome>`. O monitor é `principal`, o número contando da esquerda ou parte do nome.

### Corrigido
- Campos de texto não esticam mais os botões do lado.

## 2.2.0 (02/11/2026)

### Novo
- Cenas do Wallpaper Engine que são uma imagem animada só (as 12 de GIF, o Pixel Cyberpunk Coffee, o buh - the cat; 14 na sua biblioteca) viram vídeo direto da folha de quadros, sem abrir o Wallpaper Engine: sem janela piscando, sem gravação, em poucos segundos, e com o loop exato da própria cena. Elas ganham a tag **Vira vídeo direto** e não pedem duração nem emenda.
- GIF animado que você adiciona passa a animar: ao pôr num monitor, ele vira vídeo para aquele tamanho (a barra da Biblioteca mostra) e toca em loop. GIF pequeno, de pixel art, é ampliado sem borrar.

## 2.1.2 (02/11/2026)

### Corrigido
- As cenas de GIF do Wallpaper Engine (feitas com o modelo de GIF dele, 12 na sua biblioteca) davam erro ao converter, porque o pacote delas tem outro nome. Agora convertem, e deixaram de aparecer como "Sem leitura".

## 2.1.1 (02/11/2026)

### Corrigido
- Botões, filtros, busca e etiquetas saíam com uma elipse esticada em volta em vez de pílula; agora o canto é sempre a metade da altura.
- O campo de busca encolhia até o texto de dica e cortava o "nome".

## 2.1.0 (02/11/2026)

### Mudou
- Visual novo, da mesma família da Ilha e do Relógio Mond: fundo preto (ou cinza bem claro no tema claro), blocos com canto bem redondo e sem borda, botões e filtros em pílula, o escolhido em branco, e os títulos na mesma fonte do relógio (Quicksand).
- Os monitores ficam num bloco escuro, maiores, com o nome embaixo de cada um; o escolhido fica aceso e com um anel, os outros um pouco apagados.
- Na Biblioteca, a imagem é o cartão: canto arredondado, título embaixo, anel no que está no monitor e um leve zoom ao passar o mouse.
- Cada filtro de tipo mostra quantos tem, e o tipo sem nenhum wallpaper some.
- As tags ficam recolhidas num botão **Tags**; fechado, só as que estão ligadas aparecem.

## 2.0.0 (02/11/2026)

### Novo
- Formato próprio de wallpaper, o `.vivo`: um zip com um `wallpaper.json` que descreve camadas (imagem, gradiente, forma e partículas de chuva, neve, poeira e vaga-lumes), efeitos (desfoque, cor, grão, brilho e raios de luz), animações em loop (oscilar, girar, ruído e reagir ao som) e parallax. A documentação completa está em `docs/formato-vivo.md` e o esquema em `schema/vivo.schema.json`.
- O mesmo wallpaper se ajusta sozinho ao ultrawide, ao monitor em pé e à TV 4K: as medidas são frações do lado menor da tela e cada camada se prende a um canto ou a uma borda.
- Quatro exemplos já vêm no app, no filtro **Vivo** da Biblioteca: Noite de chuva, Vaga-lumes no crepúsculo, Neve nas montanhas e Pulso.
- Wallpaper `.vivo` que não reage a som vira vídeo para o monitor escolhido, em poucos segundos e em loop perfeito, e passa a gastar o que um vídeo gasta. Com parallax, o vídeo sai um pouco maior e anda com o mouse.
- Wallpaper `.vivo` que reage a som toca ao vivo, no limite de fps dos Ajustes, com o parallax de verdade em cada camada.
- Adicionar arquivo aceita `.vivo`, que entra com o título e as tags que ele traz.

### Mudou
- A barra de conversão da Biblioteca também mostra a renderização dos `.vivo`.

## 1.3.0 (02/11/2026)

### Novo
- As cenas do Wallpaper Engine entram na Biblioteca (filtro Cena). Clicar numa cena converte ela num vídeo em loop para o monitor escolhido: o próprio Wallpaper Engine desenha a cena numa janela fora da tela, e o Fundo Vivo grava e emenda o fim com o começo, sem a emenda aparecer.
- Antes de converter, uma janela mostra o que a cena perde (som, mouse, relógio), a duração do loop (10 a 60 s, padrão 20) e o tempo da emenda (padrão 1,5 s).
- Cada cena ganha tags automáticas para o filtro: Converte sem perder nada, Parallax vira pan, Perde reação ao som, Perde mouse/relógio e Sem leitura.
- Cena com parallax vira um vídeo um pouco maior que a tela, que anda com o mouse.
- A conversão roda à parte, numa fila, e continua mesmo com a janela fechada; a Biblioteca mostra o progresso e dá para cancelar.
- Botão direito na cena: converter de novo, ou apagar as conversões.

## 1.2.0 (02/11/2026)

### Novo
- Filtro por tags na Biblioteca: as tags dos wallpapers do Wallpaper Engine, em português, com quantos tem de cada. Dá para marcar várias (aparece só o que tem todas) e combinar com o tipo e com a busca.
- Filtro por classificação (Livre, Questionável, Adulto), lida do próprio wallpaper.
- Tags nos seus arquivos: botão direito no cartão, Editar tags. Enquanto você digita, aparecem as tags que já existem, e "anime" vira o "Anime" que já está lá.
- Limite de quadros por segundo nos Ajustes (30, 60, 120, 144 ou 160) para os wallpapers web. Nunca passa da taxa do monitor.
- O painel do monitor mostra em quantos fps o wallpaper roda e por quê; no vídeo, mostra o fps do próprio arquivo.

### Mudou
- Os wallpapers web agora rodam a 60 fps por padrão, em vez de seguir a taxa do monitor (250 no ultrawide). Medido: a mesma página caiu de 51% para 28% de um núcleo.

### Corrigido
- A grade da Biblioteca às vezes caía para duas colunas quando a barra de rolagem aparecia.

## 1.1.0 (02/11/2026)

### Novo
- Tema escuro. Em Ajustes, Aparência: Seguir o Windows (o padrão), Claro ou Escuro. Troca na hora, sem fechar a janela, e a barra de título acompanha.

### Mudou
- Barras de rolagem, sliders, listas, dicas e o menu do botão direito redesenhados para combinar com os dois temas.

## 1.0.0 (02/11/2026)

### Novo
- Papel de parede animado por monitor: vídeo, imagem ou página web, atrás dos ícones da área de trabalho.
- Biblioteca lida direto do Wallpaper Engine (os wallpapers de vídeo e web que você já baixou no Steam), mais os arquivos que você adicionar.
- Pausa sozinho quando uma janela maximizada ou um jogo em tela cheia cobre o monitor; dá para escolher tirar o wallpaper da memória durante o jogo.
- Wallpapers web com as opções do Wallpaper Engine (cores, velocidade, listas), reagindo ao som do PC e ao mouse.
- Botão para converter um vídeo maior que o monitor numa cópia do tamanho exato dele, que gasta bem menos para tocar.
- Ícone perto do relógio com pausar tudo, abrir e sair; opção de abrir junto com o Windows.
- Procurar e instalar atualização pelo GitHub, conferindo o pacote antes e voltando à versão anterior se a nova não abrir.
