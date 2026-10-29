<#
.SYNOPSIS
  Prova variants d'opcions UCI (LmrDiv, LmrStart, ...) d'una mateixa versió contra la configuracio per defecte.

.EXAMPLE
  .\tools\ajustar.ps1 -Versio 0.6.2 -Variants "LmrDiv=175","LmrDiv=275","LmrStart=4","LmrStart=6"

.NOTES
  Cada variant juga contra el mateix executable amb la configuracio per defecte, a un control mes
  rapid que match.ps1 per poder jugar mes partides. Amb 200 partides el marge d'error es d'uns
  +-40 Elo: nomes es veuen diferencies grans. Una variant s'ha de confirmar amb match.ps1 abans d'adoptar-la.
#>
param(
    [Parameter(Mandatory = $true)][string]$Versio,
    [Parameter(Mandatory = $true)][string[]]$Variants,
    [string]$CutechessCli = "C:\Program Files (x86)\Cute Chess\cutechess-cli.exe",
    [string]$Tc = "5+0.05",
    [int]$Rounds = 100,            # cada ronda son 2 partides (colors canviats)
    [int]$Concurrency = 5
)

$root = Split-Path -Parent $PSScriptRoot
$openings = Join-Path $root "tools\openings.pgn"
$motor = if (Test-Path $Versio) { $Versio } else { Join-Path $root "Versions\Ilerda-$Versio\Ilerda.exe" }
foreach ($f in @($motor, $CutechessCli, $openings)) {
    if (-not (Test-Path $f)) { Write-Error "No trobo: $f"; exit 1 }
}

foreach ($v in $Variants) {
    $nom, $valor = $v -split "=", 2
    Write-Host "== $v contra la configuracio per defecte =="
    $out = & $CutechessCli `
        -engine name=Variant cmd="$motor" proto=uci option.$nom=$valor `
        -engine name=Base cmd="$motor" proto=uci `
        -each tc=$Tc `
        -openings file="$openings" format=pgn order=random plies=8 `
        -games 2 -rounds $Rounds -repeat -recover `
        -concurrency $Concurrency 2>&1 | Tee-Object -FilePath (Join-Path $env:TEMP "ajustar-$nom-$valor.log")

    $linia = $out | Select-String "Score of Variant vs Base: (\d+) - (\d+) - (\d+)" | Select-Object -Last 1
    if (-not $linia) { Write-Warning "Sense resultat per a $v"; continue }
    $g = [int]$linia.Matches[0].Groups[1].Value
    $p = [int]$linia.Matches[0].Groups[2].Value
    $t = [int]$linia.Matches[0].Groups[3].Value
    $s = ($g + 0.5 * $t) / ($g + $p + $t)
    $sc = [math]::Min([math]::Max($s, 0.03), 0.97)
    $elo = 400 * [math]::Log10($sc / (1 - $sc))
    Write-Host ("   +{0} ={1} -{2}  ({3:P1})  -> {4:+0;-0} Elo respecte a la base" -f $g, $t, $p, $s, $elo)
}
