# Pesquisa: cenas do Wallpaper Engine no Fundo Vivo (02/11/2026)

Pesquisa só de leitura. Nada foi mudado no Wallpaper Engine, no Workshop nem no código do app. Os números vêm das 224
cenas que estão no PC dele (`C:\Program Files (x86)\Steam\steamapps\workshop\content\431960`), lidas direto nos
pacotes por um script descartável fora do projeto. Wallpaper Engine instalado: versão 2.8.42.

## 1. Pergunta

Dá para o Fundo Vivo suportar melhor as cenas do Wallpaper Engine (hoje elas só entram gravadas em vídeo pelo próprio
Wallpaper Engine)? E, de bônus, o que o Wallpaper Engine tem que valeria trazer para o app?

## 2. Recomendação

1. **Não começar agora um renderizador de cena nativo completo.** O formato é legível (o pacote, as texturas e o
   `scene.json` foram lidos sem erro nas 224 cenas), mas o que mais pesa não é o formato: é que cada efeito é um
   shader do Wallpaper Engine. Com só camadas de imagem, partículas e som, **77 das 224 cenas (34%)** têm conteúdo
   desenhável, e dessas, para cobrir de verdade é preciso reimplementar efeito por efeito: com os 10 efeitos mais
   comuns, **58 cenas (26%)**; com 20, **70 (31%)**. É um projeto de semanas para pegar um quarto da biblioteca, com
   fidelidade "parecida", não igual. O vídeo gravado já toca leve e já funciona.
2. **Fazer primeiro as vitórias pequenas, que valem já** (seção 7.3):
   - **Cenas de sprite (GIF) convertidas sem o Wallpaper Engine**: 12 "cenas de GIF" (`gifscene.pkg`) e mais ~6
     cenas que são uma imagem animada só. O quadro inteiro está na textura, com o tempo de cada quadro. Dá para
     montar o vídeo direto, em **loop perfeito de verdade** (1,3 a 4,8 s, medido), sem abrir janela nem gravar.
   - **Corrigir a leitura das cenas de GIF**: hoje elas caem em "Sem leitura" porque o app procura `scene.pkg`, e
     o pacote delas se chama `gifscene.pkg`. Pelo código, a conversão delas também deve falhar (ver 7.3); não testei.
   - **Opções da cena antes de converter** (140 cenas têm opções do autor além da cor do tema).
   - **Som da cena no vídeo** (104 cenas têm camada de som; hoje o vídeo sai mudo).
   - **Duração do loop sugerida** a partir das animações de linha do tempo e dos sprites.
   - Testar a opção `borderless` do Wallpaper Engine para gravar no tamanho certo no monitor em pé e na TV.
3. **Se quiser ir além depois**, o caminho certo é o (b) da seção 6 feito como (a): um **importador de cena para um
   `.vivo` versão 2**, desenhado pelo `vivo.js`. Antes de decidir, uma prova de uma semana em 5 cenas, comparando
   quadro a quadro com o vídeo que o próprio app já grava hoje.
4. Do bônus (seção 8), o que mais vale: **playlist por monitor**, **GIF animado de verdade** e **regras por
   programa** (pausar quando After Effects, Premiere ou ComfyUI estão abertos).

Essa recomendação mexe numa decisão escrita no `CLAUDE.md` ("cena não é desenhada pelo Fundo Vivo"). As vitórias
pequenas não mexem: a de GIF não desenha cena, só lê uma textura e faz um vídeo. Ir para o renderizador é decisão dele.

## 3. O formato

### 3.1 O pacote (`scene.pkg` e `gifscene.pkg`)

Confirmado nos arquivos dele e no RePKG:
- String com tamanho na frente (`int32` + UTF-8): `PKGV0001` a `PKGV0024` (21 versões diferentes nas cenas dele; a
  mais comum é `PKGV0021`). O layout é o mesmo em todas.
- `int32` com a quantidade de arquivos; para cada um, nome (string com tamanho), offset e tamanho (`int32`).
- Os dados começam logo depois da tabela; offset é relativo a esse ponto. Sem compressão no pacote.
- O nome do pacote segue o `file` do `project.json`: `scene.json` mora em `scene.pkg`, e `gifscene.json` mora em
  `gifscene.pkg` (as 12 cenas de GIF). O app hoje só procura `scene.pkg`.
