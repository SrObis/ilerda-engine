# Ilerda

Motor d'escacs UCI escrit en C#, fet des de zero. El nom és provisional (Ilerda és el nom romà de Lleida).

**Estat:** v0.1 — generador de moviments validat amb perft, cerca alfa-beta i protocol UCI. Encara no té xarxa NNUE.

## Què inclou

- Bitboards i generació de moviments pseudolegals amb comprovació de legalitat (enroc, captura al pas i promocions incloses).
- Cerca negamax amb alfa-beta, aprofundiment iteratiu, cerca de quiescència, extensió per escac i ordenació MVV-LVA.
- Control de temps (`wtime`, `btime`, `winc`, `binc`, `movestogo`, `movetime`, `depth`, `infinite`).
- Avaluació provisional: material + petits bonus posicionals.
- Comandes extra: `perft N`, `divide N` i `d` (mostra el tauler).

## Compilar i provar

Cal el [.NET SDK 8](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release src/Ilerda
dotnet run -c Release --project src/Ilerda -- test        # suite de perft
dotnet run -c Release --project src/Ilerda -- perft 5     # nodes des de la posició inicial
dotnet run -c Release --project src/Ilerda                # mode UCI
```

La suite de perft ha de passar sempre abans de tocar la cerca: si falla, hi ha un error al generador de moviments.

Per jugar-hi, afegeix l'executable (`src/Ilerda/bin/Release/net8.0/`) a Arena, En Croissant o Cute Chess com a motor UCI.

## Full de ruta

- [ ] Taula de transposició amb Zobrist, i detecció de repetició
- [ ] Magic bitboards (ara els atacs de peces lliscants es calculen amb rajos)
- [ ] Poda: null move, LMR, futility, killers i history
- [ ] Proves automàtiques amb fastchess/cutechess-cli i SPRT
- [ ] Avaluació NNUE entrenada amb dades d'autojoc
- [ ] Enviar a CCRL i CEGT, i després a les divisions baixes del TCEC

## Llicència

MIT. Vegeu [LICENSE](LICENSE).
