"""Ajunta, deduplica i barreja fitxers de datagen (linies "FEN | puntuacio | resultat").

Us: python merge_shuffle.py sortida.txt entrada1.txt entrada2.txt ...

Deduplica per les 4 primeres parts del FEN (peces, torn, enroc, al pas),
ignorant els comptadors de mitjos moviments. Descarta linies mal formades
(p. ex. l'ultima tallada per una aturada). Barreja amb un repartiment
aleatori en cubells i un barreig de cada cubell en memoria.
"""
import os
import random
import sys

NBUCKETS = 32


def main():
    out_path, in_paths = sys.argv[1], sys.argv[2:]
    tmp = [out_path + f".tmp{i}" for i in range(NBUCKETS)]
    files = [open(p, "wb") for p in tmp]
    rng = random.Random(12345)
    seen = set()
    total = bad = dup = 0

    for path in in_paths:
        with open(path, "rb") as f:
            for line in f:
                total += 1
                parts = line.rstrip(b"\r\n").split(b" | ")
                if len(parts) != 3 or not line.endswith(b"\n"):
                    bad += 1
                    continue
                fen = parts[0].split(b" ")
                if len(fen) < 4:
                    bad += 1
                    continue
                try:
                    int(parts[1])
                    float(parts[2])
                except ValueError:
                    bad += 1
                    continue
                h = hash(b" ".join(fen[:4]))
                if h in seen:
                    dup += 1
                    continue
                seen.add(h)
                files[rng.randrange(NBUCKETS)].write(line)
        print(f"{path}: acumulat {total} linies, {len(seen)} unics", flush=True)

    for f in files:
        f.close()
    del seen

    kept = 0
    with open(out_path, "wb") as out:
        for p in tmp:
            with open(p, "rb") as f:
                lines = f.readlines()
            rng.shuffle(lines)
            out.writelines(lines)
            kept += len(lines)
            os.remove(p)
    print(f"total {total}, mal formades {bad}, duplicades {dup}, finals {kept}")


if __name__ == "__main__":
    main()
