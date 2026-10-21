# Diario del ralph loop — All Sky Brain desktop v1

Una voce per iterazione, la più recente in fondo. Formato:

```
## AAAA-MM-GG HH:MM — <ID task> <titolo>
- Fatto:
- Test: <eseguiti> / <passati> / <falliti (nomi)>
- Fatti verificati: <fatto> — <fonte> (<data>)
- Decisioni:
- Dubbi / da confermare con Robert:
```

## 2026-10-21 23:20 — Preparazione del piano (orchestratore, sessione cloud)
- Piano scritto da `main` @ 885535f (0.2.1 rilasciata). Branch `ralph/desktop-v1`.
- Fatti verificati e fonti: vedi IMPLEMENTATION_PLAN.md §1.
- Problemi aperti noti prima del loop:
  - ZWO `ASCOM.ASICamera2.Camera` rifiutata perché "Monochrome": modello e tipo sensore da confermare.
  - Limiti di esposizione ZWO: correzione 0.2.1 non ancora provata sulla camera reale.

## 2026-10-21 23:50 — P0.1, P0.2, P3.1, P4.1, P7.1 (loop eseguito dalla sessione cloud)
- Fatto:
  - P0.1: baseline 797/797 verdi sul runner shim.
  - P0.2: `DataFolderResolver` in AllSky.Core, usato da Tray/Program.cs (18 test).
  - P3.1: `AllSky.Sky`, modello fisheye equidistante (46 test).
  - P4.1: `AllSky.Satellites`, fonte TLE CelesTrak con cache e blocco 24 h (21 test).
  - P7.1: `AllSky.YouTube`, modello titolo/descrizione (32 test).
- Test (runner shim, tutta la solution): 914 eseguiti, 913 PASS, 1 FAIL = `CaptureLoopTests.FrameMedian_UpdatesExposureForNextCapture` (instabile, preesistente). Altri test (Publish `UplinkHttpClientTests`/`UplinkJsonTests`) sono comparsi instabili in alcune esecuzioni di un agente: da tenere d'occhio.
- Fatti verificati:
  - ISS = NORAD 25544, dalla pagina CelesTrak NORAD/elements (21/10/2026).
  - Limiti YouTube: titolo 100 caratteri, descrizione 5000 byte, `<` e `>` vietati — https://developers.google.com/youtube/v3/docs/videos (pagina del 2026-10-08).
- Non verificato:
  - CSS (Tianhe) 48274 e Hubble 20580: le pagine CelesTrak sono bloccate da robots.txt per il fetcher; compaiono solo su siti terzi. NON inclusi nei bersagli predefiniti.
  - Compilazione Windows di Tray/Program.cs: serve la CI.
- Decisioni / da confermare:
  - `FisheyeModel.HorizonRadius = HorizonRadiusPx * (1 + K2)`: con K2 ≠ 0 il parametro `HorizonRadiusPx` non coincide con il raggio dell'orizzonte. Da chiarire in P3.2 (o rinominare il parametro).
  - `--data-folder` seguito da un valore che inizia con `--` viene preso come valore.
  - L'HttpClient di `CelesTrakTleSource` va creato con `AllowAutoRedirect = false`.
- Prossimi: P0.3, P1.x (desktop), P3.2, P4.2 (SGP4: SGP.NET non scaricabile qui), P5.1, P6.1.

## 2026-10-22 00:20 — P0.3, P5.1, P6.1, P6.2
- Fatto:
  - P0.3: nome "All Sky Brain" nei testi visibili; invarianti rispettati; CI Windows del branch verde (run 39680009587).
  - P5.1: `AllSky.Detection`, rilevatore di scie. Dati sintetici: richiamo 420/420 su 6 seed (SNR 5-10, lunghezza 40-200 px), 0 falsi positivi su 500 frame puri, ~4.6 ms/frame a 960x720.
  - P6.1: `AllSky.Weather` con sorgente ASCOM ObservingConditions (unità verificate sulle pagine ufficiali ASCOM, 21/10/2026: °C, %, hPa, mm/h, m/s, gradi, mag/arcsec²).
  - P6.2: sorgente Open-Meteo opt-in, 15 min di cache, backoff, attribuzione CC BY 4.0 e avviso "solo uso non commerciale".
- Test: runner shim sull'intera solution.
- Non verificato:
  - Dati sintetici idealizzati: nessuna immagine reale. Le soglie del rilevatore vanno tarate sul cielo vero.
  - Open-Meteo: le stringhe di `current_units` non sono confermate con una risposta reale; la sorgente scarta i valori con unità diversa dall'attesa.
  - Elenco dei driver ObservingConditions dal registro ASCOM: lasciato come TODO (chiave non verificata).
  - SkyBrightness e StarFWHM non letti (nessun campo nel record).
- Decisioni: pressione da `surface_pressure` (non `pressure_msl`).

## 2026-10-22 01:00 — P1.1, P3.2, P1.2, P4.2
- Fatto:
  - P1.1: scheletro `AllSky.Desktop` (WPF + NotifyIcon, Velopack all'avvio, istanza singola).
  - P3.2: calibrazione fisheye dal Sole (rilevatore + solver LM + collector).
  - P1.2: tema (Palette, ThemeManager, Styles.xaml, galleria componenti `--gallery`), palette notte rossa solo canale R.
  - P4.2: propagazione satelliti (`IOrbitPropagator`), PassFinder con ombra terrestre, adattatore SGP.NET 1.6.0.
- Test: CI Windows run 39684703136 verde (`dotnet test AllSky.sln` + publish Desktop). Il test instabile noto non ha fallito in quella run.
- Fatti verificati: SGP.NET 1.6.0 si ripristina da NuGet e compila in CI (22/10/2026) — risolve il dubbio "NuGet = repo".
- Non verificato:
  - Runtime del Desktop mai eseguito (NotifyIcon, finestra, chiusura ordinata, cambio tema a caldo).
  - Adattatore SGP4 non confrontato con valori di riferimento reali; oggetti deep-space (SDP4) non rifiutati.
  - Font Inter/JetBrains Mono: licenza da verificare in P1.10, per ora font di sistema.
