# Avisos de tercers

## Fathom (lector de taules de finals Syzygy)

`src/Ilerda/Syzygy.cs` és una adaptació a C# de `tbprobe.c` de [Fathom](https://github.com/jdart1/Fathom),
publicat amb llicència MIT. El codi de sondeig original és de Ronald de Man, que també va crear el format Syzygy.

```
The MIT License (MIT)

Copyright (c) 2013-2018 Ronald de Man
Copyright (c) 2015 basil00
Copyright (c) 2016-2025 by Jon Dart

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

De Fathom només s'adapta `tbprobe.c` (la lògica de les taules). La representació del tauler i la generació de
jugades són les pròpies d'Ilerda; no s'usa `tbchess.c` ni `stdendian.h`.

## Fitxers de dades

Les taules Syzygy (`.rtbw` i `.rtbz`) són dades i no s'inclouen al repositori. Es poden baixar, per exemple,
de <https://tablebase.lichess.ovh/tables/standard/> i es donen a Ilerda amb l'opció UCI `SyzygyPath`.

## Eines de prova

`tools/syzygy/oracle.py` fa servir [python-chess](https://github.com/niklasf/python-chess) (GPL-3.0) només com a
programa extern per comprovar els resultats del lector. No s'ha copiat cap codi de python-chess a Ilerda.
