# Canvis

## 0.9.1 — Magic bitboards

**Problema.** Els atacs de torres, alfils i dames es calculaven amb rajos casella a casella, i era una part notable del temps de cada node.

**Canvi.**

- `Attacks.Bishop`/`Rook` fan servir magic bitboards (`(occ & màscara) * màgic >> desplaçament` indexa una taula d'atacs). Els números màgics es busquen en arrencar amb una llavor fixa; el motor respon en uns 0,1 s.
- La cerca no canvia: mateixos nodes que la 0.9.0 (4.485.351 a profunditat 12 des de la posició inicial), 26 OK a `ilerda test`.

**Resultat.** Uns 4,2 M → 4,9 M nodes per segon (+18 %) a un fil. Contra la 0.9.0 (10+0,1, 100 partides): +33 =40 -27, +21 ± 53 Elo, LOS 78,1 %. És una optimització de velocitat pura, així que s'adopta sense exigir el LOS del 95 %, que amb 100 partides no es pot assolir.

## 0.9.0 — Cerca multifil (Lazy SMP)

**Problema.** El motor només feia servir un nucli, i en tornejos de mòduls es compta amb tots els de la màquina.

**Canvi.**

- Opció UCI `Threads` (1 per defecte): fils ajudants que cerquen la mateixa posició amb la taula de transposició compartida. El fil principal porta el temps i tria la jugada; els ajudants se salten alguns nivells d'aprofundiment per no repetir feina.
- La taula de transposició és ara sense bloquejos: cada entrada són dues paraules de 64 bits (dades i `clau ^ dades`) i una entrada escrita a mitges es descarta com a no trobada.
- Opcions `MoveOverhead` (0 ms per defecte) i `SoftTime` (100 = desactivat). El límit dur de temps no passa d'un terç del temps que queda.
- UCI: el `stop` que arribava just després d'un `go` ja no es perd, i els valors no numèrics de `go` s'ignoren en lloc de fer caure el motor.

**Resultat.** Amb 1 fil la cerca és idèntica a la 0.7.0 (mateixos nodes a profunditat 12) i contra ella fa +39 =29 -32, +24 ± 58 Elo (LOS 79,7 %, 10+0,1, 100 partides): soroll. `Threads=4` contra `Threads=1` (5+0,05, 200 partides): +102 =58 -40, +111 Elo. `SoftTime=60`: +5 Elo, soroll i no s'activa. Per a tornejos cal posar `Threads` al nombre de nuclis.

## 0.7.0 — Reverse futility i futility

**Problema.** A poca profunditat la cerca encara explorava jugades en posicions ja decidides pel material o la posició.

**Canvi.**

- *Reverse futility*: a profunditat 6 o menys, si l'avaluació estàtica supera beta amb un marge de 90 cp per nivell, es talla la branca. No en línia principal, en escac ni quan es busquen mats.
- *Futility*: a profunditat 4 o menys, una jugada tranquil·la que no dona escac i amb l'avaluació 110 cp per nivell per sota d'alfa es salta. La primera jugada legal es cerca sempre.
- Els marges són opcions UCI (`RfpMargin`, `FutMargin`) per afinar-los amb `tools/ajustar.ps1`; encara no s'han afinat.

**Resultat.** Contra la v0.6.1 (10+0,1, 100 partides, SPRT 0/10): +40 =41 -19, +74 ± 53 Elo, LOS 99,7 %. L'SPRT no va concloure, però el signe és molt clar.

**Eina nova: `datagen`.** `ilerda datagen <partides> <nodes> <fitxer> [fils] [llavor] [llibre.epd]` fa partides d'autojoc amb límit de nodes i escriu `FEN | puntuació (blanques) | resultat` de les posicions tranquil·les, per a Texel i NNUE. Obertures aleatòries o d'un llibre EPD, i adjudicació de partides decidides. `Searcher` té ara `NodeLimit` i `LastScore`.

## 0.6.1 — LMR (Late Move Reductions)

**Problema.** La cerca dedicava la mateixa profunditat a les jugades tranquil·les que es proven tard (ja ordenades per la taula de transposició, les captures, els killers i l'history), que gairebé mai són la millor.

**Canvi.**

- Les jugades tranquil·les a partir de la cinquena es cerquen a profunditat reduïda amb finestra nul·la, i si superen alfa es tornen a cercar a fons. No es redueixen les captures, les promocions, els escacs, els killers, res en escac ni a l'arrel.
- La reducció és `0,75 + ln(profunditat) · ln(jugada) / 2,25`, menys 1 cada 2500 punts d'*history*, i mai fa que la profunditat baixi de 1.
- Els tres paràmetres són opcions UCI (`LmrDiv`, `LmrStart`, `LmrHist`) per poder-ne provar variants sense recompilar, amb `tools/ajustar.ps1`.

**Resultat.** Contra la v0.5.0 (10+0,1, 100 partides, SPRT 0/10): +47 =30 -23, +85 ± 59 Elo, LOS 99,8 %. L'SPRT no va concloure, però el signe és molt clar. Una primera versió, que reduïa des de la quarta jugada i sense tenir en compte l'*history*, només va fer +35 ± 58 Elo i no es va publicar.

**Afinament.** Sis variants dels paràmetres, 200 partides cadascuna (5+0,05): cap queda fora del soroll (de -2 a +19 Elo, marge ±40). No s'adopta cap canvi. Vegeu el README.

## 0.5.0 — Poda null move

**Problema.** La cerca explorava totes les jugades de posicions on el bàndol ja està tan bé que no hi ha res a temer.

**Canvi.**

- `Position.MakeNullMove`/`UnmakeNullMove` passen el torn sense moure i mantenen la clau Zobrist.
- A `Negamax`, si el bàndol té peces, no està en escac i l'avaluació ja supera beta, es cerca amb el torn passat i profunditat reduïda (`R = 2 + profunditat/6`). Si encara supera beta, es poda la branca.
- No s'aplica en escac, a l'arrel, sense peces (perill de zugzwang), dues vegades seguides ni quan es busquen mats.
- `ilerda test` comprova també les claus després d'un null move.

**Resultat.** Contra la v0.4.0 (10+0,1, 100 partides, SPRT 0/10): +48 =25 -27, +74 ± 60 Elo, LOS 99,2 %. L'SPRT no va concloure, però la millora és molt probable.

**Referència externa.** Contra Stockfish amb `UCI_Elo` limitat (10+0,1, 40 partides per nivell, versió 0.4.0): 84 % al 1800, 63 % al 2100, 50 % al 2400 i 19 % al 2700, és a dir, al voltant de 2400 en aquest test. És una referència orientativa: el ritme és molt ràpid i l'escala de Stockfish no és la de CCRL.

## 0.4.0 — Killers i history

**Problema.** Les jugades que no capturen res es provaven en l'ordre en què es generaven. L'alfa-beta talla molt més quan les millors jugades surten primer.

**Canvi.**

- *Killers*: es recorden dues jugades tranquil·les per nivell de la cerca que han tallat una línia, i es proven abans que la resta.
- *History*: cada jugada tranquil·la que talla suma punts (per bàndol, casella d'origen i de destí, amb límit); les que en tenen més es proven primer. S'envelleix (es divideix per 2) abans de cada cerca.
- Ordre final: jugada de la taula de transposició, captures (MVV-LVA), promocions, killers i history.

**Resultat.** A la posició inicial arriba a profunditat 8 en ~1 s (la 0.3.0 en trigava 5,5). Contra la v0.3.0 (10+0,1, 100 partides, SPRT 0/10): +43 =28 -29, +49 ± 59 Elo, LOS 95 %. L'SPRT no va concloure, així que la millora és probable però no confirmada.

**Eines noves.** `tools/calibrar.ps1` estima l'Elo d'una versió jugant contra Stockfish amb `UCI_Elo` limitat.

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