- Nas 224 cenas: 8.310 `.json`, 3.454 `.tex`, 1.481 pares `.frag`/`.vert`, 123 `.mdl`, 271 áudios (mp3, ogg, wav,
  flac) e 228 fontes. **Nenhum `.js`**: os scripts ficam dentro do `scene.json`.

### 3.2 `scene.json`

Três blocos: `camera` (eye, center, up), `general` e `objects`.

- `general`: tamanho da cena (`orthogonalprojection` com `width`/`height`), cor de fundo (`clearcolor`), bloom
  (`bloom`, `bloomstrength`, `bloomthreshold` e a versão HDR), parallax da câmera (`cameraparallax`,
  `cameraparallaxamount`, `cameraparallaxdelay`, `cameraparallaxmouseinfluence`), tremor da câmera (`camerashake*`),
  fov e zoom, vento e gravidade para partículas, `spritesheetrefreshsync`.
- Cada objeto de `objects` é um destes, conforme a chave que tem:
  - **Imagem** (`image` → um `models/*.json`). O modelo aponta um `material`, que diz o shader base
    (`genericimage2/3/4`), a mistura e as texturas. Se o modelo tem `puppet`, é uma **marionete** (`.mdl` com malha,
    ossos e animação; `animationlayers` no objeto). Camadas especiais vêm da instalação do Wallpaper Engine, não do
    pacote: `models/util/solidlayer.json` (424 usos), `composelayer.json` (341, compõe o que está atrás),
    `fullscreenlayer.json` (124), `projectlayer.json` (60).
  - **Partícula** (`particle` → `particles/*.json`), com `instanceoverride` (rate, count, speed, alpha, size).
  - **Texto** (`text`, `font`, `pointsize`, alinhamentos, limites), quase sempre com script (relógio, data).
  - **Som** (`sound`: lista de arquivos, `playbackmode`, `volume`, `mintime`/`maxtime`).
  - **Modelo 3D** (`model`), **luz** (`light`), **câmera com trajeto** (`camera`/`path`), **forma** (`shape`) e
    grupo (objeto vazio que outros usam como `parent`).
- Campos comuns: `origin`, `scale`, `angles`, `size`, `alpha`, `color`, `brightness`, `visible`, `parent`,
  `parallaxDepth` (quanto a camada anda no parallax), `colorBlendMode`, `copybackground`, `perspective`, `effects`.
- **Efeito** = item de `effects` com `file` (`effects/<nome>/effect.json`) e uma lista de `passes`, cada um com
  `constantshadervalues` (parâmetros), `combos` (chaves que ligam variantes do shader) e `textures` (máscaras
  pintadas pelo autor, como `masks/waterwaves_mask_40f144a1`, ou texturas utilitárias como `util/clouds_256`).
- **Qualquer valor pode virar animado ou dinâmico**: um objeto com `animation` (quadros-chave com curvas, `options`
  com `fps`, `length` e `mode` = `loop`, `single` ou `mirror`), com `script` (código SceneScript em JavaScript) ou com
  `user` (ligado a uma opção do autor no `project.json`).
- As **opções do autor** ficam no `project.json` (`general.properties`): slider, bool, color, combo, text,
  textinput, scenetexture, usershortcut e grupos.

### 3.3 Texturas `.tex`

Confirmado: o layout abaixo foi percorrido do começo ao fim em **3.464 das 3.466 texturas** e terminou exatamente no
tamanho do arquivo; 302 blocos LZ4 descomprimidos deram o tamanho declarado.

- `TEXV0005\0` e `TEXI0001\0` (strings terminadas em zero, sem tamanho na frente).
- Sete `int32`: formato, flags, largura e altura da textura (alocada), largura e altura da imagem (útil), e um valor
  que ninguém decifrou.
- Formato: 0 = RGBA8888, 4 = DXT5, 6 = DXT3, 7 = DXT1, 8 = RG88, 9 = R8 (o linux-wallpaperengine lista mais).
- Flags: 1 = sem interpolação, 2 = prender as bordas, 4 = animada (sprite), 32 = textura de vídeo.
- `TEXB000n\0`, quantidade de imagens; na `0003` e `0004` vem também o formato FreeImage (−1 = dados crus,
  2 = JPEG, 13 = PNG); a `0004` tem mais um `int32`.
