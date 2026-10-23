# Diagnosi misurata del colore del timelapse (P14.1)

Generato il 2026-10-23 00:05 UTC da `AllSky.ColorMetrics.Cli`. Nessuna correzione è applicata: la correzione è P14.2.

## Cosa è stato misurato

Finestra della mediana mobile: 15 frame (centrata, bordi simmetrici). Zona valida: cerchio inscritto nel lato minore, ridotto al 90% del raggio (cerchio fisheye noto non disponibile: la config della notte non lo contiene). Fondo cielo: mediana dei pixel della zona con luminanza <= 70° percentile. Luminanza: Rec.709 su valori 0..1. Stelle: massimi locali 3x3 con luminanza >= fondo + 5 sigma (sigma = rumore robusto dalle differenze orizzontali, 1.4826 MAD / sqrt 2, minimo 1 ADU), saturi esclusi, eccesso medio di verde >= 8 ADU. Frame: JPEG dell'archivio (senza overlay), decodificati con ffmpeg in rgb24 (matrice di conversione non verificata [NV]); sidecar JSON per fase, esposizione, guadagno, Sole.

## Notti analizzate

| Notte | Frame analizzati | Frame scartati | Primo frame (UTC) | Ultimo frame (UTC) |
|---|---:|---:|---|---|
| 2026-10-22 | 955 | 0 | 2026-10-22 18:00:26 | 2026-10-23 09:59:14 |

## Tabella dei numeri: 2026-10-22

| Metrica | Valore | Unità | Nota |
|---|---:|---|---|
| Flicker: deviazione dei residui | 1.53 | % | finestra 15, 955 frame |
| Flicker: P95 di abs(residuo) | 0.71 | % | |
| Fondo R/G: escursione | 0.0757 | rapporto | max-min |
| Fondo R/G: pendenza | -0.0009 | 1/h | regressione sul tempo reale |
| Fondo R/G: residuo | 0.17 | % | dopo mediana mobile |
| Fondo B/G: escursione | 0.2852 | rapporto | max-min |
| Fondo B/G: pendenza | 0.0018 | 1/h | regressione sul tempo reale |
| Fondo B/G: residuo | 0.48 | % | dopo mediana mobile |
| Salto tipico luminanza | 0.00003 | 0..1 | mediana del valore assoluto delle differenze fra frame consecutivi della stessa fase |
| Salto tipico R/G | 0.00000 | rapporto | idem |
| Salto tipico B/G | 0.00000 | rapporto | idem |
| Confine twilight→night (18:05 UTC) | 4001.60 / n.d. / n.d. | rapporto | salto/tipico: luminanza / R/G / B/G |
| Confine night→twilight (04:11 UTC) | 1097.47 / n.d. / n.d. | rapporto | salto/tipico: luminanza / R/G / B/G |
| Confine twilight→day (05:55 UTC) | 929.19 / n.d. / n.d. | rapporto | salto/tipico: luminanza / R/G / B/G |
| Cambi di esposizione o guadagno | 93 | cambi | di cui 2 con cambio di fase |
| Banda ai cambi: salto medio | 2.013 | % | |
| Banda ai cambi: salto mediano | 0.123 | % | su 93 cambi |
| Banda fuori dai cambi: salto medio | 0.179 | % | |
| Banda: rapporto | 11.24 | rapporto | ~1 = nessuna banda |
| Stelle: mediana R/G | 1.0000 | rapporto | IQR 1.0000–1.0000 |
| Stelle: mediana B/G | 1.0000 | rapporto | IQR 1.0000–1.0657 |
| Stelle: frame usati | 955 | frame | su 955 con stelle; media 37582.2 stelle per frame |

## Difetti (con i numeri)

Soglie provvisorie: flicker 1 %, residuo di deriva 0.5 %, salto/tipico 10, banda 2. Ogni riga dice se il difetto emerge.

### 2026-10-22

