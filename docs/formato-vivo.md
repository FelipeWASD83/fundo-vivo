# Formato `.vivo` (versão 1)

O `.vivo` é o formato próprio de wallpaper do Fundo Vivo: um arquivo zip com um `wallpaper.json` e os arquivos que
ele usa. É **dado, não código**: o JSON descreve camadas, efeitos e animações, e quem desenha é o runtime do app (hoje
WebGL2 no WebView2). Trocar de runtime depois não quebra wallpaper nenhum.

O esquema que valida o JSON está em [`schema/vivo.schema.json`](../schema/vivo.schema.json). Os exemplos ficam em
[`examples/`](../examples/), um por pasta; o `ferramentas\compilar.ps1` empacota cada pasta num `.vivo` e eles
aparecem na Biblioteca (filtro **Vivo**). As imagens dos exemplos saem do `ferramentas\make_example_assets.py`.

Para criar um: monte uma pasta com o `wallpaper.json` e as imagens, selecione tudo dentro dela e use **Enviar para >
Pasta compactada**; renomeie o `.zip` para `.vivo` e adicione pela Biblioteca (**Adicionar arquivo**).

## Conteúdo do zip

```
wallpaper.json      obrigatório
preview.png         opcional (a miniatura da Biblioteca)
assets/...          imagens das camadas (png, jpg, webp)
```

## `wallpaper.json`

```json
{
  "format": "fundo-vivo",
  "version": 1,
  "title": "Noite de chuva",
  "author": "Felipe",
  "tags": ["Chuva", "Noite"],
  "loop": 20,
  "background": "#0b1020",
  "parallax": 0.03,
  "preview": "preview.png",
  "layers": [ ... ],
  "effects": [ ... ]
}
```

| Campo | O que é | Padrão |
|---|---|---|
| `format`, `version` | sempre `"fundo-vivo"` e `1` | obrigatórios |
| `title`, `author`, `tags` | o que a Biblioteca mostra; as tags entram no filtro | nome do arquivo, vazio |
| `loop` | duração do loop em segundos (1 a 120). Toda animação se repete dentro dele | 20 |
| `background` | cor de fundo (`#rgb`, `#rrggbb` ou `#rrggbbaa`) | `#000000` |
| `parallax` | quanto as camadas andam com o mouse, em fração do lado menor da tela (0 desliga; 0,02 a 0,06 é sutil) | 0 |
| `layers` | as camadas, desenhadas na ordem (a primeira fica atrás) | obrigatório |
| `effects` | efeitos sobre a imagem inteira, aplicados na ordem | nenhum |

## Medidas e encaixe (o mesmo wallpaper em 3440×1440, 1080×1920 e 3840×2160)

Toda medida é uma **fração do lado menor da tela** (`s`): na ultrawide, `s` = 1440; no monitor em pé, `s` = 1080.
Assim um círculo de `"size": 0.2` tem o mesmo tamanho relativo em qualquer proporção, e o que muda é só quanto espaço
sobra dos lados.

Cada camada tem uma **âncora** e um **deslocamento**:

- `anchor`: `center`, `top`, `bottom`, `left`, `right`, `top-left`, `top-right`, `bottom-left`, `bottom-right`. É o
  ponto da tela a que a camada se prende. Uma lua com `"anchor": "top-right"` fica no canto de cima à direita em
  qualquer monitor.
- `x`, `y`: deslocamento a partir da âncora, em frações de `s` (positivo vai para a direita e para baixo).

Imagens têm um **encaixe** (`fit`); gradientes e partículas sempre cobrem a tela toda:

| `fit` | O que faz |
|---|---|
| `cover` | cobre a tela toda, cortando o que sobra (o padrão de imagem e gradiente) |
| `contain` | cabe inteira na tela |
| `width` / `height` | ocupa a largura / altura toda, mantendo a proporção |
| `none` | altura = `size` × `s`; largura pela proporção da imagem |

Com `fit` diferente de `cover`, a imagem se prende à âncora pelo mesmo ponto: com `bottom`, a base da imagem encosta na
base da tela (montanhas, chão); com `top-left`, o canto dela no canto da tela. A borda presa continua para fora da tela
(a última linha da imagem se repete), então o parallax e o pan nunca abrem um buraco embaixo das montanhas: deixe a
última linha da imagem lisa, sem detalhe.

Uma camada presa pela altura (`fit: none` com `size` em `s`) precisa de uma imagem bem larga para cobrir um 21:9: no
ultrawide a tela tem 2,4 `s` de largura. Os exemplos usam imagens de 6400 px de largura por isso.

## Camadas

Comum a todas: `type`, `name` (opcional), `anchor`, `x`, `y`, `opacity` (0 a 1), `blend` (`normal`, `add`, `screen`,
`multiply`), `depth` (0 a 1: quanto a camada anda no parallax; o fundo perto de 0, o que está perto perto de 1),
`rotation` (graus), `scale` e `animate`.

