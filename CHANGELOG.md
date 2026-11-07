# Canvis

## 0.13.4 — Taula de transposició amb buckets, edat i avaluació estàtica

**Problema.** La taula tenia una sola entrada per posició: una col·lisió n'esborrava l'anterior, les entrades de cerques velles ocupaven lloc per sempre (sense edat) i no es guardava l'avaluació estàtica, de manera que `Eval.Evaluate` es calculava a cada node encara que la posició ja s'hagués vist.

**Canvi.**

- **Buckets de 4 entrades** (64 bytes). Una posició pot anar a qualsevol de les 4; el reemplaç tria la lliure o la de menys valor.
- **Edat** de 5 bits, que puja a cada cerca (`go`). El valor d'una entrada és `profunditat - 8 × cerques d'antiguitat`: les velles es reemplacen abans.
- **Avaluació estàtica** (16 bits) a cada entrada; Negamax la reutilitza i només crida `Eval.Evaluate` si no hi és (mai en escac).
- La jugada es guarda en 16 bits (des de, a, promoció) i l'indicador (enroc, al pas) es recupera en comparar amb les jugades generades.
- Mateix esquema sense bloquejos per a Lazy SMP (`Check = clau ^ Data`). No hi ha opcions UCI noves.
- `bench 12` (un fil): 9.868.027 nodes (abans 8.996.114) i uns 2,31 M nodes per segon (abans 2,23 M). Amb `Hash` d'1 MB: 9,91 M nodes contra 11,45 M de la 0.13.3, així que els buckets ajuden quan la taula s'omple.

**Resultat.** Contra la 0.13.3 (5+0,05, 6 simultànies): +1172 =1897 -1133 en 4.202 partides, **+3,2 ± 7,8 Elo**. És un canvi neutre en força: no perd, però tampoc es pot distingir d'un empat. Es fusiona perquè és la base de les singular extensions i de reutilitzar l'avaluació a la quiescència, i perquè ajuda quan la taula s'omple (`Hash` petit o partides llargues). El match es va aturar a 4.202 de les 5.000 previstes; el marge ja és suficient per descartar una regressió. Limitació coneguda: els buckets no estan alineats a 64 bytes, així que un bucket pot ocupar dues línies de cache.

## 0.13.3 — Pila d'acumuladors NNUE i actualització en una sola passada

**Problema.** Cada `MakeMove` actualitzava els acumuladors de la NNUE peça a peça (`Add`/`Remove`, de 2 a 4 passades per jugada, cadascuna sobre les dues perspectives) i cada `UnmakeMove` havia de desfer-ho. A més, les jugades il·legals (que deixen el rei en escac) també tocaven l'acumulador abans de ser descartades.

**Canvi.**

- `Position` guarda una **pila d'acumuladors**, una entrada per jugada feta. `UnmakeMove` només baixa el punter i no recalcula res.
- Nova `Nnue.ApplyDelta`: calcula l'acumulador nou a partir de l'anterior en una sola passada vectoritzada, `dst = src + pesos(afegides) - pesos(tretes)`, amb tres casos (jugada tranquil·la, captura, enroc).
- Les jugades il·legals es descarten abans de tocar l'acumulador.
- `bench 12` (un fil): 8.996.114 nodes, igual que la 0.13.2: la cerca no canvia, només la velocitat.
- **Velocitat:** de 1,57 M a 2,35 M nodes per segon (+50 %), un fil.
- Comprovat que l'avaluació incremental coincideix amb la recalculada des de zero, incloent-hi enroc, captura al pas i promoció amb captura.

**Resultat.** Contra la 0.13.2 (5+0,05, 6 simultànies, banc `tools/obertures.epd`): +1415 =1179 -426 en 3.020 partides, **+118,1 ± 9,8 Elo**. Els dos motors gasten el mateix temps per jugada (112 ms) i la 0.13.3 arriba 0,9 plies més lluny (11,50 contra 10,60). El match es va aturar a 3.020 partides: el marge ja era suficient.

Contra Stockfish limitat (10+0,1, 200 partides per nivell): 86,0 % contra el 2600, 35,2 % contra el 3000 i 17,2 % contra el 3190, unes 2.910 en aquesta escala (la 0.13.2, unes 2.870).

## 0.13.2 — Gestió del temps amb temps òptim i límit dur

**Problema.** Fins a la 0.13.1 el motor tenia un sol límit per jugada (`temps restant / 30 + 3/4 de l'increment`) i el gastava gairebé sempre sencer: cada iteració de l'aprofundiment iteratiu costa 2-3 cops l'anterior, així que la darrera es tallava a mitges i aquell temps es perdia. A més, gastava el mateix temps en una jugada evident que en una de difícil.

