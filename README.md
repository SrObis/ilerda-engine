# Ilerda

Motor d'escacs UCI escrit en C#, fet des de zero. El nom és provisional (Ilerda és el nom romà de Lleida).

**Estat:** v0.13.1 — generador de moviments validat amb perft, cerca alfa-beta amb taula de transposició, detecció de repeticions, ordenació amb killers i history, PVS, finestres d'aspiració, poda null move, LMR, reverse futility i futility, protocol UCI, cerca multifil (Lazy SMP, opció `Threads`) i avaluació NNUE (xarxa 768 → 256 → 1 incrustada a l'executable; les taules PeSTO es conserven com a alternativa). Vegeu el [CHANGELOG](CHANGELOG.md).

## Què inclou

- Bitboards (atacs de peces lliscants amb magic bitboards) i generació de moviments pseudolegals amb comprovació de legalitat (enroc, captura al pas i promocions incloses).
- Cerca negamax amb alfa-beta, aprofundiment iteratiu, cerca de quiescència, extensió per escac i ordenació MVV-LVA.
- Claus Zobrist incrementals, taula de transposició (opció UCI `Hash`, 64 MB per defecte) i detecció de repeticions.
- Taules per material insuficient (`Position.InsufficientMaterial`): només reis, un sol cavall o alfil, o només alfils a caselles del mateix color. La cerca i la quiescència hi retornen 0, en lloc de fiar-se de l'avaluació de la xarxa.
- Ordenació de jugades tranquil·les amb killers (2 per nivell) i history amb *malus* i *gravity*: la jugada tranquil·la que talla rep un bonus (`depth²`, màxim 1500) i les tranquil·les provades abans que no han tallat reben el mateix valor en negatiu; el canvi es redueix a mesura que el valor s'acosta al màxim (6000), així queda acotat.
- PVS (*principal variation search*): la primera jugada de cada node es cerca amb finestra completa i la resta amb finestra nul·la; només es repeteixen amb finestra completa si superen alfa en un node PV.
- Finestres d'aspiració: a partir de profunditat 5 el fil principal cerca al voltant de la puntuació de la iteració anterior (±20 cp) i eixampla la finestra al doble cada cop que falla.
- Poda null move (no en escac, ni sense peces, ni dues vegades seguides).
- LMR: les jugades tranquil·les que es proven tard es cerquen a menys profunditat i, si sorprenen, es tornen a cercar a fons. Els paràmetres són opcions UCI (`LmrDiv`, `LmrStart`, `LmrHist`).
- Reverse futility (profunditat ≤ 6) i futility (≤ 4), amb marges configurables (`RfpMargin`, `FutMargin`).
- Cerca multifil amb Lazy SMP (opció UCI `Threads`, 1 per defecte): els fils ajudants cerquen la mateixa posició amb una taula de transposició compartida sense bloquejos i el fil principal tria la jugada. Vegeu [Multifil](#multifil-lazy-smp).
- Control de temps (`wtime`, `btime`, `winc`, `binc`, `movestogo`, `movetime`, `depth`, `infinite`), amb opció `MoveOverhead` (20 ms per defecte) i un límit del terç del temps que queda.
- Avaluació NNUE (xarxa 768 → 256 → 1 amb SCReLU, acumuladors incrementals i xarxa incrustada; opció `EvalFile`) i, com a alternativa (`EvalFile none`), l'avaluació *tapered* amb taules PeSTO.
- Comandes extra: `perft N`, `divide N`, `d` (mostra el tauler) i `bench [profunditat]` (cerca de profunditat fixa, 12 per defecte, sobre 10 posicions fixes; el total de nodes serveix de signatura de la versió i és determinista amb `Threads=1`).
- `ilerda book <línies> <fitxer> [fils] [llavor] [nodes] [marge]`: crea un llibre d'obertures EPD equilibrades per a `datagen`. Vegeu [Llibre d'obertures propi](#llibre-dobertures-propi-ilerda-book).
- `ilerda datagen <partides> <nodes> <fitxer> [fils] [llavor] [llibre.epd]`: autojoc amb límit de nodes per generar dades d'entrenament (`FEN | puntuació | resultat`). Amb 0 partides no s'atura. Els fitxers van a `data/` (no es puja a GitHub).

## Compilar i provar

Cal el [.NET SDK 8](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release src/Ilerda
dotnet run -c Release --project src/Ilerda -- test        # suite de perft i claus Zobrist
dotnet run -c Release --project src/Ilerda -- perft 5     # nodes des de la posició inicial
dotnet run -c Release --project src/Ilerda                # mode UCI
```

La suite de perft ha de passar sempre abans de tocar la cerca: si falla, hi ha un error al generador de moviments.

Per jugar-hi, afegeix l'executable (`src/Ilerda/bin/Release/net8.0/`) a Arena, En Croissant o Cute Chess com a motor UCI.

## Comparar versions

Cada canvi s'ha de mesurar. `dotnet build` sobreescriu sempre `bin/Release/net8.0`, així que cada versió es guarda abans de seguir canviant el codi:

```powershell
.\tools\guardar-versio.ps1                    # compila i desa la còpia a Versions\Ilerda-<versió>\
.\tools\match.ps1 -Nou 0.3.0 -Vell 0.2.0     # matx amb cutechess-cli i test SPRT
```

El matx fa servir les obertures de `tools/openings.pgn`. Vegeu [Versions](Versions/README.md).

## Proves d'afinament

Els paràmetres de l'LMR (v0.6.2, configuració per defecte = v0.6.1) es van provar un per un amb `tools/ajustar.ps1`: cada variant juga contra el mateix executable amb la configuració per defecte, 200 partides a 5+0,05. Amb 200 partides el marge d'error és d'uns **±40 Elo**, així que només es veurien diferències grans.

| Paràmetre | Per defecte | Variant | Resultat | Punts | Elo vs base |
|---|---|---|---|---|---|
| `LmrDiv` (divisor; més alt = redueix menys) | 225 | 175 | +75 =54 -71 | 51,0 % | +7 |
| | | 275 | +70 =67 -63 | 51,8 % | +12 |
| `LmrStart` (primera jugada que es redueix) | 5 | 4 | +77 =57 -66 | 52,8 % | +19 |
| | | 6 | +68 =66 -66 | 50,5 % | +3 |
| `LmrHist` (punts d'*history* per treure 1 de reducció) | 2500 | 1500 | +74 =51 -75 | 49,8 % | -2 |
| | | 4000 | +72 =64 -64 | 52,0 % | +14 |

**Conclusió:** cap variant se separa de la base més del que el soroll permet. Tot i que `LmrStart=4` (+19) és la més alta, no es pot distingir de la sort, i no s'adopta cap canvi. L'LMR està en una zona plana: afinar aquests tres paràmetres no dona més, i el pas següent és un altre canvi de cerca o de dades. Cap variant s'ha confirmat amb un match de 100 partides a 10+0,1; si es vol adoptar-ne alguna, caldria fer-ho abans.

Els marges de la poda de la v0.7.0 es van provar igual (300 partides per variant, 5+0,05, marge d'error d'uns **±35 Elo**):

| Paràmetre | Per defecte | Variant | Resultat | Punts | Elo vs base |
|---|---|---|---|---|---|
| `RfpMargin` | 90 | 65 | +110 =90 -100 | 51,7 % | +12 |
| | | 120 | +86 =105 -109 | 46,2 % | -27 |
| `FutMargin` | 110 | 80 | +97 =92 -111 | 47,7 % | -16 |
| | | 145 | +107 =87 -106 | 50,2 % | +1 |

Cap variant se separa del soroll i no s'adopta cap canvi. Si hi ha tendència, és que un `RfpMargin` més petit potser va una mica millor, però no es pot distingir de la sort.

Referència externa de la v0.7.0 contra Stockfish amb `UCI_Elo` limitat (10+0,1, 100 partides per nivell): 57 % al 2400 (~2449), 45 % al 2550 (~2515) i 28 % al 2700 (~2532), amb una mitjana d'**uns 2500 Elo** (±35). Una primera tanda de 40 partides per nivell va donar ~2485, i el 3000 va quedar saturat (4 %). Amb les mateixes limitacions que la referència següent.

Referència externa (v0.4.0 contra Stockfish amb `UCI_Elo` limitat, 10+0,1, 40 partides per nivell): 84 % al 1800, 63 % al 2100, 50 % al 2400 i 19 % al 2700, és a dir, al voltant de 2400 en aquest test. És una referència orientativa: el ritme és molt ràpid i l'escala de Stockfish no és la de CCRL. Vegeu `tools/calibrar.ps1`.

## Multifil (Lazy SMP)

L'opció `Threads` (1 a 256, 1 per defecte) activa els fils ajudants. Cada fil té els seus killers, *history* i tauler; només es comparteix la taula de transposició.

- **Taula compartida sense bloquejos:** cada entrada són dues paraules de 64 bits (dades i `clau ^ dades`). Si dos fils escriuen alhora i l'entrada queda a mitges, la comprovació falla i es tracta com a no trobada.
- **Diversitat:** els ajudants se salten alguns nivells d'aprofundiment (`(profunditat + id) % 3 == 0`) perquè no repeteixin tots la mateixa feina. Només el fil principal informa i fixa la jugada.
- **Un fil és idèntic a la 0.7.0:** `go depth 12` des de la posició inicial dona els mateixos nodes (4.485.351). `datagen` continua usant un sol fil per cerca.
- **Temps:** el límit dur no passa d'un terç del temps que queda, es descompta `MoveOverhead` (0 per defecte), i `SoftTime` (100 per defecte = desactivat) permet no iniciar una iteració nova passat un percentatge del límit.
- **UCI:** el `stop` que arribava just després d'un `go` ja no es perd, i els valors no numèrics de `go` s'ignoren en lloc de fer caure el motor.

Proves (200 partides a 5+0,05, marge d'uns ±40 Elo), variant contra el mateix executable amb la configuració per defecte:

| Variant | Resultat | Punts | Elo vs base |
|---|---|---|---|
| `Threads=4` (contra 1 fil) | +102 =58 -40 | 65,5 % | +111 |
| `SoftTime=60` | +65 =73 -62 | 50,8 % | +5 |

Comprovació a un fil contra la 0.7.0 (10+0,1, 100 partides): amb `MoveOverhead` de 30 ms, +26 =44 -30, -14 ± 51 Elo (LOS 29,6 %); amb `MoveOverhead` de 0 (el valor per defecte), +39 =29 -32, +24 ± 58 Elo (LOS 79,7 %). Les dues dins del soroll: a un fil la cerca és idèntica a la 0.7.0 i només canvia el temps.

Els 4 fils guanyen molt clar. `SoftTime=60` queda dins del soroll i es deixa desactivat. Per defecte el motor continua amb 1 fil, així que la força per defecte no canvia; per a tornejos cal posar `Threads` al nombre de nuclis disponibles.

## NNUE

L'avaluació és una xarxa **768 → 256 (×2 perspectives) → 1** amb activació SCReLU, entrenada amb [bullet](https://github.com/jw1912/bullet) i incrustada a l'executable (`src/Ilerda/nets/ilerda-256.bin`). Opció UCI `EvalFile`: buida = xarxa incrustada, `none` = avaluació PeSTO, o la ruta d'un altre `quantised.bin` de bullet.

- **Característiques:** (peça, casella) des de cada bàndol, peces pròpies al bloc 0 i del rival al 384, casella girada en vertical per a les negres. No depenen del rei, així que els acumuladors s'actualitzen sumant i restant una fila de pesos per cada peça afegida o treta.
- **Entrenament:** 49,9 M de posicions de `datagen` (motor 0.6.1, 5.000 nodes, obertures aleatòries) més 22 M de noves (v0.10.0, 8.000 nodes, llibre propi; vegeu més avall), deduplicades i barrejades; 20 superbatches (≈40 èpoques) a una T4 de Kaggle en 4,5 minuts. Procediment complet i format del fitxer a [tools/nnue/kaggle-bullet.md](tools/nnue/kaggle-bullet.md); el subconjunt es treu amb `tools/subset`.
- **Velocitat:** uns 2,2 M de nodes per segon a un fil, contra uns 3,9 M amb PeSTO. Un primer producte final escalar anava a 1,0 M i quedava −40 ± 44 Elo contra PeSTO; vectoritzat va a +182 ± 51.

| Matx | Partides | Resultat | Elo |
|---|---|---|---|
| NNUE contra PeSTO (mateix executable), 5+0,05 | 200 | +135 =26 -39 | +182 ± 51 |
| **v0.10.0 contra v0.9.1**, 10+0,1 | 200 | +131 =31 -38 | **+175 ± 49**, LOS 100 % |

Les etiquetes venen d'un motor d'aproximadament 2400–2500 Elo i d'obertures aleatòries, i això limita la primera xarxa. El següent salt havia de venir de tornar a generar dades jugant amb aquesta xarxa (v0.11.0, a continuació).

### v0.11.0: dades noves barrejades amb les velles

- **Dades noves:** `ilerda datagen` amb la v0.10.0 (8.000 nodes) i el llibre propi `llibre.epd` (28.969 posicions): 500.000 partides en 4 tandes. Amb `tools/merge_shuffle.py` s'ajunten, es dedupliquen per les 4 primeres parts del FEN i es barregen: queden 22,0 M de posicions úniques (el 41 % eren duplicades, perquè el llibre és petit).
- **Les dades noves soles empitjoren la xarxa** (uns −140 Elo contra la 0.10.0, amb 20 i amb 8 superbatches). Falta diversitat: venen de poques obertures. Un control amb només les dades velles va donar +9, així que el procediment d'entrenament és correcte.
- **Barrejades amb les velles (≈72 M de posicions) sí que milloren**: la xarxa `mix` guanya uns +171 Elo contra la 0.10.0 (marge d'uns ±40). `tools/nnue/ilerda-train.ipynb` és el notebook de Kaggle.
- **Validació independent contra Stockfish** (`tools/comparar-sf.ps1`, `UCI_LimitStrength`, 10+0,1, 200 partides per cel·la, mateixes obertures). L'Elo de Stockfish només és una referència aproximada; compta la diferència entre les dues xarxes:

| Stockfish (`UCI_Elo`) | Xarxa 0.10.0 | Xarxa nova |
|---|---|---|
| 2200 | 81,8 % | 88,2 % |
| 2600 | 51,0 % | **70,0 %** (+140 Elo) |
| 3000 | 12,0 % | 17,0 % |

Com que la xarxa nova s'ha entrenat amb etiquetes de la 0.10.0 i s'ha provat contra ella, hi podria haver una mica d'inflació per autojoc; la prova contra Stockfish l'evita i confirma la millora.

### Intent de reentrenament amb més dades (no adoptat)

Després de la 0.11.0 es va provar de pujar el sostre amb més dades i una xarxa més gran. No es va adoptar cap xarxa: la 0.11.0 continua sent la millor.

- **Llibre gran:** `ilerda book` amb 120.000 línies (llavor 4242, marge 60, 20.000 nodes), més les 28.969 velles: 144.862 posicions.
- **Dades noves:** `ilerda datagen` amb la 0.11.0 (8.000 nodes), 2–4 semijugades aleatòries després del llibre: 500.000 partides (≈15 M de posicions). Barrejades amb les velles i deduplicades queden **109,2 M de posicions úniques**.
- **Entrenament:** 256 i 512 neurones ocultes, 20 superbatches a una T4 de Kaggle (`tools/nnue/ilerda-train.ipynb`, paràmetre `HIDDEN`). Pèrdua final: 0,0554 (256) i 0,0539 (512).

Resultats contra la 0.11.0 (cutechess-cli, mateixes obertures):

| Xarxa | Control | Partides | Resultat | Elo |
|---|---|---|---|---|
| 256 | 10+0,1 | 200 | +82 =54 -64 | +31 |
| 256 | 20+0,2 | 500 | +159 =192 -149 | **+7** (±25) |
| 512 | 10+0,1 | 200 | +33 =53 -114 | -149 |
| 512 | 40+0,4 | 240 | +48 =80 -112 | -95 |

Contra Stockfish `UCI_Elo` 2600 (10+0,1, 200 partides) la 256 va fer 75,5 % davant del 65,5 % de la 0.11.0, però la confirmació amb més partides ho deixa en un empat.

- **La 256 empata amb la 0.11.0:** més dades (109 M contra 72 M) no donen guany mesurable. Probablement el límit són les etiquetes (cerca de 8.000 nodes) i no la quantitat de dades.
- **La 512 perd:** és un 30 % més lenta (≈1,5 M de nodes/s contra ≈2,2 M) i, amb més temps, perd menys però continua perdent clarament. Tampoc es va veure cap guany que compensés la velocitat.
- Canvis conservats: `DataGen` fa 2–4 jugades aleatòries després del llibre, i el notebook té la mida (`HIDDEN`) com a paràmetre i fa servir CUDA 13.0 (el de Kaggle actual).

## SEE (provat i no adoptat)

El *Static Exchange Evaluation* simula les recaptures a la casella d'una captura per saber si perd material. Es va provar com a v0.8.0 a la branca `feature/see` (opcions UCI `See` i `SeeMargin`), contra la v0.7.0 a 10+0,1:

| Versió | Partides | Resultat | Elo | LOS |
|---|---|---|---|---|
| SEE complet (captures perdedores al final de l'ordenació i fora de la quiescència) | 400 | +137 =139 -124 | +11 ± 28 | ~79 % |
| Només a la quiescència (`See=2`) | 400 | +125 =146 -129 | -3,5 ± 27 | 40 % |

Variants contra el SEE complet (200 partides a 5+0,05, marge d'uns ±40 Elo): sense SEE -33, només ordenació -10, només quiescència +21 i quiescència amb marge de 100 cp +10. Tot queda dins del soroll i el +21 no es va confirmar. Cap versió arriba al LOS del 95 % que es demana per adoptar un canvi, així que no es fusiona. El codi es conserva a `feature/see`.

Abans, a la v0.8.0 també es va provar posar els escacs primer a l'ordenació (`prova/escacs-primer`, -9 ± 40 Elo, LOS 33 %), amb el mateix resultat.

## History de continuació (provada i no adoptada)

La *continuation history* puntua una jugada tranquil·la segons la peça i la casella d'arribada de les jugades fetes 1 i 2 plies abans (taula de 12×64×12×64 valors per fil, actualitzada amb el mateix bonus i malus que la history normal). S'hi va provar com a v0.13.2 a la branca `feature/conthist`, només per a l'ordenació de jugades tranquil·les (opció UCI `ContWeight`, el pes en %), contra la v0.13.1 a 5+0,05:

| Pes | Partides | Resultat | Elo |
|---|---|---|---|
| 100 % | 1.338 | +370 =552 -416 | -11,9 ± 14,3 |
| 30 % | 2.084 | +606 =871 -607 | -0,2 ± 11,4 |

Amb pes complet empitjora una mica (a `bench 12` també cerca més nodes: 9,44 M contra 9,00 M) i amb pes reduït queda en zero, a més de costar un 12 % de velocitat i 2,3 MB per fil. Els dos matches es van aturar abans d'hora, quan ja era clar que no hi havia guany. No es fusiona. Queda per provar com a senyal de LMR i de poda, que és on aporta més en altres motors.

## Llibre d'obertures propi (`ilerda book`)

`ilerda book <línies> <fitxer> [fils] [llavor] [nodes] [marge]` crea un llibre EPD per passar-lo a `datagen`. Per què no n'hi ha prou amb obertures aleatòries ni amb descarregar un llibre:

- **Les obertures de `datagen` eren 8–9 semijugades a l'atzar**, amb un únic filtre (±400 cp). Surten estructures absurdes (1.h4, 1.a4, Ta3, Ch3...) i el resultat final de la partida depèn més de l'error de l'obertura que de la qualitat del joc, que és el que Texel i la NNUE han d'aprendre.
- **Un llibre equilibrat dona dades més representatives:** posicions que s'assemblen a les de partides reals, menys duplicats i un resultat final menys sorollós. Per a Texel (uns 780 paràmetres) el guany és petit; per a la NNUE (molts més) la diversitat pesa més.
- **Un llibre propi evita dependre de descàrregues** (`8moves_v3`) i es pot regenerar amb una llavor o uns marges diferents.
- **Filtrar jugades a l'atzar no va bastar.** La primera idea era prohibir els peons de les columnes a, b, g, h, però és una regla fixa. Un filtre d'avaluació (descartar la línia si el motor la valora fora de ±30 cp després de les primeres jugades) tampoc: l'avaluació PeSTO no castiga 1.a4 ni Ta3, i descartava el 98 % dels intents sense treure les jugades estranyes. El mètode final és triar, a cada semijugada, **només entre les jugades que el motor valora a menys de 40 cp de la millor** (cerca de 1.500 nodes per candidata). Així cap jugada és absurda segons el motor.
- **Regla afegida:** a les 6 primeres semijugades no es mouen peons de les columnes a, b, g, h, ni cavalls a la vora, ni torres. L'avaluació sola no les veu com a dolentes.
- **Filtres finals:** la posició d'inici és dins de ±30 cp a les dues primeres semijugades i dins del marge (50 cp per defecte, cerca de 20.000 nodes) a la resta, sense escac, sense captura com a millor jugada i sense duplicats (clau Zobrist).

És més lent que el llibre aleatori (uns 10 % dels intents s'accepten). El llibre barrejat amb obertures aleatòries és una opció per a la NNUE: `datagen` accepta el llibre i fa 2–4 semijugades aleatòries després (0–2 fins a la 0.11.0; amb un llibre gran, més semijugades redueixen els duplicats).

## Full de ruta

- [x] Avaluació amb taules de posició (PeSTO)
- [x] Taula de transposició amb Zobrist, i detecció de repetició (v0.3: +230 Elo contra la v0.2 en partides ràpides)
- [x] Cerca multifil Lazy SMP (`Threads=4`: +111 Elo contra 1 fil; vegeu [Multifil](#multifil-lazy-smp))
- [x] Magic bitboards (v0.9.1: +18 % de nodes per segon amb els mateixos nodes; +21 ± 53 Elo contra la 0.9.0, 100 partides, LOS 78,1 %)
- [x] Killers i history (v0.4: +49 ± 59 Elo contra la v0.3, SPRT sense conclusió en 100 partides; s'hi dona per bona perquè no perd i arriba més lluny)
- [ ] Escacs primer a l'ordenació de jugades (provat i no adoptat: les jugades tranquil·les que donen escac directe es provaven després dels killers; contra la v0.7.0, 200 partides a 10+0,1, -9 ± 40 Elo, LOS 33 %, és a dir, indistingible del soroll)
- [x] Null move (v0.5: +74 ± 60 Elo contra la v0.4, SPRT sense conclusió en 100 partides però LOS 99 %)
- [x] LMR (v0.6.1: +85 ± 59 Elo contra la v0.5.0, LOS 99,8 %; la primera versió, v0.6.0, només va fer +35 ± 58). Vegeu [Proves d'afinament](#proves-dafinament)
- [x] Reverse futility i futility (v0.7.0: +74 ± 53 Elo contra la v0.6.1, LOS 99,7 %)
- [ ] SEE (provat i no adoptat; vegeu [SEE](#see-provat-i-no-adoptat))
- [x] Comanda `datagen` per generar dades d'autojoc
- [x] Proves automàtiques amb cutechess-cli i SPRT
- [x] Avaluació NNUE entrenada amb dades d'autojoc (v0.10.0: +175 ± 49 Elo contra la 0.9.1; vegeu [NNUE](#nnue))
- [x] PVS i finestres d'aspiració (v0.12.0: +86 Elo contra la 0.11.0, 277 partides vàlides a 5+0,05, +122 =100 -55; marge d'uns ±40 Elo)
- [x] Malus de history amb *gravity* (v0.13.0: +8,3 ± 7,3 Elo contra la 0.12.0, 5.000 partides a 5+0,05, +1490 =2139 -1371, LOS 98,7 %)
- [x] Taules per material insuficient (v0.13.1: +0,3 ± 10,2 Elo contra la 0.13.0, 2.656 partides a 5+0,05; és una correcció, no una millora de força: abans rei i alfil contra rei donava +269 cp)
- [ ] History de continuació (provada i no adoptada; vegeu [History de continuació](#history-de-continuació-provada-i-no-adoptada))
- [ ] Enviar a CCRL i CEGT, i després a les divisions baixes del TCEC

## Llicència

MIT. Vegeu [LICENSE](LICENSE).
