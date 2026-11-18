# Espera que acabi el reetiquetatge (tools\relabel-trossos.ps1: l'últim tros és data\relabel\tros21.txt)
# i després genera dades noves d'autojoc amb el motor de data\relabel\motor (la 0.15.1), sense límit de partides.
# S'atura amb Ctrl-C (les partides ja escrites es conserven; `datagen` afegeix al final del fitxer).
# Ús: .\tools\datagen-despres-relabel.ps1 [-Nodes 5000] [-Fils 11] [-Llavor 4711]
param(
    [long]$Nodes = 5000,
    [int]$Fils = 11,
    [int]$Llavor = 4711,
    [string]$Ultim = "data\relabel\tros21.txt",
    [string]$Sortida = "data\datagen-007.txt",
    [string]$Llibre = "data\llibre-total.epd"
)
Set-Location (Split-Path -Parent $PSScriptRoot)
$exe = "data\relabel\motor\Ilerda.exe"
if (-not (Test-Path $exe)) { Write-Error "Falta $exe"; exit 1 }
while (-not (Test-Path $Ultim)) {
    Write-Host "$(Get-Date -Format HH:mm) esperant que acabi el reetiquetatge ($Ultim)..."
    Start-Sleep -Seconds 60
}
Write-Host "$(Get-Date -Format HH:mm) reetiquetatge acabat: comença datagen"
& $exe datagen 0 $Nodes $Sortida $Fils $Llavor $Llibre