**Canvi.**

- Dos temps per jugada: un **temps òptim** (`temps restant / TmDiv + 3/4 de l'increment`) i un **límit dur** (`TmHard` % de l'òptim, mai més d'un terç del temps que queda).
- Passat el temps òptim no s'inicia cap iteració nova. L'òptim s'escala segons l'**estabilitat de la millor jugada**: ×2,0 si acaba de canviar, ×1,4, ×1,1, ×0,9 i ×0,8 si fa 4 iteracions o més que és la mateixa. Si la puntuació cau respecte de la iteració anterior, es multiplica fins a ×1,5 (caiguda de 100 cp o més).
- El límit dur continua aturant la cerca a mitja iteració, només en les jugades difícils.
- Opcions UCI noves: `Tm` (1; 0 = gestió antiga), `TmDiv` (50) i `TmHard` (500).
- `bench 12` (un fil): 8.996.114 nodes, igual que la 0.13.1: la cerca no canvia, només el repartiment del rellotge.

**Afinament** (1.000 partides per configuració contra la 0.13.1, 5+0,05, banc `tools/obertures.epd`): `TmDiv` 25/35/50/70 amb límit 3× donen +43, +79, +87 i +70 Elo (amb 25 es perden 2 partides per temps); amb límit 5×, `TmDiv` 40 dona +106 i `TmDiv` 50 +122; amb límit 8×, +121. Es tria 50 i 5× (marges d'uns ±17 Elo).

**Resultat.** Contra la 0.13.1 (5+0,05, 6 simultànies): +1965 =1603 -660 en 4.228 partides, **+110,9 ± 8,4 Elo**, cap partida perduda per temps. El motor gasta de mitjana 113 ms per jugada (la 0.13.1, 104) i arriba 0,17 plies més lluny. El match es va aturar a 4.228 de les 5.000 previstes; el marge ja és petit.

## 0.13.1 — Taules per material insuficient

**Problema.** Revisant 5.000 partides d'autojoc, 455 de les 2.139 taules acabaven per material insuficient, i en 347 d'aquestes un dels dos motors havia vist +3 peons o més. La xarxa dona puntuacions altes a finals que són taules teòriques: rei i alfil contra rei donava +269 cp a profunditat 8.

**Canvi.**

- Nova `Position.InsufficientMaterial()`: només reis, un sol cavall o alfil, o només alfils de totes dues bandes a caselles del mateix color.
- La cerca (`Negamax`, fora de l'arrel) i la quiescència hi retornen 0. Ara rei i alfil contra rei puntua 0.
- `bench 12` (un fil): 8.996.114 nodes (abans 8.990.810).

**Resultat.** Contra la 0.13.0 (5+0,05, 6 simultànies): +785 =1088 -783 en 2.656 partides, **+0,3 ± 10,2 Elo**. Estava previst: és una correcció d'una avaluació falsa, no una millora de força contra la versió anterior. El match es va aturar a 2.656 de les 5.000 previstes; el marge és suficient per descartar una regressió.

## 0.13.0 — Malus de history amb gravity

**Problema.** La history només tenia bonus (`depth²`) amb un topall lineal: les jugades tranquil·les que es provaven abans de la que talla no en sortien penalitzades.

**Canvi.**

- Quan una jugada tranquil·la talla, rep un bonus de `depth²` (màxim 1500) i les tranquil·les provades abans a aquell node, que no han tallat, reben el mateix valor en negatiu (fins a 64 per node).
- *Gravity*: `h += bonus - h·|bonus| / 6000`, de manera que el valor queda acotat sense retallar-lo a mà.
- `bench 12` (un fil): 8.990.810 nodes (abans 9.115.129).

**Resultat.** Contra la 0.12.0 (5+0,05, 5.000 partides, 6 simultànies): +1490 =2139 -1371, **+8,3 ± 7,3 Elo**, LOS 98,7 %. Cap partida perduda per temps. És una millora petita: la history anterior ja funcionava bé.

## 0.12.0 — PVS i finestres d'aspiració

**Problema.** Quan una jugada no es redueix amb LMR, es cercava amb finestra completa, i cada iteració començava amb finestra infinita.

**Canvi.**

- **PVS:** la primera jugada legal es cerca amb finestra completa; les altres amb finestra nul·la (reduïda si toca LMR) i només es repeteixen amb finestra completa si superen alfa en un node PV.
- **Finestres d'aspiració:** des de profunditat 5 el fil principal cerca amb la finestra [puntuació ± 20] i la dobla cada cop que falla. Els fils ajudants continuen amb finestra completa.
- **Comanda `bench [profunditat]`:** cerca de profunditat fixa (12 per defecte) sobre 10 posicions; el total de nodes (9.115.129 a profunditat 12 amb un fil) identifica la versió.
- **`MoveOverhead`** per defecte passa de 0 a 20 ms, per no perdre per temps amb interfícies gràfiques i xarxa.

**Resultat.** Contra la 0.11.0 (5+0,05, 277 partides vàlides, 6 partides simultànies): +122 =100 -55, uns **+86 Elo** (marge d'uns ±40; no és un SPRT concloent).

## 0.11.0 — Xarxa NNUE reentrenada amb dades noves

**Problema.** La xarxa de la 0.10.0 s'havia entrenat amb etiquetes d'un motor de la 0.6.1 i obertures aleatòries.

**Canvi.**

- Nova xarxa incrustada (`ilerda-256.bin`, mateixa arquitectura 768 → 256 → 1), entrenada a Kaggle amb les 50 M de posicions velles més 22 M de noves: `datagen` amb la 0.10.0 (8.000 nodes) i el llibre propi `llibre.epd`, ajuntades, deduplicades i barrejades amb `tools/merge_shuffle.py`.
- Les dades noves soles empitjoren la xarxa (uns −140 Elo): falta diversitat. Només barrejades amb les velles milloren.

**Resultat.** Contra la 0.10.0: uns +171 Elo. Contra Stockfish limitat (10+0,1, 200 partides per cel·la), la xarxa nova puntua 88,2 % (vella 81,8 %) a `UCI_Elo` 2200, 70,0 % (vella 51,0 %) a 2600 i 17,0 % (vella 12,0 %) a 3000.

**Eines.** `tools/comparar-sf.ps1` (dues xarxes contra Stockfish), `tools/merge_shuffle.py` i `tools/nnue/ilerda-train.ipynb` (notebook de Kaggle).


## 0.10.0 — Avaluació NNUE

**Problema.** L'avaluació eren les taules de posició PeSTO, escrites a mà i sense entendre res de la relació entre les peces.

**Canvi.**

- Nova avaluació NNUE (`Nnue.cs`): xarxa 768 → 256 (×2 perspectives) → 1 amb SCReLU. Les característiques són (peça, casella) vistes des de cada bàndol, així que els acumuladors s'actualitzen sumant i restant a cada peça afegida o treta (`AddPiece`/`RemovePiece`) i desfer una jugada els restaura sol. L'avaluació final és vectoritzada (`Vector<short>`).
- La xarxa va **dins de l'executable** (`src/Ilerda/nets/ilerda-256.bin`, 395 KB, recurs incrustat) i es carrega en arrencar. Opció UCI `EvalFile`: buida = xarxa incrustada, `none` = tornar a PeSTO, o la ruta d'un altre fitxer `quantised.bin` de bullet.
- Entrenada a Kaggle (GPU T4, bullet, 4,5 min) amb 50 M de posicions de `datagen` de la 0.6.1 (5.000 nodes, obertures aleatòries), deduplicades. Vegeu `tools/nnue/kaggle-bullet.md`.
- Ordre `eval` per veure l'avaluació d'una posició.

**Resultat.** Contra la 0.9.1 (10+0,1, 200 partides, un fil): +131 =31 -38, **+175 ± 49 Elo**, LOS 100 %. A 5+0,05 contra el mateix executable amb PeSTO: +135 =26 -39, +182 ± 51 Elo. La cerca va uns 1,7 cops més lenta que amb PeSTO (≈2,2 M contra ≈3,9 M nodes per segon), i tot i això guanya amb claredat.

**Eines.** `tools/subset` (extreu un subconjunt de les dades al format de bullet), `tools/ajustar.ps1` amb `-Contra` (jugar contra una altra versió) i noms de registre vàlids.


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
