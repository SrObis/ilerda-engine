<#
.SYNOPSIS
  Compara dues xarxes NNUE (la incrustada i una altra via EvalFile) jugant contra Stockfish amb la forca limitada.

.EXAMPLE
  .\tools\comparar-sf.ps1 -Versio 0.10.0 -Xarxa nets\ilerda-256-mix.bin -Nivells 2200,2600 -Rounds 100

.NOTES
  Es la comprovacio independent d'un resultat Ilerda contra Ilerda: les dues configuracions juguen
  contra el mateix adversari, als mateixos nivells i amb les mateixes obertures. L'Elo de Stockfish
  (UCI_Elo) es nomes una referencia aproximada; interessa la diferencia entre les dues xarxes.
#>
param(
    [Parameter(Mandatory = $true)][string]$Versio,
    [Parameter(Mandatory = $true)][string]$Xarxa,
    [string]$Stockfish,
    [string]$CutechessCli = "C:\Program Files (x86)\Cute Chess\cutechess-cli.exe",
    [int[]]$Nivells = @(2200, 2600),
    [string]$Tc = "10+0.1",
    [int]$Rounds = 100,            # cada ronda son 2 partides (colors canviats)
    [int]$Concurrency = 6
)

$root = Split-Path -Parent $PSScriptRoot
if (-not $Stockfish) { $Stockfish = (Get-ChildItem (Join-Path $root "tools\stockfish") -Recurse -Filter "stockfish*.exe" | Select-Object -First 1).FullName }
$openings = Join-Path $root "tools\openings.pgn"
$motor = if (Test-Path $Versio) { $Versio } else { Join-Path $root "Versions\Ilerda-$Versio\Ilerda.exe" }
$xarxaAbs = (Resolve-Path $Xarxa).Path
foreach ($f in @($motor, $Stockfish, $CutechessCli, $openings)) {
    if (-not (Test-Path $f)) { Write-Error "No trobo: $f"; exit 1 }
}

$configs = @(
    @{ Nom = "Actual"; Opcio = "" },
    @{ Nom = "Nova"; Opcio = "option.EvalFile=$xarxaAbs" }
)
foreach ($nivell in $Nivells) {
    foreach ($c in $configs) {
        Write-Host "== $($c.Nom) contra Stockfish UCI_Elo $nivell =="
        $args = @(
            "-engine", "name=Ilerda", "cmd=$motor", "proto=uci"
        )
        if ($c.Opcio) { $args += $c.Opcio }
        $args += @(
            "-engine", "name=SF", "cmd=$Stockfish", "proto=uci", "option.UCI_LimitStrength=true", "option.UCI_Elo=$nivell", "option.Threads=1", "option.Hash=16",
            "-each", "tc=$Tc",
            "-openings", "file=$openings", "format=pgn", "order=random", "plies=8",
            "-games", "2", "-rounds", "$Rounds", "-repeat", "-recover",
            "-concurrency", "$Concurrency"
        )
        $log = Join-Path $env:TEMP "comparar-sf-$($c.Nom)-$nivell.log"
        & $CutechessCli @args 2>&1 | Tee-Object -FilePath $log | Out-Null
        $linia = Select-String -Path $log -Pattern "Score of Ilerda vs SF" | Select-Object -Last 1
        Write-Host "   $($linia.Line)"
    }
}