- **`image`**: `src` (caminho dentro do zip), `fit`, `size` (com `fit: none`), `tint` (cor multiplicada).
- **`gradient`**: preenche a tela. `kind` (`linear` ou `radial`), `angle` (graus, linear; 90 = de cima para baixo),
  `center` (`[x, y]` em frações da tela, radial), `radius` (em `s`, radial), `stops` (`[[posição, cor], ...]`, até 8).
- **`shape`**: o centro fica na âncora mais o deslocamento. `shape` (`circle`, `ring`, `rect`), `color`, `size`
  (diâmetro ou largura, em `s`), `height` (só `rect`), `thickness` (só `ring`, em `s`), `softness` (0 a 1: borda
  esfumada, que vira brilho).
- **`particles`**: cobrem a tela toda. `preset` (`rain`, `snow`, `dust`, `fireflies`), `density` (1 = quantidade
  normal para a área de uma tela 1920×1080; escala com a área), `color`, `size` (em `s`), `speed` (1 = normal),
  `angle` (inclinação da chuva, em graus), `seed` (número: mesma semente, mesmo desenho).

## Animações (`animate`)

Uma lista de animações por propriedade, todas em **loop**, nunca em linha do tempo:

```json
"animate": [
  { "property": "y", "type": "oscillate", "amount": 0.01, "period": 10 },
  { "property": "rotation", "type": "spin", "period": 20 },
  { "property": "opacity", "type": "noise", "amount": 0.2, "period": 5 },
  { "property": "scale", "type": "audio", "band": "bass", "amount": 0.15 }
]
```

| `type` | O que faz |
|---|---|
| `oscillate` | soma `amount × seno(tempo / period + phase)` |
| `spin` | gira `amount` voltas a cada `period` (padrão 1 volta); só faz sentido em `rotation` |
| `noise` | soma um ruído suave de amplitude `amount`, que se repete a cada `period` |
| `audio` | soma `amount × nível` da faixa `band` (`bass`, `mid`, `treble`, `all`) do som do PC |

`property` pode ser `x`, `y` (em `s`), `scale`, `rotation` (graus) ou `opacity`. `phase` (0 a 1) desloca o começo.

## Loop perfeito

O wallpaper declara `loop`, e **todo período precisa dividir o loop**: com `loop: 20`, os períodos 20, 10, 5 e 4
fecham certinho. Se um período não divide (7 num loop de 20), o runtime arredonda para o mais próximo que divide (6,67,
três repetições) e avisa no registro. As partículas são calculadas a partir do tempo com a semente fixa: cada gota cai
um número inteiro de vezes por loop, e o quadro do fim é o mesmo do começo (é o mesmo que simular com pré-aquecimento de
um ciclo inteiro, sem guardar estado). Ao vivo, o grão muda a cada 1/24 s e a sequência também se repete no loop;
no vídeo renderizado ele fica parado (ruído que muda em todo pixel a cada quadro multiplicava por sete o tamanho do
vídeo). `spin` sempre dá um número inteiro de voltas por loop.

## Efeitos (`effects`)

| `type` | Campos | O que faz |
|---|---|---|
| `blur` | `radius` (em `s`) | desfoca tudo |
| `grade` | `brightness` (-1 a 1), `contrast`, `saturation` (1 = normal), `hue` (graus), `tint`, `tintAmount` (0 a 1) | correção de cor |
| `grain` | `amount` (0 a 0,3) | grão de filme |
| `glow` | `threshold` (0 a 1), `strength`, `radius` (em `s`) | o que é claro brilha em volta |
| `rays` | `anchor`, `x`, `y` (como uma camada: âncora mais deslocamento em `s`), `threshold`, `strength`, `length` (0 a 1) | raios de luz saindo de um ponto (o que passa do `threshold` vira raio) |

## Ao vivo ou vídeo

Ao pôr um `.vivo` num monitor, o Fundo Vivo decide sozinho:

- **Não usa nada que dependa do momento** (hoje, só a animação `audio` depende): o loop é **renderizado quadro a
  quadro** no tamanho daquele monitor e toca como vídeo, gastando o que um vídeo gasta. Com `parallax`, o vídeo sai um
  pouco maior e anda com o mouse (o mesmo esquema das cenas: o vídeo inteiro anda junto, sem a profundidade de cada
  camada).
- **Reage ao som**: toca **ao vivo** no runtime, no limite de fps dos Ajustes, com a pausa de sempre e o parallax por
  camada de verdade.

A renderização roda na fila das conversões (a mesma barra de progresso, dá para cancelar), no fps dos Ajustes até 60,
com a sobra do pan igual ao `parallax` (de 2% a 15% de cada lado). Leva poucos segundos: 1200 quadros de 3646×1526
saíram em 15,6 s aqui. Mudou o `.vivo`? Ele é renderizado de novo no próximo clique.

## Testar um wallpaper sem pôr na tela

```powershell
app\FundoVivo.exe --vivo-still meu.vivo quadro.png 3440 1440 5
```

Desenha o quadro do segundo 5 em 3440×1440 com o mesmo runtime que faz o vídeo (um sexto número põe a sobra do pan).
Erros e avisos (período que não divide o loop, imagem que não abriu) vão para `%LOCALAPPDATA%\FundoVivo\logs\scenes.log`.
