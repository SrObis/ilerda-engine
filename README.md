# Ilerda

Motor d'escacs UCI escrit en C#, fet des de zero. El nom és provisional (Ilerda és el nom romà de Lleida).

**Estat:** v0.6 — generador de moviments validat amb perft, cerca alfa-beta amb taula de transposició, detecció de repeticions, ordenació amb killers i history, poda null move, LMR, protocol UCI i avaluació amb taules de posició. Encara no té xarxa NNUE. Vegeu el [CHANGELOG](CHANGELOG.md).

## Què inclou

- Bitboards i generació de moviments pseudolegals amb comprovació de legalitat (enroc, captura al pas i promocions incloses).
- Cerca negamax amb alfa-beta, aprofundiment iteratiu, cerca de quiescència, extensió per escac i ordenació MVV-LVA.
- Claus Zobrist incrementals, taula de transposició (opció UCI `Hash`, 64 MB per defecte) i detecció de repeticions.
- Ordenació de jugades tranquil·les amb killers (2 per nivell) i history.
- Poda null move (no en escac, ni sense peces, ni dues vegades seguides).
- LMR: les jugades tranquil·les que es proven tard es cerquen a menys profunditat i, si sorprenen, es tornen a cercar a fons. Els paràmetres són opcions UCI (`LmrDiv`, `LmrStart`, `LmrHist`).
- Control de temps (`wtime`, `btime`, `winc`, `binc`, `movestogo`, `movetime`, `depth`, `infinite`).
- Avaluació *tapered* amb taules de posició PeSTO (mig joc i final, barrejats segons la fase de la partida).
- Comandes extra: `perft N`, `divide N` i `d` (mostra el tauler).

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

Referència externa (v0.4.0 contra Stockfish amb `UCI_Elo` limitat, 10+0,1, 40 partides per nivell): 84 % al 1800, 63 % al 2100, 50 % al 2400 i 19 % al 2700, és a dir, al voltant de 2400 en aquest test. És una referència orientativa: el ritme és molt ràpid i l'escala de Stockfish no és la de CCRL. Vegeu `tools/calibrar.ps1`.

## Full de ruta

- [x] Avaluació amb taules de posició (PeSTO)
- [x] Taula de transposició amb Zobrist, i detecció de repetició (v0.3: +230 Elo contra la v0.2 en partides ràpides)
- [ ] Magic bitboards (ara els atacs de peces lliscants es calculen amb rajos)
- [x] Killers i history (v0.4: +49 ± 59 Elo contra la v0.3, SPRT sense conclusió en 100 partides; s'hi dona per bona perquè no perd i arriba més lluny)
- [ ] Escacs primer a l'ordenació de jugades
- [x] Null move (v0.5: +74 ± 60 Elo contra la v0.4, SPRT sense conclusió en 100 partides però LOS 99 %)
- [x] LMR (v0.6.1: +85 ± 59 Elo contra la v0.5.0, LOS 99,8 %; la primera versió, v0.6.0, només va fer +35 ± 58). Vegeu [Proves d'afinament](#proves-dafinament)
- [ ] Poda: futility, SEE
- [x] Proves automàtiques amb cutechess-cli i SPRT
- [ ] Avaluació NNUE entrenada amb dades d'autojoc
- [ ] Enviar a CCRL i CEGT, i després a les divisions baixes del TCEC

## Llicència

MIT. Vegeu [LICENSE](LICENSE).
