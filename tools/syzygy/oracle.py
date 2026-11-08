#!/usr/bin/env python3
"""Oracle de proves per al lector Syzygy d'Ilerda: genera posicions aleatories d'un material donat i desa
"FEN | wdl | dtz" tal com ho dona python-chess (que es GPL-3.0: s'usa nomes com a programa extern, no se'n copia
codi). Despres es compara amb el que dona Syzygy.cs sobre les mateixes posicions.

wdl: de -2 (derrota) a 2 (victoria) des del bandol que juga; -1 i 1 son les "maleides" (taules per la regla dels 50).

Us: python oracle.py CARPETA_TABLEBASES (varies separades per ';') MATERIAL N SORTIDA [SEED]
    ex.: python oracle.py D:/tablebases/3-4-5 KRPvKR 2000 oracle-krpkr.txt
"""
import random
import sys

import chess
import chess.syzygy

PECES = {"K": chess.KING, "Q": chess.QUEEN, "R": chess.ROOK, "B": chess.BISHOP, "N": chess.KNIGHT, "P": chess.PAWN}


def posicio_aleatoria(material: str, rng: random.Random):
    blanques, negres = material.split("v")
    for _ in range(1000):
        b = chess.Board(None)
        lliures = list(chess.SQUARES)
        rng.shuffle(lliures)
        ok = True
        for color, llista in ((chess.WHITE, blanques), (chess.BLACK, negres)):
            for lletra in llista:
                tipus = PECES[lletra]
                for sq in lliures:
                    # els peons no poden anar a la primera ni a l'ultima fila
                    if tipus == chess.PAWN and chess.square_rank(sq) in (0, 7):
                        continue
                    lliures.remove(sq)
                    b.set_piece_at(sq, chess.Piece(tipus, color))
                    break
                else:
                    ok = False
        if not ok:
            continue
        b.turn = rng.choice([chess.WHITE, chess.BLACK])
        if not b.is_valid():
            continue
        # sense enroc ni al pas: les taules no els tenen
        b.castling_rights = 0
        b.ep_square = None
        b.halfmove_clock = 0
        if b.is_game_over(claim_draw=False):
            continue
        return b
    raise RuntimeError("no trobo cap posicio valida")


def main() -> None:
    carpeta, material, n, sortida = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    seed = int(sys.argv[5]) if len(sys.argv) > 5 else 1
    rng = random.Random(seed)
    carpetes = carpeta.split(";")
    with chess.syzygy.open_tablebase(carpetes[0]) as tb, open(sortida, "w", encoding="utf8", newline="\n") as out:
        for extra in carpetes[1:]:
            tb.add_directory(extra)
        fets = 0
        while fets < n:
            b = posicio_aleatoria(material, rng)
            try:
                wdl = tb.probe_wdl(b)
                dtz = tb.probe_dtz(b)
            except (KeyError, chess.syzygy.MissingTableError):
                sys.exit(f"falta la taula de {material} a {carpeta}")
            out.write(f"{b.fen()} | {wdl} | {dtz}\n")
            fets += 1
    print(f"{fets} posicions de {material} -> {sortida}")


if __name__ == "__main__":
    main()
