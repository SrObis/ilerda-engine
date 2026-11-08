#!/usr/bin/env python3
"""Converteix una xarxa quantised.bin sense buckets (768 -> N -> 1) al format amb 8 buckets de sortida,
repetint els pesos de sortida a cada bucket. L'avaluació ha de ser idèntica a la de la xarxa original,
així que serveix per provar el lector de buckets (el bench ha de donar els mateixos nodes).

Format de sortida (igual que el que treu bullet amb .transpose() a l1w): i16 little-endian,
l0w [768][N], l0b [N], l1w [bucket][2N], l1b [bucket], omplert a múltiple de 64 bytes.

Ús: python replicar-buckets.py entrada.bin sortida.bin [buckets=8]
"""
import struct
import sys


def main() -> None:
    src, dst = sys.argv[1], sys.argv[2]
    nb = int(sys.argv[3]) if len(sys.argv) > 3 else 8
    raw = open(src, "rb").read()

    h = 0
    for n in range(16, 4097, 16):
        size = (768 * n + n + 2 * n + 1) * 2
        if (size + 63) // 64 * 64 == len(raw):
            h = n
            break
    if h == 0:
        sys.exit(f"mida inesperada: {len(raw)} bytes")

    vals = struct.unpack(f"<{len(raw) // 2}h", raw)
    l0w = vals[: 768 * h]
    l0b = vals[768 * h : 769 * h]
    l1w = vals[769 * h : 771 * h]
    l1b = vals[771 * h]

    out = list(l0w) + list(l0b) + list(l1w) * nb + [l1b] * nb
    data = struct.pack(f"<{len(out)}h", *out)
    data += b"\0" * (-len(data) % 64)
    open(dst, "wb").write(data)
    print(f"N={h}, buckets={nb}, {len(data)} bytes -> {dst}")


if __name__ == "__main__":
    main()
