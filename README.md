# Ilerda

Motor d'escacs UCI escrit en C#, fet des de zero. El nom és provisional (Ilerda és el nom romà de Lleida).

**Estat:** v0.7 — generador de moviments validat amb perft, cerca alfa-beta amb taula de transposició, detecció de repeticions, ordenació amb killers i history, poda null move, LMR, reverse futility i futility, protocol UCI i avaluació amb taules de posició. Encara no té xarxa NNUE. Vegeu el [CHANGELOG](CHANGELOG.md).

## Què inclou

- Bitboards i generació de moviments pseudolegals amb comprovació de legalitat (enroc, captura al pas i promocions incloses).
- Cerca negamax amb alfa-beta, aprofundiment iteratiu, cerca de quiescència, extensió per escac i ordenació MVV-LVA.
- Claus Zobrist incrementals, taula de transposició (opció UCI `Hash`, 64 MB per defecte) i detecció de repeticions.
- Ordenació de jugades tranquil·les amb killers (2 per nivell) i history.
- Poda null move (no en escac, ni sense peces, ni dues vegades seguides).
- LMR: les jugades tranquil·les que es proven tard es cerquen a menys profunditat i, si sorprenen, es tornen a cercar a fons. Els paràmetres són opcions UCI (`LmrDiv`, `LmrStart`, `LmrHist`).
- Reverse futility (profunditat ≤ 6) i futility (≤ 4), amb marges configurables (`RfpMargin`, `FutMargin`).
- Control de temps (`wtime`, `btime`, `winc`, `binc`, `movestogo`, `movetime`, `depth`, `infinite`).
- Avaluació *tapered* amb taules de posició PeSTO (mig joc i final, barrejats segons la fase de la partida).
- Comandes extra: `perft N`, `divide N` i `d` (mostra el tauler).
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

## SEE (provat i no adoptat)

El *Static Exchange Evaluation* simula les recaptures a la casella d'una captura per saber si perd material. Es va provar com a v0.8.0 a la branca `feature/see` (opcions UCI `See` i `SeeMargin`), contra la v0.7.0 a 10+0,1:

| Versió | Partides | Resultat | Elo | LOS |
|---|---|---|---|---|
| SEE complet (captures perdedores al final de l'ordenació i fora de la quiescència) | 400 | +137 =139 -124 | +11 ± 28 | ~79 % |
| Només a la quiescència (`See=2`) | 400 | +125 =146 -129 | -3,5 ± 27 | 40 % |

Variants contra el SEE complet (200 partides a 5+0,05, marge d'uns ±40 Elo): sense SEE -33, només ordenació -10, només quiescència +21 i quiescència amb marge de 100 cp +10. Tot queda dins del soroll i el +21 no es va confirmar. Cap versió arriba al LOS del 95 % que es demana per adoptar un canvi, així que no es fusiona. El codi es conserva a `feature/see`.

Abans, a la v0.8.0 també es va provar posar els escacs primer a l'ordenació (`prova/escacs-primer`, -9 ± 40 Elo, LOS 33 %), amb el mateix resultat.

## Deduplicar les dades (`tools/dedup`)

`dotnet run -c Release --project tools/dedup -- <entrada> <sortida> [bits]` agrupa les posicions repetides d'un fitxer de `datagen`. És un projecte C# a part, a `tools/`, i no forma part del motor.

- **Per què:** Texel i la NNUE tracten cada línia com una observació. Una posició repetida mil vegades pesa mil vegades més que una d'única, i les estimacions d'èxit es fixen a les obertures.
- **Com:** la posició es compara pels quatre primers camps del FEN (peces, torn, enroc i al pas); el comptador de mitja jugada i el de jugades no la canvien. Es fa en dues passades i la clau és un hash de 64 bits (la probabilitat de col·lisió amb ~100 M posicions és de l'ordre de 3·10⁻⁴).
- **Format de sortida:** `FEN | puntuació mitjana | victòries blanques | taules | victòries negres`. En comptes d'escollir una sola aparició, les ramificacions d'una posició (partides que hi acaben de maneres diferents) es conserven com a comptadors, i el resultat mitjà és una estimació més fiable que un sol 1, 0,5 o 0.
- **Memòria:** la taula té 2^`bits` entrades (27 per defecte, uns 3,8 GB; cal que sigui més gran que 1,3 vegades les posicions úniques). Amb 28 bits en fa uns 7,5 GB. Si es queda curta, s'atura amb un missatge.

**Resultat amb les dades actuals** (`datagen-001`, `-002`, `-003` i `-004` ajuntats): 113.487.990 línies → **107.041.927 posicions úniques**, 0 línies no vàlides, 2,6 min (28 bits). Només un **5,7 %** eren repetides, molt menys del que s'esperava: les obertures aleatòries de 8–9 semijugades són prou variades. Per tant la deduplicació és sobretot higiene de dades i no millora gaire l'entrenament; l'estimació agrupada només afecta les poques posicions repetides. El que pesarà més és barrejar bé per partida, la qualitat de les etiquetes (5.000 nodes) i la varietat d'obertures (vegeu el llibre d'obertures propi a la branca `feature/llibre`).

## Full de ruta

- [x] Avaluació amb taules de posició (PeSTO)
- [x] Taula de transposició amb Zobrist, i detecció de repetició (v0.3: +230 Elo contra la v0.2 en partides ràpides)
- [ ] Magic bitboards (ara els atacs de peces lliscants es calculen amb rajos)
- [x] Killers i history (v0.4: +49 ± 59 Elo contra la v0.3, SPRT sense conclusió en 100 partides; s'hi dona per bona perquè no perd i arriba més lluny)
- [ ] Escacs primer a l'ordenació de jugades (provat i no adoptat: les jugades tranquil·les que donen escac directe es provaven després dels killers; contra la v0.7.0, 200 partides a 10+0,1, -9 ± 40 Elo, LOS 33 %, és a dir, indistingible del soroll)
- [x] Null move (v0.5: +74 ± 60 Elo contra la v0.4, SPRT sense conclusió en 100 partides però LOS 99 %)
- [x] LMR (v0.6.1: +85 ± 59 Elo contra la v0.5.0, LOS 99,8 %; la primera versió, v0.6.0, només va fer +35 ± 58). Vegeu [Proves d'afinament](#proves-dafinament)
- [x] Reverse futility i futility (v0.7.0: +74 ± 53 Elo contra la v0.6.1, LOS 99,7 %)
- [ ] SEE (provat i no adoptat; vegeu [SEE](#see-provat-i-no-adoptat))
- [x] Comanda `datagen` per generar dades d'autojoc
- [x] Proves automàtiques amb cutechess-cli i SPRT
- [ ] Avaluació NNUE entrenada amb dades d'autojoc
- [ ] Enviar a CCRL i CEGT, i després a les divisions baixes del TCEC

## Llicència

MIT. Vegeu [LICENSE](LICENSE).
