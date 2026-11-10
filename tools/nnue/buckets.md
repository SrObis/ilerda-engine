# Línia paral·lela: xarxa NNUE amb buckets de sortida (branca `feature/nnue-buckets`)

Estat: **adoptat a la 0.15.0**. Xarxa amb buckets + `mix2-tb.txt`: +42,9 ± 7,7 Elo contra la xarxa anterior (5.000 partides a 5+0,05). Vegeu el CHANGELOG. Pendent: entrenar la xarxa B (sense buckets, només etiquetes) per saber quina part del guany ve de cada cosa.

## Ja fet

- `Nnue.cs` llegeix xarxes sense buckets (`B = 1`, la d'ara) i amb 8 buckets de sortida; el format es detecta per la mida del fitxer.
  Pesos de sortida `l1w [bucket][2N]` i biaixos `l1b [bucket]`, com el que treu bullet amb `.transpose()` a `l1w`.
- `Eval.cs` tria el bucket pel nombre de peces (reis inclosos): `(peces - 2) / 4`, de 0 a 7.
- `tools/nnue/replicar-buckets.py` converteix la xarxa actual al format de 8 buckets repetint els pesos. **Prova de paritat superada:** `bench 12`
  dona 8.996.114 nodes amb la xarxa original i amb la replicada (i la mateixa velocitat, ~2,2 M nps), així que el lector i l'indexat per bucket són correctes.
- `tools/nnue/ilerda_buckets.rs` (exemple de bullet) i `tools/nnue/ilerda-train-buckets.ipynb` (notebook de Kaggle): mateix calendari d'entrenament que
  la xarxa actual (256 neurones, 20 superbatches, StepLR, WDL 0,75), perquè l'únic que canviï siguin els buckets.

## Per comprovar abans de fiar-se'n (no verificat)

1. **Fórmula del bucket de bullet: verificada** (2026-11-08, `crates/bullet_lib/src/game/outputs.rs`): `divisor = ceil(32 / N)` i `bucket = (peces_ocupades - 2) / divisor`; per a N = 8 és `(peces - 2) / 4`, comptant els reis dins de les peces ocupades, igual que `Eval.cs`.
2. **Disposició de `l1w` amb `.transpose()`.** Deduït de la documentació de bullet (columna-major); no he llegit el codi. Es comprova amb la primera xarxa entrenada:
   `eval` de la posició inicial i d'unes quantes posicions conegudes ha de donar valors sensats (prop de 0 a l'inici), i un matx curt contra la 0.13.3 no ha de ser un desastre.
3. L'API de bullet canvia: el notebook clona l'última versió. Si no compila, partir d'`examples/progression/2_output_buckets.rs`.
4. A la cel·la de Kaggle, `HIDDEN` només afecta el nom del fitxer; la mida real és la constant `HIDDEN_SIZE` del `.rs`.

## Dades amb el resultat de les taules (`mix2-tb.txt`)

`ilerda tbrelabel data\mix2.txt data\mix2-tb.txt "D:/tablebases/3-4-5;D:/tablebases/6" 6` posa el resultat exacte de Syzygy a les posicions de 6 peces o menys
(2,1 minuts per a 109,2 M de línies). Un 9,8 % de les posicions (10,7 M) tenen taula, i en 1,86 M (17,4 %) el resultat de la partida no coincidia
amb el teòric: 0,81 M passen a victòria blanca, 0,83 M a victòria negra i 0,21 M a taules. La puntuació no es toca.

Es compara amb dues xarxes entrenades amb el mateix calendari: A = buckets + `mix2-tb.txt` i B = sense buckets + `mix2-tb.txt`
(`ilerda-train.ipynb`), contra la xarxa actual de `src/Ilerda/nets/ilerda-256.bin`.

## Dades

- Ara: `mix2.txt` (≈72 M de posicions). Els buckets reparteixen les dades en 8 grups per nombre de peces, i els de pocs peces tenen menys exemples,
  així que més dades ajuden més que abans.
- Generar-ne de noves amb `datagen` quan l'ordinador estigui lliure (no durant un matx) i amb una versió de cerca ja millorada (0.13.5 o 0.13.6),
  amb el llibre `llibre.epd`. Les dades noves soles empitjoren la xarxa (vegeu el README): barrejar-les sempre amb les velles i deduplicar.

## Com mesurar

1. Entrenar amb les mateixes dades que la xarxa actual: només canvien els buckets. Això aïlla el guany.
2. Matx contra la xarxa sense buckets, mateixa cerca, 5+0,05, ~5.000 partides; i després contra Stockfish limitat (3000 i 3190), perquè l'autojoc exagera.
3. Si guanya, versió pròpia (0.13.x) amb la xarxa nova incrustada (`src/Ilerda/nets/`).
