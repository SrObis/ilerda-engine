#!/usr/bin/env python3
"""Prova de conversio de finals guanyats: juga Ilerda contra si mateix des de posicions guanyades (WDL = 2) i compta
quantes acaben en mat. Serveix per veure que l'arrel amb DTZ converteix on el WDL sol no ho fa.

Us: python conversio.py EXE CARPETES_TB FITXER_ORACLE N MOVETIME_MS SYZYGYROOT(0|1) [MAXJUGADES]
    ex.: python conversio.py Ilerda.exe "D:/tablebases/3-4-5;D:/tablebases/6" or-KRPvKR.txt 50 50 1
Les linies de l'oracle son "FEN | wdl | dtz"; nomes es fan servir les de wdl = 2.
"""
import sys

import chess
import chess.engine


def main() -> None:
    exe, tb, fitxer, n, ms, root = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
    maxj = int(sys.argv[7]) if len(sys.argv) > 7 else 200
    fens = []
    with open(fitxer, encoding="utf8") as f:
        for l in f:
            p = [x.strip() for x in l.split("|")]
            if len(p) >= 3 and p[1] == "2":
                fens.append(p[0])
            if len(fens) >= n:
                break

    eng = chess.engine.SimpleEngine.popen_uci(exe)
    eng.configure({"SyzygyPath": tb, "SyzygyRoot": root})
    mats = taules = altres = 0
    jugades_mat = []
    for fen in fens:
        b = chess.Board(fen)
        eng.protocol  # noqa
        try:
            eng.send_opponent_information  # noqa
        except AttributeError:
            pass
        for _ in range(maxj):
            if b.is_game_over(claim_draw=True):
                break
            r = eng.play(b, chess.engine.Limit(time=ms / 1000.0))
            b.push(r.move)
        if b.is_checkmate():
            mats += 1
            jugades_mat.append(len(b.move_stack))
        else:
            taules += 1
    eng.quit()
    mitja = sum(jugades_mat) / len(jugades_mat) if jugades_mat else 0
    print(f"SyzygyRoot={root}: {len(fens)} posicions guanyades, {mats} mats, {taules} sense mat; mitjana {mitja:.1f} semijugades fins al mat")


if __name__ == "__main__":
    main()
