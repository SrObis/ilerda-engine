<#
.SYNOPSIS
  Compila els executables de la versio actual per a Windows x64 i Linux x64 (autocontinguts, un sol fitxer)
  i els deixa a dist\ amb un .zip per plataforma, a punt per pujar-los com a Release a GitHub.

.EXAMPLE
  .\tools\publicar.ps1

.NOTES
  - Autocontingut: no cal tenir .NET instal.lat. No es fa servir NativeAOT: la xarxa NNUE fa servir
    Vector<T>, que amb JIT tria AVX2 (o el que tingui la CPU) en executar-se; amb AOT quedaria fixat al minim (SSE2).
  - La versio surt de src\Ilerda\Uci.cs. Executa abans `ilerda test` (perft + signatura del bench).
#>
$root = Split-Path -Parent $PSScriptRoot
$m = Select-String -Path (Join-Path $root "src\Ilerda\Uci.cs") -Pattern 'Version = "([^"]+)"'
if (-not $m) { Write-Error "No trobo la versio a Uci.cs."; exit 1 }
$ver = $m.Matches[0].Groups[1].Value

$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

foreach ($rid in @("win-x64", "linux-x64")) {
    $out = Join-Path $dist "tmp-$rid"
    # --source: les versions autocontingudes baixen el runtime de nuget.org (aquest PC no te cap origen configurat)
    dotnet publish (Join-Path $root "src\Ilerda") -c Release -r $rid --self-contained true `
        --source https://api.nuget.org/v3/index.json `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $out
    if ($LASTEXITCODE -ne 0) { Write-Error "La publicacio per a $rid ha fallat."; exit 1 }

    $exe = if ($rid -like "win-*") { "Ilerda.exe" } else { "Ilerda" }
    $nomFinal = if ($rid -like "win-*") { "Ilerda-$ver-$rid.exe" } else { "Ilerda-$ver-$rid" }
    Move-Item (Join-Path $out $exe) (Join-Path $dist $nomFinal)
    Remove-Item $out -Recurse -Force

    $zip = Join-Path $dist "Ilerda-$ver-$rid.zip"
    $fitxers = @((Join-Path $dist $nomFinal), (Join-Path $root "LICENSE"), (Join-Path $root "THIRD-PARTY-NOTICES.md"))
    Compress-Archive -Path $fitxers -DestinationPath $zip
    Write-Host "Creat $zip"
}
Write-Host "Fet. Fitxers a $dist"
