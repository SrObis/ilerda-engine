# Pla del port de Syzygy (Fathom -> `src/Ilerda/Syzygy.cs`)

Font: `tbprobe.c` de [jdart1/Fathom](https://github.com/jdart1/Fathom) (MIT; vegeu `THIRD-PARTY-NOTICES.md`).
Els números de línia són els de `tbprobe.c` (2.715 línies) al moment d'escriure això.

## Estat

- [x] Avisos de llicència i capçalera de `Syzygy.cs`.
- [x] Esquelet: `Syzygy.Init` troba els fitxers `.rtbw`/`.rtbz` i dedueix el màxim de peces; `ProbeWdl` retorna sempre `false`.
- [x] `oracle.py`: posicions aleatòries d'un material amb el WDL/DTZ de python-chess, per comparar.
- [x] Fases 1-6 (fitxers, índexs, codificació, descompressió, sondeig WDL, integració): validades amb `oracle.py`, 0 diferències en 30.500 posicions (v0.14.0).
- [ ] Fase 7: DTZ (v0.14.1).

## Fases (per ordre)

| Fase | Què es tradueix | Funcions de Fathom (línies) |
|---|---|---|
| 1. Fitxers | Obrir i mapar `.rtbw` amb `MemoryMappedFile`; comprovar el nombre màgic (`0x5d23e871` WDL, `0xa50c66d7` DTZ); taula hash per clau de material | `open_tb` (308), `map_file` (359), `init_tb` (750), `tb_init` (850), `add_to_hash` (734) |
| 2. Índexs | Taules constants (`Triangle`, `KKIdx`, `Flap`, `PawnTwist`...) i els binomis | `init_indices` (1245), taules (1060-1243) |
| 3. Codificació | De posició a índex dins la taula (peces sense peons i amb peons, simetries) | `leading_pawn` (1286), `encode_piece`/`encode_pawn_*` (1388-1398), `init_enc_info` (1416) |
| 4. Descompressió | Llegir la capçalera de cada taula i descomprimir blocs (símbols i parells) | `calc_symLen` (1464), `setup_pairs` (1479), `init_table` (1540), `decompress_pairs` (1669) |
| 5. Sondeig | WDL d'una posició, amb la cerca d'alfa-beta de captures | `probe_table` (1778), `probe_wdl_table` (1903), `probe_ab` (1919), `probe_wdl` (1964) |
| 6. Integració | Opcions UCI `SyzygyPath`, `SyzygyProbeDepth`, `SyzygyProbeLimit`; sondeig a `Negamax` | (codi d'Ilerda) |
| 7. DTZ (versió posterior) | Filtrar jugades a l'arrel | `probe_dtz` i `root_probe_*` |

## Com es comprova

1. Baixar les taules (ja a `D:\tablebases`).
2. `python tools/syzygy/oracle.py D:/tablebases/3-4-5 KRPvKR 2000 oracle.txt` (i altres materials, començant per
   KPvK, KRvK, KQvK, KRvKR, KRPvKR...). El lector C# ha de donar el mateix WDL a totes.
3. Una ordre UCI `tbprobe` (per afegir) imprimeix el WDL d'una posició per poder comparar en bloc.
4. Només després, integrar a la cerca i mesurar contra Stockfish.

## Decisions de disseny

- Només es porta `tbprobe.c`. El tauler i les jugades són els d'Ilerda (`Position`, `MoveGen`); no es porten `tbchess.c` ni `stdendian.h`.
- Fils: les taules són només de lectura un cop carregades, així que la lectura no necessita bloquejos; la càrrega (inicialització
  tardana de cada taula) sí que cal protegir-la.
- Peces: primer 3-5 (menys casos estranys), després 6.
- Sense `SyzygyPath`, el comportament i el `bench` han de ser idèntics als d'abans.
