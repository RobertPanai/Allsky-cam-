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

## 2026-10-22 01:40 — P1.3, P2.1, P5.2
- Fatto: shell di navigazione desktop; sidecar JSON per frame (scrittura atomica, ricostruzione retroattiva); classificatore scie (Meteor/Bolide/Satellite/AereoOSatellite, frame scartati).
- Test: runner shim 1103 eseguiti, 1102 PASS, 1 FAIL (il test instabile noto). CI Windows run 39686838761 verde (dopo 2 correzioni di compilazione: `Stretch` ambiguo con `AllSky.Core.Stretch`, `Foreground`).
- Non verificato:
  - UI Desktop mai eseguita né vista (icone, focus, barra di stato a 1100 px, notte rossa): screenshot in P1.9.
  - Voce "pubblicazioni" della barra di stato sempre "n/d" finché il servizio non espone lo stato.
  - Azimut Luna nel sidecar: errore atteso ~1° (angolo orario geocentrico). Scrittura atomica/fsync per frame provata solo su Linux: misurare la latenza sul PC reale.
  - Soglie del classificatore non tarate su cielo reale.
- Decisioni: la prima occorrenza di un oggetto che si ripete esce come Meteor, il frame successivo la riclassifica AereoOSatellite (P5.3 la tratta come provvisoria). Adattatore PassFinder → IPredictedTrackProvider rimandato a P4.4 (serve proiezione alt/az→pixel).
- Warning CI: switch non esaustivo in DesktopTexts.cs (ViewName/EmptyText) con valore enum non coperto → da sistemare.

## 2026-10-22 ~02:00 — Interruzione per limite di sessione
- Gli agenti P1.4 (a441cf9512d35ee78), P1.5 (a3413d67d9ff43d5e), P2.2 (a9c57de049397a33e) si sono fermati per il limite di sessione (reset 03:50). Lavoro parziale salvato nel commit WIP, NON compilato, NON rivisto.
- Alla ripresa: riprendere gli agenti con SendMessage (stesso contesto), poi suite shim, CI Windows, registrazione delle viste nella shell.