- Para cada imagem, a quantidade de mipmaps; para cada mipmap: largura, altura, se é LZ4, tamanho descomprimido,
  tamanho em bytes e os bytes. A versão `0001` não tem os dois campos do LZ4.
- Se for animada, vem no fim `TEXS000n\0`: quantidade de quadros; na `0003`, a largura e altura do quadro; depois
  32 bytes por quadro: imagem, tempo do quadro (float) e o retângulo dele na folha.

O que tem nas cenas dele:

| O quê | Quantas texturas |
|---|---|
| RGBA crua (a maioria com LZ4) | 768 |
| DXT5 / DXT1 / DXT3 | 951 / 38 / 10 |
| R8 / RG88 (máscaras e mapas de fluxo) | 684 / 179 |
| PNG embutido / JPEG embutido | 765 / 71 |
| Com LZ4 | 2.406 |
| Animadas (folha de sprites, `TEXS0003` em 219) | 221 |
| Marcadas como vídeo | 39 |

Duas texturas `TEXB0004` (uma cena só) não fecharam: trazem uma condição em JSON ligada a uma opção do autor
(`{"condition":...}`) e mais de uma imagem, uma por valor. É uma variante rara que o RePKG também não descreve.

### 3.4 Shaders

- **Cada efeito usado vem dentro do pacote**: as 3.905 referências a efeito oficial têm o `effect.json`, o material e
  os `.frag`/`.vert` no próprio pacote (são cópias do que o editor pôs lá).
- **Mas eles dependem da instalação do Wallpaper Engine**: os `#include` (`common.h` em 465 shaders,
  `common_blending.h` em 458, `common_blur.h` em 122) e os shaders base das camadas e partículas (`genericimage4`,
  `genericparticle`) não vêm no pacote; ficam em `wallpaper_engine\assets\shaders`. Texturas utilitárias
  (`util/...`) e os modelos `models/util/*.json` também.
- O dialeto é um GLSL com cara de HLSL e anotações em comentário: `// [COMBO] {...}` define as variantes, e cada
  `uniform` tem um JSON com o nome do parâmetro do material (`"material":"speed"`), padrão, faixa e o tipo de máscara
  (`opacitymask`, `flowmask`). É isso que liga os `constantshadervalues` do `scene.json` ao shader.
- 91 cenas usam **efeitos do Workshop** (`effects/workshop/<id>/...`), com shaders feitos por outros autores; 7 usam
  shader próprio na camada de imagem.

### 3.5 O que o binário confirmou (REA e leitura de strings)

Não havia Ghidra nem outro descompilador configurado no REA, então não houve descompilação; usei o leitor de
importações do REA e uma busca de strings no `wallpaper64.exe`.
- O renderizador é Direct3D 11 (`D3D11CreateDevice`), texto por DirectWrite, vídeo e áudio por Media Foundation
  (evidência REA `ev_1061cf9a...`). O `d3dcompiler_47.dll` vem junto: a inferência é que ele traduz os shaders e
  compila na hora.
- Versões que o executável conhece: `TEXV0005`/`0004`, `TEXB0004`, `TEXS0003`, `MDLV0023`, `MDLS0004` (esqueleto),
  `MDLA0006` (animação), `MDAT`, `MDLE`, `MDMP`. As marionetes no pacote dele vão de `MDLV0013` a `MDLV0023`.
- Os comandos `openWallpaper`, `playInWindow`, `closeWallpaper` e `applyProperties` existem no executável, e estão na
  documentação oficial de linha de comando.

## 4. Estatísticas das 224 cenas dele

