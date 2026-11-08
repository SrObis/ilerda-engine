#!/usr/bin/env python3
"""Genera src/Ilerda/SyzygyTables.cs copiant les taules constants de tbprobe.c de Fathom (MIT).
Us: python gen_taules.py RUTA/tbprobe.c SORTIDA.cs"""
import re, sys

src = open(sys.argv[1], encoding="utf8").read()

def taula(nom, tipus_c):
    m = re.search(r"static const %s %s((?:\[[0-9]*\])+)\s*=\s*\{(.*?)\n\};" % (tipus_c, nom), src, re.S)
    if not m:
        sys.exit("no trobo " + nom)
    dims = [int(x) if x else 0 for x in re.findall(r"\[([0-9]*)\]", m.group(1))]
    nums = [int(x) for x in re.findall(r"-?[0-9]+", re.sub(r"/\*.*?\*/", "", m.group(2), flags=re.S))]
    if dims[0] == 0:
        dims[0] = len(nums) // eval("*".join(str(d) for d in dims[1:]) or "1")
    assert len(nums) == eval("*".join(map(str, dims))), (nom, len(nums), dims)
    return dims, nums

def emet(nom, tipus_cs, tipus_c):
    dims, nums = taula(nom, tipus_c)
    pla = ", ".join(map(str, nums))
    if len(dims) == 1:
        return f"    public static readonly {tipus_cs}[] {nom} = {{ {pla} }};\n"
    # multidimensional: guardem aplanat i indexem a ma (files * dims[1] + col)
    return f"    public static readonly {tipus_cs}[] {nom} = {{ {pla} }}; // {dims[0]} x {dims[1]}\n"

out = []
out.append("""/*
 * Taules constants del lector Syzygy d'Ilerda, copiades de tbprobe.c de Fathom (MIT).
 * Fitxer generat per tools/syzygy/gen_taules.py; no s'edita a ma.
 *
 * Copyright (c) 2013-2018 Ronald de Man
 * Copyright (c) 2015 basil00
 * Modifications Copyright (c) 2016-2025 by Jon Dart
 * Adaptacio a C# Copyright (c) 2026 Jordi Obis
 *
 * Vegeu THIRD-PARTY-NOTICES.md per al text complet de la llicencia MIT.
 */

namespace Ilerda;

internal static class SyzygyTables
{
""")
for nom, cs, c in [("OffDiag", "sbyte", "int8_t"), ("Triangle", "byte", "uint8_t"), ("FlipDiag", "byte", "uint8_t"),
                   ("Lower", "byte", "uint8_t"), ("Diag", "byte", "uint8_t"), ("Flap", "byte", "uint8_t"),
                   ("PawnTwist", "byte", "uint8_t"), ("KKIdx", "short", "int16_t")]:
    out.append(emet(nom, cs, c))
out.append("}\n")
open(sys.argv[2], "w", encoding="utf8", newline="\n").write("".join(out))
print("ok")
