#!/usr/bin/env python3
"""Converteix els PGN dels matxs de prova (cutechess, amb comentaris "{+0.73/12 0.30s}") al format de datagen
("FEN | puntuacio (cp, des de les blanques) | resultat") per fer-los servir com a conjunt de validacio o com a
complement petit d'entrenament.

Per no contaminar les proves:
  - es descarten les primeres --min-ply semijugades de cada partida (les obertures de tools/obertures.epd);
  - es descarta qualsevol posicio que coincideixi amb una de les obertures de l'EPD;
  - es descarten les partides contra Stockfish (les seves avaluacions no son d'Ilerda); amb --amb-stockfish
    s'inclouen, pero nomes les jugades d'Ilerda;
  - 1 de cada --val-cada partides (per hash del seu contingut, determinista) va al fitxer de validacio.

Com datagen, nomes es desen posicions tranquil.les: sense escac al bandol que juga i amb la jugada feta que no
es captura ni promocio. Tambe es descarten mats, avaluacions de mes de --max-cp, profunditat petita i 50 jugades.

Cal python-chess: pip install chess

Us: python pgn_a_dades.py sortida-prefix entrada1.pgn entrada2.pgn ...
    -> sortida-prefix-train.txt i sortida-prefix-val.txt
"""
import argparse
import hashlib
import re
import sys

import chess
import chess.pgn

RX = re.compile(r"^([+-]?)(\d+(?:\.\d+)?)/(\d+)\b")  # "+0.73/12 0.30s"; els mats ("+M5/..") no coincideixen


def clau(fen: str) -> str:
    return " ".join(fen.split(" ")[:4])


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("prefix")
    ap.add_argument("pgns", nargs="+")
    ap.add_argument("--epd", default="tools/obertures.epd")
    ap.add_argument("--min-ply", type=int, default=16)
    ap.add_argument("--min-depth", type=int, default=8)
    ap.add_argument("--max-cp", type=int, default=2000)
    ap.add_argument("--val-cada", type=int, default=20)
    ap.add_argument("--amb-stockfish", action="store_true")
    a = ap.parse_args()

    obertures = set()
    try:
        with open(a.epd, encoding="utf8") as f:
            for l in f:
                if l.strip():
                    obertures.add(clau(l.strip()))
    except OSError as e:
        sys.exit(f"no puc llegir {a.epd}: {e}")

    out = {"train": open(a.prefix + "-train.txt", "w", encoding="utf8", newline="\n"),
           "val": open(a.prefix + "-val.txt", "w", encoding="utf8", newline="\n")}
    n = {"partides": 0, "sf": 0, "posicions": 0, "obertura": 0}

    for path in a.pgns:
        with open(path, encoding="utf8", errors="replace") as fh:
            while True:
                g = chess.pgn.read_game(fh)
                if g is None:
                    break
                res = {"1-0": "1.0", "0-1": "0.0", "1/2-1/2": "0.5"}.get(g.headers.get("Result", ""))
                if res is None:
                    continue
                blanques, negres = g.headers.get("White", ""), g.headers.get("Black", "")
                amb_sf = "SF" in (blanques, negres) or "Stockfish" in blanques + negres
                if amb_sf and not a.amb_stockfish:
                    n["sf"] += 1
                    continue
                n["partides"] += 1
                moves = [x.uci() for x in g.mainline_moves()]
                h = hashlib.md5(" ".join(moves).encode()).digest()
                dest = out["val"] if int.from_bytes(h[:4], "little") % a.val_cada == 0 else out["train"]

                b = g.board()
                for node in g.mainline():
                    mv = node.move
                    mover_white = b.turn == chess.WHITE
                    ilerda = (blanques if mover_white else negres) == "Ilerda" or not amb_sf
                    m = RX.match(node.comment.strip())
                    if m and ilerda and b.ply() >= a.min_ply and int(m.group(3)) >= a.min_depth:
                        cp = int(round(float(m.group(2)) * 100)) * (-1 if m.group(1) == "-" else 1)
                        if abs(cp) <= a.max_cp and not b.is_check() and not b.is_capture(mv) \
                                and mv.promotion is None and b.halfmove_clock < 50:
                            fen = b.fen()
                            if clau(fen) in obertures:
                                n["obertura"] += 1
                            else:
                                blanc = cp if mover_white else -cp   # l'avaluacio del comentari es del bandol que juga
                                dest.write(f"{fen} | {blanc} | {res}\n")
                                n["posicions"] += 1
                    b.push(mv)
                if n["partides"] % 2000 == 0:
                    print(f"{n['partides']} partides, {n['posicions']} posicions", file=sys.stderr)

    for f in out.values():
        f.close()
    print(n)


if __name__ == "__main__":
    main()