| Recurso | Cenas |
|---|---|
| Pelo menos uma camada de imagem | 223 |
| Algum efeito | 181 (153 efeitos diferentes) |
| Partículas | 155 (1.242 sistemas) |
| Script (SceneScript) | 118 |
| Camada de som | 104 |
| Texto | 99 (quase sempre relógio ou data: 98 cenas têm script de hora) |
| Reage a áudio de algum jeito | 98 |
| Efeito do Workshop | 91 |
| Composição (`composelayer`) / tela cheia (`fullscreenlayer`) | 69 / 49 |
| Linha do tempo (quadros-chave) | 52 |
| Script que lê o mouse | 46 |
| Parallax da câmera ligado | 45 |
| Marionete (puppet) / com animação | 33 / 29 |
| Bloom | 28 |
| Câmera com trajeto | 16 |
| Luz / modelo 3D | 12 / 7 |
| Cenas de GIF (`gifscene`) | 12 |
| Opções do autor além da cor do tema | 140 |

Mais números: pacote mediano de 16 MB (o maior tem 382 MB); 11 objetos por cena na mediana; 184 cenas são 16:9, só 13
são ultrawide e 2 são em pé; 132 foram feitas em 4K ou mais.

### Efeitos mais usados (cenas que usam)

waterwaves 74, waterripple 58, shake 57, opacity 48, foliagesway 48, waterflow 43, pulse 39, tint 30, godrays 29,
scroll 28, opacity do Workshop 27, barras de áudio do Workshop 26, blendgradient do Workshop 25, shine 21, blur 21,
blend 21, blurprecise 21, tint do Workshop 21, spin 17, iris 17.

A maioria é **distorção por camada guiada por máscara pintada**: ondas, ondulação de água, tremor por mapa de fluxo,
balanço de folhagem, água correndo, giro, olho que mexe. Outros são cor e brilho (pulse, tint, shine, godrays).
Vários têm variantes que mudam o comportamento (modo de mistura, ruído, perspectiva, reação a áudio), então
"suportar o efeito X" quer dizer suportar as variantes dele também.

### Partículas (cenas que usam cada peça)

- Emissores: `sphererandom` 138, `boxrandom` 81.
- Inicializadores: `lifetimerandom` 155, `sizerandom` 155, `colorrandom` 148, `velocityrandom` 134, `alpharandom` 95,
  `rotationrandom` 89, `turbulentvelocityrandom` 60, `angularvelocityrandom` 55.
- Operadores: `movement` 154, `alphafade` 147, `oscillateposition` 75, `sizechange` 73, `oscillatealpha` 72,
  `angularmovement` 62, `controlpointattract` 58, `turbulence` 48, `oscillatesize` 23, `colorchange` 13,
  `alphachange` 11, `vortex` 6.
- Desenho: `sprite` 142, `spritetrail` 57, `ropetrail` 27, `rope` 25. Sistemas com filhos: 69 cenas.

O vocabulário é pequeno: com 2 emissores, 8 inicializadores, 12 operadores e 4 jeitos de desenhar, cobre-se
praticamente tudo o que aparece nas cenas dele.

### Quantas daria para desenhar nativamente

Base: cena **sem** texto, script, marionete, modelo 3D, luz, câmera com trajeto, efeito do Workshop, shader próprio e
fora as de GIF. Sobram **77 cenas**: 23 não têm efeito nenhum, e as outras precisam dos efeitos abaixo.

| Efeitos suportados (os mais usados nessas 77) | Cenas cobertas por inteiro | Fração das 224 |
|---|---|---|
| nenhum | 23 | 10% |
| top 3 | 30 | 13% |
| top 5 | 40 | 18% |
| top 10 | 58 | 26% |
| top 15 | 64 | 29% |
| top 20 | 70 | 31% |
| todos os 29 | 77 | 34% |

Os 10 primeiros nessa base: waterripple, waterwaves, shake, waterflow, foliagesway, godrays, pulse, shine, opacity e
blur. Escolhendo de forma gulosa (o que destrava mais cenas a cada passo), os 10 primeiros dão 58, igual.

Tetos se o app também rodasse os shaders da instalação do Wallpaper Engine (caminho a2 da seção 6): 87 cenas sem
script; 103 com SceneScript; 160 com texto e script; 187 com marionete também. As 37 restantes têm 3D, luz, câmera
com trajeto ou são de GIF.

Atenção nessas contas: "coberta por inteiro" quer dizer que todos os tipos de objeto e efeito dela estão na lista,
não que o resultado fica idêntico ao Wallpaper Engine. As 77 ainda exigem camadas de composição em 6 cenas, som em
21, bloom em 6 e sistemas de partícula com filhos em 25.

