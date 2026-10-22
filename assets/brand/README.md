# Identità visiva di All Sky Brain

Questa cartella contiene il marchio e l'icona dell'app. Il disegno è stato creato per questo progetto.

## Origine e licenza

- **Marchio, icona, illustrazioni e sfondo stellare:** opera originale del progetto. La geometria è scritta in
  `tools/brand/build_brand.py` (cerchi, segmenti e un poligono a stella) e in `src/AllSky.Desktop/Theme/Illustrations.cs`.
  Non contengono immagini di terzi, né foto NASA o ESA.
- **Font inclusi nell'app:** Inter e JetBrains Mono, entrambi con licenza SIL Open Font License 1.1 (vedi sotto).
- **Licenza del marchio:** appartiene all'autore del progetto. Il repository di questo ramo non ha un file LICENSE,
  quindi la licenza del marchio va definita con quella del repository.

## File

| File | Contenuto |
|---|---|
| `allsky-brain-mark.svg` | Marchio senza sfondo: anello dell'obiettivo fisheye, anello interno, orizzonte, stella. Colore `#5CC8FF`. |
| `allsky-brain-icon.svg` | Icona: stesso disegno su sfondo nero `#000000` arrotondato, bordo `#182233`. |
| `allsky-brain.ico` | Icona multi-risoluzione: 16, 24, 32, 48, 64, 128, 256 px. Usata da exe (`ApplicationIcon`), tray e installer. |
| `../../src/AllSky.Desktop/Theme/Brand.xaml` | Geometrie WPF del marchio (`Brand.Lens`, `Brand.Inner`, `Brand.Horizon`, `Brand.Star`), generate dallo stesso script. |

Sotto 32 px l'icona omette l'anello interno e l'orizzonte, che a quelle dimensioni diventano sfocati.

## Rigenerare gli asset

Gli SVG, l'ICO e `Brand.xaml` si generano da un'unica fonte:

```
python3 tools/brand/build_brand.py           # rigenera
python3 tools/brand/build_brand.py --check   # esce con 1 se i file committati sono obsoleti
```

Lo script usa Python 3 con Pillow (testato con Pillow 12.3.0). Il risultato è deterministico: stessa versione di Pillow,
stessi byte. Dopo ogni modifica al disegno, rigenerare e committare tutti e quattro i file.

## Font

I font sono in `src/AllSky.Desktop/Assets/Fonts/` come risorse incorporate nell'exe. I file di licenza sono copiati
anche nella cartella dell'app (`OFL-*.txt`).

| Font | Versione | Fonte | File | SHA-256 | Licenza | Data |
|---|---|---|---|---|---|---|
| Inter Regular | 4.000 (`git-a52131595`) | `github.com/rsms/inter`, release `v4.0`, archivio `Inter-4.0.zip`, file `extras/ttf/Inter-Regular.ttf` | `Inter-Regular.ttf` | `64f8be6e55c37e32ef03da99714bf3aa58b8f2099bfe4f759a7578e3b8291123` | SIL OFL 1.1 | 2026-10-22 |
| Inter SemiBold | 4.000 | come sopra, file `extras/ttf/Inter-SemiBold.ttf` | `Inter-SemiBold.ttf` | `0dc98e8aa59585394880f25ab89e6d915ad5134522e961b046ca51fad3a18255` | SIL OFL 1.1 | 2026-10-22 |
| JetBrains Mono Regular | 2.305 | `github.com/JetBrains/JetBrainsMono`, branch `master`, file `fonts/ttf/JetBrainsMono-Regular.ttf` | `JetBrainsMono-Regular.ttf` | `e6fd0d7e91550b3ed2b735d4312474362c4716edc4fc0577a0f61ed782d5aed1` | SIL OFL 1.1 | 2026-10-22 |

- **Inter:** la licenza è in `OFL-Inter.txt`, copia di `LICENSE.txt` del repository `rsms/inter` (ramo `master`,
  SHA-256 `262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a`). Copyright 2016 The Inter Project Authors.
- **JetBrains Mono:** la licenza è in `OFL-JetBrainsMono.txt`, copia di `OFL.txt` del repository `JetBrains/JetBrainsMono`
  (ramo `master`, SHA-256 `a76abf002c49097d146e86740a3105a5d00450b1592e820a1109a8c5680cd697`). Copyright 2020 The JetBrains Mono Project Authors.
- Entrambe le licenze non dichiarano Reserved Font Name. I file non sono modificati.
- La versione di JetBrains Mono è quella dichiarata nella tabella `name` del file (2.305); il ramo `master` non ha una
  release con questo nome nella verifica fatta.

Se un file di font manca nel pacchetto, `ThemeManager` ripiega sui font di sistema (Segoe UI, Cascadia Mono, Consolas).
