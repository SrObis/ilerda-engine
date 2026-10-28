# Canvis

## 0.2.0 — Avaluació amb taules de posició

**Problema.** La v0.1 només comptava material i donava 5 cp per cada fila que avançava un peó, sigui quin sigui.
En una partida contra Stockfish es va veure: jugava 3.a4 i 7.h4 sense sentit, i no entenia que anava perdent
(Stockfish veia +6 mentre Ilerda creia estar a -1).

**Canvi.** `Eval.cs` fa servir l'avaluació *tapered* de PeSTO (Ronald Friederich, Chess Programming Wiki):

- Cada peça té un valor segons la casella, una taula per al mig joc i una altra per al final.
- La *fase de la partida* (segons les peces que queden) barreja les dues puntuacions.
- Així el rei vol estar enrocat al mig joc i al centre en el final, els cavalls al centre, els peons avancen per la columna que toca, etc.

**Eines noves.** `tools/match.ps1` compara dues versions amb `cutechess-cli` i un test SPRT, i
`tools/openings.pgn` té 20 obertures curtes perquè les partides no es repeteixin.
