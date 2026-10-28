# Versions

Aquí es guarden les còpies compilades de cada versió d'Ilerda (`Ilerda-0.2.0\`, `Ilerda-0.3.0\`, ...), per poder-les enfrontar entre elles amb `tools\match.ps1`.

Compilar amb `dotnet build` sobreescriu sempre `bin\Release\net8.0`, així que cal guardar la versió abans de seguir canviant el codi:

```powershell
.\tools\guardar-versio.ps1
```

Els binaris no es pugen a GitHub (estan al `.gitignore`). Per recuperar una versió antiga, fes `git checkout` de l'etiqueta corresponent i torna a executar l'script.
