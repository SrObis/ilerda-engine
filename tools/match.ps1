<#
.SYNOPSIS
  Compara dues versions del motor amb cutechess-cli i un test SPRT.

.EXAMPLE
  .\tools\match.ps1 -Nou "src\Ilerda\bin\Release\net8.0\Ilerda.exe" -Vell "C:\Motors\Ilerda-v0.1\Ilerda.exe"

.NOTES
  Cal copiar la versio anterior a una carpeta apart ABANS de recompilar, perque
  "dotnet build" sobreescriu l'.exe. cutechess-cli.exe ve dins el paquet de Cute Chess
  per a Windows; passa'n la ruta amb -CutechessCli si no es al PATH.
#>
param(
    [Parameter(Mandatory = $true)][string]$Nou,
    [Parameter(Mandatory = $true)][string]$Vell,
    [string]$CutechessCli = "cutechess-cli.exe",
    [string]$Tc = "10+0.1",        # temps per partida: segons + increment
    [int]$Rounds = 50,             # cada ronda son 2 partides (colors canviats)
    [int]$Concurrency = 2,         # partides simultanies (no superis els nuclis de la CPU)
    [double]$Elo0 = 0,             # SPRT: hipotesi nul.la (no hi ha millora)
    [double]$Elo1 = 10             # SPRT: millora que volem detectar
)

$root = Split-Path -Parent $PSScriptRoot
$openings = Join-Path $root "tools\openings.pgn"
$out = Join-Path $root "tools\resultat.pgn"

foreach ($f in @($Nou, $Vell, $openings)) {
    if (-not (Test-Path $f)) { Write-Error "No trobo: $f"; exit 1 }
}
if (-not (Get-Command $CutechessCli -ErrorAction SilentlyContinue)) {
    Write-Error "No trobo cutechess-cli ($CutechessCli). Passa la ruta amb -CutechessCli."
    exit 1
}

& $CutechessCli `
    -engine name=Nou cmd="$Nou" proto=uci `
    -engine name=Vell cmd="$Vell" proto=uci `
    -each tc=$Tc `
    -openings file="$openings" format=pgn order=random plies=8 `
    -games 2 -rounds $Rounds -repeat -recover `
    -concurrency $Concurrency `
    -sprt elo0=$Elo0 elo1=$Elo1 alpha=0.05 beta=0.05 `
    -ratinginterval 10 `
    -pgnout "$out"