## 5. Projetos abertos que leem esse formato

| Projeto | Licença | O que cobre | Estado |
|---|---|---|---|
| [RePKG](https://github.com/notscuffed/repkg) (C#) | **MIT** | Extrai pacote, converte `.tex` em imagem (DXT, R8, RG88, LZ4, PNG/JPG embutido, sprites, mp4) | Último commit em 03/2025. O layout bate com os arquivos dele (3.464 de 3.466) |
| [linux-wallpaperengine](https://github.com/Almamu/linux-wallpaperengine) (C++, OpenGL) | GPL-3.0 | Imagem, efeitos (traduz os shaders do WE para GLSL), partículas (os nomes da seção 4 quase todos), texto, som, parallax, SceneScript parcial (QuickJS), marionete só como malha parada | Muito ativo (10/2026). **Exige a pasta `assets` do WE** |
| [open-wallpaper-engine](https://github.com/waywallen/open-wallpaper-engine) (C++, Vulkan) | GPL-2.0 | Sucessor do backend de cena do plugin KDE; o README lista marionete completa, 3D, SceneScript e opções, sem dizer o que está pronto | Ativo (11/2026). Exige `assets` |
| [wallpaper-engine-kde-plugin](https://github.com/catsout/wallpaper-engine-kde-plugin) | GPL-2.0 | Imagem, efeitos, parallax, partículas, puppet warp; sem texto, 3D, script nem opções | Arquivado em 06/2026 |
| [fork do CaptSilver](https://github.com/CaptSilver/wallpaper-engine-kde-plugin/wiki/Scene-Renderer) | GPL-2.0 | Diz suportar texto, 3D, skinning de marionete, opções, câmera | Não conferi o código |
| [open-wallpaper-engine-mac](https://github.com/deepratna-awale/open-wallpaper-engine-mac) (Swift, Metal) | GPL-3.0 | Cena com SceneScript, partículas e marionete, segundo o README | Ativo. Exige `assets` |
| Lively Wallpaper | | Não suporta cena do WE (vídeo, web, shader, programa) | |

O que isso quer dizer para o Fundo Vivo (MIT):
- Só o RePKG é MIT. Dele dá para usar a descrição do formato do pacote e da textura, que é o que importa e já foi
  conferido aqui.
- Os renderizadores são GPL: servem como prova de que dá e como descrição de comportamento, mas **não se copia código
  deles** para um repositório MIT. Para ficar limpo, a implementação sai da leitura dos arquivos dele e da
  documentação oficial, não do código deles.
- **Todos os renderizadores sérios rodam os shaders do próprio usuário**, lidos da instalação do WE e do pacote na
  hora. Ninguém reimplementou os efeitos à mão.

## 6. Caminhos possíveis, com esforço e risco

As estimativas são minhas, em trabalho de agente com o Felipe testando, e são estimativas.

### (a1) Renderizador nativo de um subconjunto, com os efeitos reescritos à mão

No `vivo.js` (WebGL2, que já existe) ou num runtime irmão: ler o pacote e as texturas (o DXT entra direto como
textura comprimida, a extensão S3TC existe no WebView2 em PC; LZ4 em poucas linhas; PNG/JPG pelo navegador); camadas
com hierarquia, transformação, alfa, mistura, `colorBlendMode`, composição (desenhar numa textura o que está atrás);
parallax por camada (`parallaxDepth`); tremor de câmera; bloom; linha do tempo; som; partículas; e os efeitos, um por
um, pelos parâmetros e máscaras.

- Esforço: base (pacote, textura, camadas, composição, parallax, linha do tempo, som) 1 a 2 semanas; partículas 1 a 2
  semanas; cada efeito meio dia a dois dias com as variantes, então o top 10 leva mais 1 a 2 semanas. **Total de 4 a 6
  semanas para ~58 cenas (26%).**
- Risco: fidelidade. Sem o shader original, cada efeito é uma imitação, e partícula aleatória nunca bate igual.
  Manutenção: o WE muda versões de pacote e textura. Ganho de leveza pequeno, porque o vídeo já é leve.
- Ganha: parallax por camada de verdade, reação a áudio ao vivo, mouse, e nada de janela do WE piscando.

### (a2) Renderizador nativo que roda os shaders do WE lidos da instalação dele

Como o linux-wallpaperengine: ler na hora os shaders do pacote e os `#include` da pasta `assets` do WE dele,
pré-processar (combos, anotações, macros com cara de HLSL) e compilar para WebGL2. Nada do WE entra no repositório.

- Teto bem maior: 87 cenas sem script, 103 com um motor de SceneScript, 160 com texto.
- Esforço: 6 a 10 semanas só para chegar no nível das imagens e efeitos, porque traduzir o dialeto para GLSL ES 3.0
  sem geometry shader é trabalho pesado, e os efeitos de várias passadas usam alvos de renderização nomeados.
- Risco: depende do WE instalado (ele tem); zona cinzenta de licença. Ler o arquivo do usuário em tempo de execução
  não é copiar, mas não li os termos de uso do WE e não sei se proíbem isso. Rodar shader de autor do Workshop é rodar
  código de terceiros na GPU (risco baixo, mas existe).

### (b) Conversor de cena simples para `.vivo`

O `.vivo` de hoje tem efeitos só sobre a tela inteira, 4 tipos de partícula prontos e nenhuma máscara, distorção,
hierarquia ou sprite. Com ele como está, o conversor só pegaria cenas de imagem parada e partículas aproximadas pelos
presets: perto de 20 cenas, e as partículas sairiam diferentes. Não compensa sozinho.

**Mas (b) é a arquitetura certa para fazer (a):** um `.vivo` versão 2 com efeitos por camada, máscaras, sprites,
quadros-chave e um sistema de partículas genérico, e um importador que transforma a cena nesse JSON. Aí a cena ganha o
que o `.vivo` já tem: renderizar quadro a quadro para vídeo, ao vivo com som, parallax por camada, a mesma pausa e o
mesmo fps. O que não se encaixa é o **loop perfeito**: partícula com movimento, arrasto e turbulência é simulação com
estado, não fórmula do tempo. Para o vídeo dá para simular quadro a quadro com semente fixa, mas o fim continua não
batendo com o começo, então a emenda volta a ser por mistura, como hoje.

Prova sugerida antes de decidir (1 semana): importar 5 cenas da base de 77 (uma sem efeito, uma com waterwaves, uma
com shake, uma com partículas, uma com composição) e comparar quadro a quadro com a gravação que o app já faz.

### Comparação

| | (a1) efeitos à mão | (a2) shaders do WE | (b) para o `.vivo` de hoje | Vitórias pequenas |
|---|---|---|---|---|
| Cenas | ~58 (top 10) a 77 | 87 a 160 | ~20, aproximadas | todas as convertidas melhoram; 18 de sprite sem WE |
| Esforço | 4 a 6 semanas | 6 a 10 semanas ou mais | 1 a 2 semanas | 2 a 5 dias |
| Fidelidade | parecida | quase igual | baixa | igual à de hoje ou melhor |
| Risco | médio | alto (dialeto, licença) | baixo, pouco ganho | baixo |

## 7. Melhorias na conversão que existe hoje

### 7.1 O que já está certo

O jeito de hoje (o WE desenha, o app grava) dá fidelidade total e funciona para as 212 cenas com `scene.pkg`. A
classificação atual (parallax, som, mouse/relógio) bate com o que medi: os scripts ficam no `scene.json`, e o app já
procura neles.

### 7.2 Números que corrigem o que o app supõe

- O app procura scripts em entradas `.js` do pacote: **não existe nenhuma** nas 224 cenas. Não quebra nada (o regex
  também roda no `scene.json`), mas a leitura dos `.js` é código morto.
- A cor do tema (`schemecolor`) existe em todas; 140 cenas têm opções de verdade além dela.

### 7.3 Vitórias pequenas, em ordem de valor

1. **Cenas de sprite convertidas sem o WE, em loop perfeito.** As 12 cenas de GIF são uma camada com uma textura
   animada: 40 a 48 quadros de 1280×800 a 1920×1200, numa folha de até 7680×7560 em RGBA com LZ4 (às vezes partida
   em 2 imagens), e o tempo de cada quadro. Somados, dão loops de **1,29 a 4,8 s** (medido em 11 delas; a 12ª usa uma
   folha mais antiga). Outras ~6 cenas da base de 77 também são uma imagem animada só. Ler a folha, recortar os
   quadros e mandar para o ffmpeg dá o vídeo **exato**, sem janela, sem gravação, sem emenda, em segundos. 71 cenas
   têm alguma textura animada.
2. **Ler as cenas de GIF.** `SceneProject.ReadSceneText` e `UnpackTo` só abrem `scene.pkg`. Pelo código, a conversão
   delas deve falhar: o `UnpackTo` copia o `gifscene.pkg` sem abrir e depois tenta ler `gifscene.json` solto na pasta
   (que não existe) para desligar o parallax. O `CLAUDE.md` diz que elas convertem, então **isso é suspeita pela
   leitura do código, não testei**. Trocar para "o pacote com o nome do `file` do `project.json`" resolve as duas coisas.
3. **Opções da cena antes de converter.** O app já tem o painel de opções do autor para os web. Para cena, antes de
   gravar: mostrar as opções e gravar os valores escolhidos no `project.json` da cópia desempacotada (o app já mexe
   no `scene.json` da cópia para desligar o parallax). Também existe `-control applyProperties -properties
   RAW~({...})~END -location <janela>`, documentado. Suposição a testar: o WE usa os valores do `project.json` da cópia
   quando não tem nada salvo para aquele caminho. Serve para escolher cor, ligar e desligar camada, esconder relógio
   (que hoje fica congelado no vídeo).
4. **Som da cena.** 104 cenas têm camada de som (música ou ambiente em mp3/ogg dentro do pacote), e o vídeo sai com
   `-an`. O app pode tirar o áudio do pacote e pôr no vídeo como faixa, ou tocar à parte, usando o controle de volume
   que o vídeo já tem (começando mudo, como hoje). Fica de fora o modo aleatório (`playbackmode` com `mintime` e
   `maxtime`). Detalhe: hoje, durante a gravação, esse som provavelmente toca no PC dele; não conferi.
5. **Duração do loop sugerida.** 52 cenas têm linha do tempo com duração declarada (`length / fps`, a mais comum de
   2 s, depois 1, 5, 0,5 e 3 s; 559 em `loop` e 48 em `mirror`). Quando tudo o que se mexe tem período conhecido, o
   app pode sugerir o mínimo múltiplo comum dentro de 10 a 60 s, e a emenda fica invisível de verdade nessas partes.
   Efeitos com `g_Time` e partículas continuam precisando da mistura.
6. **Gravar no tamanho certo.** A documentação lista a opção `borderless` no `openWallpaper`. Janela sem borda (popup)
   não costuma ter o limite de altura da área de trabalho que hoje encolhe a gravação no monitor em pé e na TV.
   Inferência, não testei.
7. **Classificação melhor**, para a tag dizer o que vai perder: "Tem relógio" separado de "Mouse" (98 cenas com
   relógio contra 46 com mouse), "Marionete", "Som de fundo" e uma tag "Dá para converter sem o WE" para as de sprite.

## 8. O que o Wallpaper Engine tem e valeria trazer (bônus)

Em ordem de valor para quem usa só no próprio PC (ultrawide 3440×1440, monitor em pé 1080×1920, TV 4K, RTX 3060):

1. **Playlist por monitor**: trocar de wallpaper a cada X minutos, por hora do dia, aleatório ou em ordem, com
   transição. Com mais de 300 wallpapers na biblioteca, é o que mais muda o uso no dia a dia. Esforço médio.
2. **GIF animado de verdade**: hoje mostra só o primeiro quadro. Converter para vídeo na hora com o `VideoConverter`
   que já existe. Esforço pequeno.
3. **Regras por programa**: pausar, ou tirar da memória, quando um programa escolhido está aberto ou em foco (After
   Effects, Premiere, ComfyUI, que disputam a 3060), e o contrário (continuar com o Discord na frente). O app já tem a
   pausa por tela cheia; isto é uma lista em cima dela. Esforço pequeno a médio.
4. **Comandos de linha** (`FundoVivo.exe --pause`, `--next`, `--set <monitor> <item>`), para atalho de teclado ou
   Stream Deck, como o `-control` do WE. Esforço pequeno (o motor já recebe comandos por `WM_COPYDATA`).
5. **Opções das cenas** (7.3, item 3) e **som das cenas** (7.3, item 4).
6. **Editor de `.vivo`** com prévia, no lugar de escrever JSON. O WE tem editor de cena. Esforço grande; só faz
   sentido se ele for criar wallpapers próprios.
7. Menos importante aqui: wallpaper esticado por vários monitores (com um em pé e um ultrawide fica ruim), protetor
   de tela, ajuste de brilho e cor por wallpaper.

## 9. Confirmado, inferência e incerto

**Confirmado** (nos arquivos dele, no executável ou em fonte primária):
- O layout do pacote nas 224 cenas, e o nome do pacote seguindo o `file` (`gifscene.pkg` nas 12 de GIF).
- O layout das texturas em 3.464 de 3.466, LZ4 em 302 blocos, e os formatos e contagens da tabela 3.3.
- Que os shaders dos efeitos vêm no pacote, e que os `#include`, os shaders base, as texturas `util/` e os modelos
  `models/util/` vêm da instalação do WE.
- Todas as contagens das seções 4 e 7, saídas do script.
- Que o WE usa Direct3D 11, DirectWrite e Media Foundation (importações do executável, pelo REA); as strings das
  versões de formato e dos comandos de linha; `applyProperties` e `borderless` na documentação oficial.
- Licenças e estado dos projetos da seção 5 (lidos no GitHub por um subagente; o fork do CaptSilver e o
  open-wallpaper-engine-mac só pelo README e pela wiki).

**Inferência**:
- Que o WE traduz e compila os shaders na hora com o `d3dcompiler`.
- Que a conversão das cenas de GIF falha hoje (só leitura do código).
- Que `borderless` tira o limite de altura, e que editar o `project.json` da cópia aplica as opções.
- As estimativas de esforço e a fidelidade de cada caminho.
- A classificação "coberta por inteiro" usa os nomes dos efeitos e dos tipos de objeto, não um teste desenhando.

**Incerto ou não verificado**:
- A variante `TEXB0004` com condição (2 texturas numa cena) não foi decifrada inteira.
- O que significam as texturas marcadas como vídeo (39) não foi aberto.
- O formato `.mdl` das marionetes (versões 13 a 23) não foi estudado além do cabeçalho.
- Os termos de uso do WE sobre ler os arquivos dele por outro programa: não li.
- O REA ficou sem descompilador (sem Ghidra configurado), então nada foi descompilado. Não fez falta: os arquivos
  responderam quase tudo.

## 10. Fontes

- Arquivos do PC dele: `C:\Program Files (x86)\Steam\steamapps\workshop\content\431960\*` (224 cenas) e
  `C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\assets` (versão 2.8.42), lidos sem alterar nada.
- Scripts descartáveis (fora do projeto): `wepkg.py`, `stats.py`, `aggregate.py` e `texwalk.py` na pasta de rascunho
  da sessão, e o resultado em `stats.json`.
- REA: `inspect_pe_imports` em `wallpaper64.exe` (SHA-256 `40e2ce02...93b0`, evidência
  `ev_1061cf9a7e08f725bacdd26f38b4ed81ef857cd2d175603d537a57d3b142eaff`).
- Documentação oficial: [linha de comando](https://help.wallpaperengine.io/en/functionality/cli.html),
  [partículas](https://docs.wallpaperengine.io/en/scene/particles/introduction.html),
  [SceneScript](https://docs.wallpaperengine.io/en/scene/scenescript/reference.html).
- Projetos: [RePKG](https://github.com/notscuffed/repkg),
  [linux-wallpaperengine](https://github.com/Almamu/linux-wallpaperengine),
  [open-wallpaper-engine](https://github.com/waywallen/open-wallpaper-engine),
  [wallpaper-engine-kde-plugin](https://github.com/catsout/wallpaper-engine-kde-plugin),
  [wiki do CaptSilver](https://github.com/CaptSilver/wallpaper-engine-kde-plugin/wiki/Scene-Renderer),
  [open-wallpaper-engine-mac](https://github.com/deepratna-awale/open-wallpaper-engine-mac).
