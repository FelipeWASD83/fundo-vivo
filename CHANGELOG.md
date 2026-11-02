# Changelog

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
