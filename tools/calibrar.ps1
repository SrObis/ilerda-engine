<#
.SYNOPSIS
  Estima l'Elo d'una versio d'Ilerda jugant contra Stockfish amb la força limitada (UCI_Elo).

.EXAMPLE
  .\tools\calibrar.ps1 -Versio 0.4.0
  .\tools\calibrar.ps1 -Versio 0.4.0 -Nivells 1500,1800,2100 -Rounds 20

.NOTES
  L'Elo de Stockfish (UCI_Elo) es una referencia aproximada: esta calibrat per a un altre
  control de temps i per a una escala que no es exactament la de CCRL. Fes servir el
  resultat per comparar versions entre elles i per tenir un ordre de magnitud, no com a
  Elo oficial. Stockfish no admet UCI_Elo per sota de 1320.
#>
param(
    [Parameter(Mandatory = $true)][string]$Versio,
    [string]$Stockfish = "C:\Users\obis2\Downloads\stockfish-windows-x86-64-universal\stockfish\stockfish-windows-x86-64-universal.exe",
    [string]$CutechessCli = "C:\Program Files (x86)\Cute Chess\cutechess-cli.exe",
    [int[]]$Nivells = @(1320, 1600, 1900, 2200),
    [string]$Tc = "10+0.1",
    [int]$Rounds = 20,             # cada ronda son 2 partides (colors canviats)
    [int]$Concurrency = 3
)

$root = Split-Path -Parent $PSScriptRoot
$openings = Join-Path $root "tools\openings.pgn"
$motor = if (Test-Path $Versio) { $Versio } else { Join-Path $root "Versions\Ilerda-$Versio\Ilerda.exe" }

foreach ($f in @($motor, $Stockfish, $CutechessCli, $openings)) {
    if (-not (Test-Path $f)) { Write-Error "No trobo: $f"; exit 1 }
}

$punts = @()
foreach ($nivell in $Nivells) {
    Write-Host "== Stockfish UCI_Elo $nivell =="
    $out = & $CutechessCli `
        -engine name=Ilerda cmd="$motor" proto=uci `
        -engine name=SF cmd="$Stockfish" proto=uci option.UCI_LimitStrength=true option.UCI_Elo=$nivell option.Threads=1 option.Hash=16 `
        -each tc=$Tc `
        -openings file="$openings" format=pgn order=random plies=8 `
        -games 2 -rounds $Rounds -repeat -recover `
        -concurrency $Concurrency 2>&1 | Tee-Object -FilePath (Join-Path $env:TEMP "calibrar-$nivell.log")

    $linia = $out | Select-String "Score of Ilerda vs SF: (\d+) - (\d+) - (\d+)" | Select-Object -Last 1
    if (-not $linia) { Write-Warning "Sense resultat per al nivell $nivell"; continue }
    $g = [int]$linia.Matches[0].Groups[1].Value
    $p = [int]$linia.Matches[0].Groups[2].Value
    $t = [int]$linia.Matches[0].Groups[3].Value
    $n = $g + $p + $t
    $s = ($g + 0.5 * $t) / $n
    # Evita 0 i 1 (log infinit): tallem al voltant d'un 3 % / 97 %.
    $sc = [math]::Min([math]::Max($s, 0.03), 0.97)
    $elo = $nivell + 400 * [math]::Log10($sc / (1 - $sc))
    Write-Host ("   +{0} ={1} -{2}  ({3:P0})  -> Elo estimat {4:N0}" -f $g, $t, $p, $s, $elo)
    # Nomes els nivells no saturats donen una estimacio fiable.
    if ($s -gt 0.1 -and $s -lt 0.9) { $punts += $elo }
}

if ($punts.Count -gt 0) {
    Write-Host ("Estimacio final (mitjana dels nivells no saturats): {0:N0} Elo" -f ($punts | Measure-Object -Average).Average)
} else {
    Write-Host "Tots els nivells saturats (>90 % o <10 %): prova altres -Nivells."
}
