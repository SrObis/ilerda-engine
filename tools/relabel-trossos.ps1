# Reetiqueta data\mix2-tb.txt a trossos amb `ilerda relabel`, perque es pugui aturar i apagar l'ordinador.
# Cada tros es desa a data\relabel\trosNN.txt; es treballa en un fitxer .tmp i només es reanomena en acabar,
# així que els trossos que ja existeixen es salten i un tros tallat es refà sencer en tornar-lo a executar.
# Ús: .\tools\relabel-trossos.ps1 [-Nodes 5000] [-Troc 5000000] [-Total 109198664] [-Fils 11]
param(
    [long]$Nodes = 5000,
    [long]$Troc = 5000000,
    [long]$Total = 109198664,
    [int]$Fils = 11,
    [string]$Entrada = "data\mix2-tb.txt",
    [string]$Dir = "data\relabel"
)
New-Item -ItemType Directory -Force $Dir | Out-Null
$exe = Join-Path $PSScriptRoot "..\src\Ilerda\bin\Release\net8.0\Ilerda.exe"
if (-not (Test-Path $exe)) { Write-Error "Falta $exe (executa: dotnet build src\Ilerda -c Release)"; exit 1 }
# Es treballa amb una còpia, perquè recompilar durant la nit no canviï el motor a mig procés.
$copia = Join-Path $Dir "motor"
if (-not (Test-Path $copia)) { Copy-Item (Split-Path $exe) $copia -Recurse }
$exe = Join-Path $copia "Ilerda.exe"
$n = [math]::Ceiling($Total / $Troc)
for ($i = 0; $i -lt $n; $i++) {
    $nom = "{0}\tros{1:D2}.txt" -f $Dir, $i
    if (Test-Path $nom) { Write-Host "$nom ja existeix, es salta"; continue }
    Write-Host "== Tros $i de $n ($(Get-Date -Format HH:mm)) =="
    & $exe relabel $Entrada "$nom.tmp" $Nodes $Fils $Troc ($i * $Troc)
    if ($LASTEXITCODE -ne 0) { Write-Error "Ha fallat el tros $i"; exit 1 }
    Move-Item "$nom.tmp" $nom
}
Write-Host "Acabat: $n trossos a $Dir"