## 2026-10-22 04:30 — P1.4, P1.5, P2.2 (dopo la ripresa delle 03:50)
- Fatto: vista Live (zoom/pan, decodifica fuori UI, pannello dati), vista Camera (ricerca, collegamento, impostazioni, errori leggibili), layout overlay (`AllSky.InfoOverlay`, JSON versionato, posizionamento puro). Le due viste sono registrate nella shell (`ShellControl` riceve `IAllSkyService`).
- Test: runner shim 1241 eseguiti, 1240 PASS, 1 FAIL (instabile noto). CI Windows run 39709022899 verde (una correzione: chiave risorsa `"Size.Body"`).
- Non verificato: tutte le viste WPF mai viste a schermo; InfoOverlay: posizionamento con misure di testo approssimate (il renderer P2.3 darà quelle vere), versione 0 dello schema ipotetica.
- Da fare (task piccoli per l'Engine, non ancora nel piano):
  - temperatura sensore in `LastFrameDto`/`FrameEvent` (il pannello Live mostra sempre "n/d");
  - `DisconnectCameraAsync` in `IAllSkyService`;
  - far arrivare i valori dei limiti di esposizione 0.2.1 nel messaggio di errore restituito, non solo nel log.
- `DesktopTexts` passato alla shell: la lingua scelta va cablata in P1.8.

## 2026-10-22 06:00 — P1.6, P1.7, P1.8, P6.3 + Engine
- Fatto: vista Notti; vista Pubblicazione con adattatore `HostPublishSettingsService` (logica condivisa con le rotte, token mai riletto); vista Impostazioni e Log (Kestrel opzionale e spento di default, password LAN PBKDF2, log con maschera segreti); meteo nel sidecar (`CachedWeatherSnapshotProvider`) e righe meteo per il widget; Engine: temperatura sensore, `DisconnectCameraAsync`, campo `Warning` (limiti esposizione 0.2.1) separato da `Error`.
- Test: runner shim 1470 eseguiti, 1469 PASS, 1 FAIL (instabile noto). CI Windows run 39711132701 verde con tutte le viste registrate nella shell.
- Decisioni / rischi da confermare con Robert:
  - Il pannello web (Kestrel) non parte più di default: le installazioni esistenti perdono l'interfaccia browser finché non si attiva `ui.webEnabled`. L'auto-update non cambia la config: chi aggiorna dalla 0.2.x si trova il pannello spento (il desktop ha le sue viste).
  - Password LAN su HTTP Basic non cifrato: la vista lo dichiara.
  - Il log non ha livelli: gravità stimata da parole chiave.
  - Avvio con Windows con `--data-folder` usa la cartella predefinita.
  - Messaggi di validazione del server di pubblicazione solo in italiano.
  - Salvataggio impostazioni pubblicazione ora serializzato da lock; nell'adattatore `null` = vuoto, nelle rotte `null` = invariato.
- Non verificato: tutte le viste WPF mai viste a schermo; media player timelapse e codec; keogram↔frame su archivio reale (dopo riavvio il keogram può coprire meno frame; oltre 4096 frame colonne a blocchi); avvio con Windows nel registro; middleware password su rete reale.
- Da fare: `Engine.WeatherProvider` non è ancora impostato da nessuno (serve una sezione meteo nelle impostazioni: sorgente ASCOM/Open-Meteo opt-in); attribuzione Open-Meteo nel widget; adattatore PassFinder→IPredictedTrackProvider (P4.4).

## 2026-10-22 09:30 — P1.10, P1.11, P2.3, P3.3, P7.2
- Fatto: identità visiva (marchio SVG/XAML, `.ico` multirisoluzione deterministico da `tools/brand/build_brand.py`, splash, illustrazioni stati vuoti, sfondo stellare procedurale, font Inter 4.0 e JetBrains Mono 2.305 con OFL 1.1 incorporati); vista "Stanotte" (home); compositore + renderer WPF dell'overlay; UI calibrazione con `ICalibrationService` nell'Engine; OAuth Desktop per YouTube (PKCE S256, loopback 127.0.0.1, refresh cifrato DPAPI).
- Test: runner shim 1632/1632. CI Windows run 39735490656 verde (una correzione: `AstroTime` è internal, validazione lat/lon locale in Core).
- Fatti verificati:
  - Velopack `vpk pack --icon, -i <PATH>` esiste — https://docs.velopack.io/reference/cli/content/vpk-windows (22/10/2026).
  - Flusso OAuth Desktop (loopback 127.0.0.1, PKCE S256, state) — https://developers.google.com/identity/protocols/oauth2/native-app (22/10/2026); scadenza 7 giorni del refresh token in Testing e limite 100 token per client — https://developers.google.com/identity/protocols/oauth2#expiration.
  - Licenza Inter e JetBrains Mono: OFL 1.1, dai repository ufficiali (hash in assets/brand/README.md).
  - Crepuscoli (Torino, 21/05/2025) confrontati con timeanddate con tolleranza 2 min; tramonto/alba divergono di 2-5 min per la definizione del bordo del Sole (documentato). USNO non raggiungibile.
- Non verificato:
  - Tutto il WPF di questo gruppo (splash, font da pack URI e peso SemiBold, icona in exe/tray/installer, Stanotte, pannello calibrazione, griglia alt/az sul frame reale) mai visto a schermo.
  - Budget renderer overlay (<30 ms a 1920×1080) non misurato (serve Windows). Misure del testo stimate a 0,6 corpi/carattere.
  - Rilevamento del Sole per la calibrazione solo su dati sintetici; punti raccolti non persistono al riavvio.
  - `HttpListener` su 127.0.0.1 senza privilegi amministrativi su Windows: da provare. Procedura "Testing → In production" e verifica dello scope `youtube.upload`: [NV], nel documento docs/YOUTUBE-OAUTH.md.
  - Nomi d'errore `invalid_grant`/`access_denied` e forma della revoca presi dallo standard OAuth, non dalle pagine Google lette.
  - Glifo lunare confrontato con una sola data approssimata.
- Da decidere con Robert: manca un file LICENSE nel repository (la licenza del marchio e di tutto il codice).

## 2026-10-22 11:00 — P2.4, P4.4, P5.3, P7.3
- Fatto: overlay applicabile al timelapse (`applyToTimelapse`; `applyToLive`/`applyToPublish` solo flag, spenti); conferma transiti (`AllSky.Transits`: proiettore tracce, adattatore `IPredictedTrackProvider`, `TransitConfirmer`); archivio eventi (`AllSky.Events`: EventArchive, EventSaver, zip, etichette); upload YouTube resumable + pianificatore giornaliero con idempotenza (`AllSky.YouTube/Upload`).
- Test: runner shim 1748 eseguiti, 1747 PASS, 1 FAIL (instabile noto). CI Windows run 39737238522 verde.
- Fatti verificati (22/10/2026):
  - Protocollo upload resumable YouTube: POST uploadType=resumable, Location, PUT con Content-Range, ripresa con `bytes */total`, 308/200/201, chunk multipli di 256 KB — https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol.
  - videos.insert: 1 unità, 100/giorno, max 256 GB — https://developers.google.com/youtube/v3/docs/videos/insert.
- Non verificato:
  - ID categoria YouTube [NV]: default "28" configurabile, nessuna fonte ufficiale trovata.
  - Effetto reale della privacy per progetti API non verificati ("not restricted to private viewing mode" nella doc); `selfDeclaredMadeForKids` inviato sempre false; reason esatti dei 403 di quota; durata della sessione resumable.
  - Soglie del confermatore transiti solo su dati sintetici (rischio: bordo di nuvola allineato alla traccia → falso positivo). Il modello fisheye deve essere nelle coordinate del frame ridotto.
  - Eventi: clip con ffmpeg reale e JPEG reale non provati; posizione alt/az solo come interfaccia `IPixelToSky`.
  - `WpfFrameOverlay` non compilato a runtime/budget 30 ms non misurato.
- Da fare (cablaggio, non ancora assegnato): timer del pianificatore YouTube nell'Host (TickAsync all'avvio e ogni ~10 min); adattatore `ILatestTimelapseProvider` verso NightArchive (con variante con maschera); detection per frame nell'Engine che usa `StreakClassifier`/`EventSaver` (agganciare con finestra N−2..N+2, serve RGB a piena risoluzione); `Engine.WeatherProvider` da impostazioni; `IPixelToSky` dalla calibrazione.

