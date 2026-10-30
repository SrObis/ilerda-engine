# Entrenar la NNUE a Kaggle amb bullet

Procediment que ha funcionat (un notebook amb el dataset `ilerda-subset-50m`, **Accelerator = GPU T4** i **Internet = On**; cal tenir el telèfon verificat a Kaggle). Una xarxa 768 → 256 (×2 perspectives) → 1 amb SCReLU triga **uns 4,5 minuts** a la T4 (≈7 M posicions per segon) amb 50 M de posicions i 20 superbatches.

## 1. Les dades

`tools/subset` treu un subconjunt aleatori de `data\unic.txt` en el format de text de bullet (`FEN | puntuació | resultat`, tot des de les blanques):

```bash
dotnet run -c Release --project tools/subset -- data\unic.txt data\subset-50m.txt 50000000
```

Es comprimeix amb `tar -a -cf data\subset-50m.zip -C data subset-50m.txt` (0,63 GB) i es puja com a dataset privat. Al notebook queda a `/kaggle/input/datasets/<usuari>/ilerda-subset-50m/subset-50m.txt`.

## 2. Entorn i bullet (cel·les del notebook)

```python
!nvidia-smi
!curl --proto =https --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y 2>&1 | tail -3
```

```python
import os
os.environ['PATH'] += ':/root/.cargo/bin'
!git clone --depth 1 https://github.com/jw1912/bullet /kaggle/working/bullet 2>&1 | tail -2
!cd /kaggle/working/bullet && cargo b -r --package bullet-utils 2>&1 | tail -3
```

## 3. Convertir i barrejar

`convert --from text` accepta el format, però **descarta les línies amb resultat fraccionari** (les posicions repetides de `tools/dedup`: uns 0,2 %). Surten 32 bytes per posició.

```python
!/kaggle/working/bullet/target/release/bullet-utils convert --from text --input /kaggle/input/datasets/<usuari>/ilerda-subset-50m/subset-50m.txt --output /kaggle/working/data.bullet --threads 4
!/kaggle/working/bullet/target/release/bullet-utils shuffle --input /kaggle/working/data.bullet --mem-used-mb 8000 --output /kaggle/working/shuf.bullet
```

## 4. Entrenar

Es modifica `examples/simple.rs` de bullet (és l'exemple que ja és una xarxa `(768 -> N)x2 -> 1`):

```python
p = '/kaggle/working/bullet/examples/simple.rs'
s = open(p).read()
s = s.replace('data/baseline.data', '/kaggle/working/shuf.bullet')
s = s.replace('"simple"', '"ilerda"')
s = s.replace('end_superbatch: 40', 'end_superbatch: 20')
s = s.replace('step: 18', 'step: 7')
s = s.replace('save_rate: 10', 'save_rate: 5')
s = s.replace('HIDDEN_SIZE: usize = 128', 'HIDDEN_SIZE: usize = 256')
s = s.replace('output_directory: "checkpoints"', 'output_directory: "/kaggle/working/checkpoints"')
open(p, 'w').write(s)
```

L'enllaç amb CUDA falla si no es diu on són `nvcc` i `libcuda` (a Kaggle el toolkit és a `/usr/local/cuda-12.8` i `libcuda.so` a `/usr/local/nvidia/lib64`):

```python
!cd /kaggle/working/bullet && export PATH=$PATH:/root/.cargo/bin CUDA_PATH=/usr/local/cuda-12.8 RUSTFLAGS='-L /usr/local/nvidia/lib64' && cargo r -r --example simple --features cuda 2>&1 | grep -E 'superbatch|Saved|Total|rror' | tail -n 60
```

Un superbatch són 6104 × 16 384 ≈ 100 M posicions (2 èpoques del subconjunt). La xarxa final és `/kaggle/working/checkpoints/ilerda-20/quantised.bin` (394.816 bytes).

## 5. Baixar la xarxa

Es copia a `/kaggle/working/` i es baixa des del panell **Output** del notebook (⋮ → Download). Cal baixar-la abans d'aturar la sessió: `/kaggle/working` es perd en aturar-la si no es desa una versió.

## Format del fitxer (confirmat)

`quantised.bin` no té capçalera: tot són `i16` little-endian, en aquest ordre, i s'omple fins a un múltiple de 64 bytes.

| Tensor | Elements | Disposició |
|---|---|---|
| `l0w` | 768 · N | `[característica][neurona]` |
| `l0b` | N | |
| `l1w` | 2 · N | bàndol que juga, després l'altre |
| `l1b` | 1 | |

Quantització: QA = 255, QB = 64, escala 400. Sortida: `((Σ SCReLU(acc)·w) / QA + l1b) · 400 / (QA · QB)`. Característiques (`Chess768`): peça pròpia al bloc 0 i del rival al 384, més `tipus·64 + casella`, amb la casella girada en vertical (`^ 56`) per a les negres. La inferència és a `src/Ilerda/Nnue.cs`.
