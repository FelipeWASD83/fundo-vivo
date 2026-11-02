# Publica a versão atual do Fundo Vivo como release no GitHub (é ela que o botão "Procurar atualização" baixa).
# Antes: subir a versão no src\FundoVivo\FundoVivo.csproj e no CHANGELOG.md, commitar e dar push.
# Precisa do gh logado (gh auth login) com permissão no repositório. Publicar é decisão do Felipe.
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
$repo = 'FelipeWASD83/fundo-vivo'

function Rodar([string]$exe, [string[]]$argumentos) {
    $ErrorActionPreference = 'Continue'
    $saida = & $exe @argumentos 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ codigo = $LASTEXITCODE; saida = (($saida -join "`n").Trim()) }
}

$csproj = Get-Content (Join-Path $raiz 'src\FundoVivo\FundoVivo.csproj') -Raw -Encoding UTF8
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw 'Não achei <Version> no FundoVivo.csproj.' }
$versao = $Matches[1]
$changelog = Get-Content (Join-Path $raiz 'CHANGELOG.md') -Raw -Encoding UTF8
if ($changelog -notmatch "## $([regex]::Escape($versao)) ") { throw "O CHANGELOG.md não tem a seção da versão $versao." }

if ((Rodar git @('status', '--porcelain')).saida) { throw 'Há mudanças não commitadas. Commite antes de publicar.' }
$r = Rodar git @('fetch', '-q', 'origin')
if ($r.codigo -ne 0) { throw "Não deu para falar com o GitHub (git fetch): $($r.saida)" }
$local = (Rodar git @('rev-parse', 'HEAD')).saida
$r = Rodar git @('rev-parse', '@{u}')
if ($r.codigo -ne 0) { throw 'Este ramo não está ligado ao GitHub. Rode: git push -u origin main' }
if ($local -ne $r.saida) { throw 'O commit atual ainda não foi enviado (git push) ou está atrás do GitHub.' }
if ((Rodar gh @('release', 'view', "v$versao", '--repo', $repo)).codigo -eq 0) { throw "A release v$versao já existe." }

# Compila do zero (com o carimbo contra o Smart App Control) e empacota só a pasta do programa.
& (Join-Path $PSScriptRoot 'compilar.ps1')
$tmp = Join-Path $env:TEMP "fundo-vivo-release-$versao"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force $tmp | Out-Null
$zip = Join-Path $tmp "fundo-vivo-$versao.zip"
Compress-Archive -Path (Join-Path $raiz 'build\publish\*') -DestinationPath $zip

$linhas = $changelog -split "`r?`n"
$inicio = [array]::IndexOf($linhas, ($linhas | Where-Object { $_ -match "^## $([regex]::Escape($versao)) " } | Select-Object -First 1))
$notas = @()
for ($i = $inicio + 1; $i -lt $linhas.Length; $i++) { if ($linhas[$i] -match '^## ') { break }; $notas += $linhas[$i] }
$arqNotas = Join-Path $tmp 'notas.md'
[IO.File]::WriteAllText($arqNotas, (($notas -join "`n").Trim()), [Text.UTF8Encoding]::new($false))

Write-Host "Publicando v$versao em $repo com $zip"
$r = Rodar gh @('release', 'create', "v$versao", $zip, '--repo', $repo, '--title', "Fundo Vivo $versao", '--notes-file', $arqNotas, '--target', $local)
if ($r.codigo -ne 0) { throw "O gh não criou a release: $($r.saida)" }
Write-Host $r.saida
Write-Host 'Pronto. O botão de atualizar já enxerga esta versão.'