- Difetto, flicker: deviazione 1.53 % (P95 |residuo| 0.71 %), soglia 1.00 %.
- Non emerge, deriva cromatica sui residui (sotto 0.50 %): R/G escursione 0.0757, pendenza -0.0009 /h, residuo 0.17 %; B/G escursione 0.2852, pendenza 0.0018 /h, residuo 0.48 %.
- Segnalato, da verificare, confine di fase: twilight→night alle 2026-10-22 18:05:26Z: rapporto salto/tipico luminanza 4001.60, R/G n.d., B/G n.d. (soglia 10.00; il salto tipico di notte è quasi zero, quindi il rapporto non prova un difetto).
- Segnalato, da verificare, confine di fase: night→twilight alle 2026-10-23 04:11:23Z: rapporto salto/tipico luminanza 1097.47, R/G n.d., B/G n.d. (soglia 10.00; il salto tipico di notte è quasi zero, quindi il rapporto non prova un difetto).
- Segnalato, da verificare, confine di fase: twilight→day alle 2026-10-23 05:55:02Z: rapporto salto/tipico luminanza 929.19, R/G n.d., B/G n.d. (soglia 10.00; il salto tipico di notte è quasi zero, quindi il rapporto non prova un difetto).
- Segnalato, da verificare, bande ai cambi di esposizione/guadagno: 93 cambi (di cui 2 coincidenti con un cambio di fase); salto medio relativo 2.013 % ai cambi contro 0.179 % altrove, rapporto 11.24 (soglia 2.00; la media è trascinata da pochi salti grandi).
- Stelle (solo numeri, nessun giudizio): 955 frame con almeno 5 stelle; mediana R/G 1.0000 (IQR 1.0000–1.0000), B/G 1.0000 (IQR 1.0000–1.0657).
- Difetto, metadati colore del video incompleti: color_transfer non dichiarato: il lettore assume la curva di default; color_primaries non dichiarato: il lettore assume i primari di default.

## Metadati colore dell'encoder (ffprobe)

### 2026-10-22

| Campo | Valore |
|---|---|
| codec | h264 |
| pix_fmt | yuv420p |
| color_range | tv |
| color_space (matrice) | bt470bg |
| color_transfer | non dichiarato |
| color_primaries | non dichiarato |
| dimensioni | 1280x960 |
| r_frame_rate | 25/1 |

- color_space dichiarato: bt470bg
- color_range dichiarato: tv (range limitato)
- color_transfer non dichiarato: il lettore assume la curva di default
- color_primaries non dichiarato: il lettore assume i primari di default

## Limiti

- Notte SIMULATA dalla camera Sim (sim-1): i valori non dicono nulla sul colore reale del cielo di Robert.
- Le misure sono sui JPEG dell'archivio, non sulla decodifica del timelapse.mp4: il video può cambiare i colori durante la codifica (vedi metadati).
- Le soglie di «Difetto» sono provvisorie e non calibrate su frame reali: servono a indicare dove guardare.
- Flicker periodico a 2 frame: la mediana mobile restituisce l'escursione picco-picco, cioè circa il doppio dell'ampiezza (±5 % danno circa 10 %).
- La deviazione del flicker è più grande del P95 del valore assoluto: pochi residui grandi (vicino ai cambi di esposizione) pesano sulla deviazione.
- I rapporti salto/tipico di una rampa lineare liscia valgono circa 5 (le due mediane di 5 frame distano 5 frame).
- Ai confini di fase di notte il salto tipico di luminanza è quasi zero: i rapporti esplodono per una transizione solare attesa, non per un difetto. Se il R/G tipico è zero il rapporto è n.d.
- Stelle: il rivelatore a 5 sigma non separa le stelle dal cielo luminoso. Di giorno e al crepuscolo conta decine di migliaia di massimi per frame: il colore delle stelle di quei frame non è una misura delle stelle.
- La banda ai cambi di esposizione: la media è dominata da pochi salti grandi; la mediana dei salti ai cambi è riportata a parte.
- Non c'è il guadagno effettivo del bilanciamento del bianco e dello stretch nei sidecar: le deriva misurata è quella del risultato, non dei guadagni.
- La luminanza è media sulla zona valida, incluse stelle e nuvole: non è un fondo cielo, il fondo è nelle colonne R/G e B/G.
- Il fondo delle stelle è quello globale della zona: un gradiente di colore del cielo sposta il rapporto R/G e B/G delle stelle.
- [NV] Colore reale: la notte è simulata dalla camera Sim, quindi i numeri non dicono nulla sul colore del cielo di Robert. Da ripetere sui frame veri.
- [NV] Decodifica del timelapse.mp4 e confronto con la sorgente: non fatti in questo passo.
