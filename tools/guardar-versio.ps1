<#
.SYNOPSIS
  Compila en Release i guarda una copia a Versions\Ilerda-<versio>\ per poder-hi comparar mes endavant.

.EXAMPLE
  .\tools\guardar-versio.ps1                 # fa servir la versio de src\Ilerda\Uci.cs (per exemple 0.3.0)
  .\tools\guardar-versio.ps1 -Nom 0.3.0-tt   # nom personalitzat, per a proves intermedies

.NOTES
  No sobreescriu mai una versio ja guardada: si la carpeta existeix, s'atura.
  Puja la versio a Uci.cs (Version = "...") abans de guardar una de nova.
#>
param(
    [string]$Nom
)

$root = Split-Path -Parent $PSScriptRoot

if (-not $Nom) {
    $m = Select-String -Path (Join-Path $root "src\Ilerda\Uci.cs") -Pattern 'Version = "([^"]+)"'
    if (-not $m) { Write-Error "No trobo la versio a Uci.cs. Passa-la amb -Nom."; exit 1 }
    $Nom = $m.Matches[0].Groups[1].Value
}

$dest = Join-Path $root "Versions\Ilerda-$Nom"
if (Test-Path $dest) {
    Write-Error "Ja existeix $dest. Puja la versio a Uci.cs o passa un altre -Nom."
    exit 1
}

dotnet build -c Release (Join-Path $root "src\Ilerda")
if ($LASTEXITCODE -ne 0) { Write-Error "La compilacio ha fallat."; exit 1 }

Copy-Item (Join-Path $root "src\Ilerda\bin\Release\net8.0") $dest -Recurse
Write-Host "Versio guardada a $dest"
