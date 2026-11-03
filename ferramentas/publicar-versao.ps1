# Publica a versão atual do Fundo Vivo como release no GitHub (é ela que o botão "Procurar atualização" baixa).
# Mesmo formato do clave, do relogio-mond e do ilha-cursors: versao.json com "versao" e "repositorio", pacote
# fundo-vivo-<versão>.zip, release vX.Y.Z com o título "Fundo Vivo X.Y.Z" e as notas da seção do CHANGELOG.
# Antes: subir a versão no versao.json, no src\FundoVivo\FundoVivo.csproj e no topo do CHANGELOG.md (os três juntos),
# commitar e dar push. Precisa do gh logado (gh auth login). Publicar é decisão do Felipe.
#
# -SoGerar [pasta]: não fala com o GitHub. Compila, empacota fundo-vivo-<versão>.zip na pasta (padrão build\release,
# que é onde o setup do pacote-windows-bonito procura), com as notas e o SHA-256 ao lado, desempacota numa pasta à
# parte e confere que o Windows deixa essa cópia rodar. É o mesmo pacote que iria para a release.
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File .\ferramentas\publicar-versao.ps1
#   powershell -ExecutionPolicy Bypass -File .\ferramentas\publicar-versao.ps1 -SoGerar [<pasta>]
param([switch]$SoGerar, [Parameter(Position = 0)][string]$Pasta = '')
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

function Rodar([string]$exe, [string[]]$argumentos) {
    $ErrorActionPreference = 'Continue'
    $saida = & $exe @argumentos 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ codigo = $LASTEXITCODE; saida = (($saida -join "`n").Trim()) }
}

$info = [IO.File]::ReadAllText((Join-Path $raiz 'versao.json')) | ConvertFrom-Json
$versao = $info.versao
$repo = $info.repositorio
# a versão mora em três lugares e eles andam juntos
$csproj = [IO.File]::ReadAllText((Join-Path $raiz 'src\FundoVivo\FundoVivo.csproj'))
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw 'Não achei <Version> no FundoVivo.csproj.' }
if ($Matches[1] -ne $versao) { throw "O versao.json diz $versao e o FundoVivo.csproj diz $($Matches[1])." }
$changelog = [IO.File]::ReadAllText((Join-Path $raiz 'CHANGELOG.md'))
$topo = ($changelog -split "`r?`n") | Where-Object { $_ -match '^## ' } | Select-Object -First 1
if ($topo -notmatch "^## $([regex]::Escape($versao)) ") { throw "O topo do CHANGELOG.md não é a versão $versao ($topo)." }

if ((Rodar git @('status', '--porcelain')).saida) { throw 'Há mudanças não commitadas. Commite antes de publicar.' }
$local = (Rodar git @('rev-parse', 'HEAD')).saida
if (-not $SoGerar) {
    $r = Rodar git @('fetch', '-q', 'origin')
    if ($r.codigo -ne 0) { throw "Não deu para falar com o GitHub (git fetch): $($r.saida)" }
    $r = Rodar git @('rev-parse', '@{u}')
    if ($r.codigo -ne 0) { throw 'Este ramo não está ligado ao GitHub. Rode: git push -u origin main' }
    if ($local -ne $r.saida) { throw 'O commit atual ainda não foi enviado (git push) ou está atrás do GitHub.' }
    if ((Rodar gh @('release', 'view', "v$versao", '--repo', $repo)).codigo -eq 0) { throw "A release v$versao já existe." }
}

# Compila do zero (com o carimbo contra o Smart App Control) e empacota só a pasta do programa.
& (Join-Path $PSScriptRoot 'compilar.ps1')
# -SoGerar deixa os pacotes das outras versões onde estão (o instalador do pack pode apontar para um deles).
$tmp = if (-not $SoGerar) { Join-Path $env:TEMP "fundo-vivo-release-$versao" } elseif ($Pasta) { [IO.Path]::GetFullPath($Pasta) } else { Join-Path $raiz 'build\release' }
if (-not $SoGerar -and (Test-Path $tmp)) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force $tmp | Out-Null
$zip = Join-Path $tmp "fundo-vivo-$versao.zip"
# ZipFile do .NET grava os caminhos com barra normal, como o padrão do zip pede (o Compress-Archive do PowerShell 5.1
# grava com barra invertida).
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item $zip -Force }
# Sem os .pdb (símbolos de depuração), como os outros pacotes. O FundoVivo.exe fica na raiz do zip, que é onde o setup
# o procura (o atualizador aceita a raiz ou uma pasta, desde a 1.0.0). Entrada por entrada, sempre com barra normal.
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    $origem = Join-Path $raiz 'build\publish'
    $prefixo = (Resolve-Path $origem).Path.TrimEnd('\').Length + 1
    foreach ($arquivo in Get-ChildItem $origem -Recurse -File | Where-Object Extension -ne '.pdb') {
        $nome = $arquivo.FullName.Substring($prefixo).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $arquivo.FullName, $nome, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$linhas = $changelog -split "`r?`n"
$inicio = [array]::IndexOf($linhas, ($linhas | Where-Object { $_ -match "^## $([regex]::Escape($versao)) " } | Select-Object -First 1))
$notas = @()
for ($i = $inicio + 1; $i -lt $linhas.Length; $i++) { if ($linhas[$i] -match '^## ') { break }; $notas += $linhas[$i] }
$arqNotas = Join-Path $tmp $(if ($SoGerar) { "notas-$versao.md" } else { 'notas.md' })
[IO.File]::WriteAllText($arqNotas, (($notas -join "`n").Trim()), [Text.UTF8Encoding]::new($false))

if ($SoGerar) {
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $tmp "SHA256-$versao.txt"), "$hash  fundo-vivo-$versao.zip`n", [Text.UTF8Encoding]::new($false))
    # A prova: a cópia que sai do zip abre e o Windows deixa carregar cada DLL dela.
    $teste = Join-Path $tmp 'conferencia'
    if (Test-Path $teste) { Remove-Item $teste -Recurse -Force }
    Expand-Archive $zip -DestinationPath $teste
    $p = Start-Process (Join-Path $teste 'FundoVivo.exe') -ArgumentList '--check-load' -PassThru -Wait -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { throw "A cópia do pacote não passou no --check-load (código $($p.ExitCode))." }
    $exemplos = (Get-ChildItem (Join-Path $teste 'examples') -Filter *.vivo).Count
    Remove-Item $teste -Recurse -Force
    Write-Host "Pacote pronto: $zip"
    Write-Host "SHA-256: $hash"
    Write-Host "Commit: $local · exemplos .vivo no pacote: $exemplos · a cópia desempacotada passou no --check-load."
    return
}

Write-Host "Publicando v$versao em $repo com $zip"
$r = Rodar gh @('release', 'create', "v$versao", $zip, '--repo', $repo, '--title', "Fundo Vivo $versao", '--notes-file', $arqNotas, '--target', $local)
if ($r.codigo -ne 0) { throw "O gh não criou a release: $($r.saida)" }
Write-Host $r.saida
Write-Host 'Pronto. O botão de atualizar já enxerga esta versão.'
