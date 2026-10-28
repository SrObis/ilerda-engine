# Canvis

## 0.3.0 — Taula de transposició i repeticions

**Problema.** La v0.2 no detectava repeticions i tornava a cercar les mateixes posicions per camins diferents, així que només arribava a profunditat 6–9.

**Canvi.**

- `Position` manté una clau Zobrist incremental (peces, bàndol, drets d'enroc i columna d'en passant) i un historial de claus de la partida.
- Les posicions ja vistes des de l'últim peó o captura es valoren com a taules, també les anteriors a l'arrel de la cerca.
- Nova `TranspositionTable` (64 MB per defecte, opció UCI `Hash`): talls per límit exacte, inferior i superior, i la millor jugada guardada es prova primer. Les puntuacions de mat es guarden relatives a la posició.
- `ilerda test` comprova també que la clau incremental coincideix amb la recalculada.

**Resultat.** Contra la v0.2 (10+0,1, 95 partides, SPRT 0/10): +61 =28 -6, +230 ± 65 Elo. H1 acceptada.

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