## 2026-10-22 13:00 — P2.5, P4.3, P4.5, P4.6, P7.4
- Fatto: editor visivo della maschera (aperto da Impostazioni); previsioni dei passaggi (servizio nell'Engine, spento di default, UI in Stanotte/Live/barra di stato); salvataggio dei transiti (`TransitEventSaver`); modalità transito (policy di cadenza, spenta); vista YouTube + Host (provider ultima notte completa, worker ogni 10 min, store cifrato).
- Test: runner shim 1918/1918. CI Windows run 39740242914 verde (una correzione: `MaskLogoStore.NomeBase` era internal, il runner shim compila tutto in un solo assembly e non lo segnala).
- Nota metodo: il runner shim non rileva errori di visibilità `internal` tra assembly; la CI sì.
- Decisioni da confermare:
  - Ora predefinita dell'upload YouTube 12:30 (non 08:00): la notte osservativa finisce a mezzogiorno, alle 08:00 si caricherebbe la notte di due giorni prima.
  - "Salva tutti i passaggi visibili" salva anche i NonIdentificato.
  - `enabled` di YouTube si accende al collegamento e si spegne allo scollegamento (nessun interruttore nel pannello); fuso: la vista usa quello del PC, lo scheduler quello dell'osservatorio letto all'avvio.
  - L'elenco passaggi mostra tutti quelli sopra soglia con marcatura di visibilità; le tracce su Live solo per passaggi in corso o entro 15 min.
- Non verificato: tutto il WPF di questo gruppo; propagazione SGP4 della ISS contro fonte esterna [NV]; tempo di lettura camera di 2 s (modalità transito) stimato; `HostYouTubeService` (login/logout) senza test unitari.
- Aggiunto al piano il task P5.5 (cablaggio rilevamento nell'Engine): meteore, transiti, modalità transito non sono ancora collegati all'Engine.

## 2026-10-22 (pomeriggio) — P5.4, P5.5, cablaggio meteo/satelliti, P1.9 (in corso)
- Fatto: rilevamento collegato all'Engine (P5.4/P5.5); sezione `weather` in config (`none`/`ascom`/`openmeteo`, Open-Meteo solo con `openMeteoAcknowledgedNonCommercial: true`), `WeatherProviderFactory`, fornitore ricreato al cambio config; `FrameSidecar.Satellites` ora oggetti `{name, noradId, altDeg, azDeg}` (legge anche il vecchio formato a stringhe); `null` se `satellites.enabled` è falso, `[]` se nessuno è sopra l'orizzonte.
- Test: shim Core 603, Archive 76, Engine 85, Host 74, Events 70, Weather 37, InfoOverlay 127, tutti verdi (esecuzione dell'agente; suite completa da rieseguire in P8.2).
- P1.9 (test UI su Windows, `tests/AllSky.Desktop.Tests`): la CI compila ed esegue il progetto (xunit risolto alla v2: `Version="*"` nel csproj, da fissare). Prima esecuzione: le viste in modo normale e la finestra principale passano; in rosso-notte fallivano Eventi, Impostazioni, Camera, Log, editor maschera (ComboBox senza stile, ListBox e PasswordBox senza pennelli del tema). Aggiunti lo stile ComboBox/ComboBoxItem e i pennelli; verifica sulla CI ancora da leggere.
- Lezione: gli errori di compilazione e i ::error dei test si leggono solo dalle annotazioni; lo step Test ha `continue-on-error`, quindi "success" non basta.
- Da provare/decidere: ComboBox ora alto 32 (visibile anche in modo normale); template ComboBox scritto senza prova visiva; docs `README` "Limiti noti" e `docs/FUNZIONI.md` dicono ancora meteo/satelliti non collegati (da aggiornare); nessuna UI per le impostazioni meteo/satelliti/rilevamento (solo config.json).

## 2026-10-22 (pomeriggio, sessione Haiku) — P8.4 eseguito su richiesta; stato P8.2
- **Eccezione al cancello di P8.4.** Su richiesta esplicita di Robert è stato pubblicato il rilascio **0.3.0** (run `release-stable` 39773321991, successo; release `v0.3.0` stabile con `AllSkyBrain-win-Setup.exe`, `RELEASES`, `releases.win.json`, nupkg full e delta, portable zip). `main` è stato portato a `ralph/desktop-v1` (fast-forward, 66cb7af).
- Condizioni del cancello **non** soddisfatte al momento del rilascio: P8.2 non è `[x]`; lo smoke test con la Sim non era ancora fatto; P1.9 è aperto (rosso notte da confermare nei `::notice` del log, non leggibili dal proxy). Le altre condizioni sono vere: packId `AllSkyBrain`, exe `AllSkyBrain.exe`, scansione segreti senza credenziali reali (solo le regole di mascheratura in `LogSecretMask`).
- Verifiche fatte: suite shim completa **2011/2011** al secondo giro (il primo giro aveva 1 FAIL, il test intermittente noto); CI `build-windows` successo sul commit 66cb7af.
- Non verificato: avvio dell'exe su Windows con camera Sim; aggiornamento Velopack dalla 0.2.1 sull'altro PC; overlay meteo/satelliti a schermo; ComboBox in rosso notte (esito dei `::notice` non letto).
- Docs: README e `docs/FUNZIONI.md` aggiornati (meteo `weather.*` collegato e configurabile; satelliti nel sidecar). Commit 54a703f.
- **Smoke test headless (camera Sim), esito PASS parziale.** Avvio e arresto puliti; 18 frame JPEG con sidecar e miniature, keogram presente, log senza ERROR/Exception, nessun segreto nella cartella dati. Limiti: la prova è di giorno (0 frame notturni), dura 90 s con cadenza 5 s, interfaccia web, pubblicazione e YouTube non avviati. Non prova timelapse, startrails, maschera, rilevamento. Quindi **P8.2 resta aperto**: manca una notte simulata con timelapse e almeno un evento sintetico.
- Da controllare: con `storage.folder` che termina con `nights` i frame finiscono in `nights/nights/<data>`. Probabilmente è una mia impostazione di prova sbagliata, non un difetto; da verificare col default.
- Riferimenti della prova: `/home/claude/allsky-scratch/smoke/` (fuori dal repo).

## 2026-10-22 (14:30) — P8.2 completata con notte simulata (orologio accelerato x90)
- Harness fuori dal repo: `/home/claude/allsky-scratch/smoke2/` (Engine costruito direttamente con `EngineDependencies`, camera Sim con decoratore che inietta una scia sintetica alle 23:30 CEST; orologio da 20:00 CEST a 12:30 CEST, circa 11 minuti reali).
- Verificato: 955 frame (603 notte, 108 crepuscolo, 244 giorno); **timelapse.mp4** H.264 1280×960, 25 fps, 955 frame, 38,2 s; **keogram.jpg** e **startrails.jpg** presenti e non vuoti; **evento sintetico** salvato come `bolide` (`events/20261022T213009Z-000`) con `event.json`, `frame.jpg`, `crop.jpg`, `clip.mp4` (H.264, 5 frame); log `engine.log` senza ERROR né Exception; rilevamento: 711 frame analizzati, 1 evento; nessun segreto nella cartella dati.
- **Limiti dichiarati:**
  - Il timelapse della prova è senza overlay informativo: `IFrameOverlay` esiste solo nel Desktop WPF, quindi la maschera informativa non è verificata. Il percorso `mask.enabled`/`mask.imagePath` non è usato dal motore (commento in `FrameProcessor`).
  - L'encoder di prova è **libx264 (GPL)** con `AllowGpl=true`, solo per il test locale. L'encoder del prodotto su Windows è `h264_mf` e non è stato provato. Su quel PC va verificato in P8.2/Windows.
  - La classificazione dell'evento (bolide) riguarda una scia sintetica: non dice nulla sulla soglia reale.
- Da provare sul PC con la ZWO: overlay informativo sul timelapse; encoder `h264_mf` in edizione N e normale; scia reale; maschera.

## 2026-10-22 (15:00) — Fase 3 scritta nel piano (NON avviata)
- Su richiesta di Robert, `IMPLEMENTATION_PLAN.md` contiene ora la **fase 3 (P11–P16, 32 task)**: integrazione opzionale NINA (soggetto, mirino, grafico altezza, distanza dalla Luna, park, log collegati ai frame), accesso remoto con ruoli, studio di montaggio per materiale vecchio, colore del video, invio a un bot Telegram, rilascio 0.5.0. `PROMPT.md` vieta di iniziarla senza il via di Robert.
- Fatti verificati il 22/10/2026: plugin ninaAPI (christian-photo, MPL-2.0, REST `v2/api` + WebSocket, README su GitHub); cartella dei log di NINA `%localappdata%\NINA\logs` con livelli Error..Trace (documentazione NINA).
- **Non verificato:** porta predefinita, autenticazione, endpoint e nomi dei campi di ninaAPI (la pagina della documentazione è generata da JavaScript e non si legge con un fetch); formato righe e rotazione dei log; limiti Telegram. Sono i primi task (P11.0, P15.1).
- L'agente esperto UP Link conosce il protocollo del sito, non le API interne di NINA (le note `nina-api-notes.md` erano da scrivere al 19/10): lo si usa per rivedere le parti che toccano il contratto Uplink (P11.0, P11.9, P12.3 opzione A), non come fonte sui campi di NINA. Non è registrato come tipo di agente in questa sessione: va lanciato come agente generico con le sue istruzioni (Project, `claude/uplink-expert-agent.md`).
- Il vincolo «nessun server esposto» entra in conflitto con l'accesso esterno: la scelta (relay in uscita verso il sito, tunnel di terzi, porta aperta) è una decisione di Robert (P12.0).

## 2026-10-22 (15:30) — Decisioni di Robert sulla fase 3
- Accesso remoto: solo monitoraggio sul sito, nessun login, nessuna interazione; scelta tecnica delegata -> strada A (invio in uscita con Uplink, nessuna porta aperta). P12 riscritto (P12.0–P12.5); i task con ruoli, sessioni e coda comandi sono stati tolti.
- NINA sullo stesso PC della all-sky; plugin ninaAPI da verificare (la sessione non ha il computer collegato).
- Telegram: un solo bot con chat autorizzate.
- Idee aggiunte a P11: campo inquadrato (P11.9), capitoli YouTube (P11.10), allerta cielo che peggiora (P11.11), condensa sperimentale (P11.12). Scartato: indice di efficienza.

## 2026-10-23 — P14.2 (prima parte): correzioni temporali del colore, spente di default

**Stato:** P14.2 resta `[ ]` nel piano (non modificato, come richiesto). Fatta solo la prima parte: smoothing con costante di tempo, compensazione di esposizione e guadagno, preset, validazione. Mancano ancora: gestione delle alte luci e delle nuvole di giorno, neutralizzazione del fondo cielo, rumore temporale opzionale, misura sulla notte simulata (`smoke2`) e riduzione del flicker residuo senza perdere meteore e stelle.

**Cosa è stato fatto**
- `AllSky.Core`: sezione `color.corrections` (`ColorCorrections`), risoluzione dei preset in `ColorCorrectionPresets.Resolve` (funzione pura), validazione in `ConfigStore`. Sezione assente o `null` = tutto spento.
- `AllSky.Imaging`: `Smoother.Update(gains, alpha)` e `StretchParamsSmoother.Update(params, alpha)`, più `StretchParamsSmoother.Rescale(ratio)` (neri e bianco, midtone escluso). Le API esistenti non cambiano nell'aritmetica.
- `AllSky.Processing`: alpha = 1 - exp(-dt/tau), con tau interpolata giorno-notte come il blend del crepuscolo; compensazione del prodotto esposizione · guadagno lineare; reset per dt > 6 h o dt < 0 (solo con correzioni accese). Con correzioni spente il percorso è quello di prima.
- Test: `ColorCorrectionsTests` (Core), `SmootherTimedTests` (Imaging), `ColorCorrectionsProcessingTests` (Processing, con riferimento a `AllSky.ColorMetrics` nel csproj dei test; ColorMetrics non dipende da Processing).

**Verifiche**
- Identità con config di default: impronta SHA256 di 16 frame (RGB, JPEG e miniatura, con overlay, con `UpdateConfig` a metà) identica a prima della modifica, su due esecuzioni.
- Runner: AllSky.Imaging 184 PASS; AllSky.Processing 28 PASS; AllSky.Core 643 PASS; AllSky.ColorMetrics 37 PASS. Suite completa: 2452 test, 2451 PASS, 1 FAIL: `FrameMedian_UpdatesExposureForNextCapture`. Il test è fallito in tutte e 3 le esecuzioni sul codice nuovo (suite completa e due isolate) e in 2 su 2 su `c503eb1` (codice prima di P14.2): non è causato da questa modifica, ma qui non è risultato intermittente.
- `dotnet build`/`dotnet test` sul `.sln` non eseguiti: il restore NuGet è bloccato dal proxy (403). Il riferimento di progetto aggiunto nel csproj dei test non è quindi verificato da MSBuild.

**Misura (scena costante proporzionale all'esposizione, 30 s / 60 s ogni 20 frame, 240 frame, 320x240)**
- Salto relativo medio ai cambi (ExposureBanding): spente 1121 %, accese 0,0036 %.
- Salto assoluto di luminanza ai cambi (0..1): spente 0,8294, accese 0,00002.
- Il criterio (accese < metà delle spente) è soddisfatto con ampio margine. Il valore relativo è gonfiato: con le correzioni spente la luminanza oscilla tra 0,00 e 1,00 (saturazione e nero) e i frame vicini allo zero fanno esplodere il rapporto. Il flicker di LuminanceFlicker è 0 per entrambe, perché il trend mediano è zero sui frame neri: non informativo in questa scena.

**Limite trovato (non nel test)**
- Con un offset di bias di 800 ADU che non scala con l'esposizione: spente salto assoluto 0,759, accese 0,559; la luminanza resta tra 0 e 1 in entrambi i casi. La compensazione riduce il salto ma non basta. Ipotesi, non verificata: la riscalatura di neri e bianco porta in conto anche il bias. Diagnostica con i quattro casi (nessuna, solo tempo, solo compensazione, entrambe): `/home/claude/allsky-scratch/baseline/diagnostica-bias-P14.2.cs` (fuori dal repo).
- Lo scenario sintetico è molto più estremo della notte simulata di P14.1 (salto medio 2,0 %): non rappresenta le condizioni reali.

**Deviazioni dal task**
- «dt limitato a [0.01, 1]»: applicato all'alpha, non a dt. Con dt in secondi (30 s tra frame) il limite a 1 s avrebbe reso lo smoothing circa 30 volte più lento. Il limite a 0,01 è quello già usato da `MinSmoothingAlpha`.
- dt < 0 azzera lo smoothing, dt = 0 mantiene l'alpha precedente (il task indicava entrambi; ho distinto i due casi).
- `Color.Smoothing` non ha effetto sui guadagni e sullo stretch quando lo smoothing temporale è acceso (usato solo come valore iniziale e dopo il reset).

**Dubbi e [NV]**
- [NV] Unità del guadagno ZWO = 0,1 dB per unità: assunzione, da verificare sulla documentazione del driver. Non ho accesso a fonti esterne in questa sessione.
- [S] Preset: naturale (entrambe accese, tau 180/20 s), neutro (tau 300/40 s, saturazione 1,0), profondo (saturazione 1,25). Scelte di design, non misure.
- Il rapporto relativo ExposureBanding diverge con luminanze vicine a zero: per il confronto conviene il salto assoluto.
- Da provare sul PC con la ZWO: il comportamento con un bias reale e con il guadagno vero della camera.

**Repo**
- Un processo esterno ha creato il commit `e437fcf` («WIP P14.2: file temporaneo del subagente») durante il lavoro: contiene solo il mio file temporaneo `ZzBaselineHashTemp.cs`, già rimosso dal working tree. Non è stato riscritto nulla. Va tolto dalla storia del branch prima di pushare.

## P13.2 (23/10/2026)
Logica di montaggio in AllSky.Studio (Operations, Montage). Suite completa 2579 test. Restano nuvole, infoMask, colorCorrection, UI. Scelte da confermare: speed.factor 0,01..100; rotate 90 oraria; MAD senza 1,4826; date ISO solo UTC.

## P13.3 (23/10/2026)
Prodotti da serie in AllSky.Studio/Products. Suite 2595 test. Mancano decoder reale, selettore cerchio, timelapse da serie.

## Telegram (23/10/2026)
src/AllSky.Telegram: client, coda, limiti, impostazioni. 2706 test. Scelte da confermare: testo/didascalia oltre limite = InvalidInput; default 50 messaggi/giorno; politica video grandi = SendStillsWithLink; MB decimali; 5xx trattati come errori di rete.

## P13.4 (23/10/2026)
AllSky.Studio/Export: coda di esportazione con destinazioni finte. Suite 2770 test. Mancano rendering reale, adattatori YouTube/Telegram, UI.

## P17.3/P17.4 (23/10/2026)
MCP locale in src/AllSky.Mcp (118 test), render NINA nei test UI (da provare su Windows). Suite 2888. Da confermare: eta massima coordinate NINA 60 s.

## Test intermittente Capture (24/10/2026)
CaptureLoopTests.FrameMedian_UpdatesExposureForNextCapture: causa trovata. Il produttore legge CurrentSetting prima di chiamare la camera, quindi il frame 2 poteva partire con l esposizione vecchia (corsa). Il test ora blocca la prima attesa finche il consumer ha aggiornato l esposizione (GatedDelay). Codice di produzione invariato. Da riconfermare sulla CI Windows.

## 2026-10-24 — P1.9 (parziale), richiesta P1.12 (esposizione e gain manuali)
- Fatto: P1.9 avviata. Test `DesktopUiTests` 54/54 verdi in locale (dotnet 10, Release). Corretta la scheda «Condizioni» di Stanotte (`TonightView.cs`: `Row` a colonne Star con a capo; colonna laterale in `ScrollViewer`). Build Desktop: 0 errori.
- Test: 54 / 54 / 0.
- Restano in P1.9: i messaggi «Non disponibile…» in monospazio e troppo alti a 1280×800; pulsante primario in rosso notte poco leggibile in Impostazioni; revisione dei 1920×1080; screenshot non ancora copiati in `docs/ralph/shots/`.
- Decisioni: P1.9 resta `[~]`. La richiesta di Robert su esposizione e gain manuali è P1.12, non ancora implementata.
- Dubbi / da confermare con Robert: valore predefinito di `ManualExposureMs`; se «ecc.» include anche binning o altri parametri oltre a esposizione e gain.

## 2026-10-24 — P1.12 (parziale): esposizione e gain manuali
- Fatto: `camera.exposureMode` (`auto` / `manual`) e `camera.manualExposureMs` (predefinito 5000 ms, [S-scelta]). In `manual` l'engine usa min = max = esposizione manuale, clampata sui limiti del driver (avviso nel log se il driver la riduce). Validazione nell'engine: esposizione manuale > 0 e finita; in `manual`, non superiore a `maxExposureMs`. `CameraSettingsForm` esteso (modalità, campo manuale, `AboveMaximum`), vista Camera con casella «Esposizione manuale» e campo IT/EN.
- Il gain era già fisso in entrambe le modalità (il controller regola solo l'esposizione): la richiesta di «gain manuale» è soddisfatta già prima di questa modifica. La nota in vista lo dice.
- Test: suite completa 2891 test verdi tranne 5 falliti già prima delle modifiche (confermato con stash): `FolderScannerTests.CollegamentiSimbolici_FuoriNonSeguiti_DentroAccettati`, `McpServerHostTests.Acceso_ApreLaPortaSoloSulLoopback`, `McpServerTests.AscoltaSoloSuLoopback`, `McpServerTests.PortaOccupata_LanciaUnErroreGestibile`, `McpServerHostTests.PortaOccupata_NonLanciaELoRegistra`. Nuovi test: 8 in `CameraSettingsFormTests` (Core 651/651), 1 in `AutoExposureTests` (Capture 53/53).
- Restano in P1.12: test engine con camera Sim (esposizione costante nei frame) e test UI del pannello.
- Decisioni: valore predefinito 5000 ms [S-scelta]; nessun campo binning (la chiave `camera.binning` non è letta, già segnalato in FUNZIONI.md).
- Dubbi / da confermare con Robert: il predefinito 5000 ms; se «ecc.» include altri parametri oltre a esposizione e gain.
