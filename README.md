<p align="center"><img src="assets/logo.png" alt="Ilerda" width="200"></p>

# Ilerda

Motor d'escacs UCI escrit en C#, fet des de zero. Ilerda és el nom romà de Lleida.

**Estat:** v0.15.1 — generador de moviments validat amb perft, cerca alfa-beta amb taula de transposició, detecció de repeticions, ordenació amb killers i history, PVS, finestres d'aspiració, poda null move, reducció iterativa interna (IIR), LMR, reverse futility i futility, extensions singulars, protocol UCI, cerca multifil (Lazy SMP, opció `Threads`) i avaluació NNUE (xarxa 768 → 256 → 1 amb 8 buckets de sortida segons el nombre de peces, incrustada a l'executable; les taules PeSTO es conserven com a alternativa) i lectura de taules Syzygy (WDL i DTZ) de fins a 6 peces. Vegeu el [CHANGELOG](CHANGELOG.md).

## Què inclou

- Bitboards (atacs de peces lliscants amb magic bitboards) i generació de moviments pseudolegals amb comprovació de legalitat (enroc, captura al pas i promocions incloses).
- Cerca negamax amb alfa-beta, aprofundiment iteratiu, cerca de quiescència, extensió per escac i ordenació MVV-LVA.
- Claus Zobrist incrementals, taula de transposició (opció UCI `Hash`, 64 MB per defecte) amb buckets de 4 entrades, edat i avaluació estàtica, i detecció de repeticions.
- Taules per material insuficient (`Position.InsufficientMaterial`): només reis, un sol cavall o alfil, o només alfils a caselles del mateix color. La cerca i la quiescència hi retornen 0, en lloc de fiar-se de l'avaluació de la xarxa.
- Ordenació de jugades tranquil·les amb killers (2 per nivell) i history amb *malus* i *gravity*: la jugada tranquil·la que talla rep un bonus (`depth²`, màxim 1500) i les tranquil·les provades abans que no han tallat reben el mateix valor en negatiu; el canvi es redueix a mesura que el valor s'acosta al màxim (6000), així queda acotat.
- PVS (*principal variation search*): la primera jugada de cada node es cerca amb finestra completa i la resta amb finestra nul·la; només es repeteixen amb finestra completa si superen alfa en un node PV.
- Finestres d'aspiració: a partir de profunditat 5 el fil principal cerca al voltant de la puntuació de la iteració anterior (±20 cp) i eixampla la finestra al doble cada cop que falla.
- Extensions singulars (des de la 0.14.2, opcions `SingDepth` i `SingMargin`): si la jugada de la taula de transposició és clarament millor que totes les altres, s'allarga un nivell; si ni sense ella es perd, el node es talla. Vegeu el [CHANGELOG](CHANGELOG.md).
- Poda null move (no en escac, ni sense peces, ni dues vegades seguides).
- LMR: les jugades tranquil·les que es proven tard es cerquen a menys profunditat i, si sorprenen, es tornen a cercar a fons. Els paràmetres són opcions UCI (`LmrDiv`, `LmrStart`, `LmrHist`).
- Reverse futility (profunditat ≤ 6) i futility (≤ 4), amb marges configurables (`RfpMargin`, `FutMargin`).
- Cerca multifil amb Lazy SMP (opció UCI `Threads`, 1 per defecte): els fils ajudants cerquen la mateixa posició amb una taula de transposició compartida sense bloquejos i el fil principal tria la jugada. Vegeu [Multifil](#multifil-lazy-smp).
- Control de temps (`wtime`, `btime`, `winc`, `binc`, `movestogo`, `movetime`, `depth`, `infinite`), amb opció `MoveOverhead` (20 ms per defecte). Des de la 0.13.2, temps òptim i límit dur: passat l'òptim no es comença cap iteració nova, i l'òptim s'allarga si la millor jugada canvia o la puntuació cau, i s'escurça si la jugada és estable (opcions `TmDiv` 50 i `TmHard` 500 %; `Tm=0` torna a la gestió antiga). Mai més d'un terç del temps que queda.
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

`test` també comprova la signatura del `bench 12` (10.841.872 nodes amb un fil, constant `ExpectedBench` a `PerftSuite.cs`): si canvia sense haver tocat la cerca o la xarxa a propòsit, hi ha un error. Quan es canvia a propòsit, s'actualitza la constant. GitHub Actions (`.github/workflows/test.yml`) executa `test` a cada `push` i `pull request`.

Per jugar-hi, afegeix l'executable (`src/Ilerda/bin/Release/net8.0/`) a Arena, En Croissant o Cute Chess com a motor UCI.

## Executables per jugar torneigs

`.\tools\publicar.ps1` genera a `dist\` els executables de la versió actual per a **Windows x64** i **Linux x64** (autocontinguts, un sol fitxer, no cal tenir .NET) i un `.zip` de cada un amb la llicència. No es fa servir NativeAOT a propòsit: la xarxa NNUE usa `Vector<T>`, que amb JIT tria AVX2 (o el que tingui la CPU) en executar-se, i amb AOT quedaria fixada al mínim (SSE2). Cal una CPU x64 qualsevol; no hi ha instruccions específiques.

Opcions UCI principals: `Hash` (64 MB per defecte, fins a 16.384 MB: més enllà el límit de mida dels arrays de .NET no deixaria fer la taula més gran), `Clear Hash` (botó: buida la taula de transposició i les taules d'història), `Threads` (1 per defecte, fins a 256), `MoveOverhead`, `SyzygyPath` (carpetes separades per `;` a Windows i `:` a Linux) i `EvalFile`. No hi ha `Ponder`.

## Comparar versions

Cada canvi s'ha de mesurar. `dotnet build` sobreescriu sempre `bin/Release/net8.0`, així que cada versió es guarda abans de seguir canviant el codi:

```powershell
.\tools\guardar-versio.ps1                    # compila i desa la còpia a Versions\Ilerda-<versió>\
.\tools\match.ps1 -Nou 0.3.0 -Vell 0.2.0     # matx amb cutechess-cli i test SPRT
```

El matx fa servir les 2.000 posicions de `tools/obertures.epd` (`-Openings` accepta un altre fitxer `.epd` o `.pgn`). Vegeu [Versions](Versions/README.md).

**Per què un banc d'obertures gran.** Fins a la 0.13.1 els matxs feien servir les 20 obertures de `tools/openings.pgn`: en 5.000 partides cada obertura sortia unes 250 vegades, i el resultat podia dependre més d'aquelles 20 posicions que de la força del motor. `tools/obertures.epd` s'ha generat amb `ilerda book` (dues tandes de 2.400 línies, llavors 20261106 i 777123, motor 0.13.1), i se n'han tret els duplicats i **totes les posicions que surten als llibres d'entrenament de la NNUE** (`data/llibre*.epd`): més de la meitat coincidien, i provar el motor amb posicions d'entrenament afavoreix la xarxa. En queden 2.145 i se'n prenen 2.000 a l'atzar. Amb 5.000 partides, cada posició es juga un cop o dos amb cada color. `ajustar.ps1`, `calibrar.ps1` i `comparar-sf.ps1` continuen amb `openings.pgn` perquè els seus resultats es puguin comparar amb els anteriors.

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

La mateixa mesura amb la **v0.13.2** (10+0,1, un fil, 200 partides per nivell, obertures de `tools/openings.pgn`, les mateixes de la prova de la xarxa de la 0.11.0):

| Stockfish (`UCI_Elo`) | Resultat | Puntuació | Diferència | Estimació |
|---|---|---|---|---|
| 2600 | +151 =23 -26 | 81,2 % | +255 ± 57 | ≈2855 |
| 3000 | +27 =74 -99 | 32,0 % | -131 ± 39 | ≈2869 |
| 3190 (el màxim) | +7 =49 -144 | 15,8 % | -291 ± 49 | ≈2899 |

Els tres nivells coincideixen en unes **2.870** en l'escala de Stockfish limitat a aquest ritme. La xarxa de la 0.11.0 hi feia el 70 % contra el 2600 i el 17 % contra el 3000 (≈2.740), així que de la 0.11.0 a la 0.13.2 hi ha uns **+130 Elo** contra un adversari extern, prop dels +205 que sumen els matxs contra la versió anterior. Tampoc és l'Elo de CCRL, que juga a ritmes més lents.

I amb la **v0.13.3** (mateixes condicions):

| Stockfish (`UCI_Elo`) | Resultat | Puntuació | Diferència | Estimació |
|---|---|---|---|---|
| 2600 | +167 =10 -23 | 86,0 % | +315 ± 68 | ≈2915 |
| 3000 | +35 =71 -94 | 35,2 % | -106 ± 40 | ≈2894 |
| 3190 (el màxim) | +7 =55 -138 | 17,2 % | -272 ± 46 | ≈2918 |

I amb la **v0.13.5** (mateixes condicions):

| Stockfish (`UCI_Elo`) | Resultat | Puntuació | Diferència | Estimació |
|---|---|---|---|---|
| 2600 | +158 =19 -23 | 83,8 % | +285 ± 59 | ≈2885 |
| 3000 | +30 =79 -91 | 34,8 % | -109 ± 38 | ≈2891 |
| 3190 (el màxim) | +12 =50 -138 | 18,5 % | -258 ± 47 | ≈2932 |

La mitjana dels tres nivells és d'unes **2.900**, igual que la 0.13.3 (≈2.910) dins del marge (uns ±30 de mitjana). Contra la 0.13.4 la 0.13.5 guanya +12,9 ± 7,2 Elo en autojoc, però contra Stockfish no es veu: l'autojoc exagera les millores, i aquesta, que és de cerca, no n'és l'excepció. De 600 partides se'n perden 252 (255 amb la 0.13.3): 141 acaben amb 8 peces o menys i en 187 Ilerda havia arribat a veure +1,0 o més, és a dir, es perd sobretot als finals i després d'avaluar-los massa bé.

I amb la **v0.13.7** (correcció de l'avaluació; mateixes condicions):

| Stockfish (`UCI_Elo`) | Resultat | Puntuació | Diferència | Estimació |
|---|---|---|---|---|
| 2600 | +177 =11 -12 | 91,2 % | +407 ± 82 | (massa a prop del límit) |
| 3000 | +30 =92 -78 | 38,0 % | -85 ± 36 | ≈2915 |
| 3190 (el màxim) | +10 =65 -125 | 21,2 % | -228 ± 43 | ≈2962 |

Uns **+30 Elo** respecte de la 0.13.6 (≈2.881 i ≈2.930 als dos nivells fiables), en la mateixa direcció en tots els nivells, i +6,5 ± 6,8 en 5.000 partides directes.

Mitjana d'unes **2.910**, uns **+35 Elo** per sobre de la 0.13.2 contra un adversari extern (contra la 0.13.2 directament, +118). Els marges de cada nivell són grans (±40–70), així que la diferència entre versions és orientativa.

## Taules Syzygy

Des de la **0.14.0** Ilerda llegeix taules de finals Syzygy de fins a 6 peces: WDL (fitxers `.rtbw`, a la cerca) i, des de la **0.14.1**, DTZ (fitxers `.rtbz`, a l'arrel). El lector és un port a C# del `tbprobe.c` de Fathom (MIT).

```
setoption name SyzygyPath value D:\tablebases\3-4-5;D:\tablebases\6
```

| Opció | Per defecte | Què fa |
|---|---|---|
| `SyzygyPath` | buit | Carpetes amb les taules, separades per `;`. Buit desactiva les taules. |
| `SyzygyProbeDepth` | 1 | Profunditat mínima restant per sondejar dins la cerca. |
| `SyzygyProbeLimit` | 7 | Màxim de peces per sondejar (es limita al de les taules trobades). |
| `SyzygyRoot` | 1 | A l'arrel, només es cerquen les jugades que conserven el millor resultat segons el DTZ (0 ho desactiva). |

A la cerca, una posició sense enroc i amb el comptador de 50 jugades a 0 que és a les taules retorna victòria, taules o derrota exactes (`±(20000 - ply)`, per sota de les puntuacions de mat). Les victòries "maleïdes" (taules per la regla dels 50) compten com a taules.

**Validació:** `tools/syzygy/oracle.py` genera posicions aleatòries amb python-chess i l'ordre `tbcheck <fitxer>` les compara: 0 diferències en 30.500 posicions de 3 a 6 peces.

**Resultat de la 0.14.0 (només WDL):** 5.000 partides contra la 0.13.7 (5+0,05): -0,8 ± 6,7 Elo, empat tècnic, perquè el WDL sol no ensenya a convertir els finals guanyats.

**DTZ a l'arrel (0.14.1).** A l'arrel, cada jugada legal es puntua segons el DTZ de la posició resultant i la regla dels 50, i només es cerca entre les millors: amb victòria, la de DTZ mínim; amb derrota, la que resisteix més; amb taules, les que no perden. `tools/syzygy/conversio.py` juga Ilerda contra si mateix des de posicions guanyades: de 30 posicions, sense DTZ convertia 3 (alfil i cavall contra rei), 19 (torre i peó contra torre) o 24 (dama i torre contra torre i peó), i amb DTZ 30, 30 i 30.

Com que les partides normals gairebé no arriben a aquests finals, es va mesurar amb un llibre de 1.386 finals guanyats (`tools/syzygy/finals-guanyats.epd`, de 3 a 6 peces, DTZ entre 10 i 100): 2.000 partides a 10+0,1 contra la 0.14.0, totes dues amb taules, **+108,7 ± 13,0 Elo** (+1000 =606 -394). Amb el bàndol guanyador la 0.14.1 converteix 1.000 de 1.000 i la 0.14.0 només 394 (606 taules, 260 per la regla dels 50). Mesura la conversió dels finals, no la força general. Sense `SyzygyPath`, la cerca és idèntica.

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

L'avaluació és una xarxa **768 → 256 (×2 perspectives) → 1** amb activació SCReLU i **8 buckets de sortida** pel nombre de peces (des de la 0.15.0), entrenada amb [bullet](https://github.com/jw1912/bullet) i incrustada a l'executable (`src/Ilerda/nets/ilerda-256.bin`). Opció UCI `EvalFile`: buida = xarxa incrustada, `none` = avaluació PeSTO, o la ruta d'un altre `quantised.bin` de bullet.

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

### v0.15.0: buckets de sortida i etiquetes de les taules

- **8 buckets de sortida.** La capa de sortida té un joc de pesos per cada tram de nombre de peces (reis inclosos): `bucket = (peces - 2) / 4`. `Nnue.cs` detecta el format per la mida del fitxer (401.984 bytes amb 8 buckets) i també llegeix les xarxes sense buckets.
- **`ilerda tbrelabel`.** Posa a les posicions de 6 peces o menys el resultat exacte de Syzygy en lloc del de la partida. Sobre `mix2.txt` canvia 1,86 M de resultats d'un total de 109,2 M de línies (2,1 minuts). La puntuació no es toca. Vegeu [tools/nnue/buckets.md](tools/nnue/buckets.md).
- **Resultat:** +42,9 ± 7,7 Elo contra la 0.14.2 (5.000 partides a 5+0,05, +1885 =1844 −1271). El calibratge millora: amb +0,5 a +1 d'avaluació en el mig joc amb material igual, la puntuació real passa del 47,8 % al 60,2 %. Les derrotes passen a ser sobretot de mig joc: de 559 que arriben a 6 peces o menys, 553 ja estaven perdudes en entrar-hi.
- **Torneig contra cinc motors externs** de la [CCRL 40/15](https://www.computerchess.org.uk/ccrl/4040/) (Elo entre 2866 i 3240; 5.003 partides, 10+0,1, un fil, 16 MB, sense taules): **≈3011 ± 9 Elo** en l'escala de la CCRL, amb el nostre maquinari i control de temps (no és un Elo oficial). Vegeu el [CHANGELOG](CHANGELOG.md).

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

| Match | Partides | Resultat | Elo |
|---|---|---|---|
| Primer | 1.338 | +370 =552 -416 | -11,9 ± 14,3 |
| Segon | 2.084 | +606 =871 -607 | -0,2 ± 11,4 |
| Total | 3.422 | +976 =1423 -1023 | -4,8 ± 9 |

**Correcció:** el segon match havia de ser amb pes del 30 %, però `ContWeight` no estava declarada a la resposta `uci` i cutechess només envia les opcions que el motor anuncia. Els dos matches es van jugar, doncs, **amb pes complet**, i el pes reduït mai s'ha provat de debò. Amb pes complet no hi ha guany (a `bench 12` també cerca més nodes: 9,44 M contra 9,00 M) i costa un 12 % de velocitat i 2,3 MB per fil. Els matches es van aturar abans d'hora, quan ja era clar que no hi havia guany. No es fusiona. Queda per provar amb el pes reduït i com a senyal de LMR i de poda, que és on aporta més en altres motors.

## LMP i *improving* (provat i no adoptat)

La *late move pruning* (LMP) descarta les jugades tranquil·les tardanes a poca profunditat, i la bandera *improving* (l'avaluació estàtica és millor que la de fa 2 plies) fa la poda més agressiva quan la posició no millora i més suau quan sí. S'hi va provar com a v0.13.2 a la branca `feature/lmp-improving` (opcions UCI `LmpDepth` i `Improving`), contra la v0.13.1 a 5+0,05:

- **LMP:** a profunditat ≤ 8, es descarta la jugada tardana si ja se n'han provat més de `3 + profunditat²` (la meitat si no millora).
- **Improving:** menys marge de RFP quan millora, i una reducció extra de LMR quan no millora.

| Versió | Partides | Resultat | Elo |
|---|---|---|---|
| LMP + improving | 5.000 | +1019 =1739 -2242 | -86,7 ± 7,9 |
| Només improving (`LmpDepth=0`) | 800 | +197 =326 -277 | -34,9 ± 18,6 |
| Només LMP (`Improving=0`) | 800 | +189 =338 -273 | -36,6 ± 18,3 |

Cada part per separat perd uns 35 Elo, i juntes es sumen. Amb tot activat el motor cerca uns 0,6 plies més, però avalua unes 23 cp més optimista i perd sobretot finals de partida: poda massa. Possible causa: el llindar compta totes les jugades legals, no només les tranquil·les, i arriba fins a profunditat 8, massa per a una xarxa tan petita. No es fusiona. Queda per provar una LMP més suau (profunditat ≤ 4 i comptant només tranquil·les).

**Nota sobre les proves:** els primers matches d'ablació van ser invàlids per la mateixa raó que a la secció anterior (opcions no declarades); les xifres de dalt són de la repetició amb les opcions declarades a `uci`. Des d'ara, abans de cada match es comprova que la profunditat mitjana canviï amb l'opció.

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
- [x] Gestió del temps amb temps òptim i límit dur (v0.13.2: +110,9 ± 8,4 Elo contra la 0.13.1, 4.228 partides a 5+0,05, +1965 =1603 -660; la cerca és idèntica, només canvia com es reparteix el rellotge)
- [x] Pila d'acumuladors NNUE i actualització en una sola passada (v0.13.3: +50 % de nodes per segon amb els mateixos nodes; +118,1 ± 9,8 Elo contra la 0.13.2, 3.020 partides a 5+0,05, +1415 =1179 -426)
- [x] Taula de transposició amb buckets, edat i avaluació estàtica (v0.13.4: canvi neutre en força, +3,2 ± 7,8 Elo contra la 0.13.3, 4.202 partides a 5+0,05, +1172 =1897 -1133; es fusiona com a base de les singular extensions i perquè ajuda amb `Hash` petit: amb 1 MB, `bench 12` fa 9,91 M nodes contra 11,45 M)
- [x] Reducció iterativa interna i taula de transposició a la quiescència (v0.13.5: +12,9 ± 7,2 Elo contra la 0.13.4, 5.000 partides a 5+0,05, +1503 =2179 -1318; `bench 12` fa un 29 % menys de nodes i un 21 % menys de temps fins a la mateixa profunditat)
- [x] Triple repetició per a les posicions anteriors a l'arrel (v0.13.6: correcció d'un defecte, no una millora de força mesurable: unes 2.900 contra Stockfish limitat, igual que la 0.13.5; les jugades amb avaluació 0,00 a profunditat 64 baixen de 785 a 327 en 600 partides)
- [x] Correcció de l'avaluació per estructura de peons (v0.13.7: +6,5 ± 6,8 Elo contra la 0.13.6, 5.000 partides; uns +30 Elo contra Stockfish limitat)
- [x] Taules Syzygy WDL (v0.14.0: 0 diferències contra python-chess en 30.500 posicions; -0,8 ± 6,7 Elo contra la 0.13.7 en 5.000 partides; el guany ha de venir del DTZ)
- [x] Syzygy DTZ a l'arrel (v0.14.1: 0 diferències de DTZ contra python-chess en 30.500 posicions; +108,7 ± 13,0 Elo en 2.000 partides des de finals guanyats, converteix 1.000 de 1.000 contra 394 sense DTZ)
- [x] Extensions singulars (v0.14.2: +23,5 ± 6,8 Elo contra la 0.14.1, 5.000 partides a 5+0,05, +1410 =2518 -1072, LOS 100 %; `bench 12` fa un 64 % més de nodes; contra Stockfish limitat ≈2924 al 3000 i ≈2978 al 3190, uns +8 Elo de mitjana sobre la 0.13.7, dins del marge)
- [x] NNUE amb 8 buckets de sortida i etiquetes exactes de les taules per a 6 peces o menys (v0.15.0: +42,9 ± 7,7 Elo contra la 0.14.2, 5.000 partides a 5+0,05, +1885 =1844 −1271, LOS 100 %; ≈3011 ± 9 Elo en l'escala de la CCRL contra cinc motors externs, 5.003 partides a 10+0,1)
- [ ] History de continuació (provada i no adoptada; vegeu [History de continuació](#history-de-continuació-provada-i-no-adoptada))
- [ ] Enviar a CCRL i CEGT, i després a les divisions baixes del TCEC

## Llicència

MIT. Vegeu [LICENSE](LICENSE). El lector de taules Syzygy adapta Fathom, també MIT; vegeu [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
