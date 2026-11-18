<#
.SYNOPSIS
  Compara una xarxa NNUE nova (opcio EvalFile) amb la incrustada, amb el mateix executable, amb cutechess-cli.

.EXAMPLE
  .\tools\match-xarxa.ps1 -Xarxa data\ilerda-256-b8-relabel.bin -Rounds 2500

.NOTES
  Els dos bàndols són el mateix .exe: "Nova" carrega la xarxa de -Xarxa i "Base" fa servir la incrustada.
  Obertures: tools\obertures.epd (2.000 posicions), 5+0,05, un fil i 64 MB de Hash per bàndol, com als matxs de la 0.15.0.
  Cada ronda són 2 partides (colors canviats): 2.500 rondes = 5.000 partides. No es fa servir SPRT: es juguen totes.
#>
param(
    [Parameter(Mandatory = $true)][string]$Xarxa,
    [string]$XarxaBase = "",   # si es dona, "Base" carrega aquesta xarxa en lloc de la incrustada (per comparar dues xarxes noves)
    [string]$Motor = "data\relabel\motor\Ilerda.exe",
    [string]$CutechessCli = "C:\Program Files (x86)\Cute Chess\cutechess-cli.exe",
    [string]$Tc = "5+0.05",
    [int]$Rounds = 2500,
    [int]$Concurrency = 10,
    [string]$Sortida = "tools\resultat-xarxa.pgn"
)
$root = Split-Path -Parent $PSScriptRoot
$motor = Join-Path $root $Motor
$xarxa = Resolve-Path (Join-Path $root $Xarxa) | Select-Object -ExpandProperty Path
$openings = Join-Path $root "tools\obertures.epd"
foreach ($f in @($motor, $xarxa, $CutechessCli, $openings)) {
    if (-not (Test-Path $f)) { Write-Error "No trobo: $f"; exit 1 }
}
$baseOpt = @()
if ($XarxaBase) { $baseOpt = @("option.EvalFile=" + (Resolve-Path (Join-Path $root $XarxaBase)).Path) }
& $CutechessCli `
    -engine name=Nova cmd="$motor" proto=uci option.EvalFile="$xarxa" `
    -engine name=Base cmd="$motor" proto=uci @baseOpt `
    -each tc=$Tc `
    -openings file="$openings" format=epd order=random `
    -games 2 -rounds $Rounds -repeat -recover `
    -concurrency $Concurrency `
    -ratinginterval 50 `
    -pgnout (Join-Path $root $Sortida)
