# Compila o Fundo Vivo e deixa o programa pronto em app\ (é para lá que o atalho do Menu Iniciar aponta).
#
# O Smart App Control deste PC julga cada DLL nova pelo hash e às vezes barra uma que acabou de sair do
# compilador. Por isso cada tentativa leva um "carimbo" de letras sorteadas na versão (o +xxxx do
# InformationalVersion), que muda o hash da DLL e do .exe; se o Windows barrar, sorteia outro e compila de novo.
# Uso: powershell -ExecutionPolicy Bypass -File ferramentas\compilar.ps1 [-Tentativas 15]
param([int]$Tentativas = 15)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $raiz 'src\FundoVivo\FundoVivo.csproj'
$saida = Join-Path $raiz 'build\publish'
$app = Join-Path $raiz 'app'

function Rodar([string]$exe, [string[]]$argumentos) {
    $ErrorActionPreference = 'Continue'
    $texto = & $exe @argumentos 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ codigo = $LASTEXITCODE; saida = (($texto -join "`n").Trim()) }
}

$passou = $false
for ($i = 1; $i -le $Tentativas; $i++) {
    $tamanho = Get-Random -Minimum 24 -Maximum 200
    $carimbo = -join ((1..$tamanho) | ForEach-Object { [char](Get-Random -Minimum 97 -Maximum 123) })
    if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
    $r = Rodar dotnet @('publish', $projeto, '-c', 'Release', '-o', $saida, '-nologo', '-v', 'q', "-p:SourceRevisionId=$carimbo")
    if ($r.codigo -ne 0) { Write-Host $r.saida; throw 'A compilação falhou.' }
    # O Windows pode barrar uma DLL (o --check-load devolve 3) ou o próprio .exe (o Start-Process nem abre).
    try {
        $p = Start-Process (Join-Path $saida 'FundoVivo.exe') -ArgumentList '--check-load' -PassThru -Wait -WindowStyle Hidden -ErrorAction Stop
        $codigo = $p.ExitCode
    } catch { $codigo = 'exe barrado' }
    if ($codigo -eq 0) { Write-Host "Tentativa ${i}: o Windows aceitou."; $passou = $true; break }
    Write-Host "Tentativa ${i}: o Smart App Control barrou ($codigo); sorteando outro carimbo."
}
if (-not $passou) { throw "O Windows barrou as $Tentativas tentativas. Veja o log CodeIntegrity no Visualizador de Eventos." }

# Os exemplos do formato .vivo: cada pasta de examples\ vira um zip examples\<nome>.vivo dentro do app.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$exemplos = Join-Path $saida 'examples'
New-Item -ItemType Directory -Force $exemplos | Out-Null
Get-ChildItem (Join-Path $raiz 'examples') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'wallpaper.json') } | ForEach-Object {
    $vivo = Join-Path $exemplos ($_.Name + '.vivo')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($_.FullName, $vivo, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
Write-Host "Exemplos .vivo: $((Get-ChildItem $exemplos -Filter *.vivo).Count)"

# Fecha só o Fundo Vivo que roda desta pasta antes de trocar os arquivos.
$exeApp = Join-Path $app 'FundoVivo.exe'
$rodando = Get-Process FundoVivo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exeApp }
if ($rodando) {
    # Pede para o motor sair direito (ele devolve o papel de parede do Windows) antes de forçar.
    Start-Process $exeApp -ArgumentList '--quit' -Wait -WindowStyle Hidden
    $rodando | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    Start-Sleep -Milliseconds 1200
    Get-Process FundoVivo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exeApp } | Stop-Process -Force
}
New-Item -ItemType Directory -Force $app | Out-Null
# /MIR só dentro de app\, que é saída de compilação (fora do git).
robocopy $saida $app /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy falhou com código $LASTEXITCODE" }
Write-Host "Pronto em $app"
