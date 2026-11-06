<#
.SYNOPSIS
  Compara dues versions del motor amb cutechess-cli i un test SPRT.

.EXAMPLE
  # Versions guardades a Versions\ (vegeu tools\guardar-versio.ps1): n'hi ha prou amb el numero
  .\tools\match.ps1 -Nou 0.3.0 -Vell 0.2.0

.EXAMPLE
  # Tambe es poden passar rutes a un .exe
  .\tools\match.ps1 -Nou "src\Ilerda\bin\Release\net8.0\Ilerda.exe" -Vell 0.2.0

.NOTES
  cutechess-cli.exe ve dins el paquet de Cute Chess per a Windows; passa'n la ruta
  amb -CutechessCli si no es al PATH.
#>
param(
    [Parameter(Mandatory = $true)][string]$Nou,
    [Parameter(Mandatory = $true)][string]$Vell,
    [string]$CutechessCli = "cutechess-cli.exe",
    [string]$Tc = "10+0.1",        # temps per partida: segons + increment
    [int]$Rounds = 50,             # cada ronda son 2 partides (colors canviats)
    [int]$Concurrency = 2,         # partides simultanies (no superis els nuclis de la CPU)
    [double]$Elo0 = 0,             # SPRT: hipotesi nul.la (no hi ha millora)
    [double]$Elo1 = 10,            # SPRT: millora que volem detectar
    [string]$Openings = ""         # per defecte tools\obertures.epd (2.000 posicions); .pgn o .epd
)

$root = Split-Path -Parent $PSScriptRoot
$openings = if ($Openings) { $Openings } else { Join-Path $root "tools\obertures.epd" }
# EPD: cada linia ja es la posicio d'inici. PGN: es juguen les 8 primeres semijugades.
$openFmt = if ($openings -like "*.epd") { ,"format=epd" } else { "format=pgn", "plies=8" }
$out = Join-Path $root "tools\resultat.pgn"

# Si el valor no es un fitxer, es busca com a versio guardada: Versions\Ilerda-<valor>\Ilerda.exe
function Resolve-Motor([string]$valor) {
    if (Test-Path $valor) { return $valor }
    $guardat = Join-Path $root "Versions\Ilerda-$valor\Ilerda.exe"
    if (Test-Path $guardat) { return $guardat }
    return $valor
}
$Nou = Resolve-Motor $Nou
$Vell = Resolve-Motor $Vell

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
    -openings file="$openings" $openFmt order=random `
    -games 2 -rounds $Rounds -repeat -recover `
    -concurrency $Concurrency `
    -sprt elo0=$Elo0 elo1=$Elo1 alpha=0.05 beta=0.05 `
    -ratinginterval 10 `
    -pgnout "$out"
