# Funzioni del progetto All Sky Brain

All Sky Brain è un'applicazione per Windows che controlla una camera all-sky: cattura i frame, raggruppa le immagini per notte e crea timelapse, keogram e startrails. Il codice è in C#. Il motore (`AllSky.Engine`) e l'Host (`AllSky.Host`) sono usati dall'app desktop WPF (`AllSky.Desktop`) in-process. Le librerie di base sono **Core** (configurazione, tipi di frame e di camera, interfacce), **Astro** (posizione del Sole e della Luna, fasi del cielo, notte osservativa) e **Imaging** (demosaicing, bilanciamento del bianco, stretch e levigatura). Il ramo desktop v1 aggiunge i moduli `AllSky.Sky`, `AllSky.Satellites`, `AllSky.Detection`, `AllSky.Events`, `AllSky.Weather`, `AllSky.InfoOverlay`, `AllSky.YouTube` e `AllSky.Transits`. L'interfaccia web (opzionale, spenta di default) è in JavaScript a moduli, in `web/js` (servizi comuni) e `web/js/views` (schermate); `web/mock/server.mjs` è un motore di prova con dati finti che implementa lo stesso contratto HTTP.

Convenzioni del documento:

- **Riga**: la prima riga della dichiarazione (firma), non quella del commento XML o JSDoc.
- **Funzioni anonime** (callback, metodi di oggetti restituiti come `focus` o `refresh`): sono descritte nella voce della funzione che le crea, con la loro riga.
- **Enum, interfacce, costanti e tabelle**: sono descritti nelle note della sezione, non contati come funzioni. Le proprietà con logica (getter, proprietà statiche calcolate) sono contate.
- **Costruttori**: quelli espliciti sono voci a sé; per i `record` il costruttore implicito è descritto come tale.
- **Da verificare**: elencate in fondo al documento.

---

## Parametri di config.json

Fonti: `src/AllSky.Core/AppConfig.cs` (nomi, tipi e valori predefiniti) e `src/AllSky.Core/ConfigStore.cs` (validazione). Le chiavi sono in camelCase. Il file sta in `<cartella dati>\config.json`, con cartella dati `%LOCALAPPDATA%\AllSky` salvo `--data-folder` o `ALLSKY_DATA_FOLDER`.

Regole di lettura:

- Se il file manca, si usano i valori predefiniti; il file non viene creato da `ConfigStore.Load`, ma l'Host lo crea al primo avvio con `storage.folder` uguale alla cartella dati.
- Se il JSON non è valido, il file viene rinominato in `config.json.bad` e si usano i valori predefiniti.
- Una chiave assente prende il valore predefinito della classe. Le sezioni aggiunte nel ramo desktop v1 (`satellites`, `detection`, `youtube`, `capture.transitMode`, `overlay.applyToTimelapse`, `overlay.layout`, `optics`) sono opzionali: una configurazione precedente le legge con i valori predefiniti.
- Colonna **Validazione**: i vincoli elencati sono quelli di `ConfigStore.Validate`, salvo dove indicato. Il valore fuori dai vincoli fa fallire il salvataggio dalla UI; con modifica manuale del file, l'Host lo legge comunque.
- Colonna **Stato**: `letto` se un punto del codice applicativo legge la chiave (verificato con ricerca nel sorgente il 22/10/2026); `non letto` se nessun punto la legge.

File della cartella dati fuori da `config.json`: `youtube-auth.json` (token e client secret cifrati con DPAPI), `updates.json` (`{"autoCheck": true}`, predefinito attivo), `publish.json` e `publish-queue\` (Uplink), `satellites\` (cache dei TLE), `loghi\` (PNG della maschera, copiati dall'editor), `logs\` (`allsky-AAAAMMGG.log`, `startup.log`, `crash.log`). Le notti stanno in `storage.folder\nights\`, e gli eventi in `nights\<notte>\events\`.

### Sezioni generali

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `schemaVersion` | intero | `1` | nessuna | letto | Versione dello schema del file. |
| `language` | `"it"` \| `"en"` | `"it"` | nessuna | letto | Lingua della UI. Il cambio lingua del desktop si applica al riavvio. |
| `optics` | oggetto o `null` | `null` | nessuna | letto | Calibrazione ottica accettata (vedi sotto). Scritta dalla vista Camera, non da modificare a mano. |

### `site`: osservatorio

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `site.name` | testo | `"Osservatorio"` | nessuna | letto | Nome del sito. |
| `site.latitude` | numero | `45.07` | da -90 a 90 | letto | Latitudine in gradi, positiva a nord. |
| `site.longitude` | numero | `7.69` | da -180 a 180 | letto | Longitudine in gradi, positiva a est. |
| `site.elevationM` | numero | `240` | la UI accetta da -500 a 9000 m | letto | Quota in metri. |
| `site.timezone` | testo | `"Europe/Rome"` | identificatore IANA | letto | Fuso orario per le ore locali e per la notte osservativa. |

### `camera`

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `camera.id` | testo | `"sim-1"` | nessuna | letto | Camera da usare. Formati: `sim-N` (simulatore), `ascom-com:<ProgID>` (driver COM), `alpaca:<host>:<porta>:<deviceNumber>`. |
| `camera.gain` | intero | `250` | nessuna | letto | Guadagno in notte. |
| `camera.maxExposureMs` | numero | `30000` | nessuna | letto | Esposizione massima in millisecondi. |
| `camera.dayExposureMs` | numero | `2` | nessuna | letto | Esposizione di giorno in millisecondi. |
| `camera.exposureMode` | testo | `"auto"` | `auto` o `manual` | letto | `auto`: il controllo regola l'esposizione. `manual`: esposizione fissa a `manualExposureMs`, controllo spento. Il gain è sempre quello di `camera.gain`, in entrambe le modalità. |
| `camera.manualExposureMs` | numero | `5000` | maggiore di zero; in `manual`, non superiore a `maxExposureMs` | letto | Esposizione fissa in millisecondi, usata solo in modalità `manual`. Valore predefinito scelto [S-scelta]. Se il driver accetta meno, si usa il limite del driver e il log lo segnala. |
| `camera.binning` | intero | `1` | nessuna | **non letto** | Il valore non entra nella cattura. |

### `color`: elaborazione dell'immagine

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `color.whiteBalance` | `"auto"` \| `"manual"` | `"auto"` | nessuna | letto | Bilanciamento del bianco automatico o con guadagni fissi. |
| `color.manualGains` | tre numeri | `[1.0, 1.0, 1.0]` | nessuna | letto | Guadagni R, G, B usati con `manual`. |
| `color.smoothing` | numero | `0.85` | da 0 a 1 | letto | Levigatura tra frame successivi. |
| `color.gradientRemoval` | numero | `0.5` | nessuna | letto | Intensità della rimozione del gradiente di cielo. |
| `color.stretch` | `"mtf"` \| `"asinh"` | `"mtf"` | nessuna | letto | Curva di stretch. |
| `color.saturation` | numero | `1.1` | nessuna | letto | Saturazione. |

### `mask`: sezione non usata

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `mask.enabled` | booleano | `false` | nessuna | **non letto** | Nessun punto del codice la legge. |
| `mask.imagePath` | testo o `null` | `null` | nessuna | **non letto** | Il processing non usa questo campo (vedi il commento in `src/AllSky.Processing/FrameProcessor.cs`). |

La maschera informativa del timelapse è in `overlay.layout`.

### `overlay`: scritta e maschera informativa

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `overlay.version` | intero | `1` | nessuna | letto | Versione dello schema del sotto-oggetto. |
| `overlay.enabled` | booleano | `true` | nessuna | letto | Scritta testuale del flusso di elaborazione. |
| `overlay.showTime` | booleano | `true` | nessuna | letto | Mostra l'ora nella scritta testuale. |
| `overlay.showSensorTemp` | booleano | `true` | nessuna | letto | Mostra la temperatura del sensore nella scritta testuale. |
| `overlay.label` | testo | `""` | nessuna | letto | Testo fisso della scritta testuale. |
| `overlay.applyToTimelapse` | booleano | `false` | nessuna | letto | Applica la maschera informativa ai frame del timelapse, al momento del montaggio. |
| `overlay.applyToLive` | booleano | `false` | nessuna | **non letto** | Riservato al flusso dal vivo. Il valore non cambia nulla. |
| `overlay.applyToPublish` | booleano | `false` | nessuna | **non letto** | Riservato alle immagini pubblicate (Uplink). Lasciato `false`, il contratto delle immagini inviate non cambia. |
| `overlay.layout` | oggetto o `null` | `null` (layout predefinito) | tollerato da `OverlayLayoutCodec` | letto | Layout della maschera, descritto sotto. |

Layout (`overlay.layout`, classe `OverlayLayout`, `versione` = 1). I nomi delle proprietà sono in camelCase.

- `versione`: intero, versione dello schema.
- `maschera`: `modo` (`"nessuna"`, `"cerchio"`, `"bande"`); `cerchio` con `centroX`, `centroY` (frazioni del lato), `raggio`, `esterno` (`"opaco"` o `"sfumato"`), `sfumatura`; `bande` con `superiore` e `inferiore`.
- `stile`: `scalaTesto` (frazione dell'altezza del frame) e `margine`.
- `widget`: lista di oggetti con `tipo`, `abilitato`, `posizione`, `ordine` e `formato` (facoltativo).
  - `tipo`: `dataOra`, `sole`, `luna`, `satelliti`, `meteo`, `logo`, `testoLibero`, `esposizione`, `bussola`.
  - `posizione`: `TL`, `TR`, `BL`, `BR`, `BandaAlta`, `BandaBassa`.
  - `formato`: `pattern` e `fuso` (`"locale"` o `"utc"`) per `dataOra`; `decimali` e `mostraFase` per `sole`/`luna`; `maxElementi` per `satelliti`; `campi` per `meteo` (`temperatura`, `umidita`, `pressione`, `vento`, `nuvole`, `pioggia`, `luminositaCielo`); `fileLogo`, `altezzaMaxPercento`, `opacita` per `logo`; `testo` per `testoLibero`; `mostraGain` per `esposizione`.
- Predefinito: maschera `cerchio` (centro 0,5/0,5, raggio 0,5, esterno opaco, sfumatura 0,02), scala testo 0,028, margine 0,02. Widget attivi: `dataOra` in `BL`, `sole` in `BR`, `luna` in `BR` (ordine 1). Spenti: `satelliti`, `meteo`, `logo`, `testoLibero`, `esposizione`, `bussola`.
- Stato dei widget: `meteo` riceve dati solo con una sorgente configurata in `weather` (vedi README, sezione «Meteo»). `satelliti` riceve i nomi dei passaggi dal sidecar; il collegamento in overlay non è ancora verificato a schermo. `bussola` è sempre omesso dal compositore, che lo segnala come non disponibile.

### `capture`: cattura

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `capture.intervalSeconds` | intero | `30` | almeno 1 | letto | Intervallo tra due pose. |
| `capture.dayCapture` | booleano | `true` | nessuna | letto | Cattura anche di giorno. |
| `capture.transitMode.version` | intero | `1` | nessuna | letto | Versione dello schema del sotto-oggetto. |
| `capture.transitMode.enabled` | booleano | `false` | nessuna | letto | Modalità transito (P4.6): durante un passaggio previsto riduce l'intervallo tra le pose. |
| `capture.transitMode.intervalFraction` | numero | `0.5` | da 0,05 a 1 (dal commento di `AppConfig`; `ConfigStore.Validate` non lo controlla) | letto | Frazione dell'intervallo normale usata durante il passaggio. Non scende sotto il minimo fisico della camera. |
| `capture.transitMode.restoreMarginSeconds` | numero | `5` | nessuna | letto | Secondi dopo la fine del passaggio prima di ripristinare l'intervallo normale. |
| `capture.transitMode.readoutSeconds` | numero | `2` | nessuna | letto | Tempo di lettura e scarico dichiarato della camera. Il minimo fisico è esposizione più questo valore. Valore stimato, non misurato. |

### `products`: prodotti del timelapse

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `products.timelapse` | booleano | `true` | nessuna | letto | Genera il timelapse a fine notte. |
| `products.keogram` | booleano | `true` | nessuna | letto | Genera il keogram. |
| `products.startrails` | booleano | `true` | nessuna | letto | Genera le startrails. |
| `products.timelapseFps` | intero | `25` | da 1 a 120 (controllato dall'Engine) | letto | Frame al secondo del timelapse. |

### `storage`: archivio

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `storage.folder` | testo | nel file creato al primo avvio: la cartella dati; se la chiave manca: `%USERPROFILE%\AllSky` | nessuna | letto | Radice dell'archivio. Le notti stanno in `<folder>\nights\<AAAA-MM-GG>\`. |
| `storage.keepNights` | intero | `30` | almeno 1 | letto | Notti da conservare. Le più vecchie vengono eliminate. |

### `ui`: interfaccia

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `ui.theme` | `"auto"` \| `"day"` \| `"night"` \| `"red"` | `"auto"` | nessuna | letto | Tema dell'interfaccia. La notte rossa si attiva anche con Ctrl+R. |
| `ui.port` | intero | `5810` | da 1024 a 65535 | letto | Porta del server web, se attivo. |
| `ui.webEnabled` | booleano | `false` | nessuna | letto | Avvia il server per il browser. Con `false` l'Host non apre alcuna porta HTTP. Modifica effettiva al riavvio. |
| `ui.allowLan` | booleano | `false` | nessuna | letto | Ascolta sulla rete locale. Ha effetto solo con `webEnabled` e con `lanPasswordHash` impostato. Con `false` si ascolta solo su `127.0.0.1`. |
| `ui.lanPasswordHash` | testo o `null` | `null` | nessuna | letto | Hash PBKDF2 della password per la rete locale. Mai la password in chiaro. |

### `weather`: sorgente meteo

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `weather.version` | intero | `1` | nessuna | letto | Versione dello schema del sotto-oggetto. |
| `weather.source` | testo | `none` | `none`, `ascom` o `openmeteo` | letto | Sorgente meteo. `none` non avvia alcun fornitore. |
| `weather.ascomProgId` | testo o `null` | `null` | obbligatorio con `source` = `ascom` | letto | ProgID del driver ASCOM ObservingConditions. |
| `weather.readTimeoutSeconds` | intero | `5` | da 1 a 60 | letto | Attesa massima per ogni lettura. |
| `weather.maxAgeMinutes` | intero | `30` | da 1 a 1440 | letto | Dopo questa età il dato meteo non viene più usato. |
| `weather.openMeteoAcknowledgedNonCommercial` | booleano | `false` | nessuna | letto | Senza `true` la sorgente `openmeteo` non parte; il motivo va nel log, non è un errore di validazione. |

Stato della sorgente meteo:

- Non c'è un'interfaccia: la sezione si modifica solo in `config.json`. Il fornitore viene ricreato quando cambia la sezione, senza riavviare l'app.
- Il dato meteo finisce nel sidecar dei frame (campo `weather`) e nel widget **Meteo** della maschera, se attivo. La riga Meteo della vista Stanotte mostra ancora un segnaposto.
- `openmeteo`: l'attribuzione «Dati meteo: Open-Meteo.com (CC BY 4.0)» è definita in `OpenMeteoSource.Attribution` e passata alla Engine. Non viene ancora scritta nel sidecar né mostrata. Stato: **non collegata**.
- Unità dei campi Open-Meteo: non confermate su una risposta reale.
- `ascom`: il driver ObservingConditions non è stato provato con una stazione reale.

### `satellites`: previsione dei passaggi

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `satellites.version` | intero | `1` | nessuna | letto | Versione dello schema del sotto-oggetto. |
| `satellites.enabled` | booleano | `false` | nessuna | letto | Interruttore generale. Con `false` non parte alcuna richiesta a CelesTrak. Non c'è un interruttore nella UI: si modifica `config.json`. |
| `satellites.targets` | lista di `{ "noradId", "name" }` | `[{ "noradId": 25544, "name": "ISS (ZARYA)" }]` | da 1 a 99999 per `noradId`; massimo 20 elementi; nessun duplicato; `[]` ammesso | letto | Satelliti da seguire, per numero NORAD. `name` serve solo alla visualizzazione; vuoto: si mostra il numero. Oltre 99999 il TLE non è disponibile. |
| `satellites.windowHours` | numero | `24` | da 1 a 72 | letto | Finestra della previsione, a partire da ora. |
| `satellites.minElevationDeg` | numero | `10` | da 0 a 90 | letto | Elevazione minima perché un passaggio conti. |
| `satellites.sunMaxAltDeg` | numero | `-6` | da -90 a 0 | letto | Altezza massima del Sole per considerare il cielo buio (crepuscolo civile). |

### `detection`: meteore, bolidi e transiti

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `detection.version` | intero | `1` | da 1 alla versione corrente | letto | Versione dello schema del sotto-oggetto. |
| `detection.enabled` | booleano | `false` | nessuna | letto | Interruttore generale. Con `false` nessun worker di rilevamento parte. Non c'è un interruttore nella UI. |
| `detection.saveAllVisiblePasses` | booleano | `false` | nessuna | letto | Salva anche i passaggi non confermati nelle immagini, con il verdetto registrato. |
| `detection.analysisLongSidePx` | intero | `960` | da 240 a 4096 | letto | Lato lungo del frame ridotto usato per il rilevamento. |
| `detection.clipFps` | intero | `25` | da 1 a 120 | letto | Frame al secondo delle mini-clip degli eventi. |

`detection.streak`: soglie di `StreakDetector` e `StreakClassifier`. I valori iniziali sono da tarare su cielo reale: le prove sono su dati sintetici.

| Chiave | Predefinito | Validazione | Descrizione |
|---|---|---|---|
| `backgroundFrames` | `8` | da 1 a 120 | Frame su cui il fondo adattivo fa la media. |
| `thresholdSigma` | `3.0` | da 0 a 20 | Moltiplicatore sigma della soglia. |
| `minDifferenceLuma` | `6.0` | da 0,5 a 255 | Differenza minima assoluta, in livelli a 8 bit. |
| `minAreaPx` | `15` | da 1 a 100000 | Area minima di una componente, in pixel. |
| `minLengthPx` | `25.0` | vedi `ConfigStore.Validate` | Lunghezza minima della scia, in pixel del frame ridotto. |
| `maxWidthPx` | `6.0` | vedi `ConfigStore.Validate` | Spessore massimo della scia, in pixel. |
| `minElongation` | `6.0` | vedi `ConfigStore.Validate` | Elongazione minima. |
| `maxChangedFraction` | `0.02` | in (0, 1] | Frazione di pixel cambiati oltre la quale il frame viene scartato (nuvole, fulmini, fari). |
| `trackDistancePx` | `12.0` | vedi `ConfigStore.Validate` | Distanza massima dalla traccia prevista di un satellite, in pixel. |
| `trackAngleDeg` | `8.0` | da 0 a 90 | Scarto angolare massimo dalla traccia prevista, in gradi. |
| `repeatShiftPx` | `30.0` | vedi `ConfigStore.Validate` | Spostamento massimo tra frame consecutivi perché due scie siano lo stesso oggetto (aereo o satellite). |
| `repeatAngleDeg` | `8.0` | da 0 a 90 | Scarto angolare massimo tra frame consecutivi, in gradi. |
| `maxFrameGapSeconds` | `30.0` | da 1 a 3600 | Intervallo massimo tra frame consecutivi. Non scende sotto 2,5 volte l'intervallo di cattura. |
| `bolideSaturatedPeakLuma` | `200.0` | positivo | Picco sopra il fondo oltre il quale la scia è un bolide. |
| `bolideIntegratedLuma` | `4000.0` | positivo | Luminosità integrata oltre la quale la scia è un bolide. |
| `bolideLengthPx` | `300.0` | positivo | Lunghezza oltre la quale la scia è un bolide, in pixel del frame ridotto. |

`detection.transit`: soglie di `TransitConfirmer`. Valori iniziali da tarare; il rischio noto è il bordo di una nuvola allineato alla traccia (falso positivo).

| Chiave | Predefinito | Validazione | Descrizione |
|---|---|---|---|
| `corridorHalfWidthPx` | `4.0` | tra 0 e 50 pixel | Semiampiezza del corridoio attorno alla traccia. |
| `flankWidthPx` | `6.0` | tra 0 e 50 pixel, fascia almeno 1 | Larghezza di ciascuna fascia di fondo. |
| `sampleSpacingPx` | `1.0` | vedi `ConfigStore.Validate` | Distanza tra due campioni lungo la traccia. |
| `sigmaMultiplier` | `5.0` | vedi `ConfigStore.Validate` | Soglia in sigma robuste del fondo. |
| `minExcessLevels` | `10.0` | vedi `ConfigStore.Validate` | Soglia minima assoluta di eccesso, in livelli a 8 bit. |
| `scoreThreshold` | `0.6` | da 0 a 1 | Frazione della traccia sopra soglia perché il frame sia identificato. |
| `minVisibleTrackPx` | `40.0` | non negativo | Lunghezza minima del tratto visibile, in pixel. |

### `youtube`: caricamento su YouTube

Il client secret e i token non stanno in questa sezione: sono nel file `youtube-auth.json` della cartella dati, cifrato con DPAPI. Vedi `docs/GUIDA-YOUTUBE.md`.

| Chiave | Tipo | Predefinito | Validazione | Stato | Descrizione |
|---|---|---|---|---|---|
| `youtube.version` | intero | `1` | nessuna | letto | Versione dello schema del sotto-oggetto. |
| `youtube.enabled` | booleano | `false` | nessuna | letto | Caricamento automatico. Lo impostano collegamento e scollegamento dell'account. |
| `youtube.clientId` | testo o `null` | `null` | nessuna | letto | Client ID delle credenziali "Desktop app". Non è un segreto. |
| `youtube.dailyTime` | testo `"HH:mm"` | `"12:30"` | formato `HH:mm` (controllato da `YouTubeSettingsRules`, non da `ConfigStore.Validate`) | letto | Ora locale del caricamento. |
| `youtube.catchUpWindowHours` | intero | `12` | da 0 a 24 | letto | Se il PC era spento all'ora prevista, il caricamento parte entro queste ore dall'avvio. |
| `youtube.privacy` | `"private"` \| `"unlisted"` \| `"public"` | `"private"` | valore dall'elenco (`YouTubeSettingsRules`) | letto | Privacy del video. |
| `youtube.tags` | lista di testi | `["timelapse", "allsky", "astrofotografia"]` | nessun tag vuoto | letto | Tag del video. |
| `youtube.categoryId` | testo | `"28"` | solo cifre | letto | ID numerico della categoria YouTube. Il valore predefinito non è verificato su una fonte ufficiale [NV]. |
| `youtube.location` | testo | `"Osservatorio"` | nessuna | letto | Valore di `{luogo}` nei modelli. |
| `youtube.titleTemplate` | testo | `"Timelapse notte del {notte:yyyy-MM-dd}"` | segnaposto noti, formati .NET validi, massimo 100 caratteri | letto | Modello del titolo. |
| `youtube.descriptionTemplate` | testo | `"Timelapse della notte del {notte:yyyy-MM-dd} da {luogo}: {fotogrammi} fotogrammi, durata {durata}, meteore {meteore}, transiti {transiti}."` | segnaposto noti, massimo 5000 byte, senza `<` e `>` | letto | Modello della descrizione. |

Segnaposto dei modelli: `{data:formato}` (data locale di oggi), `{notte:formato}` (data di inizio della notte), `{ora:formato}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`. Formato predefinito della data `yyyy-MM-dd`, dell'ora `HH:mm`.

### `optics`: calibrazione ottica (scritta dalla calibrazione)

`null` finché l'utente non accetta una calibrazione nella vista Camera. Non va modificata a mano.

| Chiave | Descrizione |
|---|---|
| `optics.version` | Versione dello schema, 1. |
| `optics.centerX`, `optics.centerY` | Centro del cerchio del cielo, in pixel del frame completo. |
| `optics.scaleRadiusPx` | Raggio del cerchio dell'orizzonte, in pixel. |
| `optics.northRotationDeg` | Rotazione del Nord, in gradi. |
| `optics.mirrored` | `true` se l'asse est-ovest è invertito guardando verso l'alto. |
| `optics.k2` | Termine radiale di correzione. |
| `optics.frameWidthPx`, `optics.frameHeightPx` | Dimensioni del frame su cui è stata fatta la calibrazione. |
| `optics.source` | `"sun"` (dal Sole) o `"manual"` (inserita a mano). |
| `optics.rmsPx` | Errore RMS del fit, in pixel. |
| `optics.calibratedAtUtc` | Istante della calibrazione, UTC. |

---

## AllSky.Core (src/AllSky.Core)

### AppConfig.cs

Contiene l'enum `Language` (`It`, `En`), `WhiteBalanceMode`, `Stretch` (`Mtf`, `Asinh`) e `Theme` (`Auto`, `Day`, `Night`, `Red`), che si serializzano come stringhe camelCase. Il `record AppConfig` rispecchia il JSON della configurazione (sezioni `site`, `camera`, `color`, `mask`, `overlay`, `capture`, `products`, `storage`, `ui`, più `optics`, `satellites`, `detection` e `youtube`, aggiunte nel ramo desktop v1). Nomi e valori predefiniti completi sono nella sezione «Parametri di config.json».

#### AppConfig

- **AppConfig** (costruttore implicito, riga 37) — `AppConfig()`. Crea una configurazione con tutti i valori di default: sito "Osservatorio" a 45.07, 7.69 (quota 240 m, fuso Europe/Rome), camera `sim-1`, guadagno 250, cattura ogni 30 secondi, porta 5810, accesso dalla rete locale spento. Le sezioni annidate `SiteConfig` (riga 61), `CameraConfig` (74), `ColorConfig` (87), `MaskConfig` (102), `OverlayConfig` (109), `CaptureConfig` (120), `ProductsConfig` (127), `StorageConfig` (138) e `UiConfig` (146) sono record con costruttore implicito e proprietà `init`. La cartella di default di `StorageConfig` è `AllSky` nella cartella utente.

### CameraInfo.cs

#### CameraInfo

- **CameraInfo** (costruttore del record posizionale, riga 4) — `CameraInfo(int Width, int Height, BayerPattern Pattern, int MaxAdu, double PixelSizeUm, bool CanSetGain, double MinExposureMs, double MaxExposureMs)`. Descrive le caratteristiche statiche di una camera: dimensioni in pixel, matrice di Bayer, valore massimo in ADU, dimensione del pixel in micrometri, se il guadagno è regolabile, limiti di esposizione in millisecondi.

### BayerPattern.cs

Contiene solo l'enum `BayerPattern` (`None`, `RGGB`, `BGGR`, `GRBG`, `GBRG`). Non ha funzioni. Attenzione: l'enum omonimo in `AllSky.Imaging` non ha il valore `None`.

### ConfigStore.cs

Contiene la classe `ConfigStore`, che carica, salva e valida `AppConfig`. Il file è in JSON; il salvataggio è atomico.

#### ConfigStore

- **ConfigStore.SerializerOptions** (proprietà statica, riga 14) — `static JsonSerializerOptions SerializerOptions`. Opzioni JSON condivise: nomi di proprietà in camelCase, enum come stringhe camelCase, JSON indentato.
- **ConfigStore.Load** (riga 20) — `AppConfig Load(string path)`. Legge la configurazione dal file. Se il file non esiste restituisce i default senza crearlo. Se il JSON non è leggibile, o se il deserializzatore restituisce `null`, rinomina il file in `<path>.bad` (sovrascrivendo un eventuale precedente) e restituisce i default. Eccezioni: `ArgumentException` se il percorso è vuoto o solo spazi.
- **ConfigStore.Save** (riga 53) — `void Save(string path, AppConfig config)`. Crea la cartella se manca, scrive il JSON in `<path>.tmp` (UTF-8 senza BOM) e lo sposta sul file finale. In caso di errore cancella il file temporaneo e rilancia l'eccezione. Eccezioni: `ArgumentException` (percorso vuoto), `ArgumentNullException` (configurazione nulla), eccezioni di I/O.
- **ConfigStore.Validate** (riga 84) — `IReadOnlyList<ValidationError> Validate(AppConfig config)`. Controlla: latitudine tra -90 e 90, longitudine tra -180 e 180, intervallo di cattura di almeno 1 secondo, notti da conservare di almeno 1, porta tra 1024 e 65535, smoothing tra 0 e 1. Restituisce una lista vuota se la configurazione è valida. Eccezione: `ArgumentNullException` se la configurazione è nulla.
- **ConfigStore.InRange** (riga 124, privato statico) — `bool InRange(double value, double min, double max)`. Verifica `min <= value <= max`. Con NaN il confronto è falso, quindi restituisce `false`.
- **ConfigStore.CreateSerializerOptions** (riga 129, privato statico) — `JsonSerializerOptions CreateSerializerOptions()`. Crea le opzioni usate da `SerializerOptions`: camelCase, indentazione, enum come stringhe senza valori numerici.

### ICameraSource.cs

Interfaccia `ICameraSource : IAsyncDisposable`, implementata dalle sorgenti di frame (simulatore, ASCOM COM, ASCOM Alpaca). Espone le proprietà `Id`, `Name` e `Info` (di tipo `CameraInfo`), senza logica. Le implementazioni non sono in questi file.

#### ICameraSource

- **ICameraSource.ConnectAsync** (riga 10) — `Task ConnectAsync(CancellationToken cancellationToken)`. Apre la connessione alla camera. Il comportamento preciso dipende dall'implementazione.
- **ICameraSource.CaptureAsync** (riga 12) — `Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)`. Esegue un'esposizione con la durata in millisecondi e il guadagno indicati, e restituisce il frame grezzo.

### IClock.cs

Interfaccia `IClock` con la sola proprietà `UtcNow`. Serve a iniettare un orologio nei test. Nessuna funzione.

### IPipelineStage.cs

#### IPipelineStage

- **IPipelineStage.Process** (riga 6) — `TOut Process(TIn input)`. Trasforma un valore di ingresso nel valore di uscita di una fase di elaborazione. Non ci sono implementazioni in questi file.

### RawFrame.cs

#### RawFrame

- **RawFrame** (costruttore del record posizionale, riga 7) — `RawFrame(int Width, int Height, ushort[] Pixels, BayerPattern Pattern, DateTimeOffset Timestamp, double ExposureMs, double Gain, double? SensorTempC)`. Frame grezzo a 16 bit, in ordine row-major (indice = `y * Width + x`). La temperatura del sensore è facoltativa (`null` se non disponibile). Gli array si confrontano per riferimento.

### SystemClock.cs

#### SystemClock

- **SystemClock.UtcNow** (proprietà, riga 6) — `DateTimeOffset UtcNow`. Restituisce l'ora UTC del sistema, implementando `IClock`.

### ValidationError.cs

#### ValidationError

- **ValidationError** (costruttore del record posizionale, riga 4) — `ValidationError(string Path, string Message)`. Un errore di validazione: `Path` è il percorso JSON del campo (per esempio `site.latitude`), `Message` è il testo per l'utente.

---

## AllSky.Astro (src/AllSky.Astro)

Calcoli astronomici a bassa precisione, senza librerie esterne. Gli angoli delle funzioni pubbliche sono in gradi; l'altezza è geometrica, cioè senza rifrazione atmosferica. Le precisioni citate sono quelle dichiarate nei commenti del codice: questa documentazione non le verifica.

### AstroTime.cs

Classe statica `internal AstroTime`: funzioni di supporto (giorno giuliano, secoli, angoli, controlli di input) usate dagli altri file del modulo.

#### AstroTime

- **AstroTime.JulianDay** (riga 10) — `double JulianDay(DateTimeOffset utc)`. Giorno giuliano UT dell'istante, ricavato dai millisecondi Unix (epoca JD 2440587.5).
- **AstroTime.Centuries** (riga 16) — `double Centuries(double julianDay)`. Secoli giuliani trascorsi da J2000.0 (JD 2451545.0), cioè la differenza in giorni divisa per 36525.
- **AstroTime.NormalizeDeg** (riga 22) — `double NormalizeDeg(double deg)`. Riporta un angolo nell'intervallo [0, 360) con `NormalizeRange`.
- **AstroTime.NormalizeRange** (riga 28) — `double NormalizeRange(double value, double period)`. Riporta il valore in [0, period) usando il resto della divisione; i valori negativi vengono riportati in positivo.
- **AstroTime.ToRad** (riga 38) — `double ToRad(double deg)`. Converte gradi in radianti.
- **AstroTime.ToDeg** (riga 40) — `double ToDeg(double rad)`. Converte radianti in gradi.
- **AstroTime.SinDeg** (riga 42) — `double SinDeg(double deg)`. Seno di un angolo espresso in gradi.
- **AstroTime.CosDeg** (riga 44) — `double CosDeg(double deg)`. Coseno di un angolo espresso in gradi.
- **AstroTime.CheckLatLon** (riga 46) — `void CheckLatLon(double latDeg, double lonDeg)`. Verifica latitudine in [-90, 90] e longitudine in [-180, 180]; un NaN non passa il controllo. Eccezione: `ArgumentOutOfRangeException` se un valore è fuori intervallo.

### MoonPosition.cs

Classe statica pubblica `MoonPosition`. Usa la serie di Meeus (cap. 47) troncata ai termini principali, più i termini additivi. Le tabelle `LongDistTerms` e `LatTerms` contengono i coefficienti.

#### MoonPosition

- **MoonPosition.Altitude** (riga 81) — `double Altitude(DateTimeOffset utc, double latDeg, double lonDeg)`. Altezza della Luna in gradi sull'orizzonte geometrico, con la correzione di parallasse diurna e senza rifrazione. Calcola la posizione eclittica geocentrica, la converte in ascensione retta e declinazione, ricava l'angolo orario dal tempo sidereo di Greenwich e la longitudine dell'osservatore. Precisione dichiarata nel commento: circa 0.5°–1°, non verificata su un intervallo ampio di date. Eccezione: `ArgumentOutOfRangeException` da `AstroTime.CheckLatLon`.
- **MoonPosition.IlluminatedFraction** (riga 118) — `double IlluminatedFraction(DateTimeOffset utc)`. Frazione illuminata del disco lunare, da 0 (luna nuova) a 1 (luna piena), con k = (1 − cos ψ) / 2, dove ψ è l'elongazione Sole-Luna. Il Sole è trattato sull'eclittica. Il risultato è limitato a [0, 1].
- **MoonPosition.GeocentricEcliptic** (riga 133, privato) — `(double LonDeg, double LatDeg, double DistanceKm) GeocentricEcliptic(double t)`. Longitudine e latitudine eclittiche geocentriche della Luna e distanza in chilometri, per i secoli giuliani `t`. Somma i termini periodici delle tabelle, pesati dal fattore di eccentricità, e i termini additivi.
- **MoonPosition.EFactor** (riga 187, privato) — `double EFactor(int mCoef, double e)`. Fattore di eccentricità terrestre per i termini che contengono l'anomalia solare: `e²` se il coefficiente è ±2, `e` se è ±1, altrimenti 1.
- **MoonPosition.MeanObliquityDeg** (riga 202, privato) — `double MeanObliquityDeg(double t)`. Obliquità media dell'eclittica in gradi (Meeus, cap. 22).
- **MoonPosition.GreenwichMeanSiderealDeg** (riga 208, privato) — `double GreenwichMeanSiderealDeg(double jd, double t)`. Tempo sidereo medio di Greenwich in gradi, normalizzato in [0, 360) (Meeus, eq. 12.4).

### ObservingNight.cs

Notte osservativa: va da mezzogiorno locale a mezzogiorno locale del giorno dopo. Il suo identificatore è la data di inizio in formato `YYYY-MM-DD`. Contiene anche il record struct `PhaseBand`, cioè un intervallo di una fase del cielo.

#### PhaseBand

- **PhaseBand** (costruttore del record struct, riga 9) — `PhaseBand(SkyPhase Phase, DateTimeOffset From, DateTimeOffset To)`. Un intervallo continuo di una stessa fase. Gli intervalli di una notte sono contigui: il `To` di uno coincide con il `From` del successivo.

#### ObservingNight

- **ObservingNight.GetNightId** (riga 24) — `string GetNightId(DateTimeOffset utc, TimeZoneInfo tz)`. Identificatore della notte a cui appartiene un istante. Converte l'istante nel fuso indicato: prima delle 12 locali l'istante appartiene alla notte iniziata il giorno prima. Esempi con Europe/Rome: 19/10 alle 03:00 locali restituisce "2026-10-18"; 19/10 alle 13:00 restituisce "2026-10-19". Eccezione: `ArgumentNullException` se il fuso è nullo.
- **ObservingNight.GetBands** (riga 40, versione breve) — `IReadOnlyList<PhaseBand> GetBands(string nightId, double latDeg, double lonDeg, TimeZoneInfo tz)`. Chiama la versione completa con un passo di campionamento di 1 minuto.
- **ObservingNight.GetBands** (riga 55, versione completa) — `IReadOnlyList<PhaseBand> GetBands(string nightId, double latDeg, double lonDeg, TimeZoneInfo tz, TimeSpan step)`. Campiona l'altezza del Sole da mezzogiorno locale a mezzogiorno del giorno dopo, con il passo indicato. Classifica ogni campione con `SkyPhases.Classify` e unisce i campioni consecutivi con la stessa fase. Il confine tra due fasi è noto con un'approssimazione pari al passo. Eccezioni: `ArgumentNullException` (identificatore o fuso nulli), `ArgumentOutOfRangeException` (coordinate fuori intervallo o passo non positivo), `FormatException` se `nightId` non è nel formato `YYYY-MM-DD`.
- **ObservingNight.LocalNoonUtc** (riga 104, privato) — `DateTimeOffset LocalNoonUtc(DateOnly day, TimeZoneInfo tz)`. Mezzogiorno locale del giorno indicato, convertito in UTC.

### SkyPhase.cs

Contiene l'enum `SkyPhase` (`Day`, `CivilTwilight`, `NauticalTwilight`, `AstronomicalTwilight`, `Night`) e la classe statica `SkyPhases`.

#### SkyPhases

- **SkyPhases.Classify** (riga 23) — `SkyPhase Classify(double sunAltitudeDeg)`. Fase del cielo dall'altezza del Sole: da 0° in su è giorno; da −6° (incluso) a 0° crepuscolo civile; da −12° a −6° nautico; da −18° a −12° astronomico; sotto −18° notte. Eccezione: `ArgumentException` se il valore è NaN.
- **SkyPhases.Simplify** (riga 53) — `string Simplify(SkyPhase phase)`. Riduce la fase alle tre categorie dell'API locale: `"day"`, `"twilight"` (tutti e tre i crepuscoli) o `"night"`. Eccezione: `ArgumentOutOfRangeException` per un valore fuori dall'enum.

### SolarPosition.cs

Classe statica pubblica `SolarPosition`, versione semplificata dell'algoritmo solare NOAA (Meeus). Precisione dichiarata nel commento: 0.01°–0.1°.

#### SolarPosition

- **SolarPosition.Altitude** (riga 21) — `double Altitude(DateTimeOffset utc, double latDeg, double lonDeg)`. Altezza geometrica del Sole in gradi, negativa sotto l'orizzonte. Calcola il tempo solare vero dall'equazione del tempo e dalla longitudine, poi l'angolo orario, la declinazione e l'altezza. Eccezione: `ArgumentOutOfRangeException` da `AstroTime.CheckLatLon`.
- **SolarPosition.ComputeGeometry** (riga 54, interno) — `SunGeometry ComputeGeometry(double t)`. Calcola longitudine eclittica apparente, declinazione ed equazione del tempo (in minuti) per i secoli giuliani `t`. Usata anche da `MoonPosition.IlluminatedFraction`.
- **SunGeometry** (costruttore del record struct interno, riga 101) — `SunGeometry(double ApparentLongitudeDeg, double DeclinationDeg, double EquationOfTimeMin)`. Grandezze solari in un istante, usate solo dentro il modulo.

---

## AllSky.Imaging (src/AllSky.Imaging)

Elaborazione dell'immagine: demosaicing, bilanciamento del bianco, stretch in sRGB. Nota: esistono due tipi con lo stesso nome logico. `Stretch` qui è la classe statica di funzioni; `Core.Stretch` è l'enum della configurazione. `BayerPattern` qui non ha il valore `None`.

### BayerPattern.cs

Contiene solo l'enum `BayerPattern` (`RGGB`, `BGGR`, `GRBG`, `GBRG`), ordinato per la posizione dei pixel (0,0), (1,0), (0,1), (1,1). Nessuna funzione.

### Debayer.cs

#### Debayer

- **Debayer.Bilinear** (riga 20) — `RgbImage Bilinear(ushort[] raw, int width, int height, BayerPattern pattern)`. Demosaicing bilineare. Il canale nativo di ogni pixel conserva il valore grezzo; gli altri due canali sono la media dei pixel di quel colore nell'intorno 3×3. Ai bordi si usa la riflessione, che conserva la parità del pattern. Eccezioni: `ArgumentNullException` (raw nullo); `ArgumentOutOfRangeException` se larghezza o altezza sono inferiori a 2 o il pattern non è definito; `ArgumentException` se `raw.Length` non è uguale a `width * height`.
- **Debayer.Mirror** (riga 92, privato) — `int Mirror(int i, int n)`. Riflette un indice fuori dai bordi: −1 diventa 1, `n` diventa `n − 2`. La riflessione non ripete il bordo, quindi la parità del pattern si conserva.

### PixelSampling.cs

#### PixelSampling

- **PixelSampling.Step** (riga 13) — `int Step(long count)`. Passo di campionamento per le statistiche: 1 se `count` è al massimo 200000 (`MaxSampleSize`), altrimenti il quoziente arrotondato per eccesso, così il campione non supera mai `MaxSampleSize` elementi.

### RgbImage.cs

#### RgbImage

- **RgbImage** (costruttore del record posizionale, riga 7) — `RgbImage(int Width, int Height, float[] R, float[] G, float[] B)`. Immagine RGB a tre piani separati, in ordine row-major, con valori float nell'intervallo 0–65535. Il costruttore non copia gli array.

### Smoother.cs

#### Smoother

- **Smoother** (costruttore, riga 14) — `Smoother(double alpha)`. Crea lo smoother dei guadagni. `alpha` è il peso del nuovo campione e deve essere in (0, 1]. Eccezione: `ArgumentOutOfRangeException` se fuori intervallo.
- **Smoother.Update** (riga 25) — `(double R, double G, double B) Update((double R, double G, double B) gains)`. Al primo campione restituisce i guadagni invariati e li memorizza. Dai successivi restituisce la media esponenziale `alpha * nuovo + (1 − alpha) * precedente`, per ogni canale. Il valore memorizzato è quello restituito.

### StretchOptions.cs

Contiene l'enum `StretchKind` (`Mtf`, `Asinh`) e il record `StretchOptions`.

#### StretchOptions

- **StretchOptions** (costruttore implicito del record, riga 11) — `StretchOptions()`. Opzioni dello stretch con i default: curva MTF, midtone automatico con fondo cielo target 0.2, β = 10 per asinh, percentili di nero 0.5 e di bianco 99.8, punto bianco comune ai tre canali (`LinkedWhitePoint = true`), saturazione 1. Il midtone fisso (`Midtone`) è `null` per impostazione predefinita, cioè automatico.

### StretchParams.cs

#### StretchParams

- **StretchParams** (costruttore del record struct, riga 7) — `StretchParams(float BlackR, float BlackG, float BlackB, float White, float Midtone)`. Parametri di stretch: punto nero per ogni canale, punto bianco comune e midtone.

### StretchParamsSmoother.cs

#### StretchParamsSmoother

- **StretchParamsSmoother** (costruttore, riga 15) — `StretchParamsSmoother(double alpha)`. Crea lo smoother dei parametri. `alpha` è il peso del nuovo campione, in (0, 1]. Eccezione: `ArgumentOutOfRangeException` se fuori intervallo.
- **StretchParamsSmoother.Update** (riga 26) — `StretchParams Update(StretchParams parameters)`. Come `Smoother.Update`, ma applicata a ogni campo. Il primo campione passa invariato. La combinazione convessa mantiene `White − Black >= 1` e il midtone in (0, 1), se valgono per i parametri di partenza.
- **StretchParamsSmoother.Mix** (riga 45, privato) — `float Mix(float fresh, float previous)`. Media esponenziale di un singolo valore con il peso `alpha`.

### Stretch.cs

#### Stretch

Classe statica con le funzioni di stretch (MTF e asinh), la stima dei parametri e la conversione in sRGB a 8 bit. Il punto bianco viene calcolato con percentili interpolati; le statistiche usano un campione a passo fisso (al massimo 200000 pixel).

- **Stretch.Mtf** (riga 12) — `float Mtf(float x, float midtone)`. Midtone transfer function: `(m − 1)·x / ((2m − 1)·x − m)`, con `x` in 0–1 e `midtone` in (0, 1). Vale 0 per `x <= 0` e 1 per `x >= 1`. Eccezione: `ArgumentOutOfRangeException` se il midtone è fuori da (0, 1).
- **Stretch.Asinh** (riga 33) — `float Asinh(float x, float beta)`. Stretch asinh: `asinh(beta·x) / asinh(beta)`, con valori 0 e 1 agli estremi. Eccezione: `ArgumentOutOfRangeException` se `beta` non è maggiore di zero.
- **Stretch.AutoMidtone** (riga 57) — `float AutoMidtone(float[] channelSample, float targetBackground)`. Cerca con bisezione (100 iterazioni) il midtone per cui la mediana del campione, dopo la MTF, vale `targetBackground`. Se la mediana non è in (0, 1) restituisce 0.5. Eccezioni: `ArgumentNullException`; `ArgumentException` se il campione è vuoto; `ArgumentOutOfRangeException` se il target non è in (0, 1).
- **Stretch.Estimate** (riga 103) — `StretchParams Estimate(RgbImage img, StretchOptions opts)`. Stima i parametri di stretch: punto nero per canale al percentile `BlackPercentile`, punto bianco comune pari al massimo dei percentili di bianco dei canali non piatti, midtone automatico dal verde (o quello fisso). Eccezioni: `InvalidOperationException` se `LinkedWhitePoint` è falso, perché `StretchParams` ha un solo punto bianco; `ArgumentException` se immagine o opzioni non sono coerenti.
- **Stretch.ToSrgb8** (riga 124, con opzioni) — `byte[] ToSrgb8(RgbImage img, StretchOptions opts)`. Converte in RGB sRGB a 8 bit, interleaved (3 byte per pixel). Con `LinkedWhitePoint` (default) stima i parametri e chiama la versione con parametri. Altrimenti calcola nero e bianco per ogni canale. Poi applica lo stretch, la saturazione sulla luminanza Rec.709 e la gamma sRGB.
- **Stretch.ToSrgb8** (riga 150, con parametri) — `byte[] ToSrgb8(RgbImage img, StretchOptions opts, StretchParams p)`. Converte usando parametri già noti, per esempio stimati e poi levigati dall'host. Richiede `White − Black >= 1` su ogni canale. Eccezione: `ArgumentException` se la condizione non vale.
- **Stretch.ValidateInputs** (riga 162, privato) — `void ValidateInputs(RgbImage img, StretchOptions opts)`. Controlla che i tre canali abbiano `Width * Height` elementi, che i percentili siano in `0 <= nero < bianco <= 100`, che la saturazione sia almeno 0 e che un midtone fisso sia in (0, 1).
- **Stretch.ValidateParams** (riga 191, privato) — `void ValidateParams(StretchParams p, StretchOptions opts)`. Controlla `White − Black >= 1` per ogni canale e, con la curva MTF, che il midtone sia in (0, 1).
- **Stretch.LinkedLevels** (riga 212, privato) — `void LinkedLevels(RgbImage img, StretchOptions opts, out float blackR, out float blackG, out float blackB, out float white)`. Calcola i neri per canale e il bianco comune. Il bianco è il massimo dei percentili di bianco dei canali non piatti. Un canale piatto (intervallo minore di 1) ha nero 0. Se tutti i canali sono piatti usa 0 e 65535.
- **Stretch.ChannelLevels** (riga 255, privato) — `void ChannelLevels(float[] channel, StretchOptions opts, out float black, out float white)`. Nero e bianco di un singolo canale. Se il canale è piatto, nero 0 e bianco 65535.
- **Stretch.ChannelStats** (riga 266, privato) — `void ChannelStats(float[] channel, StretchOptions opts, out float black, out float white, out bool flat)`. Percentili di nero e di bianco su un campione ordinato, e il flag `flat` che vale `true` se `white − black < 1`.
- **Stretch.ResolveMidtone** (riga 280, privato) — `float ResolveMidtone(float[] green, float black, float white, StretchOptions opts)`. Midtone da usare: 0.5 per la curva asinh; il valore fisso se è impostato; altrimenti quello automatico calcolato sul canale verde normalizzato.
- **Stretch.Render** (riga 296, privato) — `byte[] Render(RgbImage img, StretchOptions opts, float[] black, float[] white, float midtone)`. Produce i byte sRGB: normalizza ogni canale tra nero e bianco, applica la curva, applica la saturazione sulla luminanza, applica la gamma sRGB e arrotonda a 8 bit.
- **Stretch.MtfD** (riga 324, privato) — `double MtfD(double x, double m)`. Versione in `double` della MTF, senza controlli di intervallo. La usano `Mtf` e `AutoMidtone`.
- **Stretch.Shape** (riga 329, privato) — `float Shape(float x, StretchOptions opts, float midtone)`. Applica la curva scelta in `opts` (asinh o MTF) a un valore in 0–1.
- **Stretch.SortedSample** (riga 337, privato) — `float[] SortedSample(float[] channel)`. Copia di un canale a passo fisso (`PixelSampling.Step`), ordinata in modo crescente.
- **Stretch.NormalizedSample** (riga 351, privato) — `float[] NormalizedSample(float[] channel, float black, float white)`. Campione a passo fisso normalizzato in 0–1 tra nero e bianco, usato per il midtone automatico.
- **Stretch.PercentileSorted** (riga 365, privato) — `double PercentileSorted(float[] sorted, double p)`. Percentile con interpolazione lineare su un array già ordinato; `p` è in 0–1.
- **Stretch.SrgbEncode** (riga 375, privato) — `double SrgbEncode(double v)`. Curva di gamma sRGB a tratti su un valore in 0–1, con clamp.
- **Stretch.ToByte** (riga 381, privato) — `byte ToByte(double v01)`. Converte un valore in 0–1 in un byte da 0 a 255, con clamp e arrotondamento.

### WhiteBalance.cs

Classe statica `WhiteBalance`: guadagni di bilanciamento del bianco per il cielo notturno (mediana sigma-clippata) e per il giorno (grey-world), interpolazione e applicazione. Il verde è sempre il riferimento, con guadagno 1.

#### WhiteBalance

- **WhiteBalance.EstimateNightGains** (riga 16) — `(double R, double G, double B) EstimateNightGains(RgbImage img, bool[]? skyMask)`. Guadagni dal fondo cielo notturno. Per ogni canale calcola la mediana sigma-clippata dei pixel in maschera (o di tutti i pixel) e restituisce guadagni che portano rosso e blu alla mediana del verde. Eccezioni: `ArgumentException` se la maschera non ha `Width * Height` elementi; `InvalidOperationException` se una mediana non è positiva o non ci sono pixel in maschera.
- **WhiteBalance.EstimateDayGains** (riga 41) — `(double R, double G, double B) EstimateDayGains(RgbImage img)`. Guadagni diurni grey-world. Esclude i pixel saturi (un canale maggiore o uguale a 65000), tiene i pixel la cui luminanza è tra il 5° e il 95° percentile, e calcola il guadagno di ogni canale come rapporto tra la media del verde e la media del canale. Eccezioni: `InvalidOperationException` se tutti i pixel sono saturi, se non ci sono pixel utili o se una media non è positiva.
- **WhiteBalance.Blend** (riga 115) — `(double R, double G, double B) Blend((double R, double G, double B) g1, (double R, double G, double B) g2, double t)`. Interpolazione lineare tra due set di guadagni. `t` è limitato a [0, 1]; con 0 restituisce `g1`, con 1 restituisce `g2`.
- **WhiteBalance.Apply** (riga 128) — `void Apply(RgbImage img, (double R, double G, double B) gains)`. Moltiplica in place i piani R, G e B per i rispettivi guadagni, con clamp a 0–65535. Eccezione: `ArgumentNullException` se l'immagine è nulla.
- **WhiteBalance.Scale** (riga 136, privato) — `void Scale(float[] plane, double gain)`. Moltiplica ogni valore di un piano per il guadagno, con clamp a 0–65535.
- **WhiteBalance.SigmaClippedMedian** (riga 148, privato) — `double SigmaClippedMedian(float[] channel, bool[]? mask)`. Mediana di un canale sui pixel in maschera (o su tutti). Esegue 3 iterazioni di sigma clipping: scarta i valori più lontani di 2.5 sigma dalla mediana, dove sigma = 1.4826 · MAD. Sopra 200000 pixel selezionati usa un campione a passo fisso. Eccezione: `InvalidOperationException` se la maschera non seleziona alcun pixel.
- **WhiteBalance.Median** (riga 218, privato) — `double Median(double[] values)`. Mediana di un array, che viene ordinato in place. L'array non deve essere vuoto.
- **WhiteBalance.Percentile** (riga 225, privato) — `double Percentile(double[] sorted, double p)`. Percentile con interpolazione lineare su un array già ordinato; `p` è in 0–1.
- **WhiteBalance.IsSaturated** (riga 234, privato) — `bool IsSaturated(RgbImage img, int i)`. Vero se almeno uno dei tre canali del pixel `i` è maggiore o uguale a 65000.
- **WhiteBalance.Luma** (riga 239, privato) — `double Luma(RgbImage img, int i)`. Luminanza Rec.709 del pixel `i`: 0.2126·R + 0.7152·G + 0.0722·B.

---

## Interfaccia web (web/js)

Moduli ES caricati dal browser. `app.js` avvia l'interfaccia; `api.js` parla con il motore; `i18n.js` e `time.js` gestiscono testi, numeri, orari e il calcolo solare per il nastro; `form-kit.js` fornisce i pezzi dei moduli. Le schermate sono in `web/js/views`.

### web/js/api.js

Accesso al motore locale (contratto in `docs/API.md`), solo su richieste a `/api` della stessa origine. Le costanti sono `DEFAULT_TIMEOUT_MS` (8 s) e `POLL_MS` (5 s).

#### ApiError

- **ApiError** (costruttore, riga 8) — `constructor(kind, message = null, status = 0)`. Errore di accesso al motore. `kind` è `'network'`, `'timeout'`, `'http'` o `'parse'`; `message` è il testo del motore, se c'è; `status` è il codice HTTP, 0 se assente. Imposta anche `hasMessage`.

#### Richieste

- **request** (riga 17) — `request(path, { method = 'GET', body, timeout = 8000 } = {})`. Esegue una richiesta JSON con timeout (AbortController) e corpo JSON se presente. Restituisce il JSON della risposta, o `null` se il corpo è vuoto. Lancia `ApiError`: `'timeout'` se scade il tempo; `'network'` se la richiesta o la lettura del corpo falliscono; `'parse'` se una risposta 2xx non è JSON valido; `'http'` se la risposta non è 2xx, con il messaggio `error` del motore quando c'è.

#### api (oggetto dei metodi)

- **api.status** (riga 61) — `GET /api/status`. Stato del motore.
- **api.config** (riga 62) — `GET /api/config`. Configurazione corrente.
- **api.saveConfig** (riga 63) — `saveConfig(config)`, `PUT /api/config`. Salva la configurazione; il motore risponde con `{ ok, errors }`.
- **api.cameras** (riga 64) — `GET /api/cameras/discover`. Elenco delle camere trovate.
- **api.connectCamera** (riga 65) — `connectCamera(id)`, `POST /api/camera/connect` con `{ id }`.
- **api.nights** (riga 66) — `GET /api/nights`. Elenco delle notti.
- **api.frames** (riga 67) — `frames(nightId, { limit = 1000, offset = 0 })`. Frame di una notte, a pagine. L'identificatore della notte è codificato nell'URL.
- **api.startTimelapse** (riga 69) — `startTimelapse(nightId)`, `POST /api/nights/{id}/timelapse` con corpo vuoto. Avvia la creazione del timelapse.
- **api.deleteNight** (riga 71) — `deleteNight(nightId)`, `DELETE /api/nights/{id}`.

#### api.urls

Costruiscono gli URL delle immagini, usati come `src` di `<img>` senza passare da `fetch`.

- **api.urls.latestFrame** (riga 75) — `latestFrame(version = '')`. URL del frame più recente. Con `version` aggiunge `?v=` per evitare la cache del browser.
- **api.urls.nightFrame** (riga 76) — `nightFrame(nightId, frameId)`. URL JPEG di un frame di una notte.
- **api.urls.keogram** (riga 78) — `keogram(nightId)`. URL del keogram della notte.
- **api.urls.startrails** (riga 79) — `startrails(nightId)`. URL dello startrails della notte.

#### Eventi

- **openEvents** (riga 85) — `openEvents({ onStatus, onFrame, onConnection }) → () => void`. Apre `EventSource` su `/api/events`: l'evento `status` aggiorna lo stato e ferma il polling; l'evento `frame` passa l'id e il timestamp del nuovo frame. Se la connessione SSE cade, o se `EventSource` non esiste nel browser, parte il polling di `/api/status` ogni 5 secondi. Restituisce la funzione che chiude tutto. Gli handler dell'`EventSource` sono alle righe 116 (status), 128 (frame), 136 (`onopen`), 141 (`onerror`); la funzione di chiusura è alla riga 147.
- **openEvents.poll** (riga 90, interna) — `async poll()`. Legge `/api/status`: se riesce chiama `onConnection(true)` e `onStatus`, altrimenti `onConnection(false)`.
- **openEvents.startPolling** (riga 100, interna) — `startPolling()`. Avvia subito una lettura e poi una ogni 5 secondi. Non fa nulla se il polling è già attivo o se la connessione è chiusa.
- **openEvents.stopPolling** (riga 106, interna) — `stopPolling()`. Ferma il timer del polling.

---

### web/js/app.js

Avvio dell'interfaccia: carica configurazione, lingua e stato; applica il tema; gestisce il router a hash (`#/live`, `#/nights`, `#/color`, `#/camera`, `#/site`, `#/wizard`). Le viste si caricano solo quando servono. Il contesto `ctx` passato alle viste è definito nell'oggetto alle righe 57–96.

#### Rotte

- **ROUTES.live** (riga 9) — `() => import('./views/live.js')`. Carica la schermata Live.
- **ROUTES.nights** (riga 10) — `() => optionalView('nights')`. Carica la schermata Notti, o il segnaposto se il file manca.
- **ROUTES.color** (riga 11) — `() => optionalView('color')`. Carica la schermata Colore, o il segnaposto.
- **ROUTES.camera** (riga 12) — `() => import('./views/camera.js')`. Carica la schermata Camera.
- **ROUTES.site** (riga 13) — `() => import('./views/site.js')`. Carica la schermata Sito.
- **ROUTES.wizard** (riga 14) — `() => import('./views/wizard.js')`. Carica la procedura guidata.
- **optionalView** (riga 17) — `async optionalView(name)`. Importa `views/{name}.js`. Se il file manca il browser lancia un `TypeError` e viene restituito il segnaposto; gli altri errori (per esempio un errore di sintassi) vengono rilanciati.

#### Contesto (ctx)

- **ctx.config** (getter, riga 58) — restituisce la configurazione in memoria.
- **ctx.getStatus** (riga 61) — restituisce l'ultimo stato ricevuto dal motore.
- **ctx.on** (riga 64) — `on(kind, fn) → () => void`. Registra un ascoltatore per `'status'` o `'frame'`. Restituisce la funzione per rimuoverlo.
- **ctx.setConfig** (riga 69) — `setConfig(config)`. Dopo un salvataggio riuscito rende la configurazione in memoria quella salvata, annulla l'anteprima del tema e riapplica il tema.
- **ctx.previewTheme** (riga 75) — `previewTheme(pref)`. Applica subito un tema senza salvarlo. Con `null` annulla l'anteprima.
- **ctx.setLanguage** (riga 80) — `async setLanguage(code)`. Carica la lingua, applica le stringhe, aggiorna il titolo della pagina e ridisegna la vista corrente, mantenendo il fuoco sul campo che l'aveva.
- **ctx.navigate** (riga 87) — `navigate(hash)`. Cambia l'hash della pagina, quindi la rotta.
- **ctx.wizardDone** (riga 90) — `wizardDone()`. Restituisce il flag di procedura guidata completata.
- **ctx.setWizardDone** (riga 93) — `setWizardDone(value)`. Scrive o rimuove il flag.

#### Funzioni del modulo

- **emit** (riga 47) — `emit(kind, payload)`. Chiama tutti gli ascoltatori registrati per `kind`. Un errore di un ascoltatore viene scritto in console e non blocca gli altri.
- **readWizardFlag** (riga 100) — `readWizardFlag()`. Legge `allsky.wizardDone` da `localStorage`. Restituisce `false` se l'accesso è bloccato.
- **writeWizardFlag** (riga 108) — `writeWizardFlag(value)`. Scrive il flag o lo rimuove. Gli errori di archiviazione vengono ignorati.
- **needsWizard** (riga 118) — `needsWizard(config)`. Vero se la configurazione c'è, il flag non è impostato, il nome del sito è "Osservatorio" e la camera non è scelta (assente o `sim-1`). In quel caso il router va a `#/wizard` al primo avvio.
- **resolveTheme** (riga 127) — `resolveTheme()`. Tema da mostrare: l'anteprima, poi `config.ui.theme`. Se è `'auto'`, mostra `'day'` quando il Sole è sopra l'orizzonte (`status.phase === 'day'`), altrimenti `'night'`.
- **applyTheme** (riga 133) — `applyTheme()`. Imposta `data-theme` sull'elemento `html`.
- **setOnline** (riga 139) — `setOnline(ok)`. Mostra o nasconde l'avviso di motore non raggiungibile.
- **placeholderModule** (riga 147) — `placeholderModule(key)`. Restituisce un modulo di vista sostitutivo, con titolo e testo presi da `placeholder.{key}`.
- **placeholderModule.mount** (riga 149, interna) — `mount(root)`. Sostituisce il contenuto di `root` con la sezione segnaposto.
- **placeholderModule.unmount** (riga 166, interna) — `unmount()`. Non fa nulla.
- **routeFromHash** (riga 173) — `routeFromHash()`. Ricava il nome della rotta dall'hash (per esempio `#/nights/2026-10-18` dà `nights`). Restituisce `null` se la rotta non esiste.
- **setActiveNav** (riga 178) — `setActiveNav(name)`. Imposta `aria-current="page"` sulla voce di menu della rotta attiva e lo rimuove dalle altre.
- **showViewError** (riga 188) — `showViewError(err)`. Mostra il messaggio d'errore nella vista, con `role="alert"`.
- **render** (riga 197) — `async render({ focusId = '' } = {})`. Ridisegna la rotta corrente: smonta la vista precedente, carica il modulo e lo monta. Un token scarta i rendering superati da un altro cambio di rotta. Il fuoco va al campo indicato o al primo `h1`.
- **onStatus** (riga 237) — `onStatus(status)`. Salva lo stato, applica il tema, segna il motore come raggiungibile e avvisa gli ascoltatori `'status'`.
- **onFrame** (riga 244) — `onFrame(frame)`. Avvisa gli ascoltatori `'frame'`.
- **boot** (riga 250) — `async boot()`. Avvio: legge configurazione, lingua e stato; applica tema e avviso di connessione; porta a `#/wizard` se serve; apre gli eventi del motore; registra il cambio di hash (riga 283, anonimo) e fa il primo `render`. Un errore non gestito mostra `errors.unknown` (riga 287, anonimo).

---

### web/js/i18n.js

Caricamento delle stringhe (file `i18n/{lang}.json` più le parti `forms`, `nights`, `color` unite in profondità), formattazione di numeri e orari, messaggi d'errore. L'italiano è la lingua di riserva.

- **normalizeLang** (riga 14) — `normalizeLang(code)`. Restituisce `code` se è una lingua supportata (`it`, `en`), altrimenti `'it'`.
- **isObject** (riga 18, interna) — `isObject(value)`. Vero per oggetti non nulli e non array.
- **mergeDeep** (riga 23) — `mergeDeep(base, extra)`. Unione profonda: i valori di `extra` prevalgono e le sezioni con lo stesso nome si fondono. Non modifica gli argomenti.
- **fetchJson** (riga 31, interna) — `async fetchJson(file, { optional })`. Scarica `i18n/{file}` come JSON. Se il file è facoltativo e manca o non è JSON valido, restituisce `null` e scrive un avviso; altrimenti lancia un errore.
- **fetchBundle** (riga 54, interna) — `async fetchBundle(code)`. Carica `{code}.json` e le parti facoltative `forms`, `nights`, `color`, unendole con `mergeDeep`.
- **loadLocale** (riga 62) — `async loadLocale(code)`. Carica il dizionario della lingua; l'italiano viene caricato una sola volta come riserva. Se la lingua richiesta non si carica usa l'italiano. Imposta `lang` sull'elemento `html` e restituisce la lingua effettiva.
- **locale** (riga 80) — `locale()`. Restituisce il codice della lingua corrente.
- **lookup** (riga 84, interna) — `lookup(obj, key)`. Legge un valore con chiave a punti (`"a.b.c"`); `undefined` se il percorso non esiste.
- **t** (riga 88) — `t(key, vars?)`. Restituisce la stringa della chiave nella lingua corrente, altrimenti quella in italiano, altrimenti la chiave stessa. Sostituisce i segnaposto `{nome}` con i valori di `vars`.
- **applyI18n** (riga 99) — `applyI18n(root = document)`. Imposta il testo degli elementi con `data-i18n` e l'`aria-label` di quelli con `data-i18n-aria`.
- **errorMessage** (riga 109) — `errorMessage(err)`. Usa il messaggio del motore se c'è; altrimenti la stringa `errors.{kind}` (`errors.unknown` se manca anche il tipo).
- **tag** (riga 115, interna) — `tag()`. Tag BCP-47 della lingua corrente (`it-IT` o `en-GB`).
- **localeTag** (riga 120) — `localeTag()`. Esporta il tag BCP-47 per `Intl`.
- **safeTz** (riga 124, interna) — `safeTz(timeZone)`. Restituisce il fuso se `Intl` lo riconosce, altrimenti `undefined` (fuso del browser).
- **fmt.number** (riga 135) — `fmt.number(value, { digits = 0, grouping = true })`. Numero con cifre decimali fisse e separatori della lingua. Restituisce `'–'` per `null`, `undefined` o NaN.
- **fmt.signed** (riga 144) — `fmt.signed(value, digits = 1)`. Come `fmt.number`, ma con il segno meno tipografico (−).
- **fmt.time** (riga 149) — `fmt.time(date, timeZone, { seconds = false })`. Orario HH:MM (o HH:MM:SS) nel fuso indicato, in formato 24 ore.
- **fmt.date** (riga 159) — `fmt.date(date, timeZone)`. Giorno e mese per esteso nella lingua corrente, per esempio "18 ottobre".

---

### web/js/time.js

Calcoli di orario nel fuso dell'osservatorio, senza librerie. Una notte va da mezzogiorno locale a mezzogiorno del giorno dopo; l'identificatore è la data di inizio `YYYY-MM-DD`. Il calcolo solare qui è un duplicato semplificato di quello in C#, usato dal browser per il nastro.

- **formatterFor** (riga 6, interna) — `formatterFor(timeZone)`. Restituisce un `Intl.DateTimeFormat` per il fuso, messo in cache.
- **localParts** (riga 26) — `localParts(date, timeZone)`. Restituisce anno, mese, giorno, ora (0–23), minuti e secondi locali dell'istante. Esportata.
- **offsetMs** (riga 41, interna) — `offsetMs(instant, timeZone)`. Differenza in millisecondi tra l'ora locale del fuso e UTC in quell'istante.
- **zonedDate** (riga 48) — `zonedDate(y, m, d, h, timeZone)`. Restituisce il `Date` corrispondente all'ora locale indicata (`m` da 1 a 12), correggendo l'offset di fuso e l'ora legale.
- **currentNightId** (riga 58) — `currentNightId(now, timeZone)`. Identificatore della notte in corso. Prima di mezzogiorno locale è la notte iniziata il giorno prima.
- **nightWindow** (riga 66) — `nightWindow(nightId, timeZone)`. Restituisce `{ start, end }`: da mezzogiorno locale del giorno a mezzogiorno del giorno dopo.
- **sampleBands** (riga 75) — `sampleBands(nightId, timeZone)`. Fascia di esempio con fasi fisse: giorno 12–18, crepuscolo 18–19, notte 19–5, crepuscolo 5–6, giorno 6–12. Si usa solo se il motore non fornisce `sunBands` e non ci sono coordinate del sito.
- **sampleBands.at** (riga 77, interna) — `at(day, hour)`. `Date` dell'ora locale indicata, tramite `zonedDate`.
- **sunAltitude** (riga 89) — `sunAltitude(date, latDeg, lonDeg)`. Altezza geometrica del Sole in gradi, con algoritmo solare semplificato e senza rifrazione. Il commento dichiara un errore di pochi centesimi di grado, che qui non è verificato.
- **sunAltitude.mod360** (riga 90, interna) — `mod360(x)`. Riporta un angolo nell'intervallo [0, 360).
- **sunCurve** (riga 107) — `sunCurve(nightId, timeZone, latDeg, lonDeg, stepMin = 5)`. Altezza del Sole campionata ogni `stepMin` minuti da mezzogiorno a mezzogiorno, con il punto finale. Restituisce `[{ ms, alt }]`.
- **sunCrossings** (riga 119) — `sunCrossings(curve, thresholds)`. Istanti in cui la curva attraversa una delle soglie, con interpolazione lineare tra due campioni. Restituisce `[{ ms, deg }]`.

---

### web/js/form-kit.js

Pezzi di form condivisi da Procedura guidata, Camera e Sito: campi con etichetta, aiuto ed errore, interruttori, scelte, barra di salvataggio, selezione della camera con anteprima, posizione del dispositivo. Il DOM si costruisce con `h()`, senza `innerHTML` con dati dell'utente. I campi restituiti hanno `path`, `node`, `validate`, `showError`, `focus` e `refresh`; `focus` e `refresh` sono funzioni anonime descritte nella voce del campo.

- **uid** (riga 14, interna) — `uid(base)`. Genera un identificatore univoco `base-N`, con contatore del modulo.
- **h** (riga 17) — `h(tag, props = {}, ...children)`. Costruttore minimo di elementi DOM. Chiavi speciali: `class`, `text`, e `on<evento>` per i listener. `false`, `null` e `undefined` vengono saltati; `true` diventa un attributo vuoto. I figli `null`, `undefined` e `false` vengono ignorati.
- **clone** (riga 33) — `clone(value)`. Copia profonda tramite JSON.
- **sameValue** (riga 34) — `sameValue(a, b)`. Confronto di uguaglianza tramite JSON.
- **withDefaults** (riga 37) — `withDefaults(config)`. Copia della configurazione in cui `site` esiste e il fuso è quello del browser, se mancante.
- **getPath** (riga 46) — `getPath(obj, path)`. Legge un valore a percorso puntato (`"site.name"`); `undefined` se un segmento manca.
- **setPath** (riga 50) — `setPath(obj, path, value)`. Imposta un valore a percorso puntato, creando gli oggetti intermedi mancanti.
- **numberText** (riga 63) — `numberText(value, maxDigits = 6)`. Numero nella lingua corrente, senza raggruppamento. Restituisce `''` se il valore non è finito.
- **parseNumber** (riga 69) — `parseNumber(text)`. Legge un numero digitato a mano, con virgola o punto e con il segno meno tipografico. Restituisce `null` se il testo non è un numero.
- **isTimeZone** (riga 75) — `isTimeZone(zone)`. Vero se `Intl` riconosce il fuso.
- **errorHandles** (riga 86, interna) — `errorHandles(node, input, errEl)`. Restituisce `{ showError, clearError, isShowing }` per il messaggio d'errore di un campo.
- **errorHandles.showError** (riga 87, interna) — `showError(message)`. Mostra il messaggio, marca il campo con `is-invalid` e imposta `aria-invalid`.
- **errorHandles.clearError** (riga 93, interna) — `clearError()`. Nasconde il messaggio e rimuove i marcatori di errore.
- **errorHandles.isShowing** (riga 99, interna) — `isShowing()`. Vero se il messaggio è visibile.
- **inputField** (riga 105) — `inputField({ draft, path, label, help, kind, unit, min, max, integer, scale, maxLength, required, autocomplete, onChange })`. Campo di testo o numero legato a `draft[path]`. Un numero è mostrato diviso per `scale` (per esempio millisecondi mostrati in secondi con `scale: 1000`). Un valore valido aggiorna subito la bozza; il controllo completo avviene al cambio. Restituisce `{ path, node, validate, showError, focus, refresh }`. `focus` (riga 189) porta il fuoco sull'input; `refresh` (riga 190) ricarica il valore dalla bozza. Gli handler `input` e `change` sono alle righe 169 e 179.
- **inputField.toText** (riga 132, interna) — `toText()`. Valore della bozza come testo, con i numeri divisi per `scale`.
- **inputField.read** (riga 139, interna) — `read()`. Legge l'input e restituisce `{ value }` oppure `{ error }`, con i messaggi per campo obbligatorio, lunghezza massima, intero, numero non valido e fuori intervallo. Per i numeri il valore è moltiplicato per `scale`.
- **inputField.validate** (riga 158, interna) — `validate()`. Applica `read`: mostra l'errore, oppure aggiorna la bozza e rimuove l'errore. Restituisce `true` se il campo è valido.
- **selectField** (riga 199) — `selectField({ draft, path, label, help, options, check, onChange, numeric, id })`. Menu a tendina legato a `draft[path]`. `check(valore)` restituisce un messaggio d'errore o `null`. Con `numeric` salva il valore come numero. `id` fisso serve a ritrovare il campo dopo un ridisegno. Restituisce `{ path, node, validate, showError, focus, refresh }`, con `focus` e `refresh` anonimi alle righe 241 e 242.
- **selectField.message** (riga 217, interna) — `message(value)`. Applica `check` e restituisce il messaggio o `null`.
- **selectField.validate** (riga 219, interna) — `validate()`. Se `check` passa salva il valore nella bozza e rimuove l'errore; altrimenti mostra l'errore. Restituisce `true` se valido.
- **switchField** (riga 251) — `switchField({ draft, path, label, help, disabled, disabledHelp, warning, onChange })`. Interruttore checkbox con `role="switch"` legato a `draft[path]`. Con `disabled` mostra `disabledHelp` al posto dell'aiuto. `warning` compare solo quando l'interruttore è acceso. Restituisce `{ path, node, validate, showError, focus, refresh }`; `focus` (riga 284) e `refresh` (uguale a `sync`) sono anonimi.
- **switchField.sync** (riga 268, interna) — `sync()`. Aggiorna lo stato del checkbox e la visibilità dell'avviso a partire dalla bozza.
- **choiceField** (riga 290) — `choiceField({ draft, path, legend, help, options, layout, onChange })`. Gruppo di radio esclusivi, a elenco (`list`) o a griglia (`grid`). Ogni opzione è `{ value, label, help? }`; la scelta va in `draft[path]`. Restituisce `{ path, node, validate, showError, focus, refresh }`.
- **choiceField.sync** (riga 313, interna) — `sync()`. Seleziona la radio che corrisponde al valore della bozza e aggiorna la classe `is-checked` delle etichette.
- **validateAll** (riga 342) — `validateAll(fields) → boolean`. Valida tutti i campi, mostra tutti gli errori e porta il fuoco sul primo non valido. Restituisce `true` se tutti sono validi.
- **applyServerErrors** (riga 352) — `applyServerErrors(fields, errors = [])`. Mostra sotto ciascun campo l'errore del motore (`{ path, message }`) e restituisce i messaggi che non corrispondono a nessun campo.
- **formSection** (riga 364) — `formSection({ title, lede }, ...children)`. Sezione con titolo `h2` collegato con `aria-labelledby` e una descrizione facoltativa.
- **fieldGrid** (riga 373) — `fieldGrid(...children)`. Contenitore a griglia per affiancare i campi.
- **saveBar** (riga 378) — `saveBar({ onSave })`. Barra in fondo al form, con lo stato (modifiche non salvate, esito) e il pulsante Salva, che chiama `onSave`. Restituisce `{ node, say, setDirty, setBusy }`. Il click è alla riga 382.
- **saveBar.say** (riga 388, interna) — `say(text, kind = null)`. Mostra un messaggio di stato; `kind` `'ok'` o `'error'` ne colora il testo.
- **saveBar.setDirty** (riga 397, interna) — `setDirty(on)`. Mostra o nasconde "modifiche non salvate". Un esito "ok" precedente viene cancellato quando si modifica di nuovo.
- **saveBar.setBusy** (riga 401, interna) — `setBusy(on)`. Disabilita il pulsante e mostra il testo di salvataggio in corso, o il testo normale.
- **timeZoneOptions** (riga 410) — `timeZoneOptions(current)`. Elenco di fusi per il menu: quelli di `Intl.supportedValuesOf` o una lista di riserva; aggiunge il fuso corrente se manca.
- **siteFields** (riga 423) — `siteFields(draft, onChange)`. Restituisce i campi del sito: nome, latitudine (−90…90), longitudine (−180…180), quota (−500…9000 m) e fuso.
- **siteFields.f** (riga 424, interna) — `f(key)`. Etichetta del campo, da `forms.fields.{key}.label`.
- **siteFields.help** (riga 425, interna) — `help(key)`. Testo di aiuto del campo, da `forms.fields.{key}.help`.
- **archiveFields** (riga 444) — `archiveFields(draft, onChange)`. Campi della cartella di salvataggio (obbligatoria, fino a 260 caratteri) e delle notti da conservare (intero da 1 a 3650).
- **productFields** (riga 452) — `productFields(draft, onChange)`. Un interruttore per ciascun prodotto: `timelapse`, `keogram`, `startrails`.
- **locationTool** (riga 463) — `locationTool({ onLocated })`. Pulsante che legge la posizione del dispositivo, arrotondata a 5 decimali, e chiama `onLocated({ latitude, longitude, elevation })`. La quota viene inviata solo se disponibile. Il pulsante è disabilitato se il browser non ha la geolocalizzazione. Callback: riga 471 (click), 476 (successo), 486 (errore).
- **livePreview** (riga 504) — `livePreview()`. Anteprima dell'ultimo frame, ricaricata con `start()` ogni 10 secondi e fermata con `stop()`. Se l'immagine non si carica mostra un testo al suo posto. Restituisce `{ node, start, stop }`. Gli handler `load` e `error` dell'immagine sono alle righe 512 e 516.
- **livePreview.reload** (riga 526, interna) — `reload()`. Ricarica l'immagine con un parametro di tempo, per aggirare la cache.
- **livePreview.start** (riga 531, interna) — `start()`. Ricarica subito e poi a intervalli di 10 secondi. Non fa nulla se il timer è già attivo.
- **livePreview.stop** (riga 536, interna) — `stop()`. Ferma il timer.
- **kindLabel** (riga 543, interna) — `kindLabel(kind)`. Etichetta del tipo di camera, da `forms.cameras.kind.{kind}`; se manca restituisce il tipo grezzo.
- **cameraPicker** (riga 551) — `cameraPicker({ selectedId, connected, onConnected, preview = true })`. Elenco delle camere trovate, con i pulsanti Cerca e Connetti, e anteprima dopo la connessione. `connected` è la camera già connessa, `{ id, name }`, oppure `null`. `onConnected(id)` viene chiamata dopo una connessione riuscita. Restituisce `{ node, init, destroy }`, dove `init` è la ricerca iniziale.
- **cameraPicker.say** (riga 569, interna) — `say(text, kind = null)`. Mostra un messaggio di stato, `'ok'` o `'error'`.
- **cameraPicker.setBusy** (riga 575, interna) — `setBusy(on)`. Blocca i pulsanti durante un'operazione; `busy` ricorda lo stato.
- **cameraPicker.renderList** (riga 581, interna) — `renderList()`. Ridisegna l'elenco delle camere come gruppo di radio. Il pulsante Connetti è attivo solo se una camera è scelta.
- **cameraPicker.load** (riga 615, interna) — `async load()`. Chiama `api.cameras()`. Se c'è una sola camera la sceglie; se la camera connessa è quella scelta mostra lo stato e avvia l'anteprima, altrimenti la ferma. In caso di errore mostra il messaggio.
- **cameraPicker.connect** (riga 642, interna) — `async connect()`. Chiama `api.connectCamera(chosen)`. Se la risposta ha `ok: false` mostra `error`; in caso di successo chiama `onConnected`, avvia l'anteprima e mostra lo stato. Il pulsante torna attivo in ogni caso.
- **cameraPicker.destroy** (riga 669) — `destroy()`. Ferma l'anteprima. Chiamata quando la schermata viene smontata.

---

## Schermate (web/js/views)

Ogni schermata esporta `mount(root, ctx)`, che costruisce il contenuto in `root` e restituisce `{ unmount() }` (oppure `null` se la configurazione non è disponibile). Le schermate Camera, Sito e Procedura guidata usano i campi di `form-kit.js`; Colore, Live e Notti costruiscono l'HTML a mano, con testi da `data-i18n` o da `t()`.

### web/js/views/camera.js

Schermata Camera: scelta della camera, guadagno, esposizione diurna e notturna, binning, intervallo di cattura, cattura diurna. La bozza (`session`) resta anche dopo un cambio di lingua, finché la configurazione salvata non cambia.

#### camera

- **camera.mount** (riga 13) — `mount(root, ctx)`. Monta la schermata. Crea la bozza dalla configurazione, costruisce i campi (guadagno, esposizione massima in secondi, esposizione diurna in ms, binning 1–4, intervallo di cattura, cattura diurna) e il selettore della camera. Il selettore riceve `onConnected(id)`, che imposta `camera.id` nella bozza (riga 35, anonima). Senza configurazione mostra un errore di rete.
- **camera.add** (riga 25, interna a mount) — `add(field)`. Registra un campo nell'elenco usato per validare e per mostrare gli errori del motore. Restituisce il campo.
- **camera.refreshDirty** (riga 30, interna a mount) — `refreshDirty()`. Aggiorna lo stato "modifiche non salvate" confrontando la bozza con la configurazione di partenza.
- **camera.onChange** (riga 41, interna a mount) — `onChange()`. Callback dei campi: richiama `refreshDirty`.
- **camera.save** (riga 70, interna a mount) — `async save()`. Valida tutti i campi; se sono validi invia la bozza con `PUT /api/config`. Gli errori del motore vanno sotto i campi, quelli senza campo nella barra. A successo aggiorna la configurazione di partenza e quella in memoria.
- **camera.unmount** (riga 117) — `unmount()`. Ferma il selettore della camera (anteprima).
- **camera.connectedCamera** (riga 123) — `connectedCamera(ctx)`. Restituisce `{ id, name }` della camera connessa secondo lo stato del motore, oppure `null`.

---

### web/js/views/site.js

Schermata Sito: dati del sito con posizione del dispositivo, lingua (cambia subito, si salva con Salva), tema (con anteprima), porta e accesso dalla rete locale, cartella e notti da conservare, testo sulle immagini. La porta si applica al riavvio.

#### site

- **site.mount** (riga 14) — `mount(root, ctx)`. Monta la schermata con la bozza, i campi del sito, la posizione del dispositivo, lingua, tema, porta, accesso dalla rete locale, archivio e testo sulle immagini. Callback anonime: `onLocated` (riga 39) copia latitudine, longitudine e quota e aggiorna i campi del sito; lingua (riga 53) richiama `ctx.setLanguage` quando cambia; tema (riga 68) richiama `ctx.previewTheme`. Al montaggio applica l'anteprima del tema. Un link alla procedura guidata va a `#/wizard`.
- **site.add** (riga 26, interna a mount) — `add(field)`. Registra un campo e lo restituisce.
- **site.addAll** (riga 30, interna a mount) — `addAll(list)`. Registra più campi.
- **site.refreshDirty** (riga 32, interna a mount) — `refreshDirty()`. Aggiorna lo stato "modifiche non salvate".
- **site.onChange** (riga 33, interna a mount) — `onChange()`. Callback dei campi: richiama `refreshDirty`.
- **site.save** (riga 109, interna a mount) — `async save()`. Valida i campi, invia la bozza con `PUT /api/config`, mostra gli errori del motore e, a successo, aggiorna la configurazione in memoria.
- **site.unmount** (riga 173) — `unmount()`. Annulla l'anteprima del tema, così torna quello salvato.

Nota: il campo `ui.allowLan` è disabilitato quando l'accesso dalla rete locale è spento (`disabled: !draft.ui?.allowLan`). Dalla schermata quindi non si può attivarlo; il testo di aiuto dice che è disabilitato finché non c'è una password.

---

### web/js/views/wizard.js

Procedura guidata del primo avvio, in tre passi: camera (`camera`), posizione e sito (`site`), cartella e prodotti (`archive`). Nulla viene inviato al motore finché non si preme Fine; "Salta" chiude la procedura senza salvare.

#### wizard

- **wizard.mount** (riga 12) — `mount(root, ctx)`. Crea la copia di lavoro della configurazione, i campi di ogni passo, la barra dei passi e i pulsanti Indietro, Avanti e Salta. Senza configurazione mostra un errore di rete. Callback: `onLocated` (riga 33) copia la posizione nella bozza; `onConnected` (riga 102) imposta la camera scelta; click di Indietro (riga 181), Avanti (riga 184) e Salta (riga 186, anonimi). Il Salta segna la procedura come completata e va a `#/live`.
- **wizard.connectedCamera** (riga 68, interna a mount) — `connectedCamera()`. Camera connessa secondo lo stato del motore, o `null`.
- **wizard.renderProgress** (riga 73, interna a mount) — `renderProgress()`. Ridisegna la lista dei passi (corrente, completati) e la barra di avanzamento.
- **wizard.showStep** (riga 86, interna a mount) — `showStep(n)`. Mostra il passo `n`: distrugge il selettore camera precedente, costruisce il contenuto del passo, aggiorna titolo, descrizione e pulsanti. Dal secondo render sposta il fuoco sul titolo.
- **wizard.forward** (riga 128, interna a mount) — `forward()`. Dal passo della posizione valida i campi del sito e, se c'è un errore, resta nel passo. Negli altri casi va al passo successivo; all'ultimo passo chiama `finish`.
- **wizard.finish** (riga 141, interna a mount) — `async finish()`. Valida i passi sito e archivio; se uno non è valido torna lì e porta il fuoco sul campo. Invia la bozza con `PUT /api/config`. Se il motore restituisce errori torna al passo del primo errore. A successo segna la procedura come completata e va a `#/live`.
- **wizard.unmount** (riga 195) — `unmount()`. Distrugge il selettore camera.

---

### web/js/views/color.js

Schermata Colore: confronto fra il frame originale e quello elaborato con un divisore trascinabile, istogramma del fondo cielo, cursori per bilanciamento del bianco, stabilità, gradiente, curva, saturazione. Ogni modifica si salva dopo 500 ms con `PUT /api/config`, leggendo prima la configurazione attuale per non sovrascrivere le altre sezioni. I valori consigliati sono quelli di `docs/API.md`.

#### Funzioni del modulo

- **color.clamp** (riga 41) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **color.num** (riga 45) — `num(v, fallback)`. Restituisce `v` se è un numero finito, altrimenti `fallback`.
- **color.normalizeColor** (riga 50) — `normalizeColor(c = {})`. Copia pulita della sezione `color`: valori mancanti o fuori intervallo tornano ai valori consigliati o ai limiti (guadagni tra 0.5 e 2, stabilità tra 0 e 1, saturazione tra 0 e 2). `whiteBalance` e `stretch` accettano solo i valori previsti.
- **color.template** (riga 62) — `template(uid)`. Restituisce l'HTML della schermata, con id univoci costruiti da `uid` e i testi marcati con `data-i18n`.
- **color.sampleSky** (riga 217) — `sampleSky(img)`. Disegna l'immagine elaborata su un canvas 128×128 e considera solo il cerchio del cielo (raggio pari al 47% del lato). Restituisce gli istogrammi R, G, B a 256 livelli, il numero di pixel e le mediane. Restituisce `null` se il cerchio non ha pixel. Se il canvas non può essere letto la funzione lancia un'eccezione, che `analyse` cattura.
- **color.sampleSky.median** (riga 243, interna) — `median(bins)`. Mediana di un istogramma: il primo livello in cui il conteggio cumulato arriva almeno alla metà dei pixel.

#### color.mount

- **color.mount** (riga 254) — `async mount(root, ctx)`. Monta la schermata: legge la configurazione (o la chiede al motore), inserisce il modello HTML e raccoglie i nodi con `data-role`. Gestisce il confronto, l'istogramma, i cursori, le radio e il salvataggio. Si iscrive agli eventi `status` (riga 719, anonimo) e osserva il cambio di tema per ridisegnare l'istogramma (riga 729, anonimo). Restituisce `{ unmount }`. Altri gestori anonimi: tastiera sul divisore (riga 371), pulsanti "Solo originale" e "Solo elaborata" (righe 403 e 408), Esc nella conferma di ripristino (riga 710).
- **color.frameUrl** (riga 287, interna a mount) — `frameUrl(raw)`. URL del frame del motore; con `raw` aggiunge `stage=raw` per l'originale. Il parametro `v` combina id del frame e revisione, così il browser non usa una copia vecchia.
- **color.setMessage** (riga 292, interna a mount) — `setMessage(kind, text)`. Mostra o nasconde il messaggio sopra il confronto (per esempio "nessun frame" o errore).
- **color.loadImages** (riga 300, interna a mount) — `loadImages()`. Carica originale ed elaborato del frame corrente. Senza frame nasconde le immagini, scrive il messaggio di vuoto e disegna l'istogramma vuoto.
- **color.applySplit** (riga 332, interna a mount) — `applySplit()`. Applica il divisore: `clip-path` sull'originale, posizione della maniglia, `aria-valuenow` e `aria-valuetext`, stato `aria-pressed` dei pulsanti di modalità, etichette "Originale" e "Elaborata".
- **color.setSplitFromX** (riga 345, interna a mount) — `setSplitFromX(clientX)`. Converte la posizione orizzontale del puntatore in percentuale e applica il divisore. Esce dalla modalità "solo" (`mode = null`).
- **color.onComparePointerDown** (riga 353, interna a mount) — `onComparePointerDown(event)`. Inizia il trascinamento del divisore, cattura il puntatore e sposta il divisore. Ignora i click che non sono quello principale del mouse.
- **color.onComparePointerMove** (riga 360, interna a mount) — `onComparePointerMove(event)`. Sposta il divisore mentre si trascina.
- **color.onComparePointerEnd** (riga 363, interna a mount) — `onComparePointerEnd()`. Termina il trascinamento.
- **color.analyse** (riga 416, interna a mount) — `analyse()`. Chiamata quando l'immagine elaborata è caricata: calcola gli istogrammi dal cielo (`sampleSky`), aggiorna la nota e ridisegna.
- **color.updateHistNote** (riga 426, interna a mount) — `updateHistNote()`. Scrive il giudizio sul fondo cielo: "neutro" se la differenza tra le mediane è al massimo 4, altrimenti il canale dominante; aggiunge le mediane e aggiorna l'`aria-label` del grafico.
- **color.drawHist** (riga 447, interna a mount) — `drawHist()`. Disegna l'istogramma su canvas: griglia, assi con 0, 128 e 255, tre curve (R continua, G tratteggiata, B punteggiata) a 64 barre, con l'etichetta del canale vicino al picco. Usa le dimensioni e il `devicePixelRatio` correnti, e i colori dai token CSS.
- **color.updateSwatch** (riga 542, interna a mount) — `updateSwatch()`. Aggiorna il campione di colore dei guadagni manuali e il testo che li descrive.
- **color.syncControls** (riga 553, interna a mount) — `syncControls()`. Riporta sui controlli i valori della copia locale: radio, cursori, valori mostrati e campione di colore.
- **color.setupInputs** (riga 572, interna a mount) — `setupInputs()`. Imposta `min`, `max` e `step` dei cursori.
- **color.edit** (riga 586, interna a mount) — `edit(fn)`. Applica la funzione `fn` alla copia locale della sezione `color`, riallinea i controlli e programma il salvataggio.
- **color.setStatus** (riga 615, interna a mount) — `setStatus(kind, detail)`. Mostra lo stato del salvataggio: "in corso", "salvato alle HH:MM" o l'errore. Mostra il pulsante Riprova solo in caso di errore.
- **color.scheduleSave** (riga 627, interna a mount) — `scheduleSave()`. Segna la copia come modificata e avvia il salvataggio dopo 500 ms; una nuova modifica entro quel tempo riavvia il conteggio.
- **color.errorFromResponse** (riga 636, interna a mount) — `errorFromResponse(res)`. Unisce i messaggi di `res.errors`; se mancano usa `errors.unknown`.
- **color.save** (riga 642, interna a mount) — `async save()`. Ciclo di salvataggio: finché ci sono modifiche in attesa legge la configurazione attuale, unisce la sezione `color` alla copia locale e invia `PUT /api/config`. A successo incrementa la revisione, ricarica le immagini e mostra "salvato". Un secondo salvataggio mentre il primo è in corso viene accodato dal ciclo.
- **color.openConfirm** (riga 689, interna a mount) — `openConfirm()`. Mostra la conferma del ripristino dei valori consigliati e porta il fuoco su "Ripristina".
- **color.closeConfirm** (riga 695, interna a mount) — `closeConfirm(focusReset)`. Nasconde la conferma; con `focusReset` riporta il fuoco sul pulsante Ripristina.
- **color.unmount** (riga 744, interna a mount) — `unmount()`. Disattiva la vista, scollega gli osservatori e l'ascoltatore di stato. Le modifiche in attesa vengono salvate subito.

---

### web/js/views/live.js

Schermata Live: vista circolare del cielo con l'ultimo frame o quello scelto sul nastro; stato in una frase; dati chiave (esposizione, guadagno, mediana ADU, disco libero); nastro della notte con la curva dell'altezza del Sole. Il colore del nastro segue l'altezza reale del Sole, calcolata dalle coordinate del sito.

#### Funzioni del modulo

- **live.polar** (riga 27) — `polar(radius, deg)`. Coordinate di un punto sull'anello del cielo, in un riquadro 480×480 con il nord in alto.
- **live.ringMarkup** (riga 32) — `ringMarkup()`. SVG dell'anello: tacche ogni 5°, più lunghe ogni 30°, e le lettere N, E, S, O.
- **live.template** (riga 56) — `template()`. HTML della schermata Live.
- **live.clamp** (riga 144) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **live.normalizeFrame** (riga 148) — `normalizeFrame(f)`. Converte un frame del motore in `{ id, ts, phase }`, con `ts` in millisecondi.
- **live.parseBands** (riga 152) — `parseBands(list)`. Converte gli intervalli di fase del motore in oggetti con `Date`. Scarta le fasi sconosciute e le date non valide.
- **live.siteCoords** (riga 160) — `siteCoords(site)`. Restituisce `{ lat, lon }` se le coordinate del sito sono numeri validi, altrimenti `null`.
- **live.hexToRgb** (riga 169) — `hexToRgb(value)`. Converte un colore `#rrggbb` in `[r, g, b]`; `null` se il formato non è questo.
- **live.mixRgb** (riga 176) — `mixRgb(a, b, w)`. Interpolazione lineare tra due colori RGB, con peso `w`.
- **live.cssColor** (riga 180) — `cssColor(rgb)`. Converte `[r, g, b]` nella stringa `rgb(r,g,b)` con valori arrotondati.
- **live.readRamp** (riga 185) — `readRamp(node)`. Legge i token `--phase-day`, `--phase-twilight`, `--phase-night` e restituisce `{ day, twilight, night, at(alt) }`. `at(alt)` dà il colore per un'altezza del Sole: giorno sopra 0°, transizione morbida fino alla notte a −18°.
- **live.readRamp.pick** (riga 187, interna) — `pick(name, fallback)`. Colore da un token CSS, oppure il valore di riserva se il token non è leggibile.
- **live.readRamp.at** (riga 201, interna) — `at(alt)`. Colore per un'altezza, interpolato tra le soglie 0°, −6°, −12° e −18°.
- **live.sunY** (riga 213) — `sunY(alt)`. Posizione verticale, in percentuale, di un'altezza del Sole nel riquadro della curva (da +40° in alto a −60° in basso).

#### live.mount

- **live.mount** (riga 217) — `mount(root, ctx)`. Monta la schermata e disegna subito i dati già noti, poi carica la notte. Si iscrive agli eventi `status` e `frame` del contesto e osserva il cambio di tema per ridisegnare il nastro. Gestori anonimi: link "Vai a Camera" (riga 314), caricamento ed errore dell'immagine del cielo (righe 359 e 365), "Riprova" (riga 693), "Torna all'ultimo frame" (riga 692). Restituisce `{ unmount }`.
- **live.timeText** (riga 249, interna a mount) — `timeText(ms, seconds = true)`. Orario locale del sito, o `'–'` se `ms` non è valido.
- **live.renderStatus** (riga 253, interna a mount) — `renderStatus()`. Aggiorna titolo, frase di stato, esposizione, guadagno, mediana ADU, spazio libero su disco, camera e avvisi a partire dallo stato del motore. Senza stato mostra il caricamento.
- **live.formatExposure** (riga 286, interna a mount) — `formatExposure(ms)`. Esposizione in secondi se è almeno 1000 ms, altrimenti in millisecondi.
- **live.renderCamera** (riga 291, interna a mount) — `renderCamera(cam)`. Riga dello stato della camera con un punto colorato (verde se connessa) e il nome.
- **live.renderProblem** (riga 306, interna a mount) — `renderProblem(st)`. Mostra l'avviso se lo stato è in errore, se c'è un messaggio di errore o se la camera è disconnessa. Usa il messaggio del motore, o quello generico.
- **live.renderSky** (riga 320, interna a mount) — `renderSky()`. Mostra nel cielo il frame scelto sul nastro, oppure l'ultimo frame. La didascalia con l'ora compare solo per un frame scelto. Senza immagine mostra il messaggio di caricamento, di inattività o di assenza di frame.
- **live.pctOf** (riga 371, interna a mount) — `pctOf(ms)`. Posizione in percentuale di un istante sul nastro, limitata tra 0 e 100.
- **live.sunDegAt** (riga 376, interna a mount) — `sunDegAt(ms)`. Altezza del Sole in quell'istante, calcolata con `sunAltitude`; `null` senza coordinate.
- **live.renderRibbonText** (riga 380, interna a mount) — `renderRibbonText()`. Scrive il titolo del nastro con la data e, se la fascia è di esempio, la nota relativa.
- **live.renderBands** (riga 391, interna a mount) — `renderBands()`. Colora il nastro: con coordinate, con un gradiente che segue la curva reale; senza, con fasi a gradini. Disegna anche la curva del Sole con `sunMarkup`.
- **live.sunMarkup** (riga 422, interna a mount) — `sunMarkup(pct)`. Restituisce l'SVG della curva del Sole, delle linee di passaggio di fase (tratteggiate, con alone) e dell'orizzonte a 0°.
- **live.renderAxis** (riga 444, interna a mount) — `renderAxis()`. Scrive le etichette di inizio, metà e fine della notte sotto il nastro, e le tacche orarie.
- **live.nearestFrameIndex** (riga 475, interna a mount) — `nearestFrameIndex(ms)`. Indice del frame più vicino a un istante, con ricerca binaria sull'elenco ordinato.
- **live.setPicked** (riga 488, interna a mount) — `setPicked(index)`. Sceglie il frame con indice `index`; `null` o l'ultimo indice significano "ultimo frame". Aggiorna cielo e cursore.
- **live.renderCursor** (riga 495, interna a mount) — `renderCursor()`. Posiziona il cursore sul frame scelto, scrive ora e altezza del Sole, aggiorna `aria-valuenow` e `aria-valuetext` e mostra o nasconde "Torna all'ultimo frame".
- **live.showBanner** (riga 534, interna a mount) — `showBanner(message)`. Mostra o nasconde l'avviso con il messaggio dato.
- **live.fetchAllFrames** (riga 539, interna a mount) — `async fetchAllFrames(nightId)`. Scarica tutti i frame della notte a pagine di 1000, fino a 20000. Lancia `ApiError('parse')` se una risposta non è un elenco.
- **live.reloadNight** (riga 550, interna a mount) — `async reloadNight()`. Ricarica l'elenco delle notti, individua la notte in corso, scarica i suoi frame, calcola la curva o le fasi e aggiorna il nastro. Una risposta arrivata dopo un'altra richiesta più recente viene scartata con il token.
- **live.appendNewFrames** (riga 603, interna a mount) — `async appendNewFrames()`. Scarica solo i frame successivi a quelli già presenti e li aggiunge in coda. Se la notte non è ancora nota ricarica tutto con `reloadNight`.
- **live.scheduleAppend** (riga 618, interna a mount) — `scheduleAppend()`. Avvia `appendNewFrames` dopo 400 ms, eliminando la richiesta precedente.
- **live.pickFromClientX** (riga 625, interna a mount) — `pickFromClientX(clientX)`. Sceglie il frame più vicino alla posizione orizzontale del puntatore sul nastro.
- **live.onPointerDown** (riga 634, interna a mount) — `onPointerDown(event)`. Inizia la selezione con il puntatore sul nastro, cattura il puntatore, dà il fuoco al nastro e sceglie il frame.
- **live.onPointerMove** (riga 643, interna a mount) — `onPointerMove(event)`. Aggiorna la selezione durante il trascinamento.
- **live.onPointerEnd** (riga 647, interna a mount) — `onPointerEnd()`. Termina il trascinamento.
- **live.onKeyDown** (riga 651, interna a mount) — `onKeyDown(event)`. Tastiera sul nastro: frecce di un frame, PageUp e PageDown di dieci frame, Home e Fine per il primo e l'ultimo, Esc per tornare all'ultimo.
- **live.onStatus** (riga 697, interna a mount) — `onStatus(status)`. Salva lo stato, aggiorna la vista e ricarica la notte se è cambiato il giorno del calendario notturno.
- **live.onFrame** (riga 705, interna a mount) — `onFrame()`. Se la notte non è ancora nota la ricarica; altrimenti accoda i frame nuovi con un ritardo.
- **live.unmount** (riga 731, interna a mount) — `unmount()`. Disattiva la vista, annulla i timer, scollega gli osservatori e gli ascoltatori.

---

### web/js/views/nights.js

Schermata Notti: elenco delle notti, dalla più recente, e pannello di dettaglio (un `<dialog>` modale, aperto con `#/nights/{id}`) con timelapse, keogram, startrails, nastro della notte, miniature ed eliminazione.

#### Funzioni del modulo

- **nights.clamp** (riga 17) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **nights.esc** (riga 20) — `esc(value)`. Converte in testo sicuro per l'HTML i caratteri `& < > " '`.
- **nights.urls.keogram** (riga 25) — `keogram(id)`. URL del keogram di una notte.
- **nights.urls.startrails** (riga 26) — `startrails(id)`. URL dello startrails di una notte.
- **nights.urls.timelapse** (riga 27) — `timelapse(id)`. URL del file `timelapse.mp4` della notte.
- **nights.urls.thumb** (riga 28) — `thumb(id, frameId)`. URL della miniatura JPEG di un frame.
- **nights.validId** (riga 31) — `validId(id)`. Vero se `id` è una stringa nel formato `YYYY-MM-DD`.
- **nights.nightDate** (riga 36) — `nightDate(id)`. Data della notte per esteso, per esempio "18 ottobre". Usa mezzogiorno UTC per evitare sfasamenti di fuso.
- **nights.sizeText** (riga 41) — `sizeText(mb)`. Dimensione in MB, o in GB sopra 1024 MB; `'–'` se non è un numero.
- **nights.countText** (riga 47) — `countText(n)`. Numero di frame con l'unità; `'–'` se non è un numero.
- **nights.hashNightId** (riga 51) — `hashNightId()`. Legge l'id della notte da `#/nights/{id}`; `null` se assente o non valido.
- **nights.normalizeFrame** (riga 62) — `normalizeFrame(f)`. Converte un frame in `{ id, ts, phase }`. Una fase sconosciuta diventa `'night'`.
- **nights.parseBands** (riga 66) — `parseBands(list)`. Stessa funzione di `live.parseBands`: converte e filtra gli intervalli di fase.
- **nights.alertMarkup** (riga 73) — `alertMarkup(text, kind = 'warn')`. HTML di un avviso (`notice`) con il testo escapato.
- **nights.listTemplate** (riga 79) — `listTemplate()`. HTML della lista delle notti: titolo, stato, errore con "Riprova", caricamento, stato vuoto, griglia.
- **nights.tabsMarkup** (riga 107) — `tabsMarkup()`. HTML dei tre pulsanti di scheda (timelapse, keogram, startrails).
- **nights.panelTemplate** (riga 114) — `panelTemplate()`. HTML del pannello di dettaglio: intestazione, schede, sezione del nastro con anteprima, sezione di eliminazione, e le due finestre di conferma ed ingrandimento.
- **nights.flagMarkup** (riga 185) — `flagMarkup(product, state, value)`. Etichetta di stato di un prodotto (nome e valore) con l'attributo `data-state`.
- **nights.flagsMarkup** (riga 189) — `flagsMarkup(n)`. Le tre etichette di stato (timelapse, keogram, startrails) di una notte.
- **nights.cardMarkup** (riga 201) — `cardMarkup(n, { latest, current })`. HTML della scheda di una notte nella lista: miniatura del keogram, data, numero di frame e dimensione, stato dei prodotti. `latest` e `current` scelgono l'etichetta in alto.
- **nights.mount** (riga 222) — `mount(root, ctx)`. Monta l'elenco e il pannello, collega gli eventi e carica l'elenco. Se l'hash contiene una notte apre il pannello. Gestori anonimi: click sulle schede della griglia che apre il pannello (riga 788), "Riprova" (riga 795), chiusura del pannello (riga 617), chiusura dell'ingrandimento che riporta il fuoco (riga 738), pulsante Chiudi dell'ingrandimento (riga 737), click e tastiera sulle schede del pannello (righe 797 e 802), click sui pulsanti `data-act` nel pannello (riga 825), errori di immagini e video nel pannello (riga 833), pulsanti di eliminazione (righe 845–847), caricamento ed errore dell'anteprima (righe 849 e 852), click sulla striscia (riga 858), trascinamento sul nastro (righe 888, 896, 902, 903), tastiera sul nastro (riga 906). Restituisce `{ unmount }`.
- **nights.mount.timeText** (riga 254, interna a mount) — `timeText(ms, seconds = false)`. Orario locale del sito, o `'–'`.
- **nights.mount.entryOf** (riga 255, interna a mount) — `entryOf(id)`. Notta con quell'id nell'elenco in memoria, o `null`.
- **nights.mount.currentId** (riga 256, interna a mount) — `currentId()`. Id della notte in corso nel fuso del sito.
- **nights.mount.renderList** (riga 260, interna a mount) — `renderList()`. Ridisegna la griglia delle notti, dalla più recente. L'etichetta sopra la data (la notte più recente o quella in corso) viene dalle stringhe `nights.card`.
- **nights.mount.announce** (riga 275, interna a mount) — `announce(message)`. Scrive un messaggio nell'area di stato, letta dagli screen reader.
- **nights.mount.refreshList** (riga 280, interna a mount) — `async refreshList()`. Chiama `api.nights()`, aggiorna l'elenco o l'errore, e aggiorna l'intestazione se il pannello è aperto. Una richiesta più recente scarta quelle precedenti.
- **nights.mount.renderHeader** (riga 303, interna a mount) — `renderHeader()`. Scrive titolo, numero di frame, dimensione e orario del primo e dell'ultimo frame nel pannello.
- **nights.mount.renderTabs** (riga 319, interna a mount) — `renderTabs()`. Imposta `aria-selected` e `tabIndex` delle schede secondo la scheda attiva.
- **nights.mount.selectTab** (riga 328, interna a mount) — `selectTab(name, { focus = false } = {})`. Passa alla scheda `name` se è valida, ridisegna il contenuto e, con `focus`, porta il fuoco sul pulsante.
- **nights.mount.timelapseMarkup** (riga 336, interna a mount) — `timelapseMarkup(entry)`. HTML della scheda timelapse secondo lo stato: `ready` mostra il video e il download; `generating` mostra la barra di progresso; `failed` mostra l'errore con "Riprova"; `none` offre il pulsante per crearlo.
- **nights.mount.imageMarkup** (riga 379, interna a mount) — `imageMarkup(entry, kind)`. HTML della scheda keogram o startrails: immagine, ingrandimento e download; se il prodotto manca mostra un messaggio.
- **nights.mount.renderTabBody** (riga 394, interna a mount) — `renderTabBody()`. Riempie il pannello della scheda attiva e, per il timelapse, aggiorna il progresso.
- **nights.mount.updateProgress** (riga 405, interna a mount) — `updateProgress()`. Aggiorna solo la barra e il testo di progresso mentre il lavoro è in corso, senza ricostruire il pannello.
- **nights.mount.pctOf** (riga 421, interna a mount) — `pctOf(ms)`. Posizione in percentuale di un istante sul nastro, tra 0 e 100.
- **nights.mount.renderBands** (riga 426, interna a mount) — `renderBands(entry)`. Imposta la finestra della notte, disegna i segmenti colorati per fase (o la fascia di esempio, con una nota) e le etichette orarie.
- **nights.mount.defaultPick** (riga 448, interna a mount) — `defaultPick(frames)`. Indice del frame predefinito: il frame di mezzo tra quelli di notte, o quello di mezzo dell'elenco se non ce ne sono.
- **nights.mount.setPick** (riga 458, interna a mount) — `setPick(index)`. Sceglie il frame con l'indice dato, limitato all'intervallo valido, e aggiorna il pannello.
- **nights.mount.renderPick** (riga 464, interna a mount) — `renderPick()`. Aggiorna cursore, ora, fase, anteprima e attributi ARIA del nastro. Senza frame mostra il caricamento o il messaggio di vuoto.
- **nights.mount.renderStrip** (riga 505, interna a mount) — `renderStrip()`. Crea la striscia di miniature con al massimo 72 elementi, presi a passo regolare dai frame.
- **nights.mount.highlightStrip** (riga 525, interna a mount) — `highlightStrip()`. Evidenzia la miniatura più vicina al frame scelto e la porta in vista; con "riduci movimento" lo scorrimento è immediato.
- **nights.mount.loadFrames** (riga 543, interna a mount) — `async loadFrames(id)`. Carica tutti i frame della notte a pagine di 1000, fino a 20000, poi sceglie il frame predefinito e disegna striscia e anteprima. Scarta le risposte di una notte o di una richiesta superate.
- **nights.mount.loadPanel** (riga 574, interna a mount) — `async loadPanel(id)`. Carica l'elenco se serve, poi intestazione, schede, nastro e scheda del timelapse; avvia il polling se il timelapse è in corso; carica i frame.
- **nights.mount.openPanel** (riga 594, interna a mount) — `openPanel(id, { fromHash = false } = {})`. Apre la finestra di dettaglio per l'id. Se la notte cambia, azzera lo stato del pannello. Aggiorna l'hash con `pushState` se l'apertura non viene dall'hash.
- **nights.mount.startTimelapse** (riga 635, interna a mount) — `async startTimelapse(btn)`. Avvia la creazione o il riprovo del timelapse con `POST`. In caso di errore mostra il messaggio; altrimenti passa allo stato `generating`, aggiorna l'elenco e avvia il polling.
- **nights.mount.startPolling** (riga 666, interna a mount) — `startPolling()`. Avvia il polling ogni 2 secondi, con una prima lettura subito. Non fa nulla se è già attivo.
- **nights.mount.stopPolling** (riga 672, interna a mount) — `stopPolling()`. Ferma il polling.
- **nights.mount.pollTick** (riga 678, interna a mount) — `async pollTick()`. Legge `/api/status`. Se c'è un job di timelapse per la notte aperta aggiorna il progresso. Altrimenti rilegge l'elenco e ferma il polling, a meno che il timelapse risulti ancora in corso. Un errore di lettura non ferma il lavoro.
- **nights.mount.openZoom** (riga 724, interna a mount) — `openZoom(kind, opener)`. Apre la finestra di ingrandimento di keogram o startrails. Quando si chiude il fuoco torna su `opener`.
- **nights.mount.openConfirm** (riga 747, interna a mount) — `openConfirm()`. Apre la conferma di eliminazione con data, numero di frame e dimensione.
- **nights.mount.confirmDelete** (riga 761, interna a mount) — `async confirmDelete()`. Chiama `api.deleteNight`. A successo rimuove la notte dall'elenco, annuncia l'eliminazione e chiude il pannello. In caso di errore mostra il messaggio nella conferma.
- **nights.mount.pickFromClientX** (riga 865, interna a mount) — `pickFromClientX(clientX)`. Sceglie il frame più vicino alla posizione orizzontale del puntatore sul nastro.
- **nights.mount.nearestFrameIndex** (riga 874, interna a mount) — `nearestFrameIndex(ms)`. Indice del frame più vicino a un istante, con ricerca binaria.
- **nights.mount.endDrag** (riga 899, interna a mount) — `endDrag()`. Termina il trascinamento sul nastro.
- **nights.mount.unmount** (riga 949, interna a mount) — `unmount()`. Ferma il polling e chiude le finestre di dialogo aperte.

---

## Motore di prova (web/mock/server.mjs)

Server HTTP di prova che implementa il contratto di `docs/API.md` con dati finti e serve i file statici di `web/` su `/`. Usa solo moduli integrati di Node. Il Sole è un modello di comodo (massimo di 50° verso le 13:00 locali) e la Luna una sinusoide: non sono effemeridi. Le immagini sono SVG di prova in `web/mock/assets`. Il colore elaborato è una simulazione con filtri SVG, non elaborazione reale. Le route `/api/__mock/*` sono solo per le prove.

Costanti principali: `PORT` (5810), `TZ` (Europe/Rome), `CADENCE_MS` (30 s, intervallo dei frame), `TIMELAPSE_MS` (12 s, durata finta della creazione del timelapse), `MOCK_STATE` (`capturing`, `idle` o `error`).

- **localParts** (riga 74) — `localParts(ms)`. Anno, mese, giorno, ora (0–23), minuti e secondi locali di un istante nel fuso Europe/Rome.
- **offsetMs** (riga 82) — `offsetMs(ms)`. Differenza in millisecondi tra l'ora locale e UTC in quell'istante.
- **zonedToMs** (riga 87) — `zonedToMs(y, m, d, h)`. Istante UTC corrispondente all'ora locale indicata, corretto per l'ora legale. `m` va da 1 a 12.
- **nightIdFor** (riga 96) — `nightIdFor(ms)`. Identificatore `YYYY-MM-DD` della notte di un istante: prima di mezzogiorno locale è il giorno precedente.
- **nightSpan** (riga 102) — `nightSpan(id)`. `{ start, end }` in millisecondi: da mezzogiorno locale del giorno a mezzogiorno del giorno dopo.
- **localHour** (riga 107) — `localHour(ms)`. Ora locale come numero decimale (per esempio 13.5 per le 13:30).
- **sunAltitude** (riga 113) — `sunAltitude(ms)`. Modello di comodo: `50·cos(2π(ora − 13)/24)` gradi. Non è un calcolo astronomico.
- **moonAltitude** (riga 117) — `moonAltitude(ms)`. Modello sinusoidale di comodo, ampiezza 31°, periodo di 27.3 giorni. Non è un calcolo astronomico.
- **phaseAt** (riga 121) — `phaseAt(ms)`. Fase dal modello del Sole: `'day'` sopra 0°, `'twilight'` sopra −6°, altrimenti `'night'`.
- **iso** (riga 128) — `iso(ms)`. Data in formato ISO 8601 UTC senza millisecondi.
- **round1** (riga 132) — `round1(n)`. Arrotonda a una cifra decimale.
- **bandsFor** (riga 142) — `bandsFor(nightId)`. Intervalli di fase della notte, campionati ogni minuto e uniti quando la fase non cambia. Il risultato è in cache. Restituisce `[{ phase, from, to }]` con date ISO.
- **framesFor** (riga 161) — `framesFor(nightId)`. Frame della notte ogni 30 secondi, fino all'istante corrente. Il risultato è in cache per intervallo di 30 secondi; le voci di intervalli precedenti vengono eliminate.
- **finishJobs** (riga 179) — `finishJobs()`. Chiude i job di timelapse la cui durata finta è scaduta, li segna come pronti e invalida la cache delle notti.
- **timelapseOf** (riga 190) — `timelapseOf(id)`. Stato del timelapse: prima l'override di prova, poi `'generating'` se c'è un job, `'ready'` se è stato creato; altrimenti `'none'` per la notte in corso e `'ready'` per le notti passate.
- **nightsList** (riga 198) — `nightsList()`. Elenco delle ultime sei notti (escluse quelle eliminate) con frame, dimensione, primo e ultimo frame, prodotti, stato del timelapse e fasi. Il risultato è in cache per intervallo di 30 secondi.
- **asset** (riga 231) — `asset(name)`. Legge un file da `web/mock/assets` e lo tiene in cache.
- **frameAssetFor** (riga 239) — `frameAssetFor(phase, nightId)`. Nome dell'immagine di prova per la fase: `frame-day.svg`, `frame-dusk.svg`, oppure `frame-night-a.svg` o `frame-night-b.svg` per la notte, a seconda della parità del giorno.
- **activeJobs** (riga 248) — `activeJobs()`. Elenco dei job in corso con il progresso, calcolato sul tempo trascorso e limitato a 99.
- **status** (riga 259) — `status()`. Stato del motore finto: versione, stato (`MOCK_STATE`), fase, altezze del Sole e della Luna, camera, ultimo frame (esposizione, guadagno, mediana ADU), disco, errore se `MOCK_STATE` è `error`, job in corso.
- **validateConfig** (riga 293) — `validateConfig(body)`. Controlla `schemaVersion`, lingua, tema, latitudine e longitudine. Restituisce la lista degli errori `{ path, message }`, vuota se la configurazione è valida.
- **sendJson** (riga 320) — `sendJson(res, code, body)`. Invia una risposta JSON con `Content-Length` e `Cache-Control: no-store`.
- **sendBuffer** (riga 331) — `sendBuffer(req, res, data, type, cache)`. Invia un file binario con il supporto alle richieste `Range` (necessario al video per lo spostamento). Risponde 206 per un intervallo valido, 416 se l'intervallo non è valido, 200 senza intervallo.
- **sendSvg** (riga 366) — `sendSvg(res, name, cache)`. Invia un'immagine di prova SVG.
- **sendSvgText** (riga 385) — `sendSvgText(res, text, cache)`. Invia un SVG generato come testo UTF-8.
- **colorParams** (riga 395) — `colorParams()`. Legge i parametri colore da `config.color`, con i valori di default e i limiti (guadagni 0.5–2, stabilità 0–1, saturazione 0–2).
- **processedSvg** (riga 408) — `processedSvg(name)`. Crea la versione "elaborata" del frame di prova, avvolgendo il contenuto SVG in un gruppo con filtro: sfocatura per la stabilità, matrice di saturazione, guadagni per canale (manuali, o una correzione fissa sulle notti in automatico) e un velo di orizzonte che dipende dal gradiente. È una simulazione visiva. La cache viene svuotata quando supera 50 voci.
- **readBody** (riga 443) — `readBody(req)`. Legge il corpo della richiesta come testo UTF-8. Rifiuta con errore i corpi oltre 1 MB.
- **handleEvents** (riga 463) — `handleEvents(req, res)`. Endpoint SSE `/api/events`: invia subito lo stato e il frame corrente, poi lo stato ogni 5 secondi e il frame quando cambia id. Il timer viene fermato alla chiusura della connessione. Il timer che invia gli eventi è alla riga 485 (anonimo).
- **sendStatus** (riga 472, interna a handleEvents) — `sendStatus()`. Scrive l'evento `status` con lo stato corrente.
- **checkFrame** (riga 473, interna a handleEvents) — `checkFrame()`. Scrive l'evento `frame` se l'id dell'ultimo frame è cambiato dall'invio precedente.
- **nightExists** (riga 496) — `nightExists(id)`. Vero se la notte è nell'elenco di `nightsList`.
- **framesPage** (riga 500) — `framesPage(url, nightId)`. Pagina di frame della notte. `limit` va da 1 a 1000 (predefinito 200), `offset` è almeno 0. Restituisce `{ id, timestamp, phase }`.
- **handleApi** (riga 507) — `async handleApi(req, res, url)`. Router dell'API: stato (`GET /api/status`), eventi, configurazione (`GET` e `PUT /api/config`, con validazione), camere (`GET /api/cameras/discover`, `POST /api/camera/connect`), frame più recente (`GET /api/frame/latest.jpg`, con `stage=raw` per l'originale), notti (`GET /api/nights`, frame, miniature, keogram, startrails, `timelapse.mp4`, `POST` per creare il timelapse, `DELETE` per eliminare una notte), route `/api/__mock/*` per le prove. Ogni altro percorso riceve `404` in JSON.
- **serveStatic** (riga 661) — `serveStatic(req, res, pathname)`. Serve i file di `web/` con il tipo MIME ricavato dall'estensione. Accetta solo `GET` e `HEAD` (altrimenti `405`), blocca i percorsi fuori da `web/` e quelli in `web/mock` (`404`).
- **server** (riga 700, funzione anonima passata a `http.createServer`) — `async (req, res)`. Instrada le richieste: `/api` a `handleApi`, il resto a `serveStatic`. Un errore non gestito produce `500` in JSON. Il server ascolta su `127.0.0.1` alla porta `PORT`.

---

## Inventario generato delle funzioni C#

Generato dai file sorgente il 22/10/2026, sul ramo `ralph/desktop-v1`. Copre tutti i file `.cs` di `src/` (esclusi `bin`, `obj` e i test) tranne quelli già descritti a mano nelle sezioni precedenti (`AllSky.Core`, `AllSky.Astro`, `AllSky.Imaging`: `AppConfig.cs`, `ConfigStore.cs` e gli altri file elencati sopra). Sono incluse le funzioni, i costruttori e i tipi; restano fuori le proprietà e i campi.

Per ogni voce: riga della dichiarazione, firma senza corpo e prima frase del commento XML, quando il sorgente ne ha uno. Le righe valgono per questo stato del sorgente: vanno ricontrollate dopo ogni modifica.

## AllSky.Archive (src/AllSky.Archive)

### ArchiveTypes.cs

- **TimelapseState** (riga 8, tipo) — `public enum TimelapseState`. Stato della generazione del timelapse di una notte.
- **NightProduct** (riga 17, tipo) — `public enum NightProduct`. Prodotti di una notte, ciascuno con nome file fisso nella cartella della notte.
- **FrameMeta** (riga 33, tipo) — `public sealed record FrameMeta(double ExposureMs, double Gain, int MedianAdu);`. Metadati di un frame forniti dal chiamante.
- **FrameEntry** (riga 39, tipo) — `public sealed record FrameEntry(`. Una riga di meta.jsonl.
- **NightInfo** (riga 51, tipo) — `public sealed record NightInfo(`. Riepilogo di una notte.
- **DiskUsage** (riga 65, tipo) — `public sealed record DiskUsage(long UsedBytes, long? FreeBytes);`. Uso del disco.

### FrameSidecar.cs

- **FrameSidecar** (riga 13, tipo) — `public sealed record FrameSidecar`. Metadati di un frame archiviato, salvati accanto al JPEG come `<frame>.json`.
- **ObservingSite** (riga 63, tipo) — `public sealed record ObservingSite`. Sito osservativo, in gradi (nord e est positivi).

### FrameSidecars.cs

- **FrameSidecars** (riga 9, tipo) — `public static class FrameSidecars`. Costruzione, lettura e scrittura sicura dei sidecar FrameSidecar.
- **FromCapture** (riga 28) — `public static FrameSidecar FromCapture(`. Sidecar di un frame appena catturato.
- **Reconstruct** (riga 52) — `public static FrameSidecar Reconstruct(FrameEntry entry, ObservingSite? site, TimeZoneInfo tz)`. Metadati ricostruiti per un frame senza sidecar, da una riga di meta.jsonl.
- **FromTimestamp** (riga 67) — `public static FrameSidecar FromTimestamp(DateTimeOffset timestamp, ObservingSite? site, TimeZoneInfo tz)`. Metadati ricavabili solo dall'istante e dal sito: UTC, ora locale, Sole, Luna e illuminazione.
- **TryWrite** (riga 109) — `public static bool TryWrite(NightArchive archive, FrameSidecar sidecar, Action<string, Exception?> log)`. Scrive il sidecar del frame in modo atomico.
- **Round** (riga 127) — `private static double Round(double value, int digits)`.

### NightArchive.cs

- **NightArchive** (riga 19, tipo) — `public sealed class NightArchive`. Archivio su disco delle notti osservative.
- **NightArchive** (riga 55) — `public NightArchive(string rootFolder, IClock clock, TimeZoneInfo timeZone)`. Crea l'archivio sotto `rootFolder`.
- **IsValidNightId** (riga 68) — `public static bool IsValidNightId(string? s)`. Vero se `s` e' un identificatore di notte valido "YYYY-MM-DD" con data reale.
- **IsValidFrameId** (riga 76) — `public static bool IsValidFrameId(string? s)`. Vero se `s` e' un identificatore di frame valido "yyyyMMddTHHmmssZ" con data reale.
- **SaveFrame** (riga 94) — `public void SaveFrame(DateTimeOffset ts, string phase, byte[] jpeg, byte[] thumbJpeg, FrameMeta meta)`. Salva un frame, la sua miniatura e la riga in meta.jsonl.
- **ListNights** (riga 147) — `public IReadOnlyList<NightInfo> ListNights()`. Elenco delle notti presenti, dalla piu' recente alla piu' vecchia.
- **ListFrames** (riga 189) — `public IReadOnlyList<FrameEntry> ListFrames(string nightId, int offset, int limit)`. Frame di una notte in ordine di salvataggio (cronologico in uso normale).
- **GetFramePath** (riga 208) — `public string GetFramePath(string nightId, string frameId)`. Percorso del frame JPEG.
- **GetThumbPath** (riga 216) — `public string GetThumbPath(string nightId, string frameId)`. Percorso della miniatura JPEG.
- **GetProductPath** (riga 227) — `public string GetProductPath(string nightId, NightProduct kind)`. Percorso di un prodotto della notte.
- **SetTimelapseState** (riga 245) — `public void SetTimelapseState(string nightId, TimelapseState state)`. Imposta lo stato di generazione del timelapse.
- **DeleteNight** (riga 275) — `public bool DeleteNight(string nightId)`. Cancella la cartella di una notte e il suo indice.
- **ApplyRetention** (riga 309) — `public IReadOnlyList<string> ApplyRetention(int keepNights)`. Applica la retention: conserva le `keepNights` notti piu' recenti e cancella le altre.
- **GetDiskUsage** (riga 352) — `public DiskUsage GetDiskUsage()`. Uso del disco: dimensione dell'archivio e spazio libero sul volume della root.
- **NightIndex** (riga 376, tipo) — `private sealed class NightIndex`. Stato in memoria di una notte.
- **LoadIndex** (riga 387) — `private NightIndex LoadIndex(string nightId)`. Carica l'indice di una notte da meta.jsonl, una volta sola.
- **AppendMetaLine** (riga 441) — `private static void AppendMetaLine(string nightDir, NightIndex idx, FrameEntry entry)`. Aggiunge una riga a meta.jsonl.
- **GetSidecarPath** (riga 459) — `public string GetSidecarPath(string nightId, string frameId)`. Percorso del sidecar `<frame>.json` accanto al JPEG.
- **WriteSidecar** (riga 471) — `public void WriteSidecar(FrameSidecar sidecar)`. Scrive il sidecar del frame accanto al suo JPEG, in modo atomico (file .tmp poi rinomina).
- **ReadSidecar** (riga 490) — `public FrameSidecar? ReadSidecar(string nightId, string frameId)`. Sidecar salvato del frame, o null se non esiste o non e' leggibile (un JSON corrotto e' trattato come assente).
- **GetFrameSidecar** (riga 516) — `public FrameSidecar? GetFrameSidecar(string nightId, string frameId, ObservingSite? site)`. Metadati del frame: il sidecar se esiste, altrimenti una ricostruzione da meta.jsonl (o, se il frame non e' indicizzato, dal solo istante inscritto nel nome).
- **ReadTimelapseState** (riga 551) — `private TimelapseState ReadTimelapseState(string nightId)`.
- **ValidateNightId** (riga 573) — `private static void ValidateNightId(string nightId)`.
- **ValidateFrameId** (riga 581) — `private static void ValidateFrameId(string frameId)`.
- **ResolveUnderNights** (riga 593) — `private string ResolveUnderNights(params string[] parts)`. Compone un percorso sotto `nights/` e verifica che il percorso canonico resti dentro la cartella.
- **CurrentNightId** (riga 606) — `private string CurrentNightId()`.
- **WriteAtomic** (riga 614) — `private static void WriteAtomic(string path, byte[] data)`. Scrittura atomica: file .tmp accanto alla destinazione, poi rinomina sovrascrivendo.
- **IsLink** (riga 644) — `private static bool IsLink(FileSystemInfo fsi)`. Vero per symlink e junction.
- **DirectorySizeBytes** (riga 650) — `private static long DirectorySizeBytes(string dir)`. Dimensione in byte dei file sotto una cartella, senza seguire i collegamenti.
- **DeleteTree** (riga 675) — `private static void DeleteTree(string path)`. Cancella una cartella ricorsivamente.


## AllSky.Astro: file non descritti a mano sopra (src/AllSky.Astro)

### HorizontalPosition.cs

- **HorizontalPosition** (riga 8, tipo) — `public readonly record struct HorizontalPosition`. Posizione di un astro sulla volta celeste dell'osservatore.


## AllSky.Cameras.Alpaca (src/AllSky.Cameras.Alpaca)

### AlpacaCamera.cs

- **AlpacaCamera** (riga 24, tipo) — `public sealed class AlpacaCamera : ICameraSource`. Camera ASCOM Alpaca (dispositivo di tipo camera su un server Alpaca) come ICameraSource.
- **ConnectAsync** (riga 108) — `public async Task ConnectAsync(CancellationToken cancellationToken)`. Connette la camera (PUT connected=true) e legge le caratteristiche in Info.
- **CaptureAsync** (riga 141) — `public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)`. Esegue una posa e restituisce il frame.
- **DisposeAsync** (riga 200) — `public async ValueTask DisposeAsync()`. Disconnette la camera (PUT connected=false) se e' connessa e rilascia l'HttpClient se e' stato creato qui.
- **MapBayer** (riga 230) — `internal static BayerPattern MapBayer(int sensorType, int offsetX, int offsetY)`. Traduce SensorType e BayerOffset Alpaca nel BayerPattern del frame.
- **UnsupportedSensorMessage** (riga 247) — `private static string UnsupportedSensorMessage(int sensorType)`.
- **P** (riga 260) — `private static KeyValuePair<string, string> P(string name, string value)`.
- **IsDeviceError** (riga 262) — `private static bool IsDeviceError(Exception ex)`.
- **ReadCameraInfoAsync** (riga 271) — `private async Task<CameraInfo> ReadCameraInfoAsync(CancellationToken ct)`.
- **ReadExposureLimitMsAsync** (riga 318) — `private async Task<double> ReadExposureLimitMsAsync(string property, double unknown, CancellationToken ct)`. Legge un limite di esposizione (secondi) in millisecondi; un errore dell'API o un valore non valido danno `unknown`.
- **SetGainAsync** (riga 332) — `private async Task<double> SetGainAsync(double gain, CancellationToken ct)`. Imposta il gain arrotondato e restituisce il valore applicato.
- **WaitImageReadyAsync** (riga 354) — `private async Task WaitImageReadyAsync(double exposureMs, CancellationToken ct)`. Attende imageready con polling adattivo.
- **DownloadImageAsync** (riga 388) — `private async Task DownloadImageAsync(CameraInfo info, ushort[] pixels, CancellationToken ct)`. Scarica imagearray: ImageBytes se il dispositivo lo invia, altrimenti JSON.
- **TryReadSensorTempAsync** (riga 411) — `private async Task<double?> TryReadSensorTempAsync(CancellationToken ct)`. Temperatura del sensore, o null se il dispositivo non la fornisce.
- **TryDisconnectAsync** (riga 423) — `private async Task TryDisconnectAsync()`.
- **TryAbortAsync** (riga 436) — `private async Task TryAbortAsync()`.
- **ThrowIfDisposed** (riga 448) — `private void ThrowIfDisposed()`.

### AlpacaClient.cs

- **AlpacaClient** (riga 13, tipo) — `public sealed class AlpacaClient`. Client REST minimale per ASCOM Alpaca (solo BCL).
- **AlpacaClient** (riga 24) — `public AlpacaClient(Uri baseUri, HttpClient http)`. Crea il client per il server Alpaca raggiungibile a `baseUri`.
- **GetAsync** (riga 53) — `public Task<T> GetAsync<T>(string deviceType, int deviceNumber, string command, CancellationToken cancellationToken)`. Esegue una GET su un comando del dispositivo e restituisce il campo Value deserializzato come .
- **PutAsync** (riga 67) — `public async Task PutAsync(`. Esegue una PUT su un comando del dispositivo con parametri form-urlencoded.
- **GetApiVersionsAsync** (riga 92) — `public Task<int[]> GetApiVersionsAsync(CancellationToken cancellationToken)`. Restituisce le versioni dell'API supportate dal server (GET /management/apiversions).
- **GetConfiguredDevicesAsync** (riga 102) — `public async Task<IReadOnlyList<AlpacaConfiguredDevice>> GetConfiguredDevicesAsync(CancellationToken cancellationToken)`. Restituisce l'elenco dei dispositivi configurati sul server (GET /management/v1/configureddevices).
- **SendImageArrayRequestAsync** (riga 126) — `internal async Task<HttpResponseMessage> SendImageArrayRequestAsync(int deviceNumber, CancellationToken cancellationToken)`. Invia la GET di imagearray con `Accept: application/imagebytes` e restituisce la risposta senza leggere il corpo.
- **ReadJsonAsync** (riga 144) — `internal static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)`. Legge il corpo JSON di una risposta.
- **ThrowIfError** (riga 178) — `internal static void ThrowIfError(JsonElement root)`. Lancia AlpacaException se la risposta ha ErrorNumber diverso da zero.
- **GetValueAsync** (riga 197) — `private async Task<T> GetValueAsync<T>(string url, CancellationToken cancellationToken)`.
- **DeviceUrl** (riga 211) — `private string DeviceUrl(string deviceType, int deviceNumber, string command)`.
- **QueryString** (riga 223) — `private string QueryString()`.
- **NextTransactionId** (riga 232) — `private uint NextTransactionId()`.
- **Field** (riga 238) — `private static JsonElement Field(JsonElement item, string name)`.

### AlpacaConfiguredDevice.cs

- **AlpacaConfiguredDevice** (riga 8, tipo) — `public sealed record AlpacaConfiguredDevice(string DeviceName, string DeviceType, int DeviceNumber, string UniqueId);`. Voce dell'elenco /management/v1/configureddevices di un server Alpaca.

### AlpacaDeviceInfo.cs

- **AlpacaDeviceInfo** (riga 9, tipo) — `public sealed record AlpacaDeviceInfo(string Host, int Port, string DeviceName, int DeviceNumber, string UniqueId)`. Dispositivo camera Alpaca trovato dalla discovery sulla rete locale.

### AlpacaDiscovery.cs

- **AlpacaDiscovery** (riga 15, tipo) — `public static class AlpacaDiscovery`. Scoperta dei server Alpaca sulla rete locale (solo BCL).
- **DiscoverAsync** (riga 38) — `public static async Task<IReadOnlyList<AlpacaDeviceInfo>> DiscoverAsync(`. Cerca le camere Alpaca sulla rete.
- **CollectServersAsync** (riga 80) — `private static async Task<List<(string Host, int Port)>> CollectServersAsync(`. Invia la richiesta di discovery e raccoglie i (host, porta) che rispondono, senza duplicati.
- **QueryCamerasAsync** (riga 136) — `private static async Task<List<AlpacaDeviceInfo>> QueryCamerasAsync(`. Legge i dispositivi camera di un server.
- **ParseAlpacaPort** (riga 159) — `private static int? ParseAlpacaPort(byte[] buffer)`. Estrae AlpacaPort dal JSON di risposta.
- **IsServerError** (riga 181) — `private static bool IsServerError(Exception ex)`.

### AlpacaException.cs

- **AlpacaException** (riga 4, tipo) — `public sealed class AlpacaException : Exception`. Errore restituito dal dispositivo o dal server Alpaca (ErrorNumber diverso da zero).
- **AlpacaException** (riga 9) — `public AlpacaException(int errorNumber, string message)`. Crea l'eccezione con il codice di errore Alpaca e il messaggio del dispositivo.

### ImageBytesReader.cs

- **ImageBytesReader** (riga 12, tipo) — `internal static class ImageBytesReader`. Lettore del formato ImageBytes ASCOM Alpaca (solo BCL): header di 44 byte little-endian, poi i dati.
- **ElementType** (riga 30, tipo) — `internal enum ElementType`. Codici ImageArrayElementTypes della spec (sez.
- **Header** (riga 45, tipo) — `internal readonly record struct Header(`. Campi dell'header di 44 byte (sez.
- **ReadAsync** (riga 69) — `internal static async Task ReadAsync(`. Legge da `body` un'immagine ImageBytes 2D di `width` x `height` e la scrive in `pixels` in ordine riga-maggiore (indice = y * width + x).
- **ReadJsonColumns** (riga 155) — `internal static void ReadJsonColumns(JsonElement root, int width, int height, ushort[] pixels)`. Legge l'immagine dal campo Value di una risposta JSON (array di colonne: Value[x][y]), usata quando il dispositivo non risponde con ImageBytes.
- **ParseHeader** (riga 188) — `internal static Header ParseHeader(ReadOnlySpan<byte> bytes)`. Decodifica i 44 byte dell'header in campi little-endian.
- **ElementSize** (riga 204) — `private static int ElementSize(ElementType type)`. Dimensione in byte di un elemento trasmesso.
- **ReadElement** (riga 215) — `private static int ReadElement(ReadOnlySpan<byte> element, ElementType type)`.
- **Clamp16** (riga 228) — `private static ushort Clamp16(long value)`. Limita un valore a 0..65535 (un valore negativo diventa 0, uno oltre 65535 diventa 65535).


## AllSky.Cameras.Ascom (src/AllSky.Cameras.Ascom)

### AscomComCamera.cs

- **AscomComCamera** (riga 20, tipo) — `public sealed class AscomComCamera : ICameraSource`. Camera ASCOM COM con binding tardivo: il driver si crea con il ProgID e i suoi membri si usano tramite `dynamic`, senza pacchetti NuGet ASCOM.
- **AscomComCamera** (riga 57) — `public AscomComCamera(`. Crea la camera.
- **ConnectAsync** (riga 91) — `public async Task ConnectAsync(CancellationToken ct)`. Crea il driver, imposta `Connected = true` e legge le caratteristiche del sensore.
- **CaptureAsync** (riga 135) — `public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken ct)`. Esegue una ripresa: imposta guadagno (limitato a GainMin..GainMax se regolabile), binning 1 e ROI intera, avvia l'esposizione e attende ImageReady con polling.
- **DisposeAsync** (riga 207) — `public async ValueTask DisposeAsync()`. Rilascia il driver: imposta `Connected = false`, libera l'oggetto COM e termina il thread COM.
- **DriverAsync** (riga 226) — `private Task<T> DriverAsync<T>(string operation, CancellationToken ct, Func<dynamic, T> work)`. Esegue una lettura o una funzione sul driver, sul thread COM, con traduzione degli errori.
- **DriverVoidAsync** (riga 233) — `private Task DriverVoidAsync(string operation, CancellationToken ct, Action<dynamic> work)`. Come DriverAsync{T} per le azioni senza valore di ritorno.
- **Guard** (riga 246) — `private static T Guard<T>(string operation, Func<T> action)`. Traduce le eccezioni del driver in InvalidOperationException con messaggio in italiano.
- **PrepareCapture** (riga 258) — `private (int NumX, int NumY, double AppliedGain) PrepareCapture(dynamic d, CameraInfo info, double requestedGain)`.
- **ReadSensorTemp** (riga 278) — `private static double? ReadSensorTemp(dynamic d)`.
- **ReadDriverData** (riga 291) — `private static DriverData ReadDriverData(dynamic d)`.
- **ReadExposureLimitMs** (riga 323) — `private static double ReadExposureLimitMs(Func<double> readSeconds, double unknown)`. Legge un limite di esposizione (secondi) e lo restituisce in millisecondi.
- **ReadGainRange** (riga 336) — `private static (int Min, int Max)? ReadGainRange(dynamic d)`.
- **BuildInfo** (riga 352) — `private static CameraInfo BuildInfo(DriverData data)`. Costruisce CameraInfo dai dati letti dal driver.
- **MapBayer** (riga 381) — `private static BayerPattern MapBayer(int offsetX, int offsetY)`. Mappa gli offset Bayer ASCOM sulla matrice: (0,0)=RGGB, (1,0)=GRBG, (0,1)=GBRG, (1,1)=BGGR.
- **ToRowMajor** (riga 397) — `internal static ushort[] ToRowMajor(Array? image, int width, int height)`. Converte l'array del driver (int[,], con dimensioni [NumX, NumY] oppure [NumY, NumX]) in ushort riga-maggiore.
- **ClampToUShort** (riga 452) — `private static ushort ClampToUShort(int value)`.
- **TryAbortAsync** (riga 462) — `private async Task TryAbortAsync()`.
- **ReleaseDriverAsync** (riga 475) — `private async Task ReleaseDriverAsync()`.
- **ReleaseCom** (riga 504) — `private static void ReleaseCom(object driver)`.
- **ThrowIfDisposed** (riga 521) — `private void ThrowIfDisposed()`.
- **SensorKind** (riga 530, tipo) — `private enum SensorKind`. Tipo di sensore secondo `SensorType` dell'interfaccia ASCOM Camera V3/V4.
- **DriverData** (riga 541, tipo) — `private sealed record DriverData(`. Dati letti dal driver in un'unica chiamata sul thread COM.

### AscomComThread.cs

- **AscomComThread** (riga 14, tipo) — `public sealed class AscomComThread : IDisposable`. Thread dedicato (STA su Windows) che esegue in sequenza tutte le chiamate COM verso un driver ASCOM.
- **RunAsync** (riga 57) — `public async Task<T> RunAsync<T>(`. Esegue `work` sul thread COM e restituisce il risultato.
- **Dispose** (riga 120) — `public void Dispose()`. Chiude la coda, attende fino a 5 secondi la fine del thread e ne rilascia le risorse.
- **ThrowIfDisposed** (riga 140) — `private void ThrowIfDisposed()`.
- **RunLoop** (riga 148) — `private void RunLoop()`.

### AscomDriverCatalog.cs

- **AscomDriverInfo** (riga 9, tipo) — `public sealed record AscomDriverInfo(string ProgId, string Description);`. Driver camera ASCOM registrato nel profilo di sistema.
- **RegistryViewKind** (riga 12, tipo) — `public enum RegistryViewKind`. Vista del registro da leggere: nativa (64 bit) oppure Wow6432Node (driver a 32 bit).
- **RegistrySubKey** (riga 21, tipo) — `public sealed record RegistrySubKey(string Name, string? DefaultValue);`. Una sottochiave del registro con il valore predefinito (null se assente).
- **IRegistryReader** (riga 24, tipo) — `public interface IRegistryReader`. Accesso di sola lettura al registro, iniettabile per i test.
- **WindowsRegistryReader** (riga 40, tipo) — `public sealed class WindowsRegistryReader : IRegistryReader`. Lettura reale del registro di Windows tramite Registry.
- **ListSubKeys** (riga 43) — `public IReadOnlyList<RegistrySubKey> ListSubKeys(RegistryViewKind view, string keyPath)`.
- **AscomDriverCatalog** (riga 74, tipo) — `public sealed class AscomDriverCatalog`. Elenca i driver camera ASCOM registrati (chiave HKLM\SOFTWARE\ASCOM\Camera Drivers, una sottochiave per ProgID).
- **Discover** (riga 89) — `public IReadOnlyList<AscomDriverInfo> Discover()`. Restituisce i driver camera, ordinati per ProgID.
- **CreateDefaultReader** (riga 146) — `private static IRegistryReader? CreateDefaultReader()`.
- **CreateWindowsReader** (riga 157) — `private static IRegistryReader CreateWindowsReader()`.

### IAscomDriverFactory.cs

- **IAscomDriverFactory** (riga 8, tipo) — `public interface IAscomDriverFactory`. Crea l'oggetto driver ASCOM.
- **ComDriverFactory** (riga 20, tipo) — `public sealed class ComDriverFactory : IAscomDriverFactory`. Fabbrica di default: GetTypeFromProgID(string) e CreateInstance(Type).
- **CreateDriver** (riga 23) — `public object CreateDriver(string progId)`.


## AllSky.Cameras.Sim (src/AllSky.Cameras.Sim)

### SimCamera.cs

- **SimCamera** (riga 26, tipo) — `public sealed class SimCamera : ICameraSource`. Camera simulata all-sky: produce frame Bayer a 16 bit da un modello semplificato e deterministico del cielo.
- **readonly** (riga 47) — `private static readonly (double R, double G, double B) DayTint = (0.55, 0.8, 1.0);`. Tinta RGB relativa del cielo diurno (blu).
- **SimCamera** (riga 102) — `public SimCamera(SimCameraOptions options, IClock clock)`. Crea la camera simulata.
- **ConnectAsync** (riga 153) — `public Task ConnectAsync(CancellationToken cancellationToken)`. Connette la camera simulata.
- **CaptureAsync** (riga 172) — `public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)`. Esegue una posa.
- **DisposeAsync** (riga 201) — `public ValueTask DisposeAsync()`.
- **Validate** (riga 207) — `private static void Validate(SimCameraOptions options)`.
- **IsNonNegativeFinite** (riga 261) — `private static bool IsNonNegativeFinite(double v)`.
- **BuildCellChannels** (riga 264) — `private static int[] BuildCellChannels(BayerPattern pattern)`. Canale per ciascuna delle 4 celle Bayer, nell'ordine (0,0), (1,0), (0,1), (1,1).
- **BuildShape** (riga 274) — `private float[] BuildShape()`. Forma spaziale del cielo per ogni pixel: gradiente radiale piu' bagliore cittadino, 0 fuori dal cerchio.
- **BuildStars** (riga 304) — `private void BuildStars()`. Genera il campo stellare: posizioni (AR, Dec) e picchi, deterministici dal seed.
- **SkyRateLog10PerMs** (riga 326) — `private static double SkyRateLog10PerMs(double sunDeg)`. Log10 del flusso di cielo in ADU/ms a guadagno 1x, come funzione dell'altezza del Sole (gradi).
- **ChannelTint** (riga 352) — `private double ChannelTint(int channel, double dayWeight)`. Tinta di un canale: notte = SkyGlowColor, giorno = DayTint, interpolazione lineare con `dayWeight`.
- **Render** (riga 361) — `private ushort[] Render(double exposureMs, double gain, DateTimeOffset now)`. Costruisce il frame: cielo, stelle, bias, rumore, saturazione.
- **DrawStars** (riga 409) — `private void DrawStars(DateTimeOffset now, double starScale)`. Disegna le stelle sopra l'orizzonte nel buffer, con gaussiana sigma 1.2 px.
- **LocalSiderealDeg** (riga 454) — `private static double LocalSiderealDeg(DateTimeOffset now, double lonDeg)`. Tempo sidereo locale medio in gradi, da Greenwich mean sidereal time (formula approssimata).
- **DeriveSeed** (riga 464) — `private int DeriveSeed(DateTimeOffset now)`. Seme del rumore derivato da Seed e dall'istante, deterministico (niente hash randomizzati).
- **NextGaussian** (riga 474) — `private static double NextGaussian(Random rng)`. Campione gaussiano standard (Box-Muller) dal generatore dato.
- **ToRad** (riga 481) — `private static double ToRad(double deg)`.
- **ToDeg** (riga 483) — `private static double ToDeg(double rad)`.

### SimCameraOptions.cs

- **SimCameraOptions** (riga 12, tipo) — `public sealed record SimCameraOptions`. Parametri della camera simulata.


## AllSky.Capture (src/AllSky.Capture)

### AutoExposure.cs

- **AutoExposure** (riga 25, tipo) — `public sealed class AutoExposure`. Controller puro dell'esposizione automatica.
- **AutoExposure** (riga 41) — `public AutoExposure(AutoExposureOptions options, int maxAdu, ExposureSetting initial)`. Crea il controller.
- **Update** (riga 67) — `public ExposureSetting Update(int measuredAdu, SkyPhase phase)`. Calcola la nuova impostazione a partire dalla mediana ADU dell'ultimo frame, che si suppone catturato con Current.
- **Apply** (riga 108) — `private ExposureSetting Apply(double step)`. Applica un passo in logaritmo del segnale, distribuendolo tra esposizione e gain secondo l'ordine delle leve.
- **Snap** (riga 144) — `private static double Snap(double value, double min, double max)`. Limita `value` a [min, max].
- **Fmt** (riga 158) — `private static string Fmt(double value)`. Formato invariante per i messaggi d'errore.
- **Validate** (riga 160) — `private static void Validate(AutoExposureOptions options, int maxAdu)`.

### AutoExposureOptions.cs

- **AutoExposureOptions** (riga 11, tipo) — `public sealed record AutoExposureOptions(double MinExposureMs, double MaxExposureMs, double MinGain, double MaxGain)`. Parametri del controller AutoExposure.

### CaptureLoop.cs

- **CaptureLoop** (riga 26, tipo) — `public sealed class CaptureLoop`. Loop di cattura.
- **CaptureLoop** (riga 70) — `public CaptureLoop(`. Crea il loop.
- **RunAsync** (riga 148) — `public async Task RunAsync(CancellationToken cancellationToken)`. Esegue il loop fino ad annullamento del token.
- **ProduceAsync** (riga 175) — `private async Task ProduceAsync(ChannelWriter<RawFrame> writer, CancellationToken ct)`.
- **ConsumeAsync** (riga 245) — `private async Task ConsumeAsync(ChannelReader<RawFrame> reader, CancellationToken ct)`.
- **UpdateExposure** (riga 256) — `private void UpdateExposure(RawFrame frame)`. Aggiorna l'esposizione dalla mediana del frame, ma solo se il frame usa l'impostazione corrente.
- **IsCurrentSetting** (riga 279) — `private bool IsCurrentSetting(RawFrame frame)`. Verifica se il frame usa l'impostazione corrente.
- **NotifyFrameAsync** (riga 286) — `private async Task NotifyFrameAsync(RawFrame frame, CancellationToken ct)`.
- **TryConnectAsync** (riga 301) — `private async Task TryConnectAsync(CancellationToken ct)`.
- **ClassifySky** (riga 316) — `private SkyPhase ClassifySky(DateTimeOffset utc)`.
- **OnCaptured** (riga 322) — `private void OnCaptured(RawFrame frame)`.
- **RegisterError** (riga 332) — `private int RegisterError()`.
- **SetState** (riga 342) — `private void SetState(CaptureState state)`.
- **BackoffFor** (riga 351) — `private static TimeSpan BackoffFor(int consecutiveErrors)`. Attesa dopo `consecutiveErrors` errori: 1, 2, 4, 8, 16, 32, poi 60 secondi.
- **RaiseError** (riga 359) — `private void RaiseError(Exception ex)`. Notifica un errore.

### CaptureLoopOptions.cs

- **CaptureLoopOptions** (riga 12, tipo) — `public sealed record CaptureLoopOptions(int IntervalSeconds, bool DayCapture, double Latitude, double Longitude)`. Opzioni del CaptureLoop.
- **FromConfig** (riga 18) — `public static CaptureLoopOptions FromConfig(AppConfig config)`. Crea le opzioni dalla configurazione: `Capture` e `Site` di AppConfig.

### CaptureState.cs

- **CaptureState** (riga 4, tipo) — `public enum CaptureState`. Stato del CaptureLoop.

### ExposureSetting.cs

- **ExposureSetting** (riga 8, tipo) — `public readonly record struct ExposureSetting(double ExposureMs, double Gain);`. Impostazione di cattura: tempo di esposizione in millisecondi e gain della camera.

### FrameInfo.cs

- **FrameInfo** (riga 7, tipo) — `public sealed record FrameInfo(DateTimeOffset Timestamp, double ExposureMs, double Gain);`. Informazioni sull'ultimo frame catturato con successo.

### FrameStats.cs

- **FrameStats** (riga 4, tipo) — `public static class FrameStats`. Statistiche semplici sui frame grezzi.
- **MedianAdu** (riga 16) — `public static int MedianAdu(ushort[] pixels, int step)`. Mediana dei valori ADU, calcolata su un campione sottocampionato (un pixel ogni `step`).

### ICaptureDelay.cs

- **ICaptureDelay** (riga 4, tipo) — `public interface ICaptureDelay`. Attesa asincrona usata dal CaptureLoop; iniettabile per i test senza attese reali.

### ITransitWindowSource.cs

- **TransitWindow** (riga 6, tipo) — `public sealed record TransitWindow(DateTimeOffset Start, DateTimeOffset End);`. Finestra di un passaggio previsto, in UTC: da Start a End.
- **ITransitWindowSource** (riga 12, tipo) — `public interface ITransitWindowSource`. Sorgente delle finestre di passaggio, sostituibile nei test.

### SystemCaptureDelay.cs

- **SystemCaptureDelay** (riga 4, tipo) — `public sealed class SystemCaptureDelay : ICaptureDelay`. Implementazione reale di ICaptureDelay basata su Delay(TimeSpan, CancellationToken).
- **DelayAsync** (riga 7) — `public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)`.

### TransitCadenceOptions.cs

- **TransitCadenceOptions** (riga 8, tipo) — `public sealed record TransitCadenceOptions`. Parametri della modalità transito (P4.6).
- **FromConfig** (riga 23) — `public static TransitCadenceOptions FromConfig(AppConfig.CaptureConfig.TransitModeConfig config)`. Crea le opzioni dalla configurazione, limitando i valori a intervalli sicuri (senza eccezioni).
- **ClampOrDefault** (riga 35) — `private static double ClampOrDefault(double value, double min, double max, double fallback)`.

### TransitCadencePolicy.cs

- **TransitCadencePolicy** (riga 17, tipo) — `public sealed class TransitCadencePolicy`. Calcola l'attesa tra due pose durante un passaggio previsto (P4.6).
- **TransitCadencePolicy** (riga 25) — `public TransitCadencePolicy(ITransitWindowSource windows, TransitCadenceOptions options)`. Crea la policy.
- **WaitAfterPose** (riga 58) — `public TimeSpan WaitAfterPose(DateTimeOffset poseStart, DateTimeOffset now, TimeSpan normalInterval, TimeSpan exposure)`. Attesa da `now` fino all'inizio della posa successiva.
- **IsInTransit** (riga 89) — `private bool IsInTransit(DateTimeOffset poseStart, TimeSpan margin)`. True se `poseStart` cade in un passaggio, fino alla fine più il margine.
- **TransitInterval** (riga 96) — `private TimeSpan TransitInterval(TimeSpan normalInterval, TimeSpan minimum)`. Intervallo di transito: frazione dell'intervallo normale, non sotto il minimo e non sopra il normale.
- **ClampToZero** (riga 103) — `private static TimeSpan ClampToZero(TimeSpan value)`.


## AllSky.Core: file non descritti a mano sopra (src/AllSky.Core)

### AppLog.cs

- **AppLog** (riga 6, tipo) — `public static class AppLog`. Scrittura dei log delle app Windows.
- **Append** (riga 11) — `public static void Append(string filePath, string text)`. Aggiunge una voce con data e ora al file indicato, creando le cartelle se mancano.

### AppPaths.cs

- **AppPaths** (riga 10, tipo) — `public sealed record AppPaths(string DataFolder, string ConfigPath, string WebRoot)`. Cartelle e file usati dalle app Windows (Tray e app desktop).

### Calibration/CalibrationFlow.cs

- **CalibrationFlow** (riga 7, tipo) — `public static class CalibrationFlow`. Regole pure del flusso di calibrazione: fase, residui e errore medio.
- **PhaseOf** (riga 10) — `public static CalibrationPhase PhaseOf(bool hasProposal, bool hasAccepted)`. Fase dal fatto che ci sia una proposta in sospeso e un modello accettato.
- **Residuals** (riga 21) — `public static IReadOnlyList<CalibrationResidual> Residuals(FisheyeModel model, IReadOnlyList<CalibrationPoint> points)`. Residuo di ogni punto rispetto al modello.
- **RmsPx** (riga 44) — `public static double RmsPx(IReadOnlyList<CalibrationResidual> residuals)`. Errore radiale quadratico medio dei residui validi, in pixel.
- **IsAcceptableFraction** (riga 63) — `public static bool IsAcceptableFraction(double rmsFractionOfRadius)`. Vero se l'errore frazionario e' sotto la soglia di accettazione (stessa regola di SunCalibrationCollector).

### Calibration/CalibrationManualForm.cs

- **ManualFieldProblem** (riga 7, tipo) — `public enum ManualFieldProblem`. Problema di un campo della modalita' manuale.
- **ManualCalibrationCheck** (riga 28, tipo) — `public sealed record ManualCalibrationCheck(`. Esito della lettura del form manuale.
- **CalibrationManualForm** (riga 45, tipo) — `public static class CalibrationManualForm`. Lettura e validazione dei campi della modalita' manuale: centro x/y, raggio dell'orizzonte in pixel, K2, rotazione del Nord e specchio.
- **Check** (riga 48) — `public static ManualCalibrationCheck Check(`. Legge i campi come testo digitato.
- **Format** (riga 87) — `public static string Format(double value, CultureInfo culture, int decimals = 1)`. Testo di un valore per il campo del form, con la cultura dell'interfaccia.
- **Parse** (riga 90) — `private static (double Value, ManualFieldProblem Problem) Parse(string? text, CultureInfo culture)`.

### Calibration/CalibrationModels.cs

- **PixelPoint** (riga 6, tipo) — `public readonly record struct PixelPoint(double X, double Y);`. Posizione in pixel nel sistema del frame di calibrazione.
- **CalibrationPoint** (riga 9, tipo) — `public readonly record struct CalibrationPoint(DateTimeOffset Utc, double X, double Y, double AltDeg, double AzDeg);`. Osservazione del Sole: posizione nel frame e posizione calcolata in cielo (alt/az).
- **CalibrationResidual** (riga 17, tipo) — `public sealed record CalibrationResidual(CalibrationPoint Observed, PixelPoint? Predicted, double ErrorPx);`. Residuo di un punto rispetto a un modello: dove il modello prevede il Sole e quanto dista dal punto osservato.
- **CalibrationProposal** (riga 28, tipo) — `public sealed record CalibrationProposal(`. Modello proposto in attesa di accettazione, con i residui su tutti i punti.
- **CalibrationPhase** (riga 39, tipo) — `public enum CalibrationPhase`. Fase del flusso di calibrazione.
- **CalibrationSnapshot** (riga 60, tipo) — `public sealed record CalibrationSnapshot(`. Stato completo della calibrazione, letto dall'interfaccia.
- **CalibrationActionResult** (riga 71, tipo) — `public enum CalibrationActionResult`. Esito di un'azione sulla calibrazione.

### Calibration/ICalibrationService.cs

- **ICalibrationService** (riga 14, tipo) — `public interface ICalibrationService`. Flusso di calibrazione ottica visto dall'interfaccia: stato della raccolta, proposta con residui, accetta/rifiuta e modalita' manuale.

### Calibration/OpticalCalibrationConfig.cs

- **OpticalCalibrationSource** (riga 6, tipo) — `public enum OpticalCalibrationSource`. Origine di una calibrazione: fit sulle posizioni del Sole oppure valori inseriti a mano.
- **OpticalCalibrationConfig** (riga 29, tipo) — `public sealed record OpticalCalibrationConfig`. Modello ottico accettato, salvato nella configurazione come sotto-oggetto `optics`.
- **FromModel** (riga 77) — `public static OpticalCalibrationConfig FromModel(`. Crea il sotto-oggetto da un modello.
- **ToModel** (riga 112) — `public FisheyeModel? ToModel()`. Modello fisheye, oppure null se la versione non e' supportata o se i parametri non sono validi (valori non finiti, raggio non positivo, K2 non maggiore di -1/3).

### Calibration/SkyOverlayGeometry.cs

- **CardinalDirection** (riga 6, tipo) — `public enum CardinalDirection`. Direzioni cardinali.
- **GridLine** (riga 21, tipo) — `public sealed record GridLine(bool IsAltitude, double ValueDeg, IReadOnlyList<PixelPoint> Points);`. Linea della griglia alt/az in pixel.
- **CardinalMark** (riga 24, tipo) — `public sealed record CardinalMark(CardinalDirection Direction, PixelPoint Horizon);`. Posizione in pixel di un punto cardinale sull'orizzonte.
- **SkyOverlayGeometry** (riga 30, tipo) — `public static class SkyOverlayGeometry`. Geometria della griglia alt/az sovrapposta all'immagine, calcolata dal FisheyeModel.
- **DefaultGrid** (riga 45) — `public static IReadOnlyList<GridLine> DefaultGrid(FisheyeModel model)`. Griglia predefinita: cerchi di altezza e meridiani.
- **AltitudeCircles** (riga 56) — `public static IReadOnlyList<GridLine> AltitudeCircles(FisheyeModel model, IReadOnlyList<double> altitudesDeg)`. Cerchi a altezza costante, uno per altezza, campionati su tutto l'azimut.
- **Meridians** (riga 77) — `public static IReadOnlyList<GridLine> Meridians(FisheyeModel model, double stepDeg)`. Meridiani a azimut costante, dall'orizzonte (0 gradi) allo zenit (90 gradi).
- **Cardinals** (riga 103) — `public static IReadOnlyList<CardinalMark> Cardinals(FisheyeModel model)`. Posizione dei punti cardinali N, E, S, O sull'orizzonte.
- **AddCardinal** (riga 115) — `private static void AddCardinal(List<CardinalMark> marks, FisheyeModel model, CardinalDirection direction, double azDeg)`.
- **AddProjected** (riga 124) — `private static void AddProjected(`.
- **Flush** (riga 146) — `private static void Flush(List<GridLine> lines, bool isAltitude, double value, List<PixelPoint> current)`.

### CameraView/CameraErrorFormatter.cs

- **CameraErrorKind** (riga 7, tipo) — `public enum CameraErrorKind`. Classe di un errore della camera, dedotta dal testo che l'Engine restituisce.
- **ExposureLimitsText** (riga 45, tipo) — `public sealed record ExposureLimitsText(string DriverMin, string DriverMax, string UsedMin, string UsedMax);`. Limiti di esposizione riportati dal driver e quelli effettivamente usati, come testo dell'Engine.
- **CameraErrorView** (riga 51, tipo) — `public sealed record CameraErrorView(CameraErrorKind Kind, string Message, string? Detail);`. Messaggio leggibile per la vista Camera: classe, testo principale e dettaglio tecnico facoltativo.
- **CameraErrorFormatter** (riga 57, tipo) — `public static class CameraErrorFormatter`. Traduce gli errori dell'Engine (testi in italiano con la causa del driver) in messaggi leggibili IT/EN.
- **Classify** (riga 82) — `public static CameraErrorKind Classify(string? raw)`. Dedotta la classe di errore dal testo dell'Engine.
- **ParseExposureLimits** (riga 145) — `public static ExposureLimitsText? ParseExposureLimits(string? raw)`. Legge i valori dei limiti di esposizione dal messaggio dell'Engine, per esempio "...
- **Describe** (riga 168) — `public static CameraErrorView Describe(string? raw, DesktopTexts texts)`. Messaggio leggibile per un errore dell'Engine.
- **DescribeValidation** (riga 195) — `public static CameraErrorView DescribeValidation(IReadOnlyList<ValidationError> errors, DesktopTexts texts)`. Messaggio per gli errori di validazione della configurazione (`UpdateConfig`).
- **Summarize** (riga 215) — `public static string? Summarize(string? text)`. Riduce un testo tecnico a una sola riga, senza spazi multipli e con lunghezza limitata.
- **ExposureLimitsMessage** (riga 235) — `private static string ExposureLimitsMessage(string? raw, DesktopTexts texts)`.
- **FormatMilliseconds** (riga 253) — `private static string FormatMilliseconds(string token, DesktopTexts texts)`. Valore in millisecondi per la schermata; "NaN", infiniti e valori illeggibili diventano "n/d".
- **ShowsDetail** (riga 264) — `private static bool ShowsDetail(CameraErrorKind kind)`.
- **ContainsAny** (riga 271) — `private static bool ContainsAny(string text, string[] markers)`.

### CameraView/CameraFieldMessages.cs

- **CameraFieldMessages** (riga 4, tipo) — `public static class CameraFieldMessages`. Testo sotto un campo del form delle impostazioni di cattura, nella lingua scelta.
- **For** (riga 9) — `public static string? For(CameraFieldProblem problem, DesktopTexts texts)`. Testo del problema di un campo; null se il campo è valido.

### CameraView/CameraSettingsForm.cs

- **CameraFieldProblem** (riga 6, tipo) — `public enum CameraFieldProblem`. Problema di un campo del form, da tradurre con CameraFieldMessages.
- **CameraSettingsCheck** (riga 32, tipo) — `public sealed record CameraSettingsCheck(`. Esito della validazione del form delle impostazioni di cattura.
- **CameraSettingsForm** (riga 47, tipo) — `public static class CameraSettingsForm`. Validazione e confronto del form "Impostazioni di cattura" (guadagno ed esposizione massima).
- **Check** (riga 56) — `public static CameraSettingsCheck Check(string? gainText, string? maxExposureText, CultureInfo culture)`. Valida i due campi come testo digitato dall'utente.
- **HasChanges** (riga 69) — `public static bool HasChanges(CameraSettingsCheck check, int currentGain, double currentMaxExposureMs)`. Vero se il form è valido e differisce dalla configurazione attuale.
- **FormatExposure** (riga 85) — `public static string FormatExposure(double maxExposureMs, CultureInfo culture)`. Testo di un valore da mostrare nel campo, senza arrotondare l'esposizione.
- **FormatGain** (riga 93) — `public static string FormatGain(int gain)`. Testo del guadagno da mostrare nel campo.
- **CheckGain** (riga 95) — `private static (int? Value, CameraFieldProblem Problem) CheckGain(string? text, CultureInfo culture)`.
- **CheckExposure** (riga 121) — `private static (double? Value, CameraFieldProblem Problem) CheckExposure(string? text, CultureInfo culture)`.
- **TryParseNumber** (riga 143) — `private static bool TryParseNumber(string text, CultureInfo culture, out double value)`. Prova prima la cultura dell'interfaccia, poi il punto decimale invariante.

### CameraView/CameraSources.cs

- **CameraSourceChoice** (riga 4, tipo) — `public enum CameraSourceChoice`. Filtro per sorgente dell'elenco delle camere nella vista "Camera".
- **CameraSources** (riga 23, tipo) — `public static class CameraSources`. Collega il tipo di camera restituito dall'Engine (`CameraCandidate.Kind`, `StatusDto.Camera.Kind`) al filtro della vista e alle sue etichette.
- **ChoiceOf** (riga 45) — `public static CameraSourceChoice? ChoiceOf(string? kind)`. Filtro corrispondente a un tipo di camera; null per un tipo sconosciuto.
- **Matches** (riga 56) — `public static bool Matches(CameraSourceChoice choice, string? kind)`. Vero se una camera di tipo `kind` appartiene al filtro `choice`.
- **ChoiceLabel** (riga 64) — `public static string ChoiceLabel(CameraSourceChoice choice, DesktopTexts texts)`. Etichetta di un filtro, nella lingua scelta.
- **KindLabel** (riga 81) — `public static string KindLabel(string? kind, DesktopTexts texts)`. Etichetta del tipo di una camera; per un tipo sconosciuto, il testo "sconosciuto".
- **EmptyHint** (riga 92) — `public static string? EmptyHint(CameraSourceChoice choice, DesktopTexts texts)`. Suggerimento da mostrare quando il filtro non trova camere; null se non ce n'è uno specifico.

### ConfigPeek.cs

- **ConfigPeek** (riga 9, tipo) — `public static class ConfigPeek`. Lettura minima di config.json per l'avvio delle app Windows: solo lingua e porta, senza passare dal modello completo.
- **Read** (riga 19) — `public static (bool English, int Port) Read(string configPath)`. Legge lingua e porta da config.json.

### DataFolderResolver.cs

- **DataFolderResolver** (riga 11, tipo) — `public static class DataFolderResolver`. Calcolo puro della cartella dati del Tray e del nome del mutex di istanza unica.
- **Resolve** (riga 30) — `public static string Resolve(string[]? args, string? environmentValue, string defaultFolder)`. Sceglie la cartella dati in quest'ordine: argomento `--data-folder`, variabile `ALLSKY_DATA_FOLDER`, `defaultFolder`.
- **SingleInstanceMutexName** (riga 54) — `public static string SingleInstanceMutexName(string dataFolder, string defaultFolder)`. Nome del mutex di istanza unica legato alla cartella dati.
- **ReadArgument** (riga 68) — `private static string? ReadArgument(string[]? args)`. Primo valore valido tra gli argomenti `--data-folder X` o `--data-folder=X`, o null.
- **TryMakeAbsolute** (riga 114) — `private static string? TryMakeAbsolute(string? value)`. Restituisce il percorso assoluto, o null se il valore è vuoto o non è un percorso valido.
- **NormalizeForIdentity** (riga 133) — `private static string NormalizeForIdentity(string path)`. Percorso assoluto, senza separatore finale e in minuscolo: base del confronto e dell'hash.

### Design/Palette.cs

- **Palette** (riga 15, tipo) — `public sealed record Palette(`. Palette dei colori dell'interfaccia, un valore per ruolo (specifica in docs/ralph/IMPLEMENTATION_PLAN.md §5.3).
- **ContrastRatio** (riga 113) — `public static double ContrastRatio(string fgHex, string bgHex)`. Rapporto di contrasto WCAG 2.x tra due colori esadecimali, da 1 a 21.
- **Scaled** (riga 129) — `public Palette Scaled(double factor)`. Restituisce una copia con tutti i canali RGB moltiplicati per `factor`, arrotondati e limitati a 0–255.
- **ParseHex** (riga 140) — `public static (byte R, byte G, byte B) ParseHex(string hex)`. Legge un colore "#RRGGBB" (o "RRGGBB") e restituisce i tre canali.
- **Entries** (riga 173) — `public IReadOnlyList<KeyValuePair<string, string>> Entries()`. Coppie (nome del ruolo, colore) nell'ordine della dichiarazione.
- **Map** (riga 201) — `private Palette Map(Func<string, string> transform)`. Applica una trasformazione a ogni colore della palette e restituisce la nuova palette.
- **ScaleHex** (riga 227) — `private static string ScaleHex(string hex, double factor)`. Moltiplica i canali di un colore per il fattore, con arrotondamento e limiti 0–255.
- **Scale** (riga 233) — `private static byte Scale(byte channel, double factor)`.
- **FormatHex** (riga 236) — `private static string FormatHex(byte r, byte g, byte b)`.
- **RelativeLuminance** (riga 240) — `private static double RelativeLuminance(byte r, byte g, byte b)`. Luminanza relativa sRGB (WCAG 2.x): linearizza ogni canale e somma i pesi 0.2126, 0.7152, 0.0722.
- **LinearChannel** (riga 243) — `private static double LinearChannel(byte channel)`.

### Design/StarfieldGenerator.cs

- **Star** (riga 12, tipo) — `public sealed record Star(double X, double Y, double Magnitude, double Radius, double Alpha);`. Una stella dello sfondo, in pixel dell'area di disegno.
- **StarfieldOptions** (riga 17, tipo) — `public sealed record StarfieldOptions`. Parametri dello sfondo stellare.
- **StarfieldGenerator** (riga 55, tipo) — `public static class StarfieldGenerator`. Genera le stelle di uno sfondo statico.
- **Generate** (riga 66) — `public static IReadOnlyList<Star> Generate(int seed, int width, int height, StarfieldOptions? options = null)`. Genera le stelle per un'area di `width`×`height` pixel.
- **MakeStar** (riga 128) — `private static Star MakeStar(SplitMix64 rng, double x, double y, StarfieldOptions options)`. Magnitudine, raggio e opacità di una stella alla posizione indicata.
- **SampleMagnitude** (riga 142) — `internal static double SampleMagnitude(double u, StarfieldOptions options)`. Campiona la magnitudine con la distribuzione inversa di N(<m) ∝ 10^(LogSlope·m) su [MinMagnitude, MaxMagnitude].
- **Validate** (riga 151) — `private static void Validate(StarfieldOptions options)`.
- **SplitMix64** (riga 175, tipo) — `private sealed class SplitMix64`. Generatore SplitMix64: piccolo, veloce e identico su ogni piattaforma.
- **NextUInt64** (riga 181) — `public ulong NextUInt64()`.
- **NextDouble** (riga 191) — `public double NextDouble()`. Numero in [0, 1).
- **NextGaussian** (riga 194) — `public double NextGaussian()`. Gaussiana standard con Box-Muller.

### DesktopTexts.cs

- **DesktopTexts** (riga 14, tipo) — `public sealed record DesktopTexts`. Testi dell'app desktop: finestra, icona nell'area di notifica e messaggi.
- **ViewName** (riga 2271) — `public string ViewName(ShellView view)`. Nome della vista nella barra laterale e come titolo della schermata.
- **EmptyText** (riga 2285) — `public string EmptyText(ShellView view)`. Stato vuoto curato di una vista segnaposto.
- **StateName** (riga 2299) — `public string StateName(string state)`. Traduce lo stato del servizio ("idle", "capturing", "error") nel testo della lingua corrente.
- **FormatLastFrame** (riga 2312) — `public string FormatLastFrame(string? frameId, DateTimeOffset? timestamp)`. Testo dell'ultimo frame: identificativo e ora locale (HH:mm:ss).

### Events/EventFormatter.cs

- **EventDataRow** (riga 9, tipo) — `public sealed record EventDataRow(string Label, string Value, string? Tooltip = null);`. Una riga della tabella dei dati di un evento.
- **EventFormatter** (riga 15, tipo) — `public static class EventFormatter`. Testi e formati della vista Eventi: nomi di tipo, classe ed etichetta, ore, angoli e tabella dei dati.
- **CategoryName** (riga 21) — `public static string CategoryName(EventCategory category, DesktopTexts texts)`. Nome del tipo di evento.
- **ClassName** (riga 34) — `public static string ClassName(EventClassKind kind, DesktopTexts texts)`. Nome della classe assegnata dal classificatore.
- **LabelName** (riga 48) — `public static string LabelName(EventLabelChoice label, DesktopTexts texts)`. Nome dell'etichetta dell'utente.
- **OutcomeName** (riga 60) — `public static string OutcomeName(TransitOutcome outcome, DesktopTexts texts)`. Nome dell'esito della conferma di un transito.
- **ListCaption** (riga 72) — `public static string ListCaption(EventListItem item, DesktopTexts texts)`. Titolo di una riga della galleria: tipo, ora locale e punteggio, per esempio "Meteora · 21:04:33 · 3.25".
- **FormatLocalDateTime** (riga 82) — `public static string FormatLocalDateTime(DateTimeOffset utc)`. Data e ora locali, "yyyy-MM-dd HH:mm:ss".
- **FormatLocalTime** (riga 86) — `public static string FormatLocalTime(DateTimeOffset utc)`. Sola ora locale, "HH:mm:ss".
- **FormatUtc** (riga 90) — `public static string FormatUtc(DateTimeOffset utc)`. Istante UTC per il tooltip, "yyyy-MM-dd HH:mm:ss UTC".
- **FormatScore** (riga 94) — `public static string FormatScore(double score, DesktopTexts texts)`. Punteggio con due decimali, per esempio "3.25".
- **FormatLength** (riga 101) — `public static string FormatLength(double lengthPx, DesktopTexts texts)`. Lunghezza in pixel del frame ridotto, con un decimale e l'unità, per esempio "61.5 px".
- **FormatLuma** (riga 108) — `public static string FormatLuma(double value, DesktopTexts texts)`. Luminosità con un decimale, senza unità perché è in livelli a 8 bit.
- **FormatSkyPoint** (riga 115) — `public static string FormatSkyPoint(SkyPoint? point, DesktopTexts texts)`. Altezza e azimut con un decimale e il simbolo dei gradi, per esempio "41.3° / 183.5°".
- **PreferredImagePath** (riga 130) — `public static string? PreferredImagePath(EventDetail detail)`. Immagine principale del dettaglio: la composita per un transito, il ritaglio ingrandito per una scia.
- **DataRows** (riga 142) — `public static IReadOnlyList<EventDataRow> DataRows(EventDetail detail, DesktopTexts texts)`. Tabella dei dati dell'evento.

### Events/EventListRules.cs

- **EventListRules** (riga 6, tipo) — `public static class EventListRules`. Filtro, ordinamento ed elenco delle notti per la galleria Eventi.
- **Matches** (riga 11) — `public static bool Matches(EventListItem item, EventFilter filter)`. Vero se la riga rispetta il filtro.
- **Filter** (riga 23) — `public static IReadOnlyList<EventListItem> Filter(IEnumerable<EventListItem> items, EventFilter filter)`. Righe che rispettano il filtro, nell'ordine di Sort.
- **Sort** (riga 33) — `public static IReadOnlyList<EventListItem> Sort(IEnumerable<EventListItem> items)`. Ordina dal più recente al più vecchio.
- **Nights** (riga 45) — `public static IReadOnlyList<string> Nights(IEnumerable<EventListItem> items)`. Notti presenti nelle righe, dalla più recente, senza duplicati.

### Events/EventModels.cs

- **EventCategory** (riga 4, tipo) — `public enum EventCategory`. Tipo di evento mostrato nella vista Eventi.
- **EventClassKind** (riga 17, tipo) — `public enum EventClassKind`. Classe assegnata dal classificatore a un evento.
- **EventLabelChoice** (riga 36, tipo) — `public enum EventLabelChoice`. Etichetta data dall'utente.
- **TransitOutcome** (riga 49, tipo) — `public enum TransitOutcome`. Esito della conferma di un transito nelle immagini.
- **SkyPoint** (riga 64, tipo) — `public sealed record SkyPoint(double AltDeg, double AzDeg);`. Posizione sul cielo in gradi.
- **EventFilter** (riga 69, tipo) — `public sealed record EventFilter(string? NightId = null, EventCategory? Category = null);`. Filtro della vista Eventi.
- **EventListItem** (riga 79, tipo) — `public sealed record EventListItem(`. Riga della galleria: un evento senza i dettagli.
- **TransitInfo** (riga 98, tipo) — `public sealed record TransitInfo(`. Dati di un passaggio di satellite, presenti solo per i transiti.
- **EventDetail** (riga 127, tipo) — `public sealed record EventDetail(`. Dettaglio di un evento: dati e percorsi dei file, già risolti e verificati.

### Events/IEventBrowser.cs

- **IEventBrowser** (riga 14, tipo) — `public interface IEventBrowser`. Accesso della vista Eventi all'archivio delle meteore, dei bolidi e dei transiti.

### IFrameProcessor.cs

- **ProcessedFrame** (riga 12, tipo) — `public sealed record ProcessedFrame(`. Risultato dell'elaborazione di un frame grezzo: immagine pronta per archivio e interfaccia.
- **IFrameProcessor** (riga 23, tipo) — `public interface IFrameProcessor`. Trasforma un frame grezzo in immagine elaborata.

### IJpegEncoder.cs

- **IJpegEncoder** (riga 4, tipo) — `public interface IJpegEncoder`. Codifica JPEG di un'immagine RGB a 8 bit (3 byte per pixel, righe contigue, ordine R,G,B).

### Live/LiveFrameDataFormatter.cs

- **FrameDataField** (riga 6, tipo) — `public enum FrameDataField`. Campi del pannello dati del frame, nell'ordine in cui compaiono.
- **LiveFrameData** (riga 34, tipo) — `public sealed record LiveFrameData(`. Dati grezzi del frame per il pannello della vista Live.
- **FrameDataItem** (riga 52, tipo) — `public sealed record FrameDataItem(FrameDataField Field, string Label, string Value, bool Available, string? Tooltip);`. Una riga del pannello dati, già formattata nella lingua scelta.
- **LiveFrameDataFormatter** (riga 58, tipo) — `public static class LiveFrameDataFormatter`. Formatta il pannello dati del frame della vista Live.
- **Format** (riga 66) — `public static IReadOnlyList<FrameDataItem> Format(LiveFrameData data, DesktopTexts texts)`. Formatta tutte le righe del pannello, sempre nello stesso ordine di FrameDataField.
- **FormatExposure** (riga 88) — `public static string? FormatExposure(double? milliseconds, CultureInfo culture)`. Esposizione: millisecondi sotto il secondo ("0.5 ms", "120 ms"), secondi sopra ("30.0 s").
- **FormatGain** (riga 104) — `public static string? FormatGain(double? gain, CultureInfo culture)`. Gain senza unità, con al massimo due decimali.
- **FormatMedian** (riga 112) — `public static string? FormatMedian(int? median, CultureInfo culture)`. Mediana ADU intera con l'unità "ADU".
- **FormatTemperature** (riga 120) — `public static string? FormatTemperature(double? celsius, CultureInfo culture)`. Temperatura con un decimale e unità, per esempio "-10,5 °C".
- **FormatTime** (riga 130) — `private static FrameDataItem FormatTime(LiveFrameData data, DesktopTexts texts)`.
- **Known** (riga 145) — `private static FrameDataItem Known(FrameDataField field, string label, string? value, DesktopTexts texts)`.
- **Missing** (riga 150) — `private static FrameDataItem Missing(FrameDataField field, string label, DesktopTexts texts)`.
- **IsNonNegative** (riga 155) — `private static bool IsNonNegative(double? value)`.

### Live/ZoomPan.cs

- **ZoomPanState** (riga 8, tipo) — `public readonly record struct ZoomPanState(double Zoom, double OffsetX, double OffsetY);`. Stato di zoom e spostamento della vista Live.
- **ZoomPan** (riga 19, tipo) — `public static class ZoomPan`. Matematica di zoom e trascinamento per la vista Live.
- **FitContent** (riga 44) — `public static (double Width, double Height) FitContent(double imageWidth, double imageHeight, double viewWidth, double viewHeight)`. Dimensione dell'immagine adattata alla vista, con le proporzioni originali.
- **WheelFactor** (riga 59) — `public static double WheelFactor(int wheelDelta)`. Fattore di zoom di una tacca della rotella.
- **ZoomAt** (riga 75) — `public static ZoomPanState ZoomAt(`. Applica un fattore di zoom mantenendo fermo il punto sotto il cursore (o al centro dei pulsanti).
- **PanBy** (riga 107) — `public static ZoomPanState PanBy(`. Sposta la vista di (`deltaX`, `deltaY`) pixel, limitando i bordi.
- **Clamp** (riga 128) — `public static ZoomPanState Clamp(`. Limita lo zoom a 1–8 e gli spostamenti in modo che l'immagine copra la vista, oppure resti centrata se è più piccola.
- **Axis** (riga 145) — `private static double Axis(double offset, double size, double view)`.
- **IsPositive** (riga 158) — `private static bool IsPositive(double value)`.

### LogView/LogFiles.cs

- **LogFiles** (riga 6, tipo) — `public static class LogFiles`. Accesso ai file di log sul disco.
- **LatestDailyLog** (riga 19) — `public static string? LatestDailyLog(string logsFolder)`. File giornaliero più recente nella cartella dei log, o null se non ce n'è.
- **ReadTailLines** (riga 42) — `public static IReadOnlyList<string> ReadTailLines(string path, int maxBytes)`. Ultime righe di un file, leggendo al più `maxBytes` dalla fine.

### LogView/LogRecords.cs

- **LogLevel** (riga 7, tipo) — `public enum LogLevel`. Gravità di una voce, stimata dal testo (il log dell'Host non scrive il livello).
- **LogLevelFilter** (riga 20, tipo) — `public enum LogLevelFilter`. Filtro per gravità della vista Log.
- **LogRecord** (riga 36, tipo) — `public sealed record LogRecord(DateTime? Timestamp, LogLevel Level, string Text);`. Voce di log: istante (se leggibile), gravità stimata e testo già mascherato, con le righe di continuazione.
- **LogParser** (riga 43, tipo) — `public static class LogParser`. Lettura delle righe di log: raggruppa le righe di continuazione (tracce delle eccezioni) con la voce che le precede, stima la gravità, maschera i segreti.
- **Parse** (riga 63) — `public static IReadOnlyList<LogRecord> Parse(IEnumerable<string> lines)`. Raggruppa le righe in voci.
- **Classify** (riga 114) — `public static LogLevel Classify(string text)`. Gravità stimata dal testo.
- **ContainsAny** (riga 129) — `private static bool ContainsAny(string text, string[] words)`.
- **LogFilter** (riga 144, tipo) — `public static class LogFilter`. Filtro e taglio delle voci per la vista Log.
- **Apply** (riga 151) — `public static IReadOnlyList<LogRecord> Apply(IEnumerable<LogRecord> records, LogLevelFilter level, string? query, int maxRecords)`. Voci che passano il filtro, le ultime `maxRecords` in ordine cronologico.
- **Passes** (riga 172) — `public static bool Passes(LogLevel level, LogLevelFilter filter)`. Vero se la gravità passa il filtro.

### LogView/LogSecretMask.cs

- **LogSecretMask** (riga 16, tipo) — `public static class LogSecretMask`. Maschera i segreti nel testo dei log prima che arrivino alla UI o agli appunti.
- **Apply** (riga 55) — `public static string Apply(string? text)`. Restituisce il testo con i segreti mascherati.
- **ReplaceValue** (riga 71) — `private static string ReplaceValue(Match match)`.

### Nights/FrameNavigation.cs

- **FrameNavigation** (riga 4, tipo) — `public static class FrameNavigation`. Spostamento tra i frame di una notte con le frecce.
- **Step** (riga 14) — `public static int Step(int current, int delta, int frameCount)`. Frame successivo o precedente, limitato alla notte.

### Nights/KeogramScrubber.cs

- **KeogramScrubber** (riga 14, tipo) — `public static class KeogramScrubber`. Converte la posizione orizzontale sul keogram in un frame della notte e viceversa.
- **FractionAt** (riga 19) — `public static double FractionAt(double x, double width)`. Frazione orizzontale (0 a sinistra, 1 a destra) di un punto della barra.
- **FrameIndexAt** (riga 32) — `public static int FrameIndexAt(double fraction, int frameCount)`. Indice del frame (da 0) corrispondente alla frazione.
- **FractionOfFrame** (riga 51) — `public static double? FractionOfFrame(int index, int frameCount)`. Frazione al centro della colonna di un frame, per disegnare il cursore.

### Nights/NightFrameFormatter.cs

- **NightFrameText** (riga 11, tipo) — `public sealed record NightFrameText(string Position, string Time, string TimeTooltip, string Phase);`. Testi della barra informativa del frame selezionato.
- **NightFrameFormatter** (riga 14, tipo) — `public static class NightFrameFormatter`. Formatta le informazioni del frame selezionato.
- **Format** (riga 22) — `public static NightFrameText Format(int index, int count, DateTimeOffset timestamp, string phase, DesktopTexts texts)`. Formatta il frame selezionato.
- **PhaseName** (riga 35) — `private static string PhaseName(string phase, DesktopTexts texts)`.

### Nights/NightSummary.cs

- **NightTimelapseState** (riga 6, tipo) — `public enum NightTimelapseState`. Stato del timelapse di una notte, come lo mostra la vista "Notti".
- **NightTimelapseStates** (riga 25, tipo) — `public static class NightTimelapseStates`. Conversione del campo `timelapse` di `GET /api/nights` nello stato della vista.
- **FromWire** (riga 29) — `public static NightTimelapseState FromWire(string? wire)`. Converte "none", "ready", "generating" o "failed"; qualsiasi altro valore diventa Unknown.
- **NightSummary** (riga 49, tipo) — `public sealed record NightSummary(`. Dati di una notte copiati dall'Engine, senza dipendere dai suoi DTO.
- **NightRow** (riga 64, tipo) — `public sealed record NightRow(string Id, string Title, string Subtitle, NightTimelapseState Timelapse, string TimelapseLabel);`. Una riga dell'elenco notti, già nella lingua scelta.
- **NightRowFormatter** (riga 67, tipo) — `public static class NightRowFormatter`. Formatta le righe dell'elenco notti.
- **Format** (riga 72) — `public static NightRow Format(NightSummary night, DesktopTexts texts)`. Formatta una notte per l'elenco.
- **FormatDate** (riga 92) — `public static string FormatDate(string id, CultureInfo culture)`. Data di una notte in formato esteso, es.
- **TimelapseLabel** (riga 105) — `public static string TimelapseLabel(NightTimelapseState state, DesktopTexts texts)`. Etichetta dello stato del timelapse.
- **FormatSpan** (riga 119) — `private static string FormatSpan(DateTimeOffset? first, DateTimeOffset? last)`.

### Nights/NightsViewStates.cs

- **NightsPanelState** (riga 4, tipo) — `public enum NightsPanelState`. Stato della colonna dell'elenco notti.
- **NightsViewStates** (riga 20, tipo) — `public static class NightsViewStates`. Sceglie lo stato della vista "Notti".
- **Resolve** (riga 29) — `public static NightsPanelState Resolve(bool loaded, int nightCount, bool hasError)`. Risolve lo stato.

### Nights/TimelapseControls.cs

- **TimelapseControlState** (riga 10, tipo) — `public sealed record TimelapseControlState(bool CanPlay, bool CanRegenerate, bool IsBusy, string StatusText);`. Stato dei comandi del timelapse di una notte.
- **TimelapseControls** (riga 13, tipo) — `public static class TimelapseControls`. Decide quali comandi del timelapse sono attivi e che testo mostrare.
- **Resolve** (riga 20) — `public static TimelapseControlState Resolve(NightTimelapseState state, int frames, double? jobProgress, DesktopTexts texts)`. Stato dei comandi per una notte.
- **FormatProgress** (riga 42) — `public static string FormatProgress(double? progress, DesktopTexts texts)`. Avanzamento in testo.

### Passes/IPassForecastService.cs

- **IPassForecastService** (riga 7, tipo) — `public interface IPassForecastService`. Previsione dei passaggi di satelliti per la UI (P4.3).

### Passes/PassForecastModels.cs

- **PassForecastStatus** (riga 4, tipo) — `public enum PassForecastStatus`. Stato della previsione dei passaggi, come lo vede la UI.
- **PassChangeKind** (riga 23, tipo) — `public enum PassChangeKind`. Tipo di cambiamento notificato da PassChanged.
- **PassTrackBlock** (riga 36, tipo) — `public enum PassTrackBlock`. Motivo per cui le tracce previste non sono disponibili.
- **PassForecastItem** (riga 64, tipo) — `public sealed record PassForecastItem(`. Un passaggio previsto.
- **PassForecastSnapshot** (riga 88, tipo) — `public sealed record PassForecastSnapshot(`. Previsione corrente.
- **PassChange** (riga 100, tipo) — `public sealed record PassChange(PassChangeKind Kind, PassForecastItem? Pass, DateTimeOffset AtUtc);`. Cambiamento della previsione.
- **PassTrackPoint** (riga 106, tipo) — `public sealed record PassTrackPoint(DateTimeOffset Utc, double X, double Y);`. Punto di una traccia prevista nei pixel del frame di calibrazione.
- **PassTrackPath** (riga 113, tipo) — `public sealed record PassTrackPath(`. Traccia prevista di un passaggio: uno o più tratti visibili nel cielo.
- **PassTrackState** (riga 127, tipo) — `public sealed record PassTrackState(`. Tracce previste per l'immagine Live.

### Passes/PassFormatter.cs

- **CompassPoint** (riga 7, tipo) — `public enum CompassPoint`. Punto cardinale a otto settori, per la direzione di un passaggio.
- **PassRowData** (riga 40, tipo) — `public sealed record PassRowData(string Target, string When, string Times, string Details, bool InProgress);`. Una riga dell'elenco "Prossimi passaggi", già formattata.
- **PassPanelData** (riga 45, tipo) — `public sealed record PassPanelData(string? Hint, IReadOnlyList<PassRowData> Rows);`. Contenuto della carta "Prossimi passaggi": un messaggio facoltativo e le righe.
- **PassFormatter** (riga 50, tipo) — `public static class PassFormatter`. Trasforma la previsione in testi e righe per la UI.
- **Compass** (riga 59) — `public static CompassPoint Compass(double azimuthDeg)`. Punto cardinale dell'azimut (da nord verso est).
- **CompassName** (riga 72) — `public static string CompassName(CompassPoint point, DesktopTexts texts)`. Nome del punto cardinale nella lingua scelta.
- **Direction** (riga 86) — `public static string Direction(PassForecastItem pass, DesktopTexts texts)`. Direzione del passaggio: "da NO a SE".
- **Light** (riga 97) — `public static string Light(PassForecastItem pass, DesktopTexts texts)`. Condizioni di luce: visibile a occhio nudo, illuminato con cielo non buio, oppure in ombra.
- **Times** (riga 109) — `public static string Times(PassForecastItem pass, TimeZoneInfo tz, DesktopTexts texts)`. Orari di inizio, culminazione e fine nel fuso del sito: "inizio 21:03 · culmine 21:07 · fine 21:11".
- **Row** (riga 122) — `public static PassRowData Row(PassForecastItem pass, DateTimeOffset now, TimeZoneInfo tz, DesktopTexts texts)`. Una riga dell'elenco per un passaggio, riferita all'istante `now`.
- **Panel** (riga 142) — `public static PassPanelData Panel(`. Contenuto della carta "Prossimi passaggi".

### Publication/IPublishSettingsService.cs

- **IPublishSettingsService** (riga 24, tipo) — `public interface IPublishSettingsService`. Accesso della vista Pubblicazione alle impostazioni e allo stato dell'Uplink.

### Publication/PublicationFieldMessages.cs

- **PublicationField** (riga 6, tipo) — `public enum PublicationField`. Campi del modulo Uplink, per collegare un errore del server al campo giusto.
- **PublicationFieldMessages** (riga 40, tipo) — `public static class PublicationFieldMessages`. Messaggi dei campi del modulo Uplink, nella lingua scelta.
- **FieldOf** (riga 47) — `public static PublicationField FieldOf(string? path)`. Campo del modulo corrispondente al percorso camelCase restituito dal validatore.
- **NameOf** (riga 64) — `public static string NameOf(PublicationField field, DesktopTexts texts)`. Nome del campo, come appare nell'etichetta.
- **IssueLine** (riga 89) — `public static string IssueLine(PublicationIssue issue, DesktopTexts texts)`. Riga di un errore di salvataggio.
- **ForNumber** (riga 110) — `public static string? ForNumber(PublicationNumberProblem problem, int min, int max, DesktopTexts texts)`. Messaggio sotto un campo numerico; null se il campo è valido.

### Publication/PublicationModels.cs

- **PublicationState** (riga 7, tipo) — `public enum PublicationState`. Stato della pubblicazione come lo legge la UI.
- **PublicationSettingsSnapshot** (riga 32, tipo) — `public sealed record PublicationSettingsSnapshot(`. Impostazioni correnti, senza segreti.
- **PublicationSettingsChange** (riga 52, tipo) — `public sealed record PublicationSettingsChange(`. Modifiche da salvare o da provare: sono i valori completi del modulo più le istruzioni sui segreti.
- **ToString** (riga 67) — `public override string ToString()`. Riassunto senza valori dei segreti.
- **PublicationIssue** (riga 76, tipo) — `public sealed record PublicationIssue(string Path, string Message);`. Problema di un campo restituito dal salvataggio.
- **PublicationSaveResult** (riga 81, tipo) — `public sealed record PublicationSaveResult(bool Ok, IReadOnlyList<PublicationIssue> Issues)`. Esito del salvataggio.
- **PublicationTestResult** (riga 91, tipo) — `public sealed record PublicationTestResult(bool Ok, string Code, string Message, int? StatusCode);`. Esito della prova di connessione.
- **PublicationStatusSnapshot** (riga 103, tipo) — `public sealed record PublicationStatusSnapshot(`. Stato visibile all'utente.

### Publication/PublicationSettingsForm.cs

- **PublicationNumberProblem** (riga 6, tipo) — `public enum PublicationNumberProblem`. Problema di un campo numerico del modulo Uplink.
- **PublicationNumberCheck** (riga 24, tipo) — `public sealed record PublicationNumberCheck(int? Value, PublicationNumberProblem Problem);`. Esito della lettura di un campo numerico.
- **PublicationFormCheck** (riga 27, tipo) — `public sealed record PublicationFormCheck(`. Esito della validazione dei campi numerici del modulo.
- **PublicationSettingsForm** (riga 44, tipo) — `public static class PublicationSettingsForm`. Logica del modulo "Impostazioni Uplink": lettura dei campi numerici, rilevamento delle modifiche e pulizia del testo.
- **Check** (riga 78) — `public static PublicationFormCheck Check(string? intervalText, string? longEdgeText, string? jpegQualityText, CultureInfo culture)`. Valida i tre campi numerici come testo digitato.
- **CheckInteger** (riga 93) — `public static PublicationNumberCheck CheckInteger(string? text, int min, int max, CultureInfo culture)`. Legge un intero dentro un intervallo.
- **HasChanges** (riga 124) — `public static bool HasChanges(PublicationSettingsChange change, PublicationSettingsSnapshot current)`. Vero se la modifica cambia qualcosa rispetto alle impostazioni correnti.
- **Trim** (riga 145) — `public static string Trim(string? text)`. Testo senza spazi ai bordi; vuoto se null.
- **TrimOrNull** (riga 149) — `public static string? TrimOrNull(string? text)`. Testo senza spazi ai bordi, oppure null se vuoto: come il server, un campo vuoto è assente.

### Publication/PublicationStatusFormatter.cs

- **PublicationStatusFormatter** (riga 10, tipo) — `public static class PublicationStatusFormatter`. Testi leggibili dello stato dell'Uplink: stato, contatori, ultimo invio, ultimo errore ed esito della prova.
- **StateText** (riga 15) — `public static string StateText(PublicationState state, DesktopTexts texts)`. Testo dello stato.
- **Counters** (riga 33) — `public static string Counters(PublicationStatusSnapshot status, DesktopTexts texts)`. Riga dei contatori: in coda, inviate, scartate.
- **LastSuccess** (riga 45) — `public static string LastSuccess(DateTimeOffset? lastSuccessUtc, DateTimeOffset now, DesktopTexts texts)`. Riga dell'ultimo invio riuscito.
- **NextAttempt** (riga 58) — `public static string? NextAttempt(DateTimeOffset? nextAttemptUtc, DateTimeOffset now, DesktopTexts texts)`. Riga del prossimo tentativo; null se non è programmato.
- **LastError** (riga 71) — `public static string? LastError(PublicationStatusSnapshot status, DateTimeOffset now, DesktopTexts texts)`. Riga dell'ultimo errore; null se non ce ne sono.
- **Reason** (riga 93) — `public static string Reason(string? code, int? httpStatus, DesktopTexts texts)`. Motivo leggibile di un codice di errore ("auth", "network", ...).
- **TestText** (riga 117) — `public static string TestText(PublicationTestResult result, DesktopTexts texts)`. Esito della prova di connessione, in una riga.
- **SecretStatus** (riga 130) — `public static string SecretStatus(bool hasValue, DesktopTexts texts)`. Testo dello stato di un segreto: impostato o non impostato.
- **TimeText** (riga 143) — `public static string TimeText(DateTimeOffset utc, DateTimeOffset now)`. Orario di un evento: "HH:mm" se è di oggi (ora locale), altrimenti "dd/MM HH:mm".

### Settings/AutostartCommand.cs

- **AutostartCommand** (riga 7, tipo) — `public static class AutostartCommand`. Valore della chiave di avvio automatico di Windows (HKCU\...\Run).
- **For** (riga 18) — `public static string For(string exePath)`. Riga di comando da registrare: il percorso dell'eseguibile tra virgolette.

### Settings/LanPassword.cs

- **LanPasswordProblem** (riga 7, tipo) — `public enum LanPasswordProblem`. Esito del controllo di una password per la rete locale.
- **LanPassword** (riga 23, tipo) — `public static class LanPassword`. Password per l'accesso dalla rete locale all'interfaccia web.
- **Check** (riga 37) — `public static LanPasswordProblem Check(string? password)`. Controlla la password prima di calcolarne l'hash.
- **Hash** (riga 50) — `public static string Hash(string password)`. Calcola l'hash da salvare in `ui.lanPasswordHash`.
- **Verify** (riga 68) — `public static bool Verify(string? password, string? storedHash)`. Verifica una password contro l'hash salvato.
- **TryReadBasicPassword** (riga 105) — `public static bool TryReadBasicPassword(string? authorizationHeader, out string password)`. Legge la password da un'intestazione `Authorization: Basic ...` (utente qualsiasi, password dopo i due punti).
- **Derive** (riga 134) — `private static byte[] Derive(string password, byte[] salt, int iterations)`.

### Settings/SiteForm.cs

- **SiteFieldProblem** (riga 6, tipo) — `public enum SiteFieldProblem`. Problema di un campo della posizione dell'osservatorio.
- **SiteCheck** (riga 34, tipo) — `public sealed record SiteCheck(`. Esito della validazione della posizione dell'osservatorio.
- **SiteForm** (riga 54, tipo) — `public static class SiteForm`. Validazione del form "Posizione dell'osservatorio": latitudine -90..90, longitudine -180..180 (est positiva), quota -500..9000 m (dal Mar Morto alle quote alpine e andine).
- **Check** (riga 67) — `public static SiteCheck Check(string? latitudeText, string? longitudeText, string? elevationText, CultureInfo culture)`. Valida i tre campi come testo digitato.
- **Format** (riga 80) — `public static string Format(double value, CultureInfo culture)`. Testo di un valore da mostrare nel campo, con la cultura dell'interfaccia e senza arrotondare.
- **HasChanges** (riga 91) — `public static bool HasChanges(SiteCheck check, double latitude, double longitude, double elevation)`. Vero se la posizione valida differisce da quella attuale.
- **Differs** (riga 104) — `private static bool Differs(double a, double b)`.
- **CheckRange** (riga 106) — `private static (double? Value, SiteFieldProblem Problem) CheckRange(`.
- **TryParse** (riga 128) — `private static bool TryParse(string text, CultureInfo culture, out double value)`.

### Shell/ShellNavigation.cs

- **ShellNavigation** (riga 11, tipo) — `public static class ShellNavigation`. Ordine delle viste nella barra laterale e scorciatoie da tastiera.
- **TryGetShortcut** (riga 49) — `public static bool TryGetShortcut(int number, out ShellView view)`. Traduce il numero di una scorciatoia (1..7) nella vista corrispondente.
- **ShortcutNumber** (riga 63) — `public static int ShortcutNumber(ShellView view)`. Numero della scorciatoia della vista (1..7), oppure 0 se la vista non ne ha una.
- **Move** (riga 75) — `public static ShellView Move(ShellView current, int step)`. Vista successiva (`step` positivo) o precedente (negativo) nella barra laterale.

### Shell/ShellView.cs

- **ShellView** (riga 6, tipo) — `public enum ShellView`. Viste della shell principale, nell'ordine della barra laterale (docs/ralph/IMPLEMENTATION_PLAN.md §5.2).

### Shell/StatusBarFormatter.cs

- **StatusBarFormatter** (riga 14, tipo) — `public static class StatusBarFormatter`. Trasforma i dati grezzi della barra di stato in voci già formattate nella lingua scelta.
- **Format** (riga 20) — `public static StatusBarText Format(StatusBarSnapshot snapshot, DesktopTexts texts, DateTimeOffset? now = null)`. Formatta la barra di stato.
- **FormatNextPass** (riga 46) — `private static StatusBarItem FormatNextPass(StatusBarSnapshot snapshot, DesktopTexts texts, DateTimeOffset now)`. Transito: "ISS · tra 2 h 14 min", oppure "ISS in corso".
- **ParsePhase** (riga 64) — `public static StatusPhase ParsePhase(string? phase)`. Traduce la fase del servizio ("day", "twilight", "night") in StatusPhase.
- **PhaseName** (riga 75) — `public static string PhaseName(StatusPhase phase, DesktopTexts texts)`. Nome della fase nella lingua scelta.
- **FormatDegrees** (riga 86) — `public static string? FormatDegrees(double? degrees, CultureInfo culture)`. Altezza in gradi con un decimale e segno, per esempio "-12,3°".
- **FormatCamera** (riga 96) — `private static StatusBarItem FormatCamera(StatusBarSnapshot snapshot, DesktopTexts texts)`.
- **FormatAltitude** (riga 112) — `private static StatusBarItem FormatAltitude(string label, double? degrees, DesktopTexts texts)`.
- **FormatLastFrame** (riga 118) — `private static StatusBarItem FormatLastFrame(DateTimeOffset? timestamp, DesktopTexts texts)`.
- **FormatDisk** (riga 129) — `private static StatusBarItem FormatDisk(double? freeGb, DesktopTexts texts)`.
- **FormatPublications** (riga 140) — `private static StatusBarItem FormatPublications(PublicationStatus status, DesktopTexts texts)`.
- **Known** (riga 153) — `private static StatusBarItem Known(string label, string value)`.
- **Missing** (riga 155) — `private static StatusBarItem Missing(string label, DesktopTexts texts)`.

### Shell/StatusBarModel.cs

- **StatusPhase** (riga 4, tipo) — `public enum StatusPhase`. Fase del cielo mostrata nella barra di stato.
- **PublicationStatus** (riga 20, tipo) — `public enum PublicationStatus`. Stato delle pubblicazioni (Uplink e YouTube).
- **StatusBarSnapshot** (riga 50, tipo) — `public sealed record StatusBarSnapshot(`. Dati della barra di stato così come arrivano dal servizio.
- **StatusBarItem** (riga 70, tipo) — `public sealed record StatusBarItem(string Label, string Value, bool Available, string? Tooltip);`. Una voce della barra di stato, già formattata nella lingua scelta.
- **StatusBarText** (riga 84, tipo) — `public sealed record StatusBarText(`. Barra di stato completa, nell'ordine in cui compare in basso nella finestra.

### Tonight/MoonPhaseGlyph.cs

- **MoonPhaseName** (riga 6, tipo) — `public enum MoonPhaseName`. Nome della fase lunare, usato per scegliere il testo.
- **GlyphPoint** (riga 19, tipo) — `public readonly record struct GlyphPoint(double X, double Y);`. Punto del contorno del glifo nel quadrato [-1, 1] x [-1, 1].
- **MoonPhaseGlyph** (riga 29, tipo) — `public sealed record MoonPhaseGlyph(`. Glifo della fase lunare come dati geometrici: frazione illuminata, verso (crescente o calante), nome della fase e contorno della parte illuminata.
- **At** (riga 53) — `public static MoonPhaseGlyph At(DateTimeOffset utc, double latDeg)`. Glifo della Luna nell'istante indicato.
- **Classify** (riga 66) — `public static MoonPhaseName Classify(double fraction, bool waxing)`. Nome della fase da frazione illuminata e verso.
- **Outline** (riga 95) — `public static IReadOnlyList<GlyphPoint> Outline(double fraction, bool litOnRight, int steps = 48)`. Contorno della parte illuminata.

### Tonight/TonightCalculator.cs

- **TonightSnapshot** (riga 12, tipo) — `public sealed record TonightSnapshot(`. Risultato del calcolo della vista "Stanotte" in un istante.
- **TonightUpcoming** (riga 24, tipo) — `public sealed record TonightUpcoming(`. Esito di un evento previsto dalla vista: il prossimo cambio di fase e i riferimenti futuri.
- **TonightTransit** (riga 32, tipo) — `public sealed record TonightTransit(string Target, DateTimeOffset StartUtc);`. Transito previsto di un satellite: nome e istante di inizio.
- **TonightCalculator** (riga 38, tipo) — `public static class TonightCalculator`. Calcola la notte della vista "Stanotte": fasi e altezze con `AllSky.Astro`, glifo lunare.
- **Compute** (riga 52) — `public static TonightSnapshot Compute(`. Calcola la notte che contiene `now`: da mezzogiorno a mezzogiorno locale, come definisce ObservingNight.
- **Sample** (riga 82) — `public static IReadOnlyList<TonightSample> Sample(`. Altezze del Sole e della Luna da `from` a `to`, con l'ultimo campione sul bordo.
- **AtInstant** (riga 94) — `private static TonightSample AtInstant(DateTimeOffset t, double latDeg, double lonDeg)`.
- **TonightSite** (riga 99, tipo) — `public static class TonightSite`. Risoluzione del fuso orario del sito, con lo stesso comportamento dell'Engine.
- **ResolveTimeZone** (riga 104) — `public static TimeZoneInfo ResolveTimeZone(string? id)`. Fuso orario per identificativo IANA o Windows.

### Tonight/TonightFormatter.cs

- **TonightStatusLine** (riga 10, tipo) — `public sealed record TonightStatusLine(string Label, string Value, bool Problem);`. Una riga della carta "Stato della cattura", già formattata nella lingua scelta.
- **TonightFormatter** (riga 16, tipo) — `public static class TonightFormatter`. Formattazione dei dati della vista "Stanotte".
- **FormatClock** (riga 25) — `public static string FormatClock(DateTimeOffset utc, TimeZoneInfo tz, CultureInfo culture)`. Orario locale HH:mm del fuso indicato.
- **FormatAltitude** (riga 34) — `public static string FormatAltitude(double degrees, CultureInfo culture)`. Altezza in gradi con segno e un decimale, per esempio "+12.3°" o "-0.8°".
- **FormatDuration** (riga 44) — `public static string FormatDuration(TimeSpan span, DesktopTexts texts)`. Durata leggibile: "2 h 14 min", "14 min" oppure "meno di 1 min".
- **FormatCountdown** (riga 68) — `public static string FormatCountdown(DateTimeOffset now, DateTimeOffset at, DesktopTexts texts)`. Conto alla rovescia verso `at`, con la preposizione: "tra 2 h 14 min".
- **PhaseName** (riga 79) — `public static string PhaseName(SkyPhase phase, DesktopTexts texts)`. Nome della fase del cielo nella lingua scelta.
- **MoonName** (riga 90) — `public static string MoonName(MoonPhaseName name, DesktopTexts texts)`. Nome della fase lunare nella lingua scelta.
- **FormatIllumination** (riga 104) — `public static string FormatIllumination(double fraction, CultureInfo culture)`. Percentuale di disco illuminato, da 0 a 100, senza decimali: "47%".
- **CaptureLines** (riga 116) — `public static IReadOnlyList<TonightStatusLine> CaptureLines(`. Righe della carta "Stato della cattura" a partire dallo stato del servizio.
- **TonightCaptureInput** (riga 154, tipo) — `public sealed record TonightCaptureInput(`. Dati dello stato di cattura necessari alla carta.

### Tonight/TonightGuard.cs

- **TonightGuard** (riga 4, tipo) — `internal static class TonightGuard`. Controlli sugli argomenti geografici della vista Stanotte (stessi limiti di AllSky.Astro).
- **CheckLatLon** (riga 6) — `public static void CheckLatLon(double latDeg, double lonDeg)`.

### Tonight/TonightTimeline.cs

- **TonightSample** (riga 9, tipo) — `public sealed record TonightSample(DateTimeOffset Utc, double SunAltitudeDeg, double MoonAltitudeDeg);`. Campione dell'altezza del Sole e della Luna su un intervallo della notte.
- **TonightSegment** (riga 15, tipo) — `public sealed record TonightSegment(SkyPhase Phase, DateTimeOffset From, DateTimeOffset To);`. Fascia continua della stessa fase dentro la finestra tramonto-alba.
- **TonightPhaseChange** (riga 20, tipo) — `public sealed record TonightPhaseChange(SkyPhase To, DateTimeOffset AtUtc);`. Cambio di fase del cielo: da questo istante vale `To`.
- **TonightTimeline** (riga 32, tipo) — `public sealed record TonightTimeline(`. Linea temporale dalla fine del giorno (tramonto) all'inizio del giorno successivo (alba) della notte osservativa.
- **Build** (riga 49) — `public static TonightTimeline Build(IReadOnlyList<PhaseBand> bands)`. Costruisce la linea dalle fasi della notte.
- **WithSamples** (riga 99) — `public TonightTimeline WithSamples(IReadOnlyList<TonightSample> samples)`. Stessa linea con i campioni di altezza indicati.
- **ProgressAt** (riga 109) — `public double? ProgressAt(DateTimeOffset utc)`. Posizione di un istante nella finestra tramonto-alba, da 0 (tramonto) a 1 (alba).
- **NextChangeAfter** (riga 125) — `public TonightPhaseChange? NextChangeAfter(DateTimeOffset utc)`. Primo cambio di fase strettamente dopo l'istante indicato, o null se non ce ne sono più.

### YouTube/IYouTubeService.cs

- **IYouTubeService** (riga 18, tipo) — `public interface IYouTubeService`. Accesso della vista YouTube al caricamento automatico.
- **YouTubeProblem** (riga 56, tipo) — `public enum YouTubeProblem`. Problemi che la vista può mostrare.
- **YouTubeLastUpload** (riga 103, tipo) — `public sealed record YouTubeLastUpload(string NightId, string VideoId, string VideoUrl, DateTimeOffset UploadedUtc);`. Esito dell'ultimo caricamento riuscito, per il link al video.
- **YouTubeStatus** (riga 113, tipo) — `public sealed record YouTubeStatus(`. Stato del caricamento YouTube.
- **YouTubeSettingsSnapshot** (riga 127, tipo) — `public sealed record YouTubeSettingsSnapshot(string DailyTime, string Privacy, IReadOnlyList<string> Tags, string CategoryId);`. Impostazioni modificabili dalla vista.
- **YouTubeSettingsChange** (riga 130, tipo) — `public sealed record YouTubeSettingsChange(string DailyTime, string Privacy, IReadOnlyList<string> Tags, string CategoryId);`. Modifica proposta dalla vista.
- **YouTubeSettingsProblem** (riga 133, tipo) — `public enum YouTubeSettingsProblem`. Campo non valido nel salvataggio delle impostazioni.
- **YouTubeSaveResult** (riga 144, tipo) — `public sealed record YouTubeSaveResult(bool Ok, IReadOnlyList<YouTubeSettingsProblem> Problems);`. Esito del salvataggio.
- **YouTubeLoginResult** (riga 149, tipo) — `public sealed record YouTubeLoginResult(bool Ok, YouTubeProblem? Problem);`. Esito del login.
- **YouTubeUploadOutcomeKind** (riga 152, tipo) — `public enum YouTubeUploadOutcomeKind`. Esito di un caricamento richiesto a mano.
- **YouTubeUploadOutcome** (riga 173, tipo) — `public sealed record YouTubeUploadOutcome(YouTubeUploadOutcomeKind Kind, string? VideoId);`. Esito di UploadLastNightNowAsync.

### YouTube/YouTubeLinks.cs

- **YouTubeLinks** (riga 4, tipo) — `public static class YouTubeLinks`. Link ai video caricati.
- **IsValidVideoId** (riga 10) — `public static bool IsValidVideoId(string? id)`. Vero se l'id contiene solo lettere, cifre, "-" e "_" (lunghezza da 1 a 32).
- **WatchUrl** (riga 16) — `public static string? WatchUrl(string? videoId)`. Link del video, o null se l'id non è valido.

### YouTube/YouTubeSettingsRules.cs

- **YouTubeSettingsRules** (riga 9, tipo) — `public static class YouTubeSettingsRules`. Regole pure delle impostazioni YouTube: formato dell'ora, privacy, tag e categoria.
- **TryParseDailyTime** (riga 21) — `public static bool TryParseDailyTime(string? text, out TimeOnly time)`. Legge un'ora "HH:mm" in formato invariante.
- **FormatDailyTime** (riga 29) — `public static string FormatDailyTime(TimeOnly time)`. Ora nel formato "HH:mm".
- **IsKnownPrivacy** (riga 32) — `public static bool IsKnownPrivacy(string? value)`. Vero se la privacy è uno dei tre valori dell'API.
- **IsValidCategoryId** (riga 35) — `public static bool IsValidCategoryId(string? value)`. L'ID categoria è un numero: almeno una cifra, solo cifre ASCII.
- **ParseTags** (riga 39) — `public static IReadOnlyList<string> ParseTags(string? text)`. Tag da un campo di testo separato da virgole.
- **FormatTags** (riga 50) — `public static string FormatTags(IEnumerable<string> tags)`. Tag come testo separato da virgole, per il campo della vista.
- **Validate** (riga 53) — `public static IReadOnlyList<YouTubeSettingsProblem> Validate(YouTubeSettingsChange change)`. Problemi della modifica proposta.
- **ToSnapshot** (riga 82) — `public static YouTubeSettingsSnapshot ToSnapshot(AppConfig.YouTubeConfig config)`. Impostazioni modificabili, lette dalla configurazione.
- **Apply** (riga 92) — `public static AppConfig.YouTubeConfig Apply(AppConfig.YouTubeConfig current, YouTubeSettingsChange change)`. Applica una modifica già validata.
- **ValidateConfig** (riga 114) — `public static IReadOnlyList<ValidationError> ValidateConfig(AppConfig.YouTubeConfig config)`. Validazione completa della sezione in configurazione, per l'avvio del caricamento.

### YouTube/YouTubeStatusFormatter.cs

- **YouTubeStatusFormatter** (riga 9, tipo) — `public static class YouTubeStatusFormatter`. Testi della vista YouTube: stato, problemi, esiti dei caricamenti ed errori di campo.
- **ConnectionText** (riga 14) — `public static string ConnectionText(bool connected, DesktopTexts texts)`. Riga di collegamento dell'account.
- **AutomaticText** (riga 21) — `public static string AutomaticText(bool enabled, DesktopTexts texts)`. Riga del caricamento automatico.
- **NextUploadText** (riga 28) — `public static string NextUploadText(DateTimeOffset? nextUtc, DesktopTexts texts)`. Prossimo caricamento in ora locale del PC; se non è programmato, la riga dedicata.
- **LastUploadText** (riga 37) — `public static string LastUploadText(YouTubeLastUpload? last, DesktopTexts texts)`. Ultimo caricamento riuscito: notte e data di registrazione.
- **ProblemText** (riga 46) — `public static string? ProblemText(YouTubeProblem? problem, DesktopTexts texts)`. Testo di un problema.
- **LastProblemLine** (riga 70) — `public static string? LastProblemLine(YouTubeProblem? problem, DesktopTexts texts)`. Riga "Ultimo problema: ..." per la vista.
- **OutcomeText** (riga 77) — `public static string OutcomeText(YouTubeUploadOutcomeKind kind, DesktopTexts texts)`. Testo dell'esito di un caricamento richiesto a mano.
- **IsErrorOutcome** (riga 99) — `public static bool IsErrorOutcome(YouTubeUploadOutcomeKind kind)`. Vero se l'esito è un errore da segnalare in rosso (il resto è neutro o positivo).
- **CanForceRetry** (riga 108) — `public static bool CanForceRetry(YouTubeUploadOutcomeKind kind)`. Vero se, dopo questo esito, la conferma dell'utente può sbloccare il caricamento della notte.
- **SettingsProblemText** (riga 113) — `public static string SettingsProblemText(YouTubeSettingsProblem problem, DesktopTexts texts)`. Testo di un campo non valido.
- **PrivacyName** (riga 127) — `public static string PrivacyName(string privacy, DesktopTexts texts)`. Nome della privacy nella lingua scelta.
- **LocalText** (riga 139) — `private static string LocalText(DateTimeOffset utc)`.


## AllSky.Desktop (src/AllSky.Desktop)

### App.cs

- **App** (riga 20, tipo) — `public sealed class App : System.Windows.Application`. Applicazione WPF senza XAML.
- **OnStartup** (riga 56) — `protected override void OnStartup(System.Windows.StartupEventArgs e)`.
- **ApplySavedTheme** (riga 97) — `private void ApplySavedTheme()`. Applica la modalità notte rossa salvata in `ui.theme`.
- **ShowMainWindow** (riga 110) — `public void ShowMainWindow()`. Mostra la finestra principale: dall'icona o dal doppio clic.
- **ExitApplication** (riga 127) — `public void ExitApplication()`. Esce davvero dal menu "Esci": rimuove l'icona, arresta l'host (con timeout) e chiude l'applicazione.
- **OnExit** (riga 136) — `protected override void OnExit(System.Windows.ExitEventArgs e)`.
- **StopAll** (riga 146) — `private void StopAll()`. Rimuove l'icona e arresta l'host.
- **StopHost** (riga 166) — `private void StopHost()`. Ferma e libera l'host.
- **OnDispatcherUnhandledException** (riga 184) — `private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)`. Registra l'eccezione dell'interfaccia e impedisce la chiusura immediata.

### MainWindow.cs

- **MainWindow** (riga 20, tipo) — `internal sealed class MainWindow : System.Windows.Window`. Finestra principale: sfondo nero, stato del servizio, camera e ultimo frame.
- **PrepareForExit** (riga 60) — `public void PrepareForExit()`. Smette di ascoltare il servizio e consente la chiusura vera della finestra.
- **OnPreviewKeyDown** (riga 73) — `private void OnPreviewKeyDown(object sender, KeyEventArgs e)`. Scorciatoie e frecce della shell.
- **OnClosing** (riga 82) — `private void OnClosing(object? sender, CancelEventArgs e)`. Chiusura dalla X: la finestra si nasconde.
- **OnStatusChanged** (riga 94) — `private void OnStatusChanged(StatusDto status)`. Evento dal servizio (thread di cattura): rimanda l'aggiornamento al thread dell'interfaccia.
- **PostToUi** (riga 100) — `private void PostToUi(Action update)`. Esegue l'aggiornamento sul thread dell'interfaccia, se la finestra è ancora collegata al servizio.
- **Render** (riga 117) — `private void Render(StatusDto status)`. Scrive lo stato nella shell.

### Overlay/WpfFrameOverlay.cs

- **WpfFrameOverlay** (riga 21, tipo) — `public sealed class WpfFrameOverlay : IFrameOverlay`. Maschera informativa del timelapse per il Desktop.
- **WpfFrameOverlay** (riga 40) — `public WpfFrameOverlay(OverlayLayout layout, Language language, string logoFolder, IJpegEncoder encoder)`.
- **CreateFactory** (riga 53) — `public static Func<AppConfig, IFrameOverlay?> CreateFactory(string dataFolder)`. Fabbrica per `HostOptions.FrameOverlayFactory`: a ogni job legge il layout dalla configurazione ricevuta.
- **InvalidaCacheLoghiAsync** (riga 66) — `public static Task InvalidaCacheLoghiAsync()`. Svuota la cache dei loghi del renderer condiviso, sul thread STA.
- **ApplyAsync** (riga 70) — `public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)`.
- **Compone** (riga 83) — `private byte[] Compone(WpfOverlayRenderer renderer, byte[] jpeg, FrameSidecar sidecar)`. Esegue sul thread STA: decodifica, maschera e ricodifica un frame.
- **DecodificaBgra** (riga 92) — `private static (byte[] Bgra, int Larghezza, int Altezza) DecodificaBgra(byte[] jpeg)`. JPEG in pixel BGRA a 8 bit, senza padding.
- **BgraARgb** (riga 109) — `private static byte[] BgraARgb(byte[] bgra, int larghezza, int altezza)`. Da BGRA (opaco) a RGB a 8 bit, come si aspetta IJpegEncoder.

### Overlay/WpfOverlayRenderer.cs

- **OverlayRenderResult** (riga 14, tipo) — `public sealed record OverlayRenderResult(byte[] Bgra, TimeSpan Elapsed, IReadOnlyList<string> Avvisi);`. Esito di un render: pixel BGRA, tempo misurato e avvisi (per esempio loghi mancanti).
- **OverlayTimingReport** (riga 17, tipo) — `public sealed record OverlayTimingReport(TimeSpan Mediana, TimeSpan Minimo, TimeSpan Massimo, int Esecuzioni);`. Statistiche di più render sullo stesso frame, per verificare il budget.
- **WpfOverlayRenderer** (riga 32, tipo) — `public sealed class WpfOverlayRenderer`. Esegue una OverlayScene su un frame con WPF: `DrawingVisual` per il disegno e `RenderTargetBitmap` per i pixel.
- **Render** (riga 49) — `public OverlayRenderResult Render(byte[] bgra, int larghezza, int altezza, OverlayScene scena, string? cartellaLoghi)`. Compone la scena sul frame e restituisce i pixel per l'encoder.
- **Measure** (riga 105) — `public OverlayTimingReport Measure(byte[] bgra, int larghezza, int altezza, OverlayScene scena, string? cartellaLoghi, int esecuzioni = 10)`. Misura il tempo di render: un esecuzione di riscaldamento, poi `esecuzioni` misure con Stopwatch.
- **SvuotaCacheLoghi** (riga 121) — `public void SvuotaCacheLoghi()`. Svuota la cache dei loghi (per esempio dopo la pulizia della cartella loghi).
- **EntroBudget** (riga 124) — `public static bool EntroBudget(TimeSpan tempo)`. Vero se il tempo di un frame resta sotto il budget.
- **Pennello** (riga 126) — `private SolidColorBrush Pennello(OverlayColore colore)`.
- **ToRect** (riga 138) — `private static Rect ToRect(OverlayRect area)`.
- **DisegnaMaschera** (riga 140) — `private static void DisegnaMaschera(DrawingContext dc, OverlayMascheraCerchio maschera, int larghezza, int altezza)`.
- **DisegnaTesto** (riga 173) — `private void DisegnaTesto(DrawingContext dc, OverlayTestoPosizionato testo)`.
- **DisegnaLogo** (riga 197) — `private void DisegnaLogo(DrawingContext dc, OverlayImmagine immagine, string? cartellaLoghi, List<string> avvisi)`.
- **CaricaLogo** (riga 242) — `private BitmapSource? CaricaLogo(string percorso)`. Carica un PNG con BitmapDecoder.
- **OverlayStaWorker** (riga 271, tipo) — `public sealed class OverlayStaWorker : IDisposable`. Thread STA dedicato al WpfOverlayRenderer.
- **OverlayStaWorker** (riga 279) — `public OverlayStaWorker()`.
- **EseguiAsync** (riga 287) — `public Task<T> EseguiAsync<T>(Func<WpfOverlayRenderer, T> lavoro)`. Esegue `lavoro` sul thread STA con il renderer condiviso.
- **Ciclo** (riga 306) — `private void Ciclo()`.
- **Dispose** (riga 315) — `public void Dispose()`. Chiude la coda e attende brevemente la fine del thread.

### PassForecastWiring.cs

- **PassForecastWiring** (riga 13, tipo) — `internal static class PassForecastWiring`. Dipendenze reali della previsione dei passaggi (P4.3): sorgente CelesTrak e propagatore SGP.NET.
- **CreateTleFetcher** (riga 16) — `public static ITleFetcher CreateTleFetcher(string dataFolder)`. Crea la sorgente TLE con cache nella cartella dati.

### Program.cs

- **Program** (riga 27, tipo) — `internal static class Program`. Punto di ingresso di All Sky Brain (app WPF).
- **Main** (riga 37) — `private static int Main(string[] args)`. Avvia All Sky Brain.
- **ReportCrash** (riga 114) — `internal static void ReportCrash(AppPaths paths, DesktopTexts texts, Exception ex)`. Registra un'eccezione non gestita nel crash log e mostra un messaggio (una sola volta alla volta).

### Shell/ShellControl.cs

- **ShellControl** (riga 28, tipo) — `internal sealed class ShellControl : Grid`. Shell della finestra principale: barra laterale con le icone delle viste, area centrale e barra di stato in basso.
- **Update** (riga 181) — `public void Update(StatusDto status)`. Aggiorna la barra di stato e il banner di errore con lo stato del servizio.
- **TryHandleKey** (riga 199) — `public bool TryHandleKey(KeyEventArgs e)`. Gestisce le scorciatoie della shell.
- **CreateNavItem** (riga 241) — `private RadioButton CreateNavItem(ShellView view, Style style)`. Crea una voce della barra laterale: icona vettoriale, nome e suggerimento con la scorciatoia.
- **ShowView** (riga 294) — `private void ShowView(ShellView view)`. Mostra la vista indicata e riallinea la selezione della barra laterale.
- **FocusNav** (riga 314) — `private void FocusNav(ShellView view)`. Sposta il fuoco sulla voce della barra laterale della vista indicata.
- **FocusedNav** (riga 317) — `private ShellView? FocusedNav()`. Vista della voce che ha il fuoco da tastiera, o null se il fuoco è altrove.
- **ShortcutNumber** (riga 331) — `private static int ShortcutNumber(Key key)`. Numero della scorciatoia (1..7) per il tasto premuto con Ctrl, oppure 0.
- **ToSnapshot** (riga 347) — `private static StatusBarSnapshot ToSnapshot(StatusDto status, string? passTarget, DateTimeOffset? passStart, bool passInProgress)`. Dallo stato del servizio alla barra di stato.
- **NextPass** (riga 361) — `private (string? Target, DateTimeOffset? Start, bool InProgress) NextPass()`. Passaggio in corso, o prossimo, per la barra di stato (P4.3).
- **CreateView** (riga 373) — `private static UIElement? CreateView(ShellView view, DesktopTexts texts, ThemeManager theme,`.

### Shell/ShellIcons.cs

- **ShellIcons** (riga 13, tipo) — `internal static class ShellIcons`. Icone della shell come geometrie vettoriali (Path).
- **For** (riga 59) — `internal static Geometry For(ShellView view)`. Icona della vista corrispondente alla voce della barra laterale.
- **ForPhase** (riga 74) — `internal static Geometry ForPhase(StatusPhase phase)`. Icona della fase del cielo nella barra di stato.
- **Build** (riga 82) — `private static Geometry Build(string data)`.

### Shell/StatusBarPanel.cs

- **StatusBarPanel** (riga 20, tipo) — `internal sealed class StatusBarPanel : Border`. Barra di stato in basso: camera, fase, Sole, Luna, ultimo frame, disco, pubblicazioni e il pulsante della notte rossa.
- **StatusBarPanel** (riga 28) — `public StatusBarPanel(DesktopTexts texts, ThemeManager theme)`. Crea la barra.
- **Show** (riga 61) — `public void Show(StatusBarText text)`. Sostituisce le voci con quelle della formattazione corrente.
- **CreateItem** (riga 80) — `private static StackPanel CreateItem(StatusBarItem item, System.Windows.Media.Geometry icon)`. Una voce: icona, etichetta e valore.

### Theme/Illustrations.cs

- **IllustrationKind** (riga 11, tipo) — `public enum IllustrationKind`. Stati vuoti che hanno una illustrazione (P1.10).
- **Illustrations** (riga 36, tipo) — `public static class Illustrations`. Illustrazioni vettoriali originali per gli stati vuoti, disegnate con Path e Geometry in una griglia 160×120.
- **Create** (riga 50) — `public static UIElement Create(IllustrationKind kind, DesktopTexts texts)`. Crea lo stato vuoto: disegno, titolo e testo esplicativo nella lingua di `texts`.
- **TextsFor** (riga 97) — `private static (string Title, string Body) TextsFor(IllustrationKind kind, DesktopTexts texts)`.
- **BuildDrawing** (riga 107) — `private static Canvas BuildDrawing(IllustrationKind kind)`. Disegno come Canvas 160×120 con i suoi tratti.
- **DrawStroke** (riga 133, tipo) — `private readonly record struct DrawStroke(string Data, string Role, double Width, bool Dashed = false);`. Un tratto: percorso, ruolo del colore nel tema, spessore e se è tratteggiato.
- **StrokesFor** (riga 135) — `private static DrawStroke[] StrokesFor(IllustrationKind kind)`.

### Theme/StarfieldBackground.cs

- **StarfieldBackground** (riga 19, tipo) — `public sealed class StarfieldBackground : FrameworkElement`. Sfondo stellare statico per le schermate senza immagine del cielo (P1.10).
- **StarfieldBackground** (riga 41) — `public StarfieldBackground()`. Crea lo sfondo.
- **GetVisualChild** (riga 78) — `protected override Visual GetVisualChild(int index)`.
- **OnRenderSizeChanged** (riga 89) — `protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)`.
- **OnResizeDelayElapsed** (riga 104) — `private void OnResizeDelayElapsed(object? sender, EventArgs e)`.
- **OnLoaded** (riga 110) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 121) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnThemeChanged** (riga 131) — `private void OnThemeChanged(object? sender, EventArgs e)`.
- **Regenerate** (riga 134) — `private void Regenerate()`. Ricalcola le stelle per la dimensione attuale.
- **Redraw** (riga 146) — `private void Redraw()`. Disegna le stelle con il colore del tema corrente.
- **EnsureBrushes** (riga 160) — `private void EnsureBrushes(Color color)`. Pennelli per i livelli di opacità, nel colore indicato.
- **ResolveStarColor** (riga 178) — `private Color ResolveStarColor()`. Colore del ruolo Text del tema corrente: in modalità notte rossa le stelle diventano rosse.

### Theme/ThemeManager.cs

- **ThemeManager** (riga 22, tipo) — `public sealed class ThemeManager`. Traduce la Palette in risorse WPF e le aggiorna quando cambiano la modalità notte o la luminosità.
- **readonly** (riga 54) — `private static readonly (string Key, double Value)[] Sizes =`. Scala tipografica (§5.3): 12 / 14 / 16 / 20 / 28 / 40.
- **readonly** (riga 65) — `private static readonly (string Key, double Value)[] Spaces =`. Scala di spaziatura (§5.3): 4 / 8 / 12 / 16 / 24 / 32 / 48.
- **ThemeManager** (riga 82) — `public ThemeManager(ResourceDictionary resources)`. Crea il gestore e scrive subito tutte le risorse nel dizionario indicato.
- **SetNightMode** (riga 105) — `public void SetNightMode(bool enabled)`. Attiva o disattiva la modalità notte rossa.
- **ToggleNightMode** (riga 118) — `public void ToggleNightMode()`. Inverte la modalità notte rossa (scorciatoia Ctrl+R).
- **SetBrightness** (riga 124) — `public void SetBrightness(double factor)`. Imposta la luminosità dell'interfaccia.
- **ApplyPalette** (riga 140) — `private void ApplyPalette()`. Scrive i colori della palette corrente nelle risorse, aggiornando i pennelli esistenti.
- **SetBrush** (riga 157) — `private void SetBrush(string key, Color color)`. Aggiorna il pennello con la chiave indicata.
- **WriteFixedResources** (riga 169) — `private void WriteFixedResources()`. Scrive le risorse che non cambiano con il tema: font, scala tipografica e di spaziatura.
- **EmbeddedOrSystemFont** (riga 189) — `private static FontFamily EmbeddedOrSystemFont(string fileName, string embeddedFamily, string fallback)`. Font incorporato nell'exe se il file è presente nel pacchetto, altrimenti famiglia di sistema (fallback).
- **RaiseThemeChanged** (riga 208) — `private void RaiseThemeChanged()`.
- **ToColor** (riga 211) — `private static Color ToColor(string hex)`. Converte "#RRGGBB" in Color.

### TrayIcon.cs

- **TrayIcon** (riga 24, tipo) — `internal sealed class TrayIcon : IDisposable`. Icona nell'area di notifica con menu: apri la finestra, apri il pannello nel browser, controlla gli aggiornamenti, avvio automatico con Windows, esci.
- **TrayIcon** (riga 57) — `public TrayIcon(AllSkyHostRuntime runtime, AppPaths paths, DesktopTexts texts, Action showWindow, Action exit)`. Crea l'icona e il menu.
- **Dispose** (riga 115) — `public void Dispose()`. Rimuove l'icona dall'area di notifica.
- **LoadTrayIcon** (riga 140) — `private static (System.Drawing.Icon Icon, bool Owned) LoadTrayIcon()`. Icona del marchio incorporata nell'exe (allsky-brain.ico, P1.10).
- **OpenPanel** (riga 159) — `private void OpenPanel()`. Apre l'indirizzo del pannello nel browser predefinito.
- **IsAutostartEnabled** (riga 177) — `private static bool IsAutostartEnabled()`. Restituisce true se la voce di avvio automatico esiste già nel registro.
- **SetAutostart** (riga 193) — `private void SetAutostart(bool enable)`. Scrive o rimuove la voce di avvio automatico.
- **OnCheckForUpdatesClick** (riga 225) — `private async void OnCheckForUpdatesClick(object? sender, EventArgs e)`. Voce "Controlla aggiornamenti".
- **DownloadUpdateAsync** (riga 267) — `private async Task DownloadUpdateAsync()`. Scarica l'aggiornamento e, a download completato, chiede se installarlo subito.
- **InstallAndRestart** (riga 284) — `private void InstallAndRestart()`. Programma l'installazione e chiude l'applicazione.
- **ErrorText** (riga 297) — `private string ErrorText(UpdateStatus status)`. Testo dell'errore di aggiornamento nella lingua scelta.
- **ShowMessage** (riga 301) — `private void ShowMessage(string text, System.Windows.MessageBoxImage icon)`. Messaggio con il solo pulsante OK, con il titolo dell'app.
- **ChooseWithButtons** (riga 310) — `private bool ChooseWithButtons(string text, string confirmText, string cancelText)`. Finestra con due pulsanti dal testo scelto: MessageBox non permette etichette personalizzate.

### Views/Camera/CalibrationPanel.cs

- **CalibrationPanel** (riga 33, tipo) — `public sealed class CalibrationPanel : Border`. Pannello "Calibrazione ottica" (P3.3): stato della raccolta dei punti dal Sole, anteprima dell'ultimo frame con la griglia alt/az sovrapposta, accetta/rifiuta della proposta e modalita' manuale con anteprima immediata.
- **CalibrationPanel** (riga 87) — `public CalibrationPanel(IAllSkyService service, ICalibrationService calibration, DesktopTexts texts)`. Crea il pannello.
- **OnLoaded** (riga 110) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 126) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnSnapshotChanged** (riga 134) — `private void OnSnapshotChanged()`.
- **OnFrameProduced** (riga 147) — `private void OnFrameProduced(FrameEvent frame)`.
- **OnFrameReady** (riga 152) — `private async void OnFrameReady()`.
- **LoadFrameAsync** (riga 162) — `private async Task LoadFrameAsync()`.
- **FitPreview** (riga 189) — `private void FitPreview()`.
- **DrawOverlay** (riga 220) — `private void DrawOverlay()`.
- **AddPolyline** (riga 284) — `private void AddPolyline(IReadOnlyList<PixelPoint> points, double scale, string brushKey, double opacity)`.
- **AddSegment** (riga 307) — `private void AddSegment(Point from, Point to)`.
- **AddDot** (riga 322) — `private void AddDot(Point at, double diameter, string brushKey)`.
- **AddCardinalLabel** (riga 332) — `private void AddCardinalLabel(CardinalMark mark, Point center, double scale)`.
- **CardinalText** (riga 351) — `private string CardinalText(CardinalDirection direction)`.
- **Map** (riga 359) — `private static Point Map(PixelPoint p, double scale)`.
- **Render** (riga 363) — `private void Render()`.
- **RenderStatus** (riga 370) — `private void RenderStatus()`.
- **RenderButtons** (riga 427) — `private void RenderButtons()`.
- **SetBusy** (riga 436) — `private void SetBusy(bool busy)`.
- **RunAsync** (riga 443) — `private async Task RunAsync(Func<CalibrationActionResult> action, string successText, Action? onSuccess)`.
- **ActionMessage** (riga 485) — `private string ActionMessage(CalibrationActionResult result)`.
- **OnAcceptClick** (riga 494) — `private async void OnAcceptClick(object sender, RoutedEventArgs e)`.
- **OnRejectClick** (riga 499) — `private async void OnRejectClick(object sender, RoutedEventArgs e)`.
- **ManualPreviewModel** (riga 507) — `private FisheyeModel? ManualPreviewModel()`.
- **OnManualEdited** (riga 509) — `private void OnManualEdited()`.
- **RefreshManual** (riga 520) — `private void RefreshManual()`.
- **ProblemText** (riga 543) — `private string? ProblemText(ManualFieldProblem problem)`.
- **SetProblem** (riga 552) — `private static void SetProblem(TextBlock block, string? text)`.
- **FillManualFromReference** (riga 559) — `private void FillManualFromReference()`.
- **FillFields** (riga 599) — `private void FillFields(double cx, double cy, double horizonRadius, double k2, double rotation, bool mirrored, CultureInfo c)`.
- **OnManualUseClick** (riga 609) — `private async void OnManualUseClick(object sender, RoutedEventArgs e)`.
- **OnManualResetClick** (riga 621) — `private void OnManualResetClick(object sender, RoutedEventArgs e)`.
- **BuildLayout** (riga 629) — `private UIElement BuildLayout()`.
- **BuildPreview** (riga 667) — `private UIElement BuildPreview()`.
- **BuildManualSection** (riga 705) — `private UIElement BuildManualSection()`.
- **AddField** (riga 749) — `private TextBlock AddField(StackPanel stack, string label, TextBox box)`.
- **BuildTextBox** (riga 759) — `private TextBox BuildTextBox(string accessibleName)`.
- **BuildPanelTitle** (riga 767) — `private TextBlock BuildPanelTitle(string text)`.
- **BuildLabel** (riga 774) — `private TextBlock BuildLabel(string text)`.
- **BuildBodyText** (riga 782) — `private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")`.
- **BuildButton** (riga 790) — `private Button BuildButton(string text, bool primary)`.

### Views/Camera/CameraView.cs

- **CameraView** (riga 29, tipo) — `public class CameraView : UserControl`. Vista "Camera": ricerca e collegamento delle camere (simulatore, ASCOM COM, ASCOM Alpaca), stato del collegamento, guadagno ed esposizione massima con validazione, e un segnaposto per la calibrazione ottica (P3.3).
- **CameraView** (riga 103) — `public CameraView(IAllSkyService service)`. Crea la vista con i testi in italiano.
- **CameraView** (riga 111) — `public CameraView(IAllSkyService service, DesktopTexts texts)`. Crea la vista con i testi nella lingua scelta.
- **OnLoaded** (riga 128) — `private async void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 148) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnStatusChanged** (riga 158) — `private void OnStatusChanged(AllSky.Engine.StatusDto status)`.
- **SearchAsync** (riga 172) — `private async Task SearchAsync()`.
- **ConnectAsync** (riga 204) — `private async Task ConnectAsync(CameraCandidate candidate)`.
- **DisconnectAsync** (riga 232) — `private async Task DisconnectAsync()`.
- **SaveAsync** (riga 260) — `private async Task SaveAsync()`.
- **RenderList** (riga 320) — `private void RenderList()`.
- **RenderStatus** (riga 363) — `private void RenderStatus(AllSky.Engine.StatusDto status)`.
- **RenderErrors** (riga 384) — `private void RenderErrors()`.
- **RenderProblems** (riga 404) — `private CameraSettingsCheck RenderProblems()`.
- **ShowSettings** (riga 412) — `private void ShowSettings(AllSky.Core.AppConfig config)`.
- **SetBusy** (riga 419) — `private void SetBusy(bool busy, string? progress)`.
- **UpdateConnectEnabled** (riga 429) — `private void UpdateConnectEnabled()`.
- **CurrentChoice** (riga 435) — `private CameraSourceChoice CurrentChoice()`.
- **SetProblem** (riga 442) — `private static void SetProblem(TextBlock block, string? text)`.
- **ListItemStyle** (riga 448) — `private Style ListItemStyle()`.
- **BuildCandidateContent** (riga 450) — `private UIElement BuildCandidateContent(CameraCandidate candidate, string kindLabel)`.
- **BuildLayout** (riga 473) — `private UIElement BuildLayout()`.
- **BuildErrorBlock** (riga 517) — `private UIElement BuildErrorBlock()`.
- **BuildSearchPanel** (riga 534) — `private Border BuildSearchPanel()`.
- **BuildConnectionPanel** (riga 580) — `private Border BuildConnectionPanel()`.
- **BuildSettingsPanel** (riga 618) — `private Border BuildSettingsPanel()`.
- **BuildCalibrationPanel** (riga 647) — `private Border BuildCalibrationPanel()`.
- **OnConnectClickAsync** (riga 664) — `private async Task OnConnectClickAsync()`.
- **BuildPanel** (riga 685) — `private Border BuildPanel(UIElement content, string name)`.
- **BuildPanelTitle** (riga 699) — `private TextBlock BuildPanelTitle(string text)`.
- **BuildLabel** (riga 706) — `private TextBlock BuildLabel(string text)`.
- **BuildBodyText** (riga 714) — `private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")`.
- **BuildTextBox** (riga 722) — `private TextBox BuildTextBox(string accessibleName)`.
- **BuildButton** (riga 729) — `private Button BuildButton(string text, bool primary)`.

### Views/ComponentGalleryWindow.cs

- **ComponentGalleryWindow** (riga 23, tipo) — `internal sealed class ComponentGalleryWindow : Window`. "Galleria componenti": mostra tutti gli stili del tema (pulsanti, campi, interruttore, slider, barra, pannello), la scala tipografica e di spaziatura e la palette con la sigla di ogni ruolo.
- **ComponentGalleryWindow** (riga 33) — `public ComponentGalleryWindow(ThemeManager theme)`. Crea la galleria collegata al gestore del tema.
- **OnPreviewKeyDown** (riga 88) — `private void OnPreviewKeyDown(object sender, KeyEventArgs e)`. Ctrl+R: inverte la modalità notte rossa.
- **OnThemeChanged** (riga 98) — `private void OnThemeChanged(object? sender, EventArgs e)`. Il tema è cambiato (da qui o da altrove): riallinea i controlli e le sigle esadecimali.
- **SetNight** (riga 104) — `private void SetNight(bool enabled)`. Applica la modalità notte senza far rientrare l'evento del controllo.
- **RefreshLabels** (riga 113) — `private void RefreshLabels()`. Riporta interruttore, slider, etichetta e sigle della palette allo stato del tema.
- **ModeSection** (riga 135) — `private StackPanel ModeSection()`.
- **ButtonSection** (riga 150) — `private WrapPanel ButtonSection()`.
- **FieldSection** (riga 162) — `private StackPanel FieldSection()`.
- **TypeSection** (riga 188) — `private StackPanel TypeSection()`.
- **SpaceSection** (riga 205) — `private WrapPanel SpaceSection()`.
- **PaletteSection** (riga 227) — `private WrapPanel PaletteSection()`.
- **Section** (riga 255) — `private Border Section(string title, UIElement body)`. Pannello con titolo: usa lo stile "Panel" e "PanelTitle" del tema.
- **MakeButton** (riga 270) — `private Button MakeButton(string label, string styleKey, bool enabled)`. Pulsante con uno dei tre stili del tema, eventualmente disabilitato.
- **Text** (riga 282) — `private TextBlock Text(string text, string sizeKey)`. Testo con la dimensione presa dalla risorsa indicata (la sigla "Size.*" o "Font.*" passa dal tema).
- **Gap** (riga 289) — `private static UIElement Gap(double height)`.
- **Spacer** (riga 291) — `private static UIElement Spacer(double width)`.
- **Space** (riga 294) — `private double Space(int n)`. Legge una spaziatura della scala (Space.n) dalle risorse, in unità indipendenti dal dispositivo.

### Views/Events/EventClipPlayer.cs

- **EventClipPlayer** (riga 21, tipo) — `internal sealed class EventClipPlayer : Grid`. Riproduttore della mini-clip di un evento: MediaElement con Riproduci, Pausa e Stop, e un messaggio al posto del video quando la clip manca o il lettore non la apre (di solito per un codec di sistema assente).
- **EventClipPlayer** (riga 35) — `public EventClipPlayer(DesktopTexts texts)`. Crea il riproduttore vuoto.
- **Show** (riga 100) — `public void Show(string path)`. Carica la clip.
- **ShowPlaceholder** (riga 119) — `public void ShowPlaceholder(string text)`. Rilascia la clip e mostra un messaggio al posto del video.
- **Release** (riga 126) — `public void Release()`. Ferma la riproduzione, chiude il file e stacca la sorgente.
- **TogglePlayback** (riga 141) — `private void TogglePlayback()`.
- **StopPlayback** (riga 162) — `private void StopPlayback()`.
- **OnMediaOpened** (riga 175) — `private void OnMediaOpened(object sender, RoutedEventArgs e)`.
- **OnMediaEnded** (riga 185) — `private void OnMediaEnded(object sender, RoutedEventArgs e)`.
- **OnMediaFailed** (riga 193) — `private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)`.
- **ShowMessage** (riga 200) — `private void ShowMessage(string text, string? detail)`.
- **UpdateButtons** (riga 207) — `private void UpdateButtons()`.

### Views/Events/EventsData.cs

- **EventsData** (riga 12, tipo) — `internal static class EventsData`. Letture per la vista Eventi.
- **LoadImage** (riga 26) — `internal static BitmapSource? LoadImage(string? path, int decodeWidth)`. Immagine decodificata e congelata, ridotta a `decodeWidth`.
- **ListAll** (riga 33) — `internal static IReadOnlyList<EventListItem> ListAll(IEventBrowser events)`. Tutti gli eventi attivi.

### Views/Events/EventsView.cs

- **EventsView** (riga 37, tipo) — `public sealed class EventsView : UserControl`. Vista "Eventi": galleria di meteore, bolidi e transiti con filtri per notte e per tipo, dettaglio con la mini-clip e i dati, etichette "Confermato" e "Falso positivo" per tarare le soglie, ed esportazione dell'evento in uno zip.
- **EventsView** (riga 132) — `public EventsView(IEventBrowser events, DesktopTexts texts)`. Crea la vista.
- **OnLoaded** (riga 163) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 175) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **RefreshAsync** (riga 194) — `private async Task RefreshAsync()`. Rilegge l'archivio e ricostruisce filtri e galleria.
- **CurrentFilter** (riga 228) — `private EventFilter CurrentFilter()`.
- **RebuildFilterOptions** (riga 241) — `private void RebuildFilterOptions()`. Riempie il filtro per notte con le notti presenti, mantenendo la scelta se la notte esiste ancora.
- **RebuildGallery** (riga 263) — `private void RebuildGallery()`. Riempie la galleria con gli eventi che passano il filtro, e riseleziona quello aperto se c'è ancora.
- **CreateItem** (riga 303) — `private ListBoxItem CreateItem(EventListItem item)`.
- **LoadThumbnailAsync** (riga 364) — `private async Task LoadThumbnailAsync(string path)`. Decodifica una miniatura fuori dal thread dell'interfaccia e la applica a tutte le caselle che la mostrano.
- **ClearGallery** (riga 388) — `private void ClearGallery()`.
- **OnFilterChanged** (riga 404) — `private void OnFilterChanged(object sender, SelectionChangedEventArgs e)`.
- **OnSelectionChanged** (riga 414) — `private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)`.
- **ShowDetailAsync** (riga 429) — `private async Task ShowDetailAsync(EventListItem item)`.
- **ApplyDetail** (riga 471) — `private void ApplyDetail(EventDetail detail)`. Mostra un dettaglio già letto.
- **LoadDetailImageAsync** (riga 496) — `private async Task LoadDetailImageAsync(EventDetail detail)`.
- **ShowImageMessage** (riga 523) — `private void ShowImageMessage(string text)`.
- **BuildDataRows** (riga 530) — `private void BuildDataRows(IReadOnlyList<EventDataRow> rows)`.
- **ClearDetail** (riga 568) — `private void ClearDetail()`.
- **SetDetailPlaceholder** (riga 585) — `private void SetDetailPlaceholder(string text)`.
- **UpdateButtons** (riga 592) — `private void UpdateButtons()`.
- **SetLabelAsync** (riga 607) — `private async Task SetLabelAsync(EventLabelChoice wanted)`. Imposta l'etichetta.
- **ExportAsync** (riga 647) — `private async Task ExportAsync()`. Esporta l'evento in uno zip scelto dall'utente.
- **BuildLayout** (riga 691) — `private UIElement BuildLayout()`.
- **BuildContent** (riga 740) — `private UIElement BuildContent()`. Area sotto l'intestazione: galleria e dettaglio, oppure lo stato vuoto se non c'è nessun evento.
- **BuildDetailBody** (riga 793) — `private UIElement BuildDetailBody()`.
- **BuildEmptyState** (riga 882) — `private UIElement BuildEmptyState()`.
- **BuildPanel** (riga 890) — `private Border BuildPanel(string? title, UIElement content)`. Riquadro con il titolo in maiuscoletto, come gli altri pannelli dell'app.
- **BuildFilter** (riga 912) — `private static StackPanel BuildFilter(string label, ComboBox box, double width)`.
- **PopulateFilters** (riga 926) — `private void PopulateFilters()`. Voci fisse dei filtri: nessuna notte e tutti i tipi.
- **SelectByTag** (riga 938) — `private static void SelectByTag(ComboBox box, object? tag)`. Seleziona la voce con lo stesso tag, o la prima se non c'è più.
- **EventKey** (riga 953, tipo) — `private readonly record struct EventKey(string NightId, string Id);`. Chiave di un evento: notte e identificatore.
- **ThumbSlot** (riga 956, tipo) — `private sealed record ThumbSlot(string Path, Image Image);`. Un'immagine della galleria in attesa della sua miniatura.

### Views/Live/FrameDecoder.cs

- **FrameDecoder** (riga 15, tipo) — `internal static class FrameDecoder`. Decodifica un JPEG in un BitmapSource congelato, pronto per essere usato sul thread dell'interfaccia.
- **Decode** (riga 21) — `public static BitmapSource Decode(byte[] jpeg)`. Decodifica i byte di un JPEG.

### Views/Live/LiveView.cs

- **LiveView** (riga 30, tipo) — `public sealed class LiveView : UserControl`. Vista "Live": l'immagine corrente del cielo, grande, con zoom, trascinamento, versione grezza e pannello dati del frame.
- **BuildToolbar** (riga 95) — `private UIElement BuildToolbar()`.
- **BuildImageArea** (riga 126) — `private UIElement BuildImageArea()`.
- **BuildSidePanel** (riga 168) — `private UIElement BuildSidePanel()`.
- **CreateButton** (riga 193) — `private Button CreateButton(string content, string name, Action onClick)`.
- **CreatePanelTitle** (riga 208) — `private TextBlock CreatePanelTitle(string text)`.
- **CreateDataRow** (riga 215) — `private Grid CreateDataRow(FrameDataItem item, out TextBlock value)`.
- **CreateOverlayToggle** (riga 243) — `private CheckBox CreateOverlayToggle(string text)`.
- **CreateSatelliteToggle** (riga 260) — `private CheckBox CreateSatelliteToggle()`. Interruttore delle tracce previste: attivo solo con previsione accesa e calibrazione valida.
- **PanelStyle** (riga 276) — `private static Style PanelStyle()`.
- **OnLoaded** (riga 278) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 290) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnFrameProduced** (riga 305) — `private void OnFrameProduced(FrameEvent frame)`.
- **OnRawChanged** (riga 315) — `private void OnRawChanged()`.
- **RefreshAsync** (riga 317) — `private async void RefreshAsync()`.
- **Load** (riga 349) — `private LiveLoad Load(bool raw)`.
- **Apply** (riga 366) — `private void Apply(LiveLoad load)`.
- **ToFrameData** (riga 399) — `private static LiveFrameData ToFrameData(LastFrameDto? last)`.
- **ApplyTransform** (riga 409) — `private void ApplyTransform()`.
- **UpdateTrackTransform** (riga 428) — `private void UpdateTrackTransform()`. Stessa trasformazione dell'immagine: il frame di calibrazione va sulla dimensione mostrata.
- **ApplyTracks** (riga 441) — `private void ApplyTracks(PassTrackState? state)`.
- **RebuildTracks** (riga 468) — `private void RebuildTracks()`. Ridisegna le tracce: solo se l'interruttore è acceso e l'immagine è visibile.
- **ZoomBy** (riga 520) — `private void ZoomBy(double factor)`.
- **ResetView** (riga 528) — `private void ResetView()`.
- **OnMouseWheel** (riga 534) — `private void OnMouseWheel(object sender, MouseWheelEventArgs e)`.
- **OnMouseDown** (riga 549) — `private void OnMouseDown(object sender, MouseButtonEventArgs e)`.
- **OnMouseMove** (riga 562) — `private void OnMouseMove(object sender, MouseEventArgs e)`.
- **EndDrag** (riga 577) — `private void EndDrag()`.
- **LiveLoad** (riga 584, tipo) — `private sealed record LiveLoad(BitmapSource? Image, LiveFrameData Data, string? Error, PassTrackState? Tracks);`. Esito di un caricamento in background.

### Views/Log/LogView.cs

- **LogView** (riga 25, tipo) — `public class LogView : UserControl`. Vista "Log": ultime voci del log giornaliero con filtro per gravità e testo, scorrimento automatico in fondo, copia negli appunti e apertura del file.
- **LogView** (riga 61) — `public LogView(AppPaths paths, DesktopTexts texts)`. Crea la vista con i testi nella lingua scelta.
- **OnLoaded** (riga 80) — `private async void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 92) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnTick** (riga 98) — `private async void OnTick(object? sender, EventArgs e)`.
- **RefreshAsync** (riga 108) — `private async Task RefreshAsync()`.
- **ReadSnapshot** (riga 136) — `private LogSnapshot ReadSnapshot()`.
- **Render** (riga 149) — `private void Render()`.
- **SetEmpty** (riga 190) — `private void SetEmpty(string? text)`.
- **CurrentLevel** (riga 196) — `private LogLevelFilter CurrentLevel()`.
- **BuildEntry** (riga 202) — `private TextBlock BuildEntry(LogRecord record)`. Una voce: ora, etichetta di gravità in testo (non solo colore) e testo già mascherato.
- **CopyVisible** (riga 234) — `private void CopyVisible()`.
- **OpenFile** (riga 257) — `private void OpenFile()`.
- **SetStatus** (riga 275) — `private void SetStatus(string text, bool error)`.
- **BuildLayout** (riga 283) — `private UIElement BuildLayout()`.
- **AddRow** (riga 388) — `private static void AddRow(Grid grid, UIElement element, int row)`.
- **BuildLabel** (riga 394) — `private TextBlock BuildLabel(string text)`.
- **BuildCaption** (riga 402) — `private TextBlock BuildCaption(string brushKey)`.
- **BuildButton** (riga 410) — `private Button BuildButton(string text, bool primary)`.
- **LogSnapshot** (riga 424, tipo) — `private sealed record LogSnapshot(string? FilePath, IReadOnlyList<LogRecord> Records);`.

### Views/Mask/MaskEditorView.cs

- **MaskEditorView** (riga 41, tipo) — `public class MaskEditorView : UserControl`. Vista "Maschera": editor visivo della maschera informativa del timelapse.
- **MaskEditorView** (riga 121) — `public MaskEditorView(IAllSkyService service, AppPaths paths, DesktopTexts texts)`. Crea la vista.
- **OnLoaded** (riga 145) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 162) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnFrameProduced** (riga 171) — `private void OnFrameProduced(AllSky.Engine.FrameEvent evento)`.
- **CaricaModulo** (riga 186) — `private void CaricaModulo()`. Riempie i controlli dallo stato.
- **CaricaDaConfigurazione** (riga 221) — `private void CaricaDaConfigurazione(AppConfig config)`.
- **LeggiModulo** (riga 234) — `private void LeggiModulo()`. Legge i controlli della maschera, del testo e dell'applicazione nello stato.
- **LeggiDettaglio** (riga 276) — `private void LeggiDettaglio()`. Legge i controlli del widget selezionato nel suo formato.
- **Percento** (riga 348) — `private double? Percento(TextBox? casella, string nomeCampo, double minimo, double massimo)`. Legge un campo percentuale.
- **OnModificato** (riga 366) — `private void OnModificato(bool ricostruisciElenco = false)`.
- **OnSelezioneCambiata** (riga 386) — `private void OnSelezioneCambiata(object sender, SelectionChangedEventArgs e)`.
- **OnAbilitato** (riga 398) — `private void OnAbilitato(int indice, bool abilitato)`.
- **OnSu** (riga 411) — `private void OnSu(object sender, RoutedEventArgs e)`.
- **OnGiu** (riga 413) — `private void OnGiu(object sender, RoutedEventArgs e)`.
- **SpostaSelezionato** (riga 415) — `private void SpostaSelezionato(bool su)`.
- **OnRimuovi** (riga 437) — `private void OnRimuovi(object sender, RoutedEventArgs e)`.
- **OnAggiungiTesto** (riga 457) — `private void OnAggiungiTesto(object sender, RoutedEventArgs e)`.
- **OnCaricaLogo** (riga 473) — `private async void OnCaricaLogo(object sender, RoutedEventArgs e)`.
- **OnRipristina** (riga 507) — `private void OnRipristina(object sender, RoutedEventArgs e)`.
- **OnSalva** (riga 523) — `private async void OnSalva(object sender, RoutedEventArgs e)`.
- **IniziaOccupato** (riga 577) — `private void IniziaOccupato()`.
- **FineOccupato** (riga 585) — `private void FineOccupato()`.
- **PianificaAnteprima** (riga 594) — `private void PianificaAnteprima()`.
- **OnTimerAnteprima** (riga 600) — `private async void OnTimerAnteprima(object? sender, EventArgs e)`.
- **AggiornaAnteprimaAsync** (riga 606) — `private async Task AggiornaAnteprimaAsync()`.
- **PreparaFotogramma** (riga 658) — `private AnteprimaFotogramma PreparaFotogramma(AppConfig config)`. Recupera l'ultimo frame e i suoi metadati.
- **FotogrammaSintetico** (riga 680) — `private byte[] FotogrammaSintetico()`. Cielo di esempio: fondo quasi nero con stelle a seme fisso.
- **FusoOrario** (riga 713) — `private static TimeZoneInfo FusoOrario(string? id)`.
- **AnteprimaFotogramma** (riga 725, tipo) — `private sealed record AnteprimaFotogramma(byte[] Jpeg, FrameSidecar Sidecar, bool Sintetico);`.
- **HaSelezione** (riga 729) — `private bool HaSelezione()`.
- **IndiceDi** (riga 731) — `private int IndiceDi(MaskWidgetDraft voce)`.
- **RicostruisciElenco** (riga 744) — `private void RicostruisciElenco()`.
- **CreaRiga** (riga 766) — `private ListBoxItem CreaRiga(int indice, MaskWidgetDraft voce)`.
- **AggiornaPulsantiWidget** (riga 807) — `private void AggiornaPulsantiWidget()`.
- **RicostruisciDettaglio** (riga 816) — `private void RicostruisciDettaglio()`. Ricostruisce i controlli del formato per il tipo del widget selezionato.
- **AggiornaVisibilita** (riga 938) — `private void AggiornaVisibilita()`.
- **RaccogliProblemi** (riga 947) — `private List<string> RaccogliProblemi()`. Problemi che bloccano il salvataggio: campi non validi e errori di validazione del layout.
- **AggiornaStato** (riga 955) — `private void AggiornaStato()`. Aggiorna messaggi, stato sporco e abilitazione del salvataggio.
- **ImpostaStato** (riga 975) — `private void ImpostaStato(string testo, bool errore)`.
- **MessaggioLogo** (riga 981) — `private string MessaggioLogo(MaskLogoErrore errore)`.
- **NomeTipo** (riga 993) — `private string NomeTipo(OverlayWidgetKind tipo)`.
- **NomePosizione** (riga 1006) — `private string NomePosizione(OverlayPosition posizione)`.
- **NomeMeteo** (riga 1016) — `private string NomeMeteo(OverlayMeteoField campo)`.
- **BuildLayout** (riga 1029) — `private UIElement BuildLayout()`.
- **CostruisciAnteprima** (riga 1056) — `private UIElement CostruisciAnteprima()`.
- **CostruisciImpostazioni** (riga 1083) — `private UIElement CostruisciImpostazioni()`.
- **CostruisciWidget** (riga 1130) — `private UIElement CostruisciWidget()`.
- **CostruisciMaschera** (riga 1163) — `private UIElement CostruisciMaschera()`.
- **CostruisciTesto** (riga 1200) — `private UIElement CostruisciTesto()`.
- **CostruisciLoghi** (riga 1212) — `private UIElement CostruisciLoghi()`.
- **CostruisciApplicazione** (riga 1221) — `private UIElement CostruisciApplicazione()`.
- **Sezione** (riga 1244) — `private static Border Sezione(string titolo, UIElement contenuto)`.
- **Titolo** (riga 1258) — `private static TextBlock Titolo(string testo, bool grande = false)`.
- **Testo** (riga 1266) — `private static TextBlock Testo(string testo, string brushKey, string sizeKey)`.
- **Etichetta** (riga 1274) — `private static TextBlock Etichetta(string testo)`.
- **Riga** (riga 1283) — `private static Grid Riga(string etichetta, UIElement controllo)`. Riga "etichetta: controllo" su due colonne.
- **Campo** (riga 1297) — `private static TextBox Campo(string nome, string valore, double larghezza)`.
- **Combo** (riga 1311) — `private static ComboBox Combo(string nome, IReadOnlyList<string> voci)`.
- **Interruttore** (riga 1326) — `private static CheckBox Interruttore(string testo, bool attivo)`.
- **Pulsante** (riga 1333) — `private static Button Pulsante(string testo, bool primario)`.

### Views/Nights/NightsData.cs

- **NightsData** (riga 20, tipo) — `internal static class NightsData`. Letture dell'archivio per la vista "Notti".
- **NightList** (riga 32, tipo) — `internal sealed record NightList(IReadOnlyList<NightSummary> Nights, string? Error);`. Esito della lettura dell'elenco.
- **NightDetail** (riga 41, tipo) — `internal sealed record NightDetail(`. Dati di una notte selezionata.
- **ListNights** (riga 51) — `public static NightList ListNights(IAllSkyService service)`. Elenco delle notti, dalla più recente.
- **LoadDetail** (riga 67) — `public static NightDetail LoadDetail(IAllSkyService service, string nightId)`. Frame, keogram, startrails e percorso del timelapse di una notte.
- **LoadFrame** (riga 94) — `public static BitmapSource? LoadFrame(IAllSkyService service, string nightId, string frameId)`. Immagine di un frame, congelata; null se il file manca o non si decodifica.
- **TryDecode** (riga 113) — `public static BitmapSource? TryDecode(string path, int decodePixelWidth)`. Decodifica un JPEG in un'immagine congelata, ridotta a `decodePixelWidth` se è più larga.
- **ToSummary** (riga 145) — `private static NightSummary ToSummary(NightDto dto)`.

### Views/Nights/NightsKeogramBar.cs

- **NightsKeogramBar** (riga 24, tipo) — `internal sealed class NightsKeogramBar : Grid`. Keogram della notte usato come barra di scorrimento temporale.
- **NightsKeogramBar** (riga 45) — `public NightsKeogramBar(DesktopTexts texts)`. Crea la barra.
- **SetImage** (riga 101) — `public void SetImage(BitmapSource? image)`. Mostra il keogram, o rilascia l'immagine precedente con `null`.
- **SetMessage** (riga 109) — `public void SetMessage(string? text)`. Testo da mostrare al posto dell'immagine, o null per nasconderlo.
- **SetCursor** (riga 117) — `public void SetCursor(double? fraction)`. Posiziona il cursore su una frazione da 0 a 1, o lo nasconde con `null`.
- **PositionCursor** (riga 123) — `private void PositionCursor()`.
- **OnMouseDown** (riga 138) — `private void OnMouseDown(object sender, MouseButtonEventArgs e)`.
- **OnMouseMove** (riga 147) — `private void OnMouseMove(object sender, MouseEventArgs e)`.
- **OnMouseUp** (riga 157) — `private void OnMouseUp(object sender, MouseButtonEventArgs e)`.
- **OnKeyDown** (riga 163) — `private void OnKeyDown(object sender, KeyEventArgs e)`.
- **Report** (riga 181) — `private void Report(double x)`.

### Views/Nights/NightsTimelapsePlayer.cs

- **NightsTimelapsePlayer** (riga 20, tipo) — `internal sealed class NightsTimelapsePlayer : Grid`. Riproduttore del timelapse di una notte: MediaElement con Play, Pausa e Stop, e un messaggio al posto del video quando non c'è un file o quando il lettore non riesce a riprodurlo (di solito per un codec mancante).
- **NightsTimelapsePlayer** (riga 34) — `public NightsTimelapsePlayer(DesktopTexts texts)`. Crea il riproduttore vuoto.
- **Show** (riga 100) — `public void Show(string path)`. Carica il file del timelapse.
- **ShowPlaceholder** (riga 119) — `public void ShowPlaceholder(string text)`. Rilascia il file e mostra un messaggio al posto del video.
- **Release** (riga 126) — `public void Release()`. Ferma la riproduzione, chiude il file e stacca la sorgente.
- **TogglePlayback** (riga 141) — `private void TogglePlayback()`.
- **StopPlayback** (riga 162) — `private void StopPlayback()`.
- **OnMediaOpened** (riga 175) — `private void OnMediaOpened(object sender, RoutedEventArgs e)`.
- **OnMediaEnded** (riga 185) — `private void OnMediaEnded(object sender, RoutedEventArgs e)`.
- **OnMediaFailed** (riga 193) — `private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)`.
- **ShowMessage** (riga 200) — `private void ShowMessage(string text, string? detail)`.
- **UpdateButtons** (riga 207) — `private void UpdateButtons()`.

### Views/Nights/NightsView.cs

- **NightsView** (riga 35, tipo) — `public sealed class NightsView : UserControl`. Vista "Notti": elenco delle notti archiviate con frame e stato del timelapse, keogram usato come barra di scorrimento temporale, startrails, riproduzione del timelapse e pulsante "Rigenera timelapse".
- **NightsView** (riga 121) — `public NightsView(IAllSkyService service)`. Crea la vista con i testi in italiano.
- **NightsView** (riga 129) — `public NightsView(IAllSkyService service, DesktopTexts texts)`. Crea la vista con i testi nella lingua scelta.
- **OnLoaded** (riga 153) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 167) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnFrameProduced** (riga 191) — `private void OnFrameProduced(FrameEvent frame)`.
- **OnFrameOnUi** (riga 201) — `private void OnFrameOnUi()`.
- **OnStatusChanged** (riga 209) — `private void OnStatusChanged(StatusDto status)`.
- **OnStatusOnUi** (riga 219) — `private void OnStatusOnUi(StatusDto status)`.
- **RefreshAllAsync** (riga 242) — `private async Task RefreshAllAsync()`.
- **RefreshListAsync** (riga 249) — `private async Task RefreshListAsync()`.
- **LoadDetailAsync** (riga 279) — `private async Task LoadDetailAsync()`.
- **PumpFrameAsync** (riga 298) — `private async Task PumpFrameAsync()`.
- **ApplyList** (riga 338) — `private void ApplyList(NightsData.NightList result)`.
- **ApplyDetail** (riga 371) — `private void ApplyDetail(NightsData.NightDetail detail)`.
- **RebuildList** (riga 394) — `private void RebuildList()`.
- **UpdateListStatus** (riga 423) — `private void UpdateListStatus()`.
- **ApplyNightHeader** (riga 438) — `private void ApplyNightHeader()`.
- **ApplyTimelapseControls** (riga 453) — `private void ApplyTimelapseControls()`.
- **ShowFrame** (riga 490) — `private void ShowFrame(int index)`.
- **ResetDetail** (riga 523) — `private void ResetDetail()`. Riporta la vista allo stato "in caricamento" per la notte selezionata, senza leggere nulla.
- **UpdateSelectionVisibility** (riga 544) — `private void UpdateSelectionVisibility()`.
- **OnSelectionChanged** (riga 553) — `private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)`.
- **OnFractionRequested** (riga 568) — `private void OnFractionRequested(double fraction)`.
- **OnStepRequested** (riga 578) — `private void OnStepRequested(int delta)`.
- **RegenerateAsync** (riga 588) — `private async Task RegenerateAsync()`.
- **SelectedNight** (riga 621) — `private NightSummary? SelectedNight()`.
- **IndexOfNight** (riga 623) — `private int IndexOfNight(string? id)`.
- **SetMessage** (riga 636) — `private static void SetMessage(TextBlock text, string? message)`.
- **CreateItem** (riga 642) — `private ListBoxItem CreateItem(NightRow row)`.
- **TimelapseBrush** (riga 681) — `private static string TimelapseBrush(NightTimelapseState state)`.
- **ListItemStyle** (riga 689) — `private Style ListItemStyle()`.
- **PanelStyle** (riga 691) — `private static Style PanelStyle()`.
- **CreatePanelTitle** (riga 693) — `private TextBlock CreatePanelTitle(string text)`.
- **Note** (riga 700) — `private TextBlock Note(string text, Thickness margin)`.
- **BuildLayout** (riga 710) — `private UIElement BuildLayout()`.
- **BuildListPanel** (riga 734) — `private UIElement BuildListPanel()`.
- **BuildDetail** (riga 762) — `private ScrollViewer BuildDetail()`.
- **BuildImagePanel** (riga 858) — `private UIElement BuildImagePanel(`.

### Views/PlaceholderView.cs

- **PlaceholderView** (riga 15, tipo) — `internal sealed class PlaceholderView : Grid`. Schermata segnaposto di una vista non ancora implementata: titolo e stato vuoto curato, dentro un pannello del tema.
- **PlaceholderView** (riga 20) — `public PlaceholderView(string title, string emptyText)`. Crea il segnaposto con il titolo della vista e il testo dello stato vuoto.

### Views/Publication/PublicationView.cs

- **PublicationView** (riga 34, tipo) — `public class PublicationView : UserControl`. Vista "Pubblicazione": impostazioni dell'Uplink (server, dispositivo, intervallo, qualità, token), stato della coda, prova di connessione e il pannello YouTube (caricamento automatico, P7.4).
- **PublicationView** (riga 86) — `public PublicationView(IPublishSettingsService service)`. Crea la vista con i testi in italiano.
- **OnLoaded** (riga 121) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 136) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnStatusChanged** (riga 147) — `private void OnStatusChanged(PublicationStatusSnapshot status)`.
- **SaveAsync** (riga 161) — `private async Task SaveAsync()`.
- **TestAsync** (riga 219) — `private async Task TestAsync()`.
- **BuildChange** (riga 265) — `private PublicationSettingsChange BuildChange(PublicationFormCheck check)`. Legge il modulo in una modifica.
- **ShowSettings** (riga 284) — `private void ShowSettings(PublicationSettingsSnapshot settings)`.
- **RenderStatus** (riga 306) — `private void RenderStatus(PublicationStatusSnapshot status)`.
- **StateBrushKey** (riga 321) — `private static string StateBrushKey(PublicationState state)`. Colore dello stato.
- **RenderProblems** (riga 331) — `private PublicationFormCheck RenderProblems()`. Mostra i problemi dei campi numerici sotto ciascun campo.
- **ShowIssues** (riga 345) — `private void ShowIssues(IReadOnlyList<PublicationIssue> issues)`.
- **ClearIssues** (riga 356) — `private void ClearIssues()`.
- **ShowSaveMessage** (riga 362) — `private void ShowSaveMessage(string text, string brushKey)`.
- **ShowTestResult** (riga 369) — `private void ShowTestResult(string text, bool ok)`.
- **SetBusy** (riga 376) — `private void SetBusy(bool busy)`.
- **SetProgress** (riga 383) — `private static void SetProgress(TextBlock block, string? text)`.
- **SetOptional** (riga 389) — `private static void SetOptional(TextBlock block, string? text)`.
- **BuildLayout** (riga 397) — `private UIElement BuildLayout()`.
- **BuildStatusPanel** (riga 441) — `private Border BuildStatusPanel()`.
- **BuildTestPanel** (riga 466) — `private Border BuildTestPanel()`.
- **BuildSettingsPanel** (riga 488) — `private Border BuildSettingsPanel()`.
- **ShowSettingsPlaceholders** (riga 592) — `private void ShowSettingsPlaceholders()`. Stato iniziale dei campi testuali prima del primo caricamento (evita testi vuoti non localizzati).
- **BuildPanel** (riga 602) — `private Border BuildPanel(UIElement content, string name)`.
- **BuildPanelTitle** (riga 616) — `private TextBlock BuildPanelTitle(string text)`.
- **BuildLabel** (riga 623) — `private TextBlock BuildLabel(string text)`.
- **BuildBodyText** (riga 631) — `private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")`.
- **BuildTextBox** (riga 639) — `private TextBox BuildTextBox(string accessibleName, double width)`.
- **BuildPasswordBox** (riga 647) — `private PasswordBox BuildPasswordBox(string accessibleName)`. Campo per i segreti.
- **BuildCheckBox** (riga 659) — `private CheckBox BuildCheckBox(string text)`.
- **BuildButton** (riga 666) — `private Button BuildButton(string text, bool primary)`.

### Views/Publication/YouTubePanel.cs

- **YouTubePanel** (riga 31, tipo) — `public sealed class YouTubePanel : UserControl`. Pannello YouTube della vista Pubblicazione (P7.4): collegamento e scollegamento dell'account, stato del caricamento, impostazioni (ora, privacy, tag, categoria) e "Carica ora la notte scorsa" con conferma.
- **YouTubePanel** (riga 76) — `public YouTubePanel(IYouTubeService service, DesktopTexts texts)`. Crea il pannello con i testi della lingua scelta.
- **OnLoaded** (riga 100) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 114) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnStatusChanged** (riga 124) — `private void OnStatusChanged(YouTubeStatus status)`.
- **LoginAsync** (riga 138) — `private async Task LoginAsync()`.
- **LogoutAsync** (riga 193) — `private async Task LogoutAsync()`.
- **SaveSettingsAsync** (riga 217) — `private async Task SaveSettingsAsync()`.
- **UploadLastNightAsync** (riga 259) — `private async Task UploadLastNightAsync()`.
- **RunUploadAsync** (riga 287) — `private async Task<YouTubeUploadOutcome?> RunUploadAsync(bool acceptUncertainRisk)`. Esito del caricamento, o null se l'operazione è stata annullata.
- **OpenVideo** (riga 310) — `private void OpenVideo()`.
- **Render** (riga 330) — `private void Render(YouTubeStatus status)`.
- **ShowSettings** (riga 357) — `private void ShowSettings(YouTubeSettingsSnapshot settings)`.
- **ShowSettingsProblems** (riga 372) — `private void ShowSettingsProblems(YouTubeSaveResult result)`.
- **ClearSettingsProblems** (riga 383) — `private void ClearSettingsProblems()`.
- **ShowResult** (riga 389) — `private void ShowResult(string? text, bool ok)`.
- **SetBusy** (riga 396) — `private void SetBusy(bool busy)`.
- **SetProgress** (riga 405) — `private void SetProgress(string? text)`.
- **SetOptional** (riga 411) — `private static void SetOptional(TextBlock block, string? text, string brushKey)`.
- **SelectedPrivacy** (riga 418) — `private string SelectedPrivacy()`.
- **Confirm** (riga 421) — `private bool Confirm(string text)`.
- **BuildLayout** (riga 427) — `private UIElement BuildLayout()`.
- **BuildPanel** (riga 517) — `private Border BuildPanel(UIElement content, string name)`.
- **BuildPanelTitle** (riga 531) — `private TextBlock BuildPanelTitle(string text)`.
- **BuildSectionTitle** (riga 538) — `private TextBlock BuildSectionTitle(string text)`.
- **BuildLabel** (riga 546) — `private TextBlock BuildLabel(string text)`.
- **BuildBodyText** (riga 554) — `private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")`.
- **BuildTextBox** (riga 562) — `private TextBox BuildTextBox(string accessibleName, double width)`.
- **BuildPasswordBox** (riga 570) — `private PasswordBox BuildPasswordBox(string accessibleName)`. Campo del secret: nessun valore viene mai scritto dall'applicazione.
- **BuildButton** (riga 582) — `private Button BuildButton(string text, bool primary)`.

### Views/Settings/AutostartRegistry.cs

- **AutostartRegistry** (riga 11, tipo) — `internal static class AutostartRegistry`. Avvio con Windows: voce nella chiave HKCU\...\Run dell'utente corrente.
- **IsEnabled** (riga 16) — `public static bool IsEnabled()`. Vero se la voce di avvio esiste.
- **Set** (riga 33) — `public static void Set(bool enable, string exePath)`. Scrive o rimuove la voce di avvio.

### Views/Settings/SettingsView.cs

- **SettingsView** (riga 30, tipo) — `public class SettingsView : UserControl`. Vista "Impostazioni": posizione dell'osservatorio, lingua, tema, cartella dati (sola lettura), avvio con Windows, aggiornamenti, accesso dal browser (interfaccia web spenta di default, rete locale con password).
- **SettingsView** (riga 80) — `public SettingsView(AllSkyHostRuntime runtime, AppPaths paths, DesktopTexts texts, ThemeManager theme)`. Crea la vista con i testi nella lingua scelta.
- **OnLoaded** (riga 101) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 113) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnThemeChanged** (riga 119) — `private void OnThemeChanged(object? sender, EventArgs e)`.
- **RenderAll** (riga 133) — `private void RenderAll()`.
- **RenderSite** (riga 144) — `private void RenderSite(AllSky.Core.AppConfig config)`.
- **RenderLanguage** (riga 152) — `private void RenderLanguage(AllSky.Core.AppConfig config)`.
- **RenderTheme** (riga 165) — `private void RenderTheme()`.
- **RenderAutostart** (riga 178) — `private void RenderAutostart()`.
- **RenderUpdates** (riga 191) — `private void RenderUpdates(UpdateStatus status)`.
- **RenderAccess** (riga 238) — `private void RenderAccess(AllSky.Core.AppConfig config)`.
- **RenderSiteProblems** (riga 261) — `private void RenderSiteProblems()`.
- **SiteProblemText** (riga 269) — `private string? SiteProblemText(SiteFieldProblem problem)`.
- **SaveSiteAsync** (riga 282) — `private async Task SaveSiteAsync()`.
- **OnLanguageChanged** (riga 325) — `private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)`.
- **OnThemeSelectionChanged** (riga 353) — `private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)`.
- **OnAutostartChanged** (riga 381) — `private void OnAutostartChanged(object sender, RoutedEventArgs e)`.
- **OnAutoCheckChanged** (riga 403) — `private void OnAutoCheckChanged(object sender, RoutedEventArgs e)`.
- **CheckNowAsync** (riga 423) — `private async Task CheckNowAsync()`.
- **OpenDataFolder** (riga 447) — `private void OpenDataFolder()`.
- **SaveAccessAsync** (riga 460) — `private async Task SaveAccessAsync()`.
- **SaveConfigAsync** (riga 516) — `private async Task<bool> SaveConfigAsync(AllSky.Core.AppConfig updated, TextBlock info, string successText)`. Salva la configurazione fuori dal thread della UI e mostra l'esito.
- **BeginBusy** (riga 540) — `private void BeginBusy()`. Blocca i pulsanti di salvataggio e di controllo mentre un'operazione è in corso.
- **EndBusy** (riga 548) — `private void EndBusy()`.
- **BuildLayout** (riga 558) — `private UIElement BuildLayout()`.
- **BuildSitePanel** (riga 602) — `private Border BuildSitePanel()`.
- **BuildGeneralPanel** (riga 640) — `private Border BuildGeneralPanel()`.
- **BuildMaskPanel** (riga 678) — `private Border BuildMaskPanel()`.
- **OpenMaskEditor** (riga 689) — `private void OpenMaskEditor()`.
- **BuildFolderPanel** (riga 704) — `private Border BuildFolderPanel()`.
- **BuildStartupPanel** (riga 722) — `private Border BuildStartupPanel()`.
- **BuildUpdatesPanel** (riga 741) — `private Border BuildUpdatesPanel()`.
- **OnWebBoxClick** (riga 773) — `private void OnWebBoxClick(object sender, RoutedEventArgs e)`.
- **BuildAccessPanel** (riga 778) — `private Border BuildAccessPanel()`.
- **SetProblem** (riga 823) — `private static void SetProblem(TextBlock block, string? text)`.
- **SetInfo** (riga 829) — `private void SetInfo(TextBlock block, string text, bool error)`.
- **BuildProblem** (riga 835) — `private TextBlock BuildProblem()`.
- **BuildPanel** (riga 843) — `private Border BuildPanel(UIElement content, string name)`.
- **BuildPanelTitle** (riga 857) — `private TextBlock BuildPanelTitle(string text)`.
- **BuildLabel** (riga 864) — `private TextBlock BuildLabel(string text)`.
- **BuildBodyText** (riga 872) — `private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")`.
- **BuildTextBox** (riga 880) — `private TextBox BuildTextBox(string accessibleName)`.
- **BuildButton** (riga 888) — `private Button BuildButton(string text, bool primary)`.

### Views/SplashWindow.cs

- **SplashWindow** (riga 20, tipo) — `internal sealed class SplashWindow : Window`. Schermata d'avvio di All Sky Brain (P1.10): marchio, nome e versione, senza bordi di finestra.
- **SplashWindow** (riga 28) — `public SplashWindow(DesktopTexts texts)`. Crea la schermata.
- **CloseSplash** (riga 96) — `public void CloseSplash()`. Chiude la schermata.
- **MarkPath** (riga 108) — `private static Path MarkPath(ResourceDictionary brand, string key, double strokeWidth, double opacity, bool stroke)`. Percorso di un elemento del marchio: tratto con ruolo Accent, o riempimento se `stroke` è falso.
- **ApplicationVersion** (riga 132) — `private static string ApplicationVersion()`. Versione dell'applicazione, senza il suffisso di build dopo il "+".

### Views/Tonight/MoonGlyphView.cs

- **MoonGlyphView** (riga 13, tipo) — `internal sealed class MoonGlyphView : Canvas`. Glifo vettoriale della fase lunare: disco scuro, parte illuminata come Path.
- **MoonGlyphView** (riga 24) — `public MoonGlyphView()`.
- **Show** (riga 46) — `public void Show(MoonPhaseGlyph glyph)`. Mostra la fase indicata.
- **ToPixel** (riga 75) — `private static Point ToPixel(GlyphPoint p, double center, double radius)`.

### Views/Tonight/TonightTimelineCanvas.cs

- **TonightTimelineCanvas** (riga 19, tipo) — `internal sealed class TonightTimelineCanvas : Canvas`. Linea temporale da tramonto ad alba: fasce dei crepuscoli in alto, curve di altezza di Sole e Luna sotto, marcatore dell'ora corrente.
- **TonightTimelineCanvas** (riga 32) — `public TonightTimelineCanvas()`.
- **Show** (riga 40) — `public void Show(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz, DesktopTexts texts)`. Imposta la linea da disegnare e l'istante del marcatore.
- **Redraw** (riga 53) — `private void Redraw()`.
- **AddCurve** (riga 137) — `private void AddCurve(`.
- **AddText** (riga 162) — `private void AddText(string text, double x, double y, string brushKey, double maxWidth, bool rightAligned = false)`.
- **PhaseBrushKey** (riga 180) — `private static string PhaseBrushKey(SkyPhase phase)`. Chiave del pennello di una fase, dalla palette.

### Views/Tonight/TonightView.cs

- **TonightView** (riga 31, tipo) — `public sealed class TonightView : UserControl`. Vista "Stanotte", la schermata iniziale: immagine del cielo dal vivo, linea temporale da tramonto ad alba, carte "Condizioni", "Prossimi eventi" e "Stato della cattura".
- **TonightView** (riga 68) — `public TonightView(IAllSkyService service)`. Crea la vista con i testi in italiano.
- **TonightView** (riga 76) — `public TonightView(IAllSkyService service, DesktopTexts texts)`. Crea la vista con i testi nella lingua scelta.
- **BuildLayout** (riga 91) — `private UIElement BuildLayout()`.
- **BuildImageArea** (riga 129) — `private UIElement BuildImageArea()`.
- **BuildConditions** (riga 153) — `private UIElement BuildConditions()`.
- **BuildEvents** (riga 177) — `private UIElement BuildEvents()`.
- **BuildCapture** (riga 198) — `private UIElement BuildCapture()`.
- **BuildTimeline** (riga 206) — `private UIElement BuildTimeline()`.
- **LegendItem** (riga 244) — `private TextBlock LegendItem(string text, string brushKey)`.
- **FramePanel** (riga 252) — `private Border FramePanel(UIElement content, double top = 0)`.
- **CreatePanelTitle** (riga 262) — `private TextBlock CreatePanelTitle(string text)`.
- **Row** (riga 269) — `private Grid Row(string label, TextBlock value)`.
- **Hint** (riga 294) — `private TextBlock Hint(TextBlock block)`.
- **PanelStyle** (riga 303) — `private static Style PanelStyle()`.
- **OnLoaded** (riga 305) — `private void OnLoaded(object sender, RoutedEventArgs e)`.
- **OnUnloaded** (riga 318) — `private void OnUnloaded(object sender, RoutedEventArgs e)`.
- **OnFrameProduced** (riga 333) — `private void OnFrameProduced(AllSky.Engine.FrameEvent frame)`.
- **RefreshAsync** (riga 343) — `private async void RefreshAsync()`.
- **Load** (riga 374) — `private TonightLoad Load()`.
- **Apply** (riga 407) — `private void Apply(TonightLoad load)`.
- **ApplyImage** (riga 433) — `private void ApplyImage(BitmapSource? image)`.
- **ApplyConditions** (riga 448) — `private void ApplyConditions(TonightSnapshot snapshot)`.
- **ApplyEvents** (riga 462) — `private void ApplyEvents(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz)`.
- **PassPanel** (riga 485) — `private PassPanelData PassPanel(DateTimeOffset now, TimeZoneInfo tz)`. Previsione dei passaggi in forma di carta (P4.3).
- **ApplyPasses** (riga 495) — `private void ApplyPasses(PassPanelData? passes)`.
- **PassRowView** (riga 513) — `private UIElement PassRowView(PassRowData row)`.
- **ApplySegments** (riga 539) — `private void ApplySegments(TonightTimeline timeline, TimeZoneInfo tz)`.
- **NowLine** (riga 562) — `private string NowLine(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz)`.
- **ApplyCapture** (riga 582) — `private void ApplyCapture(StatusDto? status, DateTimeOffset now)`.
- **TonightLoad** (riga 610, tipo) — `private sealed record TonightLoad(`. Caricamento in background: stato, notte calcolata e immagine.


## AllSky.Detection (src/AllSky.Detection)

### IPredictedTrackProvider.cs

- **PredictedTrack** (riga 13, tipo) — `public sealed record PredictedTrack(string Name, double X1, double Y1, double X2, double Y2);`. Traccia prevista di un satellite nel frame, in pixel del frame ridotto (stesse coordinate di Streak).
- **IPredictedTrackProvider** (riga 20, tipo) — `public interface IPredictedTrackProvider`. Fonte delle tracce previste nel frame.

### LumaFrame.cs

- **LumaFrame** (riga 7, tipo) — `public record LumaFrame(int Width, int Height, byte[] Pixels, DateTimeOffset TimestampUtc);`. Fotogramma di luminanza a 8 bit, riga per riga: il pixel (x, y) sta in `Pixels[y * Width + x]`.

### Streak.cs

- **Streak** (riga 19, tipo) — `public sealed record Streak(`. Scia luminosa rilevata in un frame.

### StreakClassification.cs

- **StreakClass** (riga 4, tipo) — `public enum StreakClass`. Classe assegnata a una scia dal StreakClassifier.
- **ClassifiedStreak** (riga 22, tipo) — `public sealed record ClassifiedStreak(Streak Streak, StreakClass Class);`. Una scia con la sua classe.
- **FrameClassification** (riga 28, tipo) — `public sealed record FrameClassification(`. Esito della classificazione di un frame.

### StreakClassifier.cs

- **StreakClassifier** (riga 25, tipo) — `public sealed class StreakClassifier`. Classifica le scie prodotte dal StreakDetector frame per frame.
- **Reset** (riga 46) — `public void Reset()`. Interrompe la catena dei frame consecutivi (per esempio dopo un cambio di sessione o di camera).
- **Classify** (riga 58) — `public FrameClassification Classify(DateTimeOffset timestampUtc, IReadOnlyList<Streak> streaks, bool frameDiscarded)`. Classifica le scie di un frame.
- **Classify** (riga 88) — `private StreakClass Classify(Streak streak, IReadOnlyList<PredictedTrack> predicted, bool consecutive)`.
- **MatchesAnyTrack** (riga 103) — `private bool MatchesAnyTrack(Streak streak, IReadOnlyList<PredictedTrack> predicted)`.
- **IsRepeat** (riga 123) — `private bool IsRepeat(Streak current, Streak previous)`.
- **IsBolide** (riga 138) — `private bool IsBolide(Streak streak)`.
- **Midpoint** (riga 143) — `private static (double X, double Y) Midpoint(Streak s)`.
- **IsZeroLength** (riga 145) — `private static bool IsZeroLength(double x1, double y1, double x2, double y2)`.
- **AngleBetweenDeg** (riga 149) — `private static double AngleBetweenDeg(double ax1, double ay1, double ax2, double ay2, double bx1, double by1, double bx2, double by2)`.
- **DistanceToSegment** (riga 156) — `private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)`.

### StreakClassifierOptions.cs

- **StreakClassifierOptions** (riga 8, tipo) — `public sealed record StreakClassifierOptions`. Soglie del StreakClassifier.
- **Validate** (riga 51) — `public void Validate()`. Verifica che i parametri siano coerenti.
- **RequireNonNegative** (riga 64) — `private static void RequireNonNegative(double value, string name)`.
- **RequirePositive** (riga 70) — `private static void RequirePositive(double value, string name)`.
- **RequireAngle** (riga 76) — `private static void RequireAngle(double value, string name)`.

### StreakDetector.cs

- **StreakDetector** (riga 22, tipo) — `public sealed class StreakDetector`. Rilevatore di scie luminose (meteore, satelliti in transito) in sequenze di frame notturni.
- **StreakDetector** (riga 66) — `public StreakDetector(StreakDetectorOptions options)`. Crea il rilevatore.
- **Reset** (riga 74) — `public void Reset()`. Azzera il fondo adattivo.
- **Detect** (riga 88) — `public IReadOnlyList<Streak> Detect(`. Elabora un frame e restituisce le scie trovate, ordinate per punteggio decrescente.
- **DiffBin** (riga 195) — `private static int DiffBin(float d)`.
- **HistogramMedianBin** (riga 199) — `private static int HistogramMedianBin(int[] hist, int total)`.
- **IsInsideAnyCircle** (riga 214) — `private static bool IsInsideAnyCircle(int x, int y, ReadOnlySpan<(int X, int Y, int R)> circles)`.
- **FloodFill** (riga 229) — `private ComponentAccumulator FloodFill(int start, int width, int height)`.
- **ComponentAccumulator** (riga 273, tipo) — `private struct ComponentAccumulator`.
- **Add** (riga 284) — `public void Add(int x, int y, double excess)`.
- **BuildStreak** (riga 301) — `private Streak? BuildStreak(ComponentAccumulator acc, double noiseSigma)`.
- **EnsureBuffers** (riga 373) — `private void EnsureBuffers(int width, int height)`.
- **ValidateFrame** (riga 390) — `private static void ValidateFrame(LumaFrame frame)`.

### StreakDetectorOptions.cs

- **StreakDetectorOptions** (riga 6, tipo) — `public sealed record StreakDetectorOptions`. Parametri del rilevatore di scie.
- **Validate** (riga 33) — `public void Validate()`. Verifica che i parametri siano coerenti.


## AllSky.Engine (src/AllSky.Engine)

### AllSkyEngine.Calibration.cs

- **AllSkyEngine** (riga 18, tipo) — `public sealed partial class AllSkyEngine : ICalibrationService`. Calibrazione ottica (P3.3): alimentazione del collector dal Sole e implementazione di ICalibrationService.
- **GetSnapshot** (riga 26) — `public CalibrationSnapshot GetSnapshot()`.
- **AcceptProposal** (riga 38) — `public CalibrationActionResult AcceptProposal()`.
- **RejectProposal** (riga 74) — `public CalibrationActionResult RejectProposal()`.
- **SetManual** (riga 87) — `public CalibrationActionResult SetManual(FisheyeModel model)`.
- **FeedCalibration** (riga 104) — `private void FeedCalibration(RawFrame raw)`. Cerca il Sole nel frame e, se la posizione e' valida, lo aggiunge al collector.
- **LumaOf** (riga 141) — `private static byte[] LumaOf(RawFrame raw)`.
- **RaiseCalibration** (riga 153) — `private void RaiseCalibration()`.

### AllSkyEngine.Detection.cs

- **AllSkyEngine** (riga 25, tipo) — `public sealed partial class AllSkyEngine`. Rilevamento di meteore, bolidi e transiti (P5.5): cablaggio nell'Engine.
- **OfferDetection** (riga 44) — `private void OfferDetection(RawFrame raw, ProcessedFrame processed)`. Riduce il frame archiviato e lo accoda al rilevamento.
- **EnsureDetectionWorker** (riga 98) — `private Channel<DetectionJob> EnsureDetectionWorker()`. Crea il worker alla prima richiesta.
- **RunDetectionAsync** (riga 129) — `private async Task RunDetectionAsync(ChannelReader<DetectionJob> reader, DetectionPipeline pipeline, CancellationToken ct)`.
- **StopDetectionAsync** (riga 159) — `private async Task StopDetectionAsync()`. Chiude il rilevamento: smette di accettare frame, attende il worker (al massimo un minuto), poi salva le meteore in coda e rimuove le cartelle di lavoro.
- **TransitPolicyFor** (riga 217) — `private TransitCadencePolicy? TransitPolicyFor(AppConfig config)`. Modalità transito (P4.6) per la cattura: con `capture.transitMode` spenta restituisce null e il loop resta com'era.

### AllSkyEngine.Frames.cs

- **AllSkyEngine** (riga 27, tipo) — `public sealed partial class AllSkyEngine`. Frame e prodotti notturni: consumer del ciclo di cattura, accumulatori di keogram e startrails, cambio notte a mezzogiorno locale, salvataggio periodico e scrittura dei file.
- **NightSnapshot** (riga 53, tipo) — `private sealed record NightSnapshot(`. Copia dei prodotti di una notte: byte gia' calcolati, nessun riferimento agli accumulatori.
- **OnFrameAsync** (riga 65) — `private Task OnFrameAsync(RawFrame raw, CancellationToken ct)`. Consumer dei frame: elabora, archivia, aggiorna gli accumulatori e lo stato, poi emette l'evento.
- **WriteFrameSidecar** (riga 133) — `private void WriteFrameSidecar(RawFrame raw, ProcessedFrame processed)`. Scrive il sidecar `<frame>.json` accanto al JPEG appena archiviato.
- **OfferToPublisher** (riga 179) — `private void OfferToPublisher(RawFrame raw, ProcessedFrame processed)`. Offre il frame archiviato alla pubblicazione.
- **PublishModeOf** (riga 211) — `private static PublishMode? PublishModeOf(string phase)`.
- **ReportFrameFailure** (riga 219) — `private void ReportFrameFailure(string what, Exception ex, string advice)`.
- **OnTick** (riga 230) — `private void OnTick()`. Chiamato dal ciclo prima di ogni attesa: chiude la notte se e' passato mezzogiorno locale e salva i prodotti parziali ogni 30 minuti.
- **AccumulateFrame** (riga 267) — `private void AccumulateFrame(RawFrame raw, ProcessedFrame processed)`. Aggiorna gli accumulatori con il frame, dopo aver gestito l'eventuale cambio notte.
- **AdvanceNightLocked** (riga 298) — `private NightSnapshot? AdvanceNightLocked(string night)`. Se `night` e' successiva alla notte degli accumulatori, chiude quella notte: restituisce la copia dei prodotti e azzera gli accumulatori.
- **AddLocked** (riga 320) — `private void AddLocked(ProcessedFrame processed, DateTimeOffset timestamp)`.
- **TakeSnapshotLocked** (riga 340) — `private NightSnapshot TakeSnapshotLocked()`. Copia i prodotti degli accumulatori della notte corrente.
- **StartFinishNight** (riga 357) — `private void StartFinishNight(NightSnapshot snapshot, AppConfig config)`. Chiude una notte in background: scrive keogram e startrails, accoda il timelapse, applica la retention.
- **FinishNight** (riga 362) — `private void FinishNight(NightSnapshot snapshot, AppConfig config)`.
- **WriteNightProducts** (riga 384) — `private void WriteNightProducts(NightSnapshot snapshot, AppConfig config)`. Scrive keogram.jpg e startrails.jpg (anche parziali) secondo la configurazione.
- **WriteJpeg** (riga 410) — `private void WriteJpeg(string path, byte[] rgb, int width, int height)`. Codifica e scrive un JPEG in modo atomico (file temporaneo poi rinomina).
- **FlushPartialProducts** (riga 425) — `private void FlushPartialProducts()`. Salvataggio dei prodotti parziali alla chiusura del servizio, cosi' non si perde il lavoro della notte.
- **ProcessWith** (riga 451) — `private ProcessedFrame ProcessWith(RawFrame raw)`.
- **FrameIdOf** (riga 464) — `private static string FrameIdOf(DateTimeOffset timestampUtc)`.

### AllSkyEngine.Nights.cs

- **AllSkyEngine** (riga 8, tipo) — `public sealed partial class AllSkyEngine`. Notti e frame dell'archivio: elenchi, percorsi e cancellazione.
- **ListNights** (riga 13) — `public IReadOnlyList<NightDto> ListNights()`.
- **ListFrames** (riga 46) — `public IReadOnlyList<FrameDto> ListFrames(string nightId, int offset, int limit)`.
- **GetFramePath** (riga 55) — `public string GetFramePath(string nightId, string frameId)`.
- **GetThumbPath** (riga 58) — `public string GetThumbPath(string nightId, string frameId)`.
- **GetProductPath** (riga 61) — `public string GetProductPath(string nightId, NightProduct kind)`.
- **DeleteNight** (riga 64) — `public bool DeleteNight(string nightId)`.
- **SunBands** (riga 88) — `private static IReadOnlyList<SunBandDto> SunBands(string nightId, AppConfig config, TimeZoneInfo tz)`.
- **TimelapseWire** (riga 102) — `private static string TimelapseWire(TimelapseState state)`.

### AllSkyEngine.Passes.cs

- **AllSkyEngine** (riga 21, tipo) — `public sealed partial class AllSkyEngine : IPassForecastService`. Previsione dei passaggi di satelliti (P4.3): implementazione di IPassForecastService e ciclo di vita del servizio.
- **InitPassForecasting** (riga 34) — `private void InitPassForecasting(IOrbitPropagatorFactory? propagators, ITleFetcher? fetcher)`. Collega la previsione alle dipendenze.
- **StartPassForecasting** (riga 45) — `private void StartPassForecasting()`. Avvia il ciclo della previsione.
- **StopPassForecastingAsync** (riga 66) — `private async Task StopPassForecastingAsync()`. Ferma il ciclo della previsione e attende la fine dell'ultimo passo.
- **PassLoopAsync** (riga 105) — `private async Task PassLoopAsync(CancellationToken ct)`.
- **PassTickAsync** (riga 134) — `private async Task PassTickAsync(CancellationToken ct)`.
- **GetForecast** (riga 162) — `public PassForecastSnapshot GetForecast()`.
- **GetTracks** (riga 175) — `public PassTrackState GetTracks()`.
- **ToPassSettings** (riga 197) — `private static PassForecastSettings ToPassSettings(AppConfig config)`. Traduce la configurazione nelle impostazioni della previsione.

### AllSkyEngine.Timelapse.cs

- **AllSkyEngine** (riga 12, tipo) — `public sealed partial class AllSkyEngine`. Coda dei timelapse: un solo job alla volta, in ordine di richiesta.
- **TimelapseFrame** (riga 17, tipo) — `private sealed record TimelapseFrame(string Id, string Path);`. Frame di un job: identificatore (per il sidecar) e percorso del JPEG archiviato.
- **JobState** (riga 25, tipo) — `private sealed class JobState`. Lavoro di timelapse noto alla coda: in attesa oppure in corso.
- **RequestTimelapseAsync** (riga 33) — `public Task<bool> RequestTimelapseAsync(string nightId)`.
- **TryEnqueueTimelapse** (riga 44) — `private bool TryEnqueueTimelapse(string nightId)`. Accoda il timelapse se la notte ha frame, non e' gia' in coda e il servizio e' avviato.
- **StartTimelapseWorker** (riga 66) — `private void StartTimelapseWorker()`.
- **StopTimelapseWorkerAsync** (riga 79) — `private async Task StopTimelapseWorkerAsync()`.
- **RunTimelapseWorkerAsync** (riga 124) — `private async Task RunTimelapseWorkerAsync(ChannelReader<string> reader, CancellationToken ct)`.
- **RunTimelapseJobAsync** (riga 139) — `private async Task RunTimelapseJobAsync(string nightId, CancellationToken ct)`.
- **SetJobProgress** (riga 199) — `private void SetJobProgress(string nightId, double progress)`.
- **SafeSetTimelapseState** (riga 221) — `private void SafeSetTimelapseState(NightArchive archive, string nightId, TimelapseState state)`.
- **ClearStaleTimelapseMarkers** (riga 234) — `private void ClearStaleTimelapseMarkers()`. Marker "generating" lasciati da un arresto brusco: senza job in coda diventano "none".
- **BuildTimelapseOverlay** (riga 250) — `private FrameOverlayPlan? BuildTimelapseOverlay(`. Maschera del timelapse, o null se disattivata.
- **CollectFrames** (riga 287) — `private static List<TimelapseFrame> CollectFrames(NightArchive archive, string nightId)`.

### AllSkyEngine.cs

- **AllSkyEngine** (riga 21, tipo) — `public sealed partial class AllSkyEngine : IAllSkyService, AllSky.Core.Calibration.ICalibrationService, IAsyncDisposable`. Servizio applicativo dell'all-sky: cattura, elaborazione, archivio, prodotti e configurazione.
- **AllSkyEngine** (riga 94) — `public AllSkyEngine(EngineDependencies dependencies)`. Crea il servizio e carica la configurazione.
- **StartAsync** (riga 149) — `public async Task StartAsync(CancellationToken cancellationToken = default)`. Avvia il servizio: coda dei timelapse, pulizia dei marker residui e connessione alla camera configurata.
- **StopAsync** (riga 197) — `public async Task StopAsync()`. Arresta il servizio in modo pulito: ferma il ciclo di cattura, rilascia la camera, salva i prodotti parziali della notte in corso, attende i lavori in background e ferma la coda dei timelapse.
- **DisposeAsync** (riga 229) — `public async ValueTask DisposeAsync()`. Equivale a StopAsync.
- **GetStatus** (riga 237) — `public StatusDto GetStatus()`.
- **GetLatestJpeg** (riga 292) — `public byte[]? GetLatestJpeg(bool raw)`.
- **DiscoverCamerasAsync** (riga 330) — `public async Task<IReadOnlyList<CameraCandidate>> DiscoverCamerasAsync(CancellationToken cancellationToken)`.
- **ConnectCameraAsync** (riga 352) — `public async Task<ConnectResult> ConnectCameraAsync(string id, CancellationToken cancellationToken)`.
- **DisconnectCameraAsync** (riga 379) — `public async Task DisconnectCameraAsync(CancellationToken cancellationToken)`.
- **GetConfig** (riga 398) — `public AppConfig GetConfig()`.
- **UpdateConfig** (riga 404) — `public ConfigUpdateResult UpdateConfig(AppConfig config)`.
- **ConnectInternalAsync** (riga 451) — `private async Task<ConnectResult> ConnectInternalAsync(string id, CancellationToken ct)`.
- **ConnectFailed** (riga 526) — `private ConnectResult ConnectFailed(string message)`.
- **StartLoop** (riga 534) — `private void StartLoop(ICameraSource camera, bool keepExposure)`. Avvia un nuovo ciclo di cattura.
- **OnLoopError** (riga 606) — `private void OnLoopError(Exception ex)`.
- **StopCaptureAsync** (riga 613) — `private async Task StopCaptureAsync()`.
- **StopLoopAsync** (riga 632) — `private async Task StopLoopAsync()`.
- **RestartLoopAsync** (riga 673) — `private async Task RestartLoopAsync()`. Riavvia il ciclo mantenendo la camera collegata e l'esposizione corrente.
- **ApplyChangesAsync** (riga 693) — `private async Task ApplyChangesAsync(AppConfig previous, AppConfig config)`. Applica in background i cambi di configurazione che richiedono di toccare la camera o il ciclo.
- **DisposeQuietlyAsync** (riga 744) — `private async Task DisposeQuietlyAsync(ICameraSource camera)`.
- **ResolveKindAsync** (riga 756) — `private async Task<string> ResolveKindAsync(string id, CancellationToken ct)`.
- **RememberKinds** (riga 786) — `private void RememberKinds(IReadOnlyList<CameraCandidate> candidates)`.
- **PersistCameraId** (riga 798) — `private void PersistCameraId(string id)`. Salva su disco l'identificativo della camera collegata, se diverso da quello in configurazione.
- **WaitForBackgroundAsync** (riga 828) — `private async Task WaitForBackgroundAsync()`. Chiusura del servizio: attesa dei lavori in background e rilascio delle risorse.
- **TrackBackground** (riga 847) — `private void TrackBackground(Task task)`.
- **Celestial** (riga 858) — `private static (double? Sun, double? Moon, string? Phase) Celestial(AppConfig config, DateTimeOffset now)`.
- **Disk** (riga 872) — `private DiskDto? Disk(NightArchive archive, DateTimeOffset now)`.
- **SetError** (riga 906) — `private void SetError(string? message)`.
- **ConfigSnapshot** (riga 914) — `private AppConfig ConfigSnapshot()`.
- **TimeZoneOf** (riga 922) — `private TimeZoneInfo TimeZoneOf()`.
- **ArchiveOf** (riga 930) — `private NightArchive ArchiveOf()`.
- **SnapshotJobsLocked** (riga 938) — `private IReadOnlyList<JobDto> SnapshotJobsLocked()`.
- **RaiseStatus** (riga 944) — `private void RaiseStatus()`. Emette lo stato corrente.
- **Notify** (riga 954) — `private void Notify<T>(Action<T>? handler, T value)`.
- **Fmt** (riga 975) — `private static string Fmt(double valueMs)`. Formato invariante di un valore in millisecondi, per i messaggi di log.
- **ValidateAll** (riga 977) — `private static IReadOnlyList<ValidationError> ValidateAll(AppConfig config)`.
- **ConfigErrorText** (riga 1021) — `private static string ConfigErrorText(IEnumerable<ValidationError> errors)`.
- **ResolveTimeZone** (riga 1027) — `private static TimeZoneInfo? ResolveTimeZone(string? id)`.
- **CreateArchive** (riga 1044) — `private NightArchive CreateArchive(AppConfig config, TimeZoneInfo tz)`.

### Calibration/SunCalibrationSession.cs

- **SunCalibrationSession** (riga 22, tipo) — `public sealed class SunCalibrationSession`. Stato della calibrazione dal Sole in memoria: punti raccolti, proposta in sospeso e frequenza di ricerca del Sole.
- **ObserveFrame** (riga 37) — `public void ObserveFrame(int width, int height)`. Registra le dimensioni del frame in cui si raccolgono i punti.
- **ShouldDetect** (riga 47) — `public bool ShouldDetect(DateTimeOffset utc, double sunAltDeg)`. Vero se conviene cercare il Sole in questo frame: altezza ammessa e nessuna attesa in corso.
- **RecordMiss** (riga 61) — `public void RecordMiss(DateTimeOffset utc)`. Segna un tentativo senza Sole rilevato: il prossimo sara' dopo RetryAfterMiss.
- **AddSighting** (riga 73) — `public bool AddSighting(DateTimeOffset utc, double x, double y, double altDeg, double azDeg)`. Aggiunge un avvistamento del Sole e, con abbastanza punti, rifa' il fit.
- **SetManual** (riga 100) — `public bool SetManual(FisheyeModel model)`. Propone un modello manuale al posto della proposta in sospeso.
- **Reject** (riga 127) — `public void Reject()`. Scarta la proposta in sospeso.
- **Pending** (riga 136) — `public CalibrationProposal? Pending()`. Proposta in sospeso, null se non c'e'.
- **Consume** (riga 148) — `public void Consume(CalibrationProposal accepted)`. Toglie la proposta solo se e' ancora quella indicata (confronto per riferimento): una proposta nuova arrivata nel frattempo non viene persa.
- **Snapshot** (riga 160) — `public CalibrationSnapshot Snapshot(OpticalCalibrationConfig? accepted)`. Foto dello stato per l'interfaccia.
- **FrameSize** (riga 179) — `public (int Width, int Height) FrameSize()`. Dimensioni del frame di riferimento (0 se nessun frame ancora).
- **Refit** (riga 188) — `private void Refit()`.

### Detecting/DetectionJob.cs

- **DetectionJob** (riga 11, tipo) — `internal sealed record DetectionJob(`. Un frame archiviato e pronto per il rilevamento.
- **RecentFrameIds** (riga 28, tipo) — `internal sealed class RecentFrameIds`. Ultimi cinque identificativi di frame archiviati: la finestra [N−2, N+2] della clip.
- **Add** (riga 34) — `public void Add(string frameId)`.
- **Snapshot** (riga 41) — `public string[] Snapshot()`. Identificativi dal più vecchio al più recente.

### Detecting/DetectionMapping.cs

- **DetectionMapping** (riga 10, tipo) — `internal static class DetectionMapping`. Traduce la sezione `detection` della configurazione nelle opzioni dei moduli (P5.5).
- **ToDetector** (riga 12) — `public static StreakDetectorOptions ToDetector(AppConfig.DetectionConfig.StreakSettings s)`.
- **ToClassifier** (riga 28) — `public static StreakClassifierOptions ToClassifier(AppConfig.DetectionConfig.StreakSettings s, int captureIntervalSeconds)`. Classificatore.
- **ToConfirmation** (riga 40) — `public static TransitConfirmationOptions ToConfirmation(AppConfig.DetectionConfig.TransitSettings t)`.
- **ToEventSaver** (riga 51) — `public static EventSaverOptions ToEventSaver(AppConfig.DetectionConfig d)`.
- **ToTransitSave** (riga 53) — `public static TransitSaveOptions ToTransitSave(AppConfig.DetectionConfig d)`.

### Detecting/DetectionPipeline.cs

- **DetectionPipeline** (riga 38, tipo) — `internal sealed class DetectionPipeline`. Elaborazione in background dei frame per il rilevamento (P5.5): meteore e bolidi, transiti dei satelliti previsti.
- **DetectionPipeline** (riga 77) — `public DetectionPipeline(`.
- **ProcessAsync** (riga 96) — `public async Task ProcessAsync(DetectionJob job, CancellationToken ct)`. Elabora un frame: transiti e, di notte, meteore.
- **ShutdownAsync** (riga 124) — `public async Task ShutdownAsync(CancellationToken ct)`. Chiusura del servizio: salva le meteore ancora in coda (senza il frame successivo, quindi senza controllo di ripetizione), abbandona i passaggi in corso e rimuove la cartella di lavoro.
- **SessionKey** (riga 156, tipo) — `private sealed record SessionKey(`. Chiave della sessione: se cambia, i moduli vengono ricostruiti (il fondo del rilevatore riparte da zero).
- **Session** (riga 172, tipo) — `private sealed class Session`. Moduli di una sessione.
- **EnsureSession** (riga 193) — `private Session? EnsureSession(DetectionJob job)`.
- **BuildSession** (riga 244) — `private Session BuildSession(SessionKey key, DetectionJob job)`.
- **ClipWriterFor** (riga 283) — `private IClipWriter ClipWriterFor()`.
- **PendingFrame** (riga 307, tipo) — `private sealed record PendingFrame(`. Frame della catena in attesa del successivo: il frame N si salva quando arriva N+2.
- **MeteorStepAsync** (riga 318) — `private async Task MeteorStepAsync(Session session, DetectionJob job, CancellationToken ct)`.
- **MoonCircles** (riga 360) — `private static (int X, int Y, int R)[] MoonCircles(Session session, AppConfig config, DateTimeOffset utc)`. Esclusione della Luna: un cerchio attorno alla sua posizione, se è sopra l'orizzonte e la calibrazione è valida.
- **FlushPendingAsync** (riga 379) — `private async Task FlushPendingAsync(Session session, string[] windowIds, CancellationToken ct)`.
- **SaveCandidatesAsync** (riga 396) — `private async Task SaveCandidatesAsync(Session session, PendingFrame frame, FrameClassification? next, string[] windowIds, CancellationToken ct)`.
- **StagedFrame** (riga 430, tipo) — `private sealed record StagedFrame(DateTimeOffset TimestampUtc, string FrameId, string NightId, string Path);`. Frame ridotto di un passaggio, già su disco.
- **ActiveTransit** (riga 433, tipo) — `private sealed class ActiveTransit`. Passaggio in corso di raccolta: i frame in disco e il passaggio da confermare alla fine.
- **TransitStepAsync** (riga 448) — `private async Task TransitStepAsync(Session session, DetectionJob job, CancellationToken ct)`.
- **StageAsync** (riga 491) — `private static async Task StageAsync(ActiveTransit active, DetectionJob job, CancellationToken ct)`.
- **FinalizeTransitAsync** (riga 507) — `private async Task FinalizeTransitAsync(Session session, ActiveTransit active, AppConfig config, CancellationToken ct)`.
- **LumaFramesOf** (riga 590) — `private static IEnumerable<LumaFrame> LumaFramesOf(IEnumerable<StagedFrame> frames)`.
- **ReadReduced** (riga 600) — `private static (int Width, int Height, byte[] Rgb) ReadReduced(string path)`. Legge un frame ridotto scritto da StageAsync: intestazione di 8 byte con larghezza e altezza, poi RGB.
- **KeyOf** (riga 618) — `private static string KeyOf(PassForecastItem item)`.
- **StorageFolderOf** (riga 621) — `private static string StorageFolderOf(AppConfig config)`.
- **StagingRootOf** (riga 629) — `private static string StagingRootOf(AppConfig config)`.
- **DeleteQuietly** (riga 631) — `private void DeleteQuietly(string path)`.

### Detecting/ForecastTrackProvider.cs

- **ForecastTrackProvider** (riga 14, tipo) — `internal sealed class ForecastTrackProvider : IPredictedTrackProvider`. Tracce previste per il classificatore (P5.5).
- **ForecastTrackProvider** (riga 24) — `public ForecastTrackProvider(`.
- **GetTracks** (riga 38) — `public IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc)`.

### Detecting/ForecastTransitWindowSource.cs

- **ForecastTransitWindowSource** (riga 10, tipo) — `public sealed class ForecastTransitWindowSource : ITransitWindowSource`. Finestre dei passaggi previsti per la modalità transito (P4.6), lette dalla previsione corrente.
- **ForecastTransitWindowSource** (riga 15) — `public ForecastTransitWindowSource(Func<IReadOnlyList<PassForecastItem>> passes)`.
- **NextWindow** (riga 21) — `public TransitWindow? NextWindow(DateTimeOffset utc)`.

### Detecting/LumaReduction.cs

- **LumaReduction** (riga 7, tipo) — `internal static class LumaReduction`. Riduzione del frame per il rilevamento: media a blocchi k×k fino al lato lungo richiesto, poi luminanza.
- **Factor** (riga 10) — `public static int Factor(int width, int height, int longSidePx)`. Fattore intero k tale che il lato lungo diventa circa `longSidePx`.
- **Reduce** (riga 20) — `public static (int Width, int Height, byte[] Rgb) Reduce(byte[] rgb, int width, int height, int factor)`. Riduce un'immagine RGB 8 bit con media a blocchi k×k (le righe e colonne in eccesso vengono scartate).
- **Luma** (riga 61) — `public static byte[] Luma(byte[] rgb, int width, int height)`. Luminanza 8 bit (pesi Rec.

### Detecting/SkyScaling.cs

- **SkyScaling** (riga 17, tipo) — `internal static class SkyScaling`. Calibrazione accettata portata sulle coordinate del frame ridotto del rilevatore (P5.5).
- **ForReducedFrame** (riga 23) — `public static SkyCalibrationResult? ForReducedFrame(`. Calibrazione valida per il frame ridotto di dimensioni `detWidth` x `detHeight`, oppure null se manca, è invalida o non corrisponde alle dimensioni del frame intero.
- **PixelToSkyAdapter** (riga 61, tipo) — `internal sealed class PixelToSkyAdapter : IPixelToSky`. Pixel del frame ridotto → alt/az, con il modello della calibrazione.
- **PixelToSkyAdapter** (riga 65) — `public PixelToSkyAdapter(FisheyeModel? model)`.
- **ToAltAz** (riga 70) — `public AltAz? ToAltAz(double x, double y)`.

### Detecting/UnavailableClipWriter.cs

- **UnavailableClipWriter** (riga 9, tipo) — `internal sealed class UnavailableClipWriter : IClipWriter`. Scrittore di clip usato quando ffmpeg non è configurato: ogni richiesta fallisce con un motivo leggibile.
- **UnavailableClipWriter** (riga 13) — `public UnavailableClipWriter(string reason)`.
- **WriteAsync** (riga 18) — `public Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)`.

### Dtos.cs

- **StatusDto** (riga 21, tipo) — `public sealed record StatusDto(`. Stato completo dell'applicazione: corrisponde a `GET /api/status` di docs/API.md.
- **CameraStatusDto** (riga 40, tipo) — `public sealed record CameraStatusDto(string Id, string Name, string Kind, bool Connected);`. Camera di sistema: identificativo, nome, tipo e stato di connessione.
- **LastFrameDto** (riga 49, tipo) — `public sealed record LastFrameDto(`. Ultimo frame archiviato con i dati di esposizione.
- **DiskDto** (riga 60, tipo) — `public sealed record DiskDto(double? FreeGb, double UsedGb);`. Uso del disco in GB (1 GB = 1024^3 byte).
- **JobDto** (riga 66, tipo) — `public sealed record JobDto(string Kind, string NightId, double Progress);`. Lavoro in coda o in corso.
- **FrameEvent** (riga 72, tipo) — `public sealed record FrameEvent(string Id, DateTimeOffset Timestamp, double? SensorTempC = null);`. Evento emesso per ogni frame archiviato.
- **NightDto** (riga 86, tipo) — `public sealed record NightDto(`. Notte osservativa (da mezzogiorno a mezzogiorno locale), corrisponde a un elemento di `GET /api/nights`.
- **SunBandDto** (riga 101, tipo) — `public sealed record SunBandDto(string Phase, DateTimeOffset From, DateTimeOffset To);`. Intervallo continuo di una fase del cielo.
- **FrameDto** (riga 107, tipo) — `public sealed record FrameDto(string Id, DateTimeOffset Timestamp, string Phase);`. Riga di `GET /api/nights/{id}/frames`.
- **ConnectResult** (riga 113, tipo) — `public sealed record ConnectResult(bool Ok, string? Error, string? Warning = null);`. Esito di `POST /api/camera/connect`.
- **ConfigUpdateResult** (riga 118, tipo) — `public sealed record ConfigUpdateResult(bool Ok, IReadOnlyList<ValidationError> Errors);`. Esito di `PUT /api/config`.

### EngineDependencies.cs

- **EngineDependencies** (riga 31, tipo) — `public sealed record EngineDependencies(`. Dipendenze iniettate in AllSkyEngine.

### EngineException.cs

- **EngineException** (riga 7, tipo) — `public sealed class EngineException : Exception`. Errore atteso del servizio.
- **EngineException** (riga 11) — `public EngineException(string message)`. Crea l'eccezione con un messaggio per l'utente.
- **EngineException** (riga 19) — `public EngineException(string message, Exception inner)`. Crea l'eccezione con un messaggio e la causa originale.

### EngineSupport.cs

- **TickingDelay** (riga 10, tipo) — `internal sealed class TickingDelay : ICaptureDelay`. Attesa del ciclo di cattura che esegue un'azione prima di ogni attesa.
- **TickingDelay** (riga 16) — `public TickingDelay(ICaptureDelay inner, Action onTick)`. Crea l'attesa che chiama `onTick` prima di delegare a `inner`.
- **DelayAsync** (riga 23) — `public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)`.
- **ActionProgress** (riga 31, tipo) — `internal sealed class ActionProgress : IProgress<double>`. Avanzamento riportato da una funzione lunga tramite una semplice azione.
- **ActionProgress** (riga 36) — `public ActionProgress(Action<double> action)`. Crea il collegamento con l'azione.
- **Report** (riga 42) — `public void Report(double value)`.

### ExposureLimits.cs

- **ExposureLimits** (riga 8, tipo) — `public static class ExposureLimits`. Sanifica i limiti di esposizione letti dal driver prima di usarli nel controller di esposizione.
- **IsUsable** (riga 17) — `public static bool IsUsable(double valueMs)`. Vero se `valueMs` è finito e strettamente positivo.
- **Sanitize** (riga 28) — `public static (double Min, double Max) Sanitize(double infoMinMs, double infoMaxMs, double configMaxMs)`. Restituisce (min, max) in millisecondi.

### Forecasting/PassForecaster.cs

- **PassForecastSettings** (riga 17, tipo) — `public sealed record PassForecastSettings(`. Impostazioni della previsione, già tradotte dalla configurazione.
- **PassForecaster** (riga 52, tipo) — `public sealed class PassForecaster`. Previsione dei passaggi: download dei TLE con la sorgente (che applica la policy CelesTrak), calcolo dei passaggi, elenco per la UI, eventi di inizio e fine, tracce in pixel.
- **PassForecaster** (riga 93) — `public PassForecaster(ITleFetcher fetcher, IOrbitPropagatorFactory factory)`. Crea il servizio con la sorgente dei TLE e la fabbrica dei propagatori.
- **PassKey** (riga 102, tipo) — `private readonly record struct PassKey(int NoradId, DateTimeOffset StartUtc);`. Un passaggio in una forma comoda per i confronti: numero NORAD e istante di inizio.
- **Entry** (riga 105, tipo) — `private sealed record Entry(PassForecastItem Item, SatellitePass Pass, IOrbitPropagator Propagator);`. Un passaggio calcolato con il suo propagatore, per le tracce.
- **RefreshAsync** (riga 111) — `public async Task<bool> RefreshAsync(PassForecastSettings settings, DateTimeOffset now, CancellationToken cancellationToken)`. Scarica i TLE se dovuto e ricalcola la previsione se dovuto.
- **RefreshCoreAsync** (riga 126) — `private async Task<bool> RefreshCoreAsync(PassForecastSettings settings, DateTimeOffset now, CancellationToken ct)`.
- **Compute** (riga 196) — `private void Compute(PassForecastSettings settings, DateTimeOffset now)`. Calcola i passaggi per tutti i bersagli con un TLE.
- **Snapshot** (riga 285) — `public PassForecastSnapshot Snapshot(DateTimeOffset now)`. Previsione riferita a `now`: passaggi ancora da finire, quello in corso e il prossimo.
- **Evaluate** (riga 312) — `public IReadOnlyList<PassChange> Evaluate(DateTimeOffset now)`. Cambiamenti dall'ultima chiamata: passaggi iniziati, finiti e ricalcolo della previsione.
- **Tracks** (riga 361) — `public IReadOnlyList<PassTrackPath> Tracks(`. Tracce in pixel dei passaggi in corso o che iniziano entro `lead`, proiettate con la calibrazione.
- **WatchedPass** (riga 408, tipo) — `internal sealed record WatchedPass(PassForecastItem Item, SatellitePass Pass, IOrbitPropagator Propagator);`. Passaggio calcolato con il suo propagatore, per il rilevamento dei transiti (P5.5).
- **WatchedPasses** (riga 411) — `internal IReadOnlyList<WatchedPass> WatchedPasses(DateTimeOffset from, DateTimeOffset until)`. Passaggi calcolati che finiscono dopo `from` e iniziano prima di `until`.
- **ProjectWatched** (riga 426) — `internal ProjectedPassTrack ProjectWatched(WatchedPass watched, SkyCalibrationResult calibration, ObserverSite site)`. Proietta un passaggio nei pixel con la calibrazione.
- **TrackedSatellites** (riga 438) — `internal IReadOnlyList<TrackedSatellite> TrackedSatellites(PassForecastSettings settings)`. Propagatori nuovi, uno per bersaglio con TLE, per il classificatore di scie.
- **ToItem** (riga 464) — `private static PassForecastItem ToItem(SatelliteTarget target, SatellitePass pass)`.

### IAllSkyService.cs

- **IAllSkyService** (riga 10, tipo) — `public interface IAllSkyService`. Servizio applicativo esposto dall'Host HTTP.

### ICameraProvider.cs

- **CameraCandidate** (riga 10, tipo) — `public sealed record CameraCandidate(string Id, string Name, string Kind, string Detail);`. Candidato trovato dalla ricerca delle camere.
- **ICameraProvider** (riga 15, tipo) — `public interface ICameraProvider`. Fornitore di camere iniettato nell'Engine.


## AllSky.Events (src/AllSky.Events)

### EventArchive.cs

- **EventArchive** (riga 23, tipo) — `public sealed class EventArchive`. Archivio su disco degli eventi.
- **EventArchive** (riga 41) — `public EventArchive(string rootFolder)`. Apre l'archivio sotto `rootFolder`.
- **GetEventFolder** (riga 48) — `public string GetEventFolder(string nightId, string eventId)`. Cartella di un evento.
- **GetEventFilePath** (riga 57) — `public string GetEventFilePath(string nightId, string eventId, string fileName)`. Percorso di un file dell'evento.
- **CreateEventFolder** (riga 67) — `public string CreateEventFolder(string nightId, string eventId)`. Crea la cartella di un evento nuovo.
- **WriteFile** (riga 83) — `public void WriteFile(string nightId, string eventId, string fileName, byte[] data)`. Scrive un file dentro la cartella di un evento esistente, in modo atomico.
- **WriteRecord** (riga 98) — `public void WriteRecord(EventRecord record)`. Scrive `event.json` di un evento esistente, in modo atomico.
- **Get** (riga 108) — `public EventRecord? Get(string nightId, string eventId)`. Evento per notte e id, o null se non esiste o non e' leggibile.
- **List** (riga 120) — `public IReadOnlyList<EventRecord> List(EventQuery? query = null)`. Elenco degli eventi filtrati, dal piu' recente al piu' vecchio.
- **SetLabel** (riga 169) — `public EventRecord SetLabel(string nightId, string eventId, UserLabel label, string? reason = null)`. Imposta l'etichetta dell'utente.
- **SetStatus** (riga 196) — `public EventRecord SetStatus(string nightId, string eventId, EventStatus status, string? reason = null)`. Cambia lo stato dell'evento (per esempio Discarded) senza toccare i suoi file.
- **ExportZip** (riga 223) — `public string ExportZip(string nightId, string eventId, string zipPath)`. Esporta l'evento come zip: una cartella con il nome dell'id, dentro event.json e i file prodotti.
- **ReadRecordLocked** (riga 274) — `private EventRecord? ReadRecordLocked(string nightId, string eventId)`.
- **WriteRecordLocked** (riga 305) — `private void WriteRecordLocked(EventRecord record)`.
- **RequireFolder** (riga 313) — `private void RequireFolder(string nightId, string eventId)`.
- **NightIdsLocked** (riga 321) — `private IReadOnlyList<string> NightIdsLocked()`.
- **ValidateRecord** (riga 341) — `private static void ValidateRecord(EventRecord record)`.
- **FileNamesAreSafe** (riga 359) — `private static bool FileNamesAreSafe(EventRecord record)`.
- **ValidateIds** (riga 371) — `private static void ValidateIds(string nightId, string eventId)`.
- **ValidateNightId** (riga 380) — `private static void ValidateNightId(string nightId)`.
- **ValidateFileName** (riga 388) — `private static void ValidateFileName(string fileName)`.
- **EnsureUnderNights** (riga 396) — `private void EnsureUnderNights(string full)`.
- **Matches** (riga 406) — `private static bool Matches(EventRecord record, EventQuery query)`.
- **CleanText** (riga 414) — `private static string? CleanText(string? text, string paramName)`.
- **CreateJsonOptions** (riga 426) — `private static JsonSerializerOptions CreateJsonOptions()`.
- **AddEntry** (riga 437) — `private static void AddEntry(ZipArchive zip, string folder, string eventId, string fileName)`.
- **WriteAtomic** (riga 443) — `private static void WriteAtomic(string path, byte[] data)`. Scrittura atomica: file .tmp accanto alla destinazione, poi rinomina sovrascrivendo.
- **TryDelete** (riga 462) — `private static void TryDelete(string path)`.
- **IsLink** (riga 478) — `private static bool IsLink(FileSystemInfo fsi)`. Vero per symlink e junction.

### EventFrame.cs

- **EventFrame** (riga 4, tipo) — `public sealed record EventFrame`. Frame da cui salvare gli eventi: pixel a piena risoluzione e dimensioni del frame ridotto del rilevatore.
- **IPixelToSky** (riga 37, tipo) — `public interface IPixelToSky`. Posizione sul cielo di un punto del frame ridotto.
- **EventSaverOptions** (riga 44, tipo) — `public sealed record EventSaverOptions`. Parametri del salvataggio.
- **Validate** (riga 65) — `public void Validate()`. Verifica i valori.
- **Require** (riga 75) — `private static void Require(bool ok, string name)`.
- **EventSaveResult** (riga 85, tipo) — `public sealed record EventSaveResult`. Esito di un evento salvato.

### EventIds.cs

- **EventIds** (riga 11, tipo) — `public static class EventIds`. Identificatori degli eventi.
- **IsValid** (riga 16) — `public static bool IsValid(string? id)`. Vero se `id` e' un identificatore di evento valido.
- **ForStreak** (riga 23) — `public static string ForStreak(string frameId, int streakIndex)`. Id di una scia del frame: "<frameId>-<indice a 3 cifre>".
- **ForTransit** (riga 43) — `public static string ForTransit(int noradId, DateTimeOffset startUtc)`. Id di un transito: "transit-<NORAD>-<inizio UTC "yyyyMMddTHHmmssZ">".

### EventSaver.cs

- **EventSaver** (riga 25, tipo) — `public sealed class EventSaver`. Salva gli eventi meteora e bolide di un frame: ritaglio ingrandito, frame intero con la scia evidenziata, mini-clip dei frame vicini e metadati (`event.json`).
- **EventSaver** (riga 57) — `public EventSaver(`. Crea il salvatore.
- **SaveAsync** (riga 86) — `public async Task<IReadOnlyList<EventSaveResult>> SaveAsync(`. Salva un evento per ogni scia Meteor o Bolide del frame.
- **DiscardRepeats** (riga 131) — `public IReadOnlyList<EventRecord> DiscardRepeats(string nightId, string previousFrameId, FrameClassification next, string? reason = null)`. Marca come scartati gli eventi del frame `previousFrameId` che la classificazione del frame successivo riconosce come ripetuti (AereoOSatellite).
- **SaveStreakAsync** (riga 168) — `private async Task<EventSaveResult> SaveStreakAsync(EventFrame frame, int index, ClassifiedStreak item, CancellationToken ct)`.
- **ExistingFramePaths** (riga 267) — `private List<string> ExistingFramePaths(string nightId, IReadOnlyList<string> frameIds)`.
- **RemoveFolderQuietly** (riga 286) — `private void RemoveFolderQuietly(string nightId, string id)`.
- **IsRepeatedIn** (riga 302) — `private bool IsRepeatedIn(Streak streak, FrameClassification next)`.
- **IsRepeat** (riga 314) — `private bool IsRepeat(PixelSegment previous, PixelSegment current)`.
- **IsZeroLength** (riga 330) — `private static bool IsZeroLength(PixelSegment s)`.
- **AngleBetweenDeg** (riga 332) — `private static double AngleBetweenDeg(PixelSegment a, PixelSegment b)`.
- **ToSegment** (riga 339) — `private static PixelSegment ToSegment(Streak s)`.
- **ValidateFrame** (riga 341) — `private static void ValidateFrame(EventFrame frame)`.

### EventTypes.cs

- **EventKind** (riga 6, tipo) — `public enum EventKind`. Tipo di evento salvato.
- **EventStatus** (riga 22, tipo) — `public enum EventStatus`. Stato di un evento.
- **UserLabel** (riga 32, tipo) — `public enum UserLabel`. Etichetta data dall'utente nella vista Eventi.
- **AltAz** (riga 45, tipo) — `public sealed record AltAz`. Posizione sul cielo in gradi: altezza sopra l'orizzonte e azimut da nord verso est.
- **PixelSegment** (riga 55, tipo) — `public sealed record PixelSegment`. Segmento della scia nelle coordinate del frame ridotto del rilevatore (x verso destra, y verso il basso).
- **EventFiles** (riga 74, tipo) — `public sealed record EventFiles`. Nomi dei file dell'evento, relativi alla sua cartella.
- **EventRecord** (riga 97, tipo) — `public sealed record EventRecord`. Un evento salvato su disco (file `event.json`).
- **EventQuery** (riga 164, tipo) — `public sealed record EventQuery`. Filtro per List.

### FrameRaster.cs

- **CropRect** (riga 4, tipo) — `public sealed record CropRect`. Area quadrata di un frame da ritagliare.
- **FrameRaster** (riga 20, tipo) — `public static class FrameRaster`. Operazioni sui pixel RGB a 8 bit (righe contigue, R,G,B) per ritagli e evidenziazioni.
- **Copy** (riga 23) — `public static byte[] Copy(byte[] rgb)`. Copia del buffer RGB.
- **MaxPerPixel** (riga 36) — `public static byte[] MaxPerPixel(IEnumerable<byte[]> frames, int width, int height)`. Massimo per pixel e per canale di una sequenza di frame RGB della stessa dimensione (immagine composita di un transito).
- **ComputeCrop** (riga 76) — `public static CropRect ComputeCrop(int width, int height, double minX, double minY, double maxX, double maxY, int minSide, int marginPx)`. Quadrato centrato sulla scia (bounding box in pixel del frame), con margine e lato minimo.
- **ExtractMagnified** (riga 99) — `public static byte[] ExtractMagnified(byte[] rgb, int width, int height, CropRect crop, int factor)`. Ritaglio ingrandito con replica dei pixel (nessuna interpolazione): ogni pixel sorgente diventa un blocco `factor` x `factor`.
- **DrawSegment** (riga 131) — `public static void DrawSegment(byte[] rgb, int width, int height, double x1, double y1, double x2, double y2, int thickness, byte r, byte g, byte b)`. Segmento disegnato con un pennello quadrato di lato `thickness`, in place.
- **DrawRectangle** (riga 158) — `public static void DrawRectangle(byte[] rgb, int width, int height, double left, double top, double right, double bottom, int thickness, byte r, byte g, byte b)`. Rettangolo di contorno con spessore dato, in place.
- **SetPixel** (riga 166) — `private static void SetPixel(byte[] rgb, int width, int height, int x, int y, byte r, byte g, byte b)`.

### IClipWriter.cs

- **IClipWriter** (riga 9, tipo) — `public interface IClipWriter`. Scrive una mini-clip MP4 da una sequenza di JPEG gia' archiviati.
- **TimelapseClipWriter** (riga 20, tipo) — `public sealed class TimelapseClipWriter : IClipWriter`. Implementazione di IClipWriter sopra il builder del timelapse (ffmpeg).
- **TimelapseClipWriter** (riga 28) — `public TimelapseClipWriter(ITimelapseBuilder builder, TimelapseOptions baseOptions)`. Crea lo scrittore.
- **WriteAsync** (riga 35) — `public Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)`.

### TransitDetails.cs

- **TransitDetails** (riga 9, tipo) — `public sealed record TransitDetails`. Metadati di un transito salvato (Transit).

### TransitEventSaver.cs

- **TransitFrame** (riga 11, tipo) — `public sealed record TransitFrame`. Frame archiviato, pronto per il salvataggio di un transito.
- **TransitSaveRequest** (riga 36, tipo) — `public sealed record TransitSaveRequest`. Richiesta di salvataggio di un passaggio.
- **TransitSaveOptions** (riga 64, tipo) — `public sealed record TransitSaveOptions`. Parametri del salvataggio di un transito.
- **Validate** (riga 79) — `public void Validate()`. Verifica i valori.
- **TransitSaveOutcome** (riga 94, tipo) — `public enum TransitSaveOutcome`. Esito di SaveAsync.
- **TransitSaveResult** (riga 107, tipo) — `public sealed record TransitSaveResult`. Risultato di un salvataggio di transito.
- **TransitEventSaver** (riga 164, tipo) — `public sealed class TransitEventSaver`. Salva un transito di satellite come evento Transit: clip MP4 dei frame della finestra, immagine composita (massimo per pixel), metadati (TransitDetails) in `event.json`.
- **TransitEventSaver** (riga 187) — `public TransitEventSaver(`. Crea il salvatore.
- **SaveAsync** (riga 205) — `public async Task<TransitSaveResult> SaveAsync(TransitSaveRequest request, CancellationToken ct = default)`. Salva il passaggio, se il verdetto e le opzioni lo richiedono.
- **CreateAsync** (riga 240) — `private async Task<TransitSaveResult> CreateAsync(TransitSaveRequest request, string id, IReadOnlyList<TransitFrame> window, CancellationToken ct)`.
- **WriteClipAsync** (riga 315) — `private async Task<(string? FileName, string? Error, string? OverlayError)> WriteClipAsync(string nightId, string id, IReadOnlyList<TransitFrame> window, Cancel`.
- **SelectWindow** (riga 402) — `private static IReadOnlyList<TransitFrame> SelectWindow(SatellitePass pass, IReadOnlyList<TransitFrame> frames)`. Frame del passaggio più il precedente e il successivo, in ordine temporale.
- **ValidateRequest** (riga 419) — `private static void ValidateRequest(TransitSaveRequest request)`.
- **RemoveFolderQuietly** (riga 466) — `private void RemoveFolderQuietly(string nightId, string id)`.
- **TryDeleteFile** (riga 482) — `private static void TryDeleteFile(string path)`.


## AllSky.Host (src/AllSky.Host)

### AllSkyHost.cs

- **AllSkyHost** (riga 20, tipo) — `public static class AllSkyHost`. Punto d'ingresso dell'Host HTTP locale: avvia il motore, il server e la UI.
- **StartAsync** (riga 40) — `public static Task<AllSkyHostRuntime> StartAsync(HostOptions options, CancellationToken cancellationToken = default)`. Avvia il motore e il server HTTP.
- **StartCoreAsync** (riga 46) — `internal static async Task<AllSkyHostRuntime> StartCoreAsync(HostOptions options, HostTuning tuning, CancellationToken cancellationToken)`. Avvio con parametri interni, usato dai test.
- **StartWebAsync** (riga 196) — `private static async Task<WebApplication> StartWebAsync(int preferred, bool lan, HostContext context, DailyFileLog log, CancellationToken ct)`. Prova le porte in ordine (la richiesta, poi le successive fino a PortSearchSpan) finche' una e' libera.
- **BuildWebApplication** (riga 228) — `private static WebApplication BuildWebApplication(int port, bool lan, HostContext context)`. Costruisce il server HTTP con gli endpoint e la UI, senza avviarlo.
- **ArchiveFolder** (riga 312) — `private static string ArchiveFolder(AppConfig config)`. Cartella dell'archivio delle notti, con lo stesso ripiego del motore (`storage.folder` vuoto).
- **LoadOrCreateConfig** (riga 320) — `private static AppConfig LoadOrCreateConfig(string configPath, string dataFolder, DailyFileLog log)`. Crea il file di configurazione con i valori predefiniti se manca.
- **EnsureDirectory** (riga 347) — `private static string EnsureDirectory(string path)`.
- **IsAddressInUse** (riga 362) — `private static bool IsAddressInUse(Exception ex)`. Vero se la catena di eccezioni contiene "indirizzo gia' in uso" (Kestrel la incapsula in un IOException).
- **StopWebAfterFailureAsync** (riga 380) — `private static async Task StopWebAfterFailureAsync(WebApplication app, DailyFileLog log)`.
- **StopAfterFailureAsync** (riga 393) — `private static async Task StopAfterFailureAsync(AllSkyEngine engine, DailyFileLog log)`.

### AllSkyHostRuntime.cs

- **AllSkyHostRuntime** (riga 15, tipo) — `public sealed class AllSkyHostRuntime : IAsyncDisposable`. Host in esecuzione: il servizio di cattura, il server HTTP locale e il log.
- **SetUpdateAutoCheck** (riga 61) — `public void SetUpdateAutoCheck(bool enabled)`. Attiva o disattiva il controllo automatico degli aggiornamenti: lo salva in `updates.json` e lo applica subito.
- **WaitForShutdownAsync** (riga 98) — `public async Task WaitForShutdownAsync(CancellationToken cancellationToken)`. Attende l'arresto dell'Host.
- **StopAsync** (riga 108) — `public Task StopAsync()`. Arresta l'Host: chiude le connessioni SSE, ferma il server HTTP, arresta il motore (salva i prodotti parziali della notte in corso e ferma la camera).
- **DisposeAsync** (riga 118) — `public async ValueTask DisposeAsync()`. Equivale a StopAsync.
- **StopCoreAsync** (riga 123) — `private async Task StopCoreAsync()`.

### ApiRoutes.cs

- **ApiRoutes** (riga 17, tipo) — `internal static class ApiRoutes`. Endpoint di `/api` secondo docs/API.md.
- **Map** (riga 26) — `public static void Map(WebApplication app, HostContext host)`. Registra tutti gli endpoint dell'API.
- **ConnectRequest** (riga 191, tipo) — `private sealed record ConnectRequest(string? Id);`. Corpo di `POST /api/camera/connect`.
- **ConfigResult** (riga 196, tipo) — `private sealed record ConfigResult(bool Ok, IReadOnlyList<ValidationError> Errors);`. Esito di `PUT /api/config`.
- **Json** (riga 198) — `private static IResult Json(object? value)`.
- **Fail** (riga 204) — `internal static IResult Fail(int status, string message)`. Risposta d'errore uniforme, con il codice HTTP indicato.
- **NightNotFound** (riga 209) — `private static IResult NightNotFound(string nightId)`.
- **ServeFile** (riga 215) — `private static IResult ServeFile(HttpContext context, string path, string contentType, string notFound, string cacheControl, bool enableRange)`. Serve un file se esiste, altrimenti 404 con il messaggio dato.
- **NightExists** (riga 227) — `private static bool NightExists(IAllSkyService engine, string nightId)`. Vero se la notte compare nell'elenco dell'archivio.
- **CheckNightId** (riga 233) — `private static void CheckNightId(string nightId)`. Verifica il formato YYYY-MM-DD prima di usare l'id in un'operazione di scrittura.
- **StripJpgExtension** (riga 242) — `private static string? StripJpgExtension(string file)`. "frame.jpg" diventa "frame"; null se l'estensione non e' .jpg.
- **ParseQueryInt** (riga 253) — `private static int ParseQueryInt(IQueryCollection query, string name, int defaultValue, int min, int max)`.
- **ReadBodyAsync** (riga 269) — `private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)`.
- **ReadJsonAsync** (riga 280) — `private static async Task<T?> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct)`.
- **TrimJsonPath** (riga 299) — `private static string TrimJsonPath(string path)`. "$.ui.theme" diventa "ui.theme".

### CameraProvider.cs

- **CameraProvider** (riga 17, tipo) — `internal sealed class CameraProvider : ICameraProvider`. Fornitore concreto delle camere: simulatore sempre disponibile, driver ASCOM COM (solo Windows) e camere ASCOM Alpaca trovate sulla rete locale.
- **CameraProvider** (riga 31) — `public CameraProvider(Func<AppConfig> config, HostTuning tuning, DailyFileLog log)`. Crea il fornitore.
- **DiscoverAsync** (riga 42) — `public async Task<IReadOnlyList<CameraCandidate>> DiscoverAsync(CancellationToken cancellationToken)`. Elenca il simulatore, i driver ASCOM COM e le camere Alpaca.
- **Create** (riga 62) — `public ICameraSource Create(string id)`. Crea la sorgente per l'identificativo indicato, senza connetterla.
- **DiscoverAscomCom** (riga 100) — `private IEnumerable<CameraCandidate> DiscoverAscomCom()`. Driver ASCOM COM registrati.
- **DiscoverAlpacaAsync** (riga 126) — `private async Task<IReadOnlyList<CameraCandidate>> DiscoverAlpacaAsync(CancellationToken cancellationToken)`. Camere Alpaca sulla rete locale, con la durata di ricerca configurata.
- **CreateAlpaca** (riga 154) — `private static AlpacaCamera CreateAlpaca(string id)`. Legge `alpaca:<host>:<porta>:<deviceNumber>`; l'host puo' contenere due punti (IPv6).

### DailyFileLog.cs

- **DailyFileLog** (riga 10, tipo) — `internal sealed class DailyFileLog`. Log su file con rotazione giornaliera: ogni riga va in `allsky-YYYYMMDD.log` nella cartella indicata, in base alla data locale.
- **Write** (riga 28) — `public void Write(string message, Exception? exception = null)`. Scrive un messaggio, con l'eventuale eccezione completa, nel file del giorno.
- **Sanitize** (riga 57) — `private string Sanitize(string text)`. Applica il filtro; se fallisce il testo originale non viene scritto.

### ErrorMapping.cs

- **ErrorBody** (riga 7, tipo) — `internal sealed record ErrorBody(string Error);`. Corpo di ogni risposta d'errore: `{ "error": "messaggio" }`.
- **ErrorMapping** (riga 10, tipo) — `internal static class ErrorMapping`. Traduce le eccezioni in codici HTTP e messaggi per l'utente.
- **Map** (riga 15) — `public static (int Status, string Message) Map(Exception ex, HostContext host)`. Restituisce il codice HTTP e il messaggio per un'eccezione non gestita dall'endpoint.
- **Safe** (riga 35) — `private static string Safe(HostContext host, string message)`. Il messaggio passa dal filtro dei segreti prima di arrivare alla UI.
- **CleanParameterSuffix** (riga 39) — `private static string CleanParameterSuffix(string message)`. Toglie il suffisso "(Parameter 'x')" che .NET aggiunge ai messaggi degli argomenti.

### EventStream.cs

- **EventStream** (riga 15, tipo) — `internal static class EventStream`. Server-Sent Events di `GET /api/events`: evento `status` (come `/api/status`) all'apertura e a ogni cambio, evento `frame` (`{id,timestamp}`) per ogni frame archiviato.
- **ServeAsync** (riga 23) — `public static async Task ServeAsync(HttpContext context, HostContext host)`. Gestisce una connessione SSE fino alla disconnessione del client o all'arresto dell'Host.
- **Subscribe** (riga 108) — `private static void Subscribe(PublishServices publishing, Action<PublishStatus> handler)`. Si iscrive agli stati della pubblicazione.
- **Unsubscribe** (riga 121) — `private static void Unsubscribe(PublishServices publishing, Action<PublishStatus> handler)`. Annulla l'iscrizione alla chiusura dello stream.
- **FlushPendingAsync** (riga 134) — `private static async Task FlushPendingAsync(HttpContext context, ChannelReader<string> reader, CancellationToken token)`. Scrive tutti i messaggi accodati e svuota il buffer della risposta.
- **Frame** (riga 145) — `private static string Frame(string name, object payload)`. Formatta un evento SSE; il JSON compatto non contiene a capo, quindi una riga `data` basta.

### Events/EventArchiveBrowser.cs

- **EventArchiveBrowser** (riga 22, tipo) — `public sealed class EventArchiveBrowser : IEventBrowser`. Adattatore di IEventBrowser per la vista Eventi del desktop: traduce EventArchive nei tipi di AllSky.Core e risolve i file dell'evento in percorsi assoluti verificati.
- **EventArchiveBrowser** (riga 31) — `public EventArchiveBrowser(Func<string> rootFolder)`. Crea l'adattatore.
- **List** (riga 37) — `public IReadOnlyList<EventListItem> List(EventFilter filter)`.
- **Get** (riga 54) — `public EventDetail? Get(string nightId, string id)`.
- **SetLabel** (riga 62) — `public EventDetail SetLabel(string nightId, string id, EventLabelChoice label)`.
- **ExportZip** (riga 70) — `public string ExportZip(string nightId, string id, string zipPath)`.
- **ToListItem** (riga 78) — `private EventListItem ToListItem(EventArchive archive, EventRecord record)`.
- **ToDetail** (riga 96) — `private EventDetail ToDetail(EventArchive archive, EventRecord record)`.
- **ToTransit** (riga 118) — `private static TransitInfo ToTransit(TransitDetails info)`.
- **ExistingFile** (riga 135) — `private static string? ExistingFile(EventArchive archive, EventRecord record, string? fileName)`. Percorso assoluto del file dell'evento, solo se il nome è dichiarato e il file esiste.
- **ToCategory** (riga 146) — `private static EventCategory ToCategory(EventKind kind)`.
- **ToKind** (riga 153) — `private static EventKind ToKind(EventCategory category)`.
- **ToClassKind** (riga 160) — `private static EventClassKind ToClassKind(AllSky.Detection.StreakClass value)`.
- **ToLabel** (riga 169) — `private static EventLabelChoice ToLabel(UserLabel label)`.
- **ToUserLabel** (riga 176) — `private static UserLabel ToUserLabel(EventLabelChoice label)`.
- **Archive** (riga 186) — `private EventArchive Archive()`. Archivio per la radice corrente.

### HostContext.cs

- **HostContext** (riga 14, tipo) — `internal sealed record HostContext(`. Dipendenze condivise dagli endpoint HTTP di una singola istanza dell'Host.
- **Clean** (riga 25) — `public string Clean(string text)`. Testo destinato alla UI, con i segreti mascherati.
- **FilterStatus** (riga 28) — `public StatusDto FilterStatus(StatusDto status)`. Stato del motore con il messaggio d'errore filtrato (il messaggio nasce da eccezioni).

### HostJson.cs

- **HostJson** (riga 12, tipo) — `internal static class HostJson`. Opzioni JSON degli endpoint: camelCase, enum come stringhe camelCase, date UTC come `yyyy-MM-ddTHH:mm:ssZ`.
- **Create** (riga 17) — `private static JsonSerializerOptions Create()`.
- **UtcDateTimeOffsetConverter** (riga 33, tipo) — `private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>`. Scrive ogni DateTimeOffset in UTC nel formato `yyyy-MM-ddTHH:mm:ssZ`.
- **Read** (riga 35) — `public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)`.
- **Write** (riga 40) — `public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)`.

### HostOptions.cs

- **HostOptions** (riga 22, tipo) — `public sealed record HostOptions(`. Opzioni di avvio dell'Host HTTP locale.

### HostPublishSettingsService.cs

- **HostPublishSettingsService** (riga 13, tipo) — `internal sealed class HostPublishSettingsService : IPublishSettingsService`. Adattatore di IPublishSettingsService per la vista Pubblicazione del desktop.
- **HostPublishSettingsService** (riga 18) — `public HostPublishSettingsService(PublishServices? services)`.
- **GetSettings** (riga 31) — `public PublicationSettingsSnapshot GetSettings()`.
- **GetStatus** (riga 48) — `public PublicationStatusSnapshot GetStatus()`.
- **SaveSettings** (riga 56) — `public PublicationSaveResult SaveSettings(PublicationSettingsChange change)`.
- **TestConnectionAsync** (riga 78) — `public async Task<PublicationTestResult> TestConnectionAsync(PublicationSettingsChange change, CancellationToken cancellationToken)`.
- **OnPublisherStatusChanged** (riga 97) — `private void OnPublisherStatusChanged(PublishStatus status)`.
- **ToBody** (riga 112) — `private static PublishRoutes.PublishSettingsBody ToBody(PublicationSettingsChange c)`. Converte il modulo della vista nel corpo delle rotte.
- **ToSnapshot** (riga 128) — `private static PublicationStatusSnapshot ToSnapshot(PublishStatus status, Redactor redactor)`.
- **ToState** (riga 139) — `private static PublicationState ToState(PublishState state)`.

### HostTuning.cs

- **HostTuning** (riga 9, tipo) — `internal sealed record HostTuning(TimeSpan AlpacaDiscoveryTimeout, bool EnableAlpacaDiscovery)`. Parametri interni di avvio, separati da HostOptions per poter rendere deterministici i test (niente attesa reale della discovery Alpaca, nessun accesso alla rete locale).

### LanAccess.cs

- **LanAccess** (riga 10, tipo) — `internal sealed class LanAccess`. Controllo della password per le richieste dalla rete locale (HTTP Basic).
- **IsAuthorized** (riga 19) — `public bool IsAuthorized(string? authorizationHeader, string storedHash)`. Vero se l'intestazione Authorization contiene la password giusta per l'hash salvato.

### PublishRoutes.cs

- **PublishRoutes** (riga 18, tipo) — `internal static class PublishRoutes`. Endpoint `/api/publish/*`: impostazioni, stato e prova di connessione.
- **Map** (riga 28) — `public static void Map(WebApplication app, HostContext host)`. Registra gli endpoint della pubblicazione.
- **PublishReply** (riga 52, tipo) — `internal sealed record PublishReply(int Status, object Body);`. Risposta di un endpoint: codice HTTP e corpo JSON.
- **PublishSettingsBody** (riga 58, tipo) — `internal sealed record PublishSettingsBody`. Corpo di PUT e POST /api/publish/*.
- **PublishSettingsView** (riga 75, tipo) — `internal sealed record PublishSettingsView(`. Vista delle impostazioni per la UI: nessun segreto, solo i booleani di presenza.
- **PublishStatusView** (riga 88, tipo) — `internal sealed record PublishStatusView(`. Vista dello stato per la UI e per l'evento SSE `publish`.
- **ReadBodyAsync** (riga 100) — `internal static async Task<PublishSettingsBody> ReadBodyAsync(HttpRequest request, CancellationToken ct)`. Legge il corpo JSON delle impostazioni.
- **IsLocalRequest** (riga 130) — `internal static bool IsLocalRequest(IPAddress? remote)`. Vero se la richiesta arriva dal computer locale.
- **SaveSettings** (riga 133) — `internal static PublishReply SaveSettings(HostContext host, IPAddress? remote, PublishSettingsBody body)`. Valida, salva e applica le impostazioni.
- **TestSettingsAsync** (riga 150) — `internal static async Task<PublishReply> TestSettingsAsync(HostContext host, IPAddress? remote, PublishSettingsBody body, CancellationToken ct)`. Prova di connessione con i valori del form (anche non salvati).
- **SettingsView** (riga 162) — `internal static PublishSettingsView SettingsView(PublishSettings s)`. Impostazioni visibili alla UI.
- **StatusView** (riga 175) — `internal static PublishStatusView StatusView(HostContext host)`. Stato corrente; senza servizi di pubblicazione risponde "disabled".
- **ViewOf** (riga 184) — `internal static PublishStatusView ViewOf(PublishStatus status, Redactor redactor)`. Converte lo stato del publisher nella vista per la UI e per l'evento SSE.
- **Refused** (riga 195) — `private static PublishReply Refused()`.
- **Iso** (riga 197) — `private static string? Iso(DateTimeOffset? value)`.

### PublishServices.cs

- **PublishSecrets** (riga 11, tipo) — `internal sealed class PublishSecrets`. Segreti in memoria per la sanificazione: quelli salvati (token e valore dell'header extra) e quelli digitati in una prova di connessione non ancora salvati.
- **SetSaved** (riga 18) — `public void SetSaved(PublishSettings settings)`. Sostituisce i segreti salvati con quelli delle impostazioni indicate.
- **SetTransient** (riga 29) — `public void SetTransient(IEnumerable<string?> values)`. Imposta i segreti digitati nella prova di connessione in corso (non salvati).
- **ClearTransient** (riga 45) — `public void ClearTransient()`. Rimuove i segreti della prova di connessione.
- **Current** (riga 54) — `public IReadOnlyCollection<string> Current()`. Tutti i segreti correnti, salvati e provvisori.
- **PublishServices** (riga 67, tipo) — `internal sealed class PublishServices : IAsyncDisposable`. Servizi della pubblicazione creati dall'Host: impostazioni (con lo store su disco), publisher, client HTTP, log e sanificazione.
- **PublishServices** (riga 74) — `public PublishServices(`. Crea i servizi.
- **Replace** (riga 120) — `public void Replace(PublishSettings settings)`. Registra le impostazioni appena salvate, aggiornando anche i segreti per la sanificazione.
- **DisposeAsync** (riga 131) — `public ValueTask DisposeAsync()`. Chiude il client HTTP.
- **HostPublishing** (riga 139, tipo) — `internal static class HostPublishing`. Costruzione dei servizi di pubblicazione dentro l'Host, con avvio e arresto che non fanno mai cadere l'Host.
- **TryCreate** (riga 154) — `public static PublishServices? TryCreate(HostOptions options, string dataFolder, string logFolder, PublishSecrets secrets, DailyFileLog hostLog)`. Crea i servizi.
- **StartPublisherAsync** (riga 168) — `public static async Task StartPublisherAsync(PublishServices publishing, DailyFileLog hostLog)`. Avvia il ciclo di pubblicazione.
- **StopPublisherAsync** (riga 182) — `public static async Task StopPublisherAsync(PublishServices publishing, DailyFileLog hostLog)`. Ferma il ciclo di pubblicazione.
- **Create** (riga 194) — `private static PublishServices Create(HostOptions options, string dataFolder, string logFolder, PublishSecrets secrets)`.
- **BaselineJpegEncoder** (riga 226) — `new WebImagePreparer(new BaselineJpegEncoder()),`.
- **SystemJitter** (riga 228) — `new RetryPolicy(new SystemJitter()),`.
- **CreateHttpClient** (riga 243) — `private static HttpClient CreateHttpClient(HostOptions options)`. Un solo client per tutta la vita dell'Host.

### PublishSettingsOperations.cs

- **PublishSettingsOperations** (riga 10, tipo) — `internal static class PublishSettingsOperations`. Logica di salvataggio e di prova delle impostazioni di pubblicazione, condivisa tra le rotte HTTP (PublishRoutes) e l'adattatore della vista desktop (HostPublishSettingsService).
- **SaveOutcome** (riga 20, tipo) — `internal sealed record SaveOutcome(bool Ok, IReadOnlyList<SettingsError> Errors);`. Esito del salvataggio.
- **TestOutcome** (riga 23, tipo) — `internal sealed record TestOutcome(bool Ok, string Code, string Message, int? StatusCode);`. Esito della prova di connessione, con il testo gia' sanificato.
- **Current** (riga 26) — `internal static PublishSettings Current(PublishServices? services)`. Impostazioni salvate correnti; senza servizi, quelle predefinite.
- **Save** (riga 32) — `internal static SaveOutcome Save(PublishServices? services, PublishRoutes.PublishSettingsBody body)`. Unisce, valida, salva e applica.
- **TestAsync** (riga 69) — `internal static async Task<TestOutcome> TestAsync(PublishServices? services, PublishRoutes.PublishSettingsBody body, CancellationToken ct)`. Prova di connessione con i valori indicati (anche non salvati).
- **SanitizeError** (riga 97) — `internal static string? SanitizeError(string? text, Redactor redactor)`. Testo di errore sanificato, o null se non c'e' errore.
- **Merge** (riga 100) — `internal static PublishSettings Merge(PublishSettings current, PublishRoutes.PublishSettingsBody body)`. Unisce il corpo ricevuto alle impostazioni correnti.
- **Trimmed** (riga 125) — `private static string? Trimmed(string? value)`. Testo senza spazi ai bordi, oppure null se vuoto: un segreto digitato con spazi o a capo non e' valido.
- **EmptyToNull** (riga 127) — `private static string? EmptyToNull(string? value)`.

### SecretMask.cs

- **SecretMask** (riga 11, tipo) — `internal static class SecretMask`. Filtro dei segreti per il log generale e per i messaggi d'errore delle risposte HTTP.
- **Apply** (riga 27) — `public static string Apply(string text, IReadOnlyCollection<string> secrets)`. Restituisce il testo con i segreti mascherati.

### StaticFiles.cs

- **StaticFiles** (riga 13, tipo) — `internal static class StaticFiles`. Serve i file statici della UI dalla cartella `web`.
- **ServeAsync** (riga 35) — `public static async Task ServeAsync(HttpContext context, string webRoot)`. Risponde con il file richiesto, oppure 404 se non e' servibile.
- **ResolveFile** (riga 58) — `private static string? ResolveFile(string? requestPath, string webRoot)`. Percorso completo del file, oppure null se la richiesta non e' servibile.
- **WriteNotFoundAsync** (riga 88) — `private static Task WriteNotFoundAsync(HttpContext context)`.

### UnsupportedSecretProtector.cs

- **UnsupportedSecretProtector** (riga 9, tipo) — `internal sealed class UnsupportedSecretProtector : ISecretProtector`. Protezione dei segreti per i sistemi senza DPAPI: ogni operazione lancia PlatformNotSupportedException.
- **Protect** (riga 15) — `public byte[] Protect(byte[] plain)`. Lancia sempre PlatformNotSupportedException.
- **Unprotect** (riga 18) — `public byte[] Unprotect(byte[] protectedData)`. Lancia sempre PlatformNotSupportedException.

### UpdateRoutes.cs

- **UpdateRoutes** (riga 16, tipo) — `internal static class UpdateRoutes`. Endpoint `/api/update/*`: stato dell'aggiornamento, controllo, download, installazione e impostazione del controllo automatico.
- **Map** (riga 30) — `public static void Map(WebApplication app, HostContext host)`. Registra gli endpoint degli aggiornamenti.
- **UpdateStatusView** (riga 123, tipo) — `internal sealed record UpdateStatusView(`. Vista dello stato per la UI.
- **IsLocal** (riga 133) — `private static bool IsLocal(HttpContext context)`.
- **Refused** (riga 135) — `private static IResult Refused()`.
- **View** (riga 138) — `private static UpdateStatusView View(HostContext host)`. Stato corrente; senza servizio degli aggiornamenti risponde "disabled".
- **ViewOf** (riga 146) — `private static UpdateStatusView ViewOf(UpdateStatus status, UpdateServices services)`.
- **ReadBodyAsync** (riga 157) — `private static async Task<UpdateSettingsBody?> ReadBodyAsync(HttpRequest request, CancellationToken ct)`. Legge il corpo JSON.
- **Iso** (riga 186) — `private static string? Iso(DateTimeOffset? value)`.
- **UpdateSettingsBody** (riga 191, tipo) — `internal sealed record UpdateSettingsBody`. Corpo di PUT /api/update/settings e forma di `updates.json`.
- **UpdateServices** (riga 199, tipo) — `internal sealed record UpdateServices(UpdateService Service, UpdateSettingsStore Store)`. Servizio degli aggiornamenti e il suo archivio, creati dall'Host all'avvio.
- **Create** (riga 207) — `public static UpdateServices Create(IUpdateBackend? backend, string dataFolder)`. Crea il servizio con l'impostazione salvata.
- **UpdateSettingsStore** (riga 219, tipo) — `internal sealed class UpdateSettingsStore`. Impostazione del controllo automatico in `updates.json` (`{"autoCheck": true}`).
- **UpdateSettingsStore** (riga 226) — `public UpdateSettingsStore(string filePath)`.
- **Load** (riga 233) — `public bool Load()`. Legge il controllo automatico.
- **Save** (riga 268) — `public void Save(bool autoCheck)`. Salva il controllo automatico in modo atomico.
- **Quarantine** (riga 291) — `private void Quarantine()`.
- **TryDelete** (riga 303) — `private static void TryDelete(string path)`.

### YouTube/HostYouTubeService.cs

- **HostYouTubeService** (riga 12, tipo) — `internal sealed class HostYouTubeService : IYouTubeService`. Implementazione di IYouTubeService nell'Host: collega OAuth, scheduler e configurazione.
- **HostYouTubeService** (riga 30) — `public HostYouTubeService(`.
- **GetStatus** (riga 54) — `public YouTubeStatus GetStatus()`.
- **GetSettings** (riga 82) — `public YouTubeSettingsSnapshot GetSettings()`.
- **LoginAsync** (riga 85) — `public async Task<YouTubeLoginResult> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken)`.
- **LogoutAsync** (riga 148) — `public async Task LogoutAsync(CancellationToken cancellationToken)`.
- **SaveSettings** (riga 175) — `public YouTubeSaveResult SaveSettings(YouTubeSettingsChange change)`.
- **UploadLastNightNowAsync** (riga 195) — `public async Task<YouTubeUploadOutcome> UploadLastNightNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken)`.
- **ShouldRun** (riga 214) — `internal bool ShouldRun()`. Vero se il caricamento automatico e' acceso e l'account e' collegato.
- **TickAsync** (riga 217) — `internal async Task TickAsync(CancellationToken cancellationToken)`. Un passo del caricamento automatico.
- **Stop** (riga 237) — `internal void Stop()`. Arresto dell'Host: annulla i caricamenti in corso.
- **RunAsync** (riga 239) — `private async Task<UploadOutcome> RunAsync(Func<CancellationToken, Task<UploadOutcome>> operation, CancellationToken cancellationToken)`.
- **Record** (riga 265) — `private YouTubeUploadOutcome Record(UploadOutcome outcome)`.
- **PreviousProblem** (riga 292) — `private YouTubeProblem? PreviousProblem()`.
- **SetProblem** (riga 300) — `private void SetProblem(YouTubeProblem problem)`.
- **Fail** (riga 308) — `private YouTubeLoginResult Fail(YouTubeProblem problem)`.
- **UpdateConfig** (riga 315) — `private bool UpdateConfig(Func<AppConfig.YouTubeConfig, AppConfig.YouTubeConfig> change)`.
- **IsConnected** (riga 331) — `private bool IsConnected()`.
- **ZoneOf** (riga 333) — `private TimeZoneInfo ZoneOf()`.
- **JournalProblem** (riga 335) — `private static YouTubeProblem? JournalProblem(UploadJournalState state, DateTimeOffset now)`.
- **LastUpload** (riga 350) — `private static YouTubeLastUpload? LastUpload(UploadJournalState state)`.
- **MapKind** (riga 364) — `private static YouTubeUploadOutcomeKind MapKind(UploadOutcomeKind kind)`.
- **Code** (riga 381) — `private static string Code(Exception ex)`.
- **Notify** (riga 383) — `private void Notify()`.

### YouTube/NightArchiveLatestTimelapseProvider.cs

- **NightArchiveLatestTimelapseProvider** (riga 18, tipo) — `public sealed class NightArchiveLatestTimelapseProvider : ILatestTimelapseProvider`. Fonte del timelapse per il caricamento automatico: l'ultima notte COMPLETA con `timelapse.mp4` pronto.
- **NightArchiveLatestTimelapseProvider** (riga 27) — `public NightArchiveLatestTimelapseProvider(Func<AppConfig> config, AllSky.Core.IClock clock)`.
- **GetLatestCompleted** (riga 34) — `public LatestTimelapse? GetLatestCompleted()`.

### YouTube/UnavailableYouTubeService.cs

- **UnavailableYouTubeService** (riga 10, tipo) — `internal sealed class UnavailableYouTubeService : IYouTubeService`. Servizio di ripiego quando il caricamento YouTube non parte in questo avvio (per esempio il registro e' illeggibile).
- **GetStatus** (riga 16) — `public YouTubeStatus GetStatus()`.
- **GetSettings** (riga 18) — `public YouTubeSettingsSnapshot GetSettings()`.
- **LoginAsync** (riga 24) — `public Task<YouTubeLoginResult> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken)`.
- **LogoutAsync** (riga 27) — `public Task LogoutAsync(CancellationToken cancellationToken)`.
- **SaveSettings** (riga 29) — `public YouTubeSaveResult SaveSettings(YouTubeSettingsChange change)`.
- **UploadLastNightNowAsync** (riga 31) — `public Task<YouTubeUploadOutcome> UploadLastNightNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken)`.

### YouTube/YouTubeConfigMapper.cs

- **YouTubeConfigMapper** (riga 8, tipo) — `internal static class YouTubeConfigMapper`. Traduce la sezione `youtube` della configurazione nelle impostazioni del caricamento.
- **ResolveZone** (riga 11) — `public static TimeZoneInfo ResolveZone(string? id)`. Fuso orario dell'osservatorio; se il nome non esiste, UTC (come il motore).
- **Errors** (riga 29) — `public static IReadOnlyList<ValidationError> Errors(AppConfig.YouTubeConfig config)`. Errori della sezione, comprese le sintassi dei modelli di titolo e descrizione.
- **ToUploadSettings** (riga 49) — `public static YouTubeUploadSettings ToUploadSettings(AppConfig.YouTubeConfig config)`. Impostazioni dello scheduler.
- **ToPrivacy** (riga 70) — `private static YouTubePrivacy ToPrivacy(string privacy)`.

### YouTube/YouTubeUploadWorker.cs

- **YouTubeUploadWorker** (riga 9, tipo) — `public sealed class YouTubeUploadWorker`. Ciclo in background del caricamento automatico: un tick all'avvio e poi ogni `interval`.
- **YouTubeUploadWorker** (riga 23) — `public YouTubeUploadWorker(Func<CancellationToken, Task> tick, Func<bool> shouldRun, TimeSpan interval, Action<string> log)`.
- **Start** (riga 40) — `public void Start()`. Avvia il ciclo.
- **StopAsync** (riga 55) — `public async Task StopAsync()`. Annulla il ciclo e attende che termini.
- **RunAsync** (riga 90) — `private async Task RunAsync(CancellationToken cancellationToken)`.
- **TickOnceAsync** (riga 107) — `private async Task TickOnceAsync(CancellationToken cancellationToken)`.

### YouTubeSecretProtectorAdapter.cs

- **YouTubeSecretProtectorAdapter** (riga 7, tipo) — `public sealed class YouTubeSecretProtectorAdapter : AllSky.YouTube.ISecretProtector`. Adattatore sottile: espone il protettore dei segreti della pubblicazione (DPAPI su Windows) come ISecretProtector, cosi' le credenziali YouTube usano la stessa cifratura.
- **YouTubeSecretProtectorAdapter** (riga 11) — `public YouTubeSecretProtectorAdapter(AllSky.Publish.ISecretProtector inner)`.
- **Protect** (riga 16) — `public byte[] Protect(byte[] plain)`.
- **Unprotect** (riga 18) — `public byte[] Unprotect(byte[] protectedData)`.

### YouTubeServices.cs

- **YouTubeServices** (riga 14, tipo) — `internal sealed class YouTubeServices : IAsyncDisposable`. Caricamento automatico YouTube dentro l'Host: servizio per la vista, ciclo in background e client HTTP.
- **YouTubeServices** (riga 24) — `private YouTubeServices(HostYouTubeService service, YouTubeUploadWorker worker, HttpClient oauthHttp, HttpClient uploadHttp)`.
- **TryCreate** (riga 39) — `public static YouTubeServices? TryCreate(HostOptions options, string dataFolder, IAllSkyService engine, DailyFileLog log)`. Crea i servizi.
- **Start** (riga 53) — `public void Start()`. Avvia il ciclo in background.
- **StopAsync** (riga 56) — `public async Task StopAsync()`. Ferma il ciclo (annullando un caricamento in corso) e lo scheduler.
- **DisposeAsync** (riga 63) — `public ValueTask DisposeAsync()`. Chiude i client HTTP.
- **Create** (riga 70) — `private static YouTubeServices Create(HostOptions options, string dataFolder, IAllSkyService engine, DailyFileLog log)`.
- **DefaultOpenBrowser** (riga 112) — `private static Task DefaultOpenBrowser(Uri url, CancellationToken cancellationToken)`. Apre l'URL nel browser predefinito.
- **YouTubeClock** (riga 119, tipo) — `private sealed class YouTubeClock : AllSky.YouTube.IClock`. Orologio del modulo YouTube: ora UTC di sistema.


## AllSky.Imaging: file non descritti a mano sopra (src/AllSky.Imaging)

### DarkBuilder.cs

- **DarkBuilder** (riga 11, tipo) — `public static class DarkBuilder`. Costruzione di master dark a partire da piu' frame grezzi.
- **Combine** (riga 25) — `public static MasterDark Combine(`. Combina i frame con la mediana per pixel e restituisce il master dark.
- **MedianFrame** (riga 57) — `internal static ushort[] MedianFrame(IReadOnlyList<ushort[]> frames, int width, int height)`. Mediana per pixel di piu' frame (uso interno anche per il rilevamento dei pixel caldi).
- **ValidateFrames** (riga 96) — `internal static void ValidateFrames(IReadOnlyList<ushort[]> frames, int width, int height)`. Verifica che i frame siano non vuoti, di dimensione coerente e con dimensioni valide.
- **MedianSorted** (riga 138) — `private static ushort MedianSorted(ReadOnlySpan<ushort> sorted)`. Mediana di una sequenza gia' ordinata e non vuota; per lunghezza pari, media dei due centrali per difetto.

### DarkLibrary.cs

- **DarkLibrary** (riga 14, tipo) — `public sealed class DarkLibrary`. Registro in memoria dei master dark, indicizzato per (esposizione, gain, fascia di temperatura).
- **DarkKey** (riga 31, tipo) — `public readonly record struct DarkKey(double ExposureMs, double Gain, int? TempBand);`. Chiave di registro di un master dark.
- **BandOf** (riga 38) — `public static int? BandOf(double? temperatureC)`. Restituisce l'indice della fascia di temperatura, oppure null se la temperatura manca.
- **KeyFor** (riga 50) — `public static DarkKey KeyFor(MasterDark dark)`. Chiave con cui il master dark viene registrato.
- **Add** (riga 58) — `public void Add(MasterDark dark)`. Registra un master dark.
- **Find** (riga 100) — `public MasterDark? Find(double exposureMs, double gain, double? tempC = null)`. Cerca il master dark piu' adatto.
- **BandDistance** (riga 153) — `private static int BandDistance(int? wanted, int? have)`. Distanza in fasce tra la temperatura richiesta e quella del dark; 0 se non richiesta.

### DarkSubtract.cs

- **DarkSubtract** (riga 4, tipo) — `public static class DarkSubtract`. Sottrazione del master dark da un frame grezzo.
- **Apply** (riga 14) — `public static void Apply(ushort[] frame, MasterDark dark, int pedestal)`. Sottrae il master dark in-place.

### DebayerHamiltonAdams.cs

- **DebayerHamiltonAdams** (riga 14, tipo) — `public static class DebayerHamiltonAdams`. Demosaicing Bayer edge-aware, variante semplificata di Hamilton-Adams (1997).
- **Demosaic** (riga 31) — `public static RgbImage Demosaic(ushort[] raw, int width, int height, BayerPattern pattern)`. Restituisce un'immagine RGB da un mosaico Bayer.
- **Color** (riga 147) — `private static int Color(int[] layout, int x, int y)`.
- **Mirror** (riga 150) — `private static int Mirror(int i, int n)`.

### GradientModel.cs

- **GradientModel** (riga 9, tipo) — `public sealed class GradientModel`. Modello del gradiente di fondo, per canale (0 = R, 1 = G, 2 = B).
- **GradientModel** (riga 14) — `internal GradientModel(int width, int height, double[][] coefficients, double[] skyMean)`.
- **GetCoefficients** (riga 29) — `public double[] GetCoefficients(int channel)`. Copia dei 6 coefficienti del canale, in ordine (1, u, v, u^2, uv, v^2).
- **GetSkyMean** (riga 36) — `public double GetSkyMean(int channel)`. Media del modello sui pixel di cielo del canale (usata da Apply).
- **EvaluateAt** (riga 43) — `public double EvaluateAt(int channel, int x, int y)`. Valore del fondo stimato nel pixel (x, y) del canale, senza centratura sulla media del cielo.
- **CoefficientsRef** (riga 49) — `internal double[] CoefficientsRef(int channel)`.
- **SkyMeanRef** (riga 51) — `internal double SkyMeanRef(int channel)`.
- **NormU** (riga 54) — `internal static double NormU(double x, int width)`.
- **NormV** (riga 56) — `internal static double NormV(double y, int height)`.
- **Evaluate** (riga 58) — `internal static double Evaluate(double[] c, double u, double v)`.
- **CheckChannel** (riga 61) — `private static void CheckChannel(int channel)`.

### GradientRemoval.cs

- **GradientRemoval** (riga 12, tipo) — `public static class GradientRemoval`. Rimozione del gradiente di fondo (vignettatura, inquinamento luminoso) su un'immagine RGB lineare.
- **Fit** (riga 26) — `public static GradientModel Fit(RgbImage img, SkyMask mask)`. Stima il gradiente di fondo di ogni canale sui pixel di cielo della maschera.
- **Apply** (riga 227) — `public static void Apply(RgbImage img, GradientModel model, float strength)`. Sottrae in place strength * (modello - media del modello sul cielo) da ogni canale.
- **CheckSameSize** (riga 264) — `private static void CheckSameSize(RgbImage img, int width, int height, string paramName)`.
- **MedianSorted** (riga 273) — `private static double MedianSorted(double[] a, int n)`.
- **SolveGauss** (riga 284) — `private static double[] SolveGauss(double[,] a, double[] rhs)`.

### HotPixelFilter.cs

- **HotPixelFilter** (riga 30, tipo) — `public static class HotPixelFilter`. Rilevamento e correzione dei pixel caldi su un mosaico Bayer.
- **Build** (riga 42) — `public static HotPixelMap Build(ushort[] frame, int width, int height, double sigma)`. Costruisce la mappa dei pixel caldi da un frame.
- **Build** (riga 129) — `public static HotPixelMap Build(IReadOnlyList<ushort[]> frames, int width, int height, double sigma)`. Costruisce la mappa dalla mediana per pixel di piu' frame (stessa dimensione), poi applica il criterio del singolo frame.
- **Apply** (riga 146) — `public static void Apply(ushort[] frame, int width, int height, HotPixelMap map)`. Sostituisce i pixel della mappa con la mediana dei loro vicini dello stesso colore (passo 2).
- **SameColourMedian** (riga 196) — `private static double SameColourMedian(ushort[] frame, int width, int height, int x, int y)`. Mediana degli 8 vicini dello stesso colore (passo 2).
- **AllImmediateNeighboursAtOrBelow** (riga 234) — `private static bool AllImmediateNeighboursAtOrBelow(ushort[] frame, int width, int x, int y, double limit)`. True se i 8 vicini a distanza 1 (di qualsiasi colore) hanno valore <= limit.
- **Median** (riga 256) — `private static double Median(Span<double> values)`. Mediana di un campione non vuoto.
- **ValidateSize** (riga 268) — `private static void ValidateSize(ushort[] frame, int width, int height)`.
- **ValidateSigma** (riga 282) — `private static void ValidateSigma(double sigma)`.

### HotPixelMap.cs

- **HotPixelMap** (riga 10, tipo) — `public sealed record HotPixelMap(int Width, int Height, int[] Indices);`. Mappa dei pixel caldi di un sensore: indici lineari (y * Width + x) dei pixel da correggere, in ordine crescente.

### MasterDark.cs

- **MasterDark** (riga 15, tipo) — `public sealed record MasterDark(`. Master dark: frame di offset/corrente di buio combinato da piu' dark grezzi con stessa esposizione, gain e temperatura.

### SkyMask.cs

- **SkyMask** (riga 8, tipo) — `public sealed class SkyMask`. Maschera booleana del cielo, un valore per pixel in ordine row-major (indice = y * Width + x).
- **SkyMask** (riga 12) — `private SkyMask(int width, int height, bool[] sky)`.
- **Create** (riga 48) — `public static SkyMask Create(int width, int height, SkyMaskOptions options)`. Genera la maschera da un cerchio fisheye (equidistante) e da un profilo d'orizzonte opzionale.
- **Full** (riga 100) — `public static SkyMask Full(int width, int height)`. Maschera con tutti i pixel di cielo.
- **WithExcludedDiscs** (riga 112) — `public SkyMask WithExcludedDiscs(IEnumerable<(double X, double Y, double Radius)> discs)`. Restituisce una nuova maschera in cui i pixel dentro uno o piu' dischi sono esclusi (false).
- **IsSky** (riga 148) — `public bool IsSky(int x, int y)`. Restituisce true se il pixel (x, y) e' cielo.
- **ToBoolArray** (riga 164) — `public bool[] ToBoolArray()`. Copia della maschera come bool[] di Width * Height elementi (compatibile con WhiteBalance).
- **ToByteMask** (riga 167) — `public byte[] ToByteMask()`. Maschera a byte di Width * Height elementi: 255 = cielo, 0 = escluso.
- **ValidateSize** (riga 178) — `private static void ValidateSize(int width, int height)`.
- **BuildHorizon** (riga 197) — `private static (double[] Az, double[] El) BuildHorizon(IReadOnlyList<(double AzimuthDeg, double ElevationDeg)> horizon)`.
- **HorizonAt** (riga 230) — `private static double HorizonAt(double[] az, double[] el, double a)`.
- **NormalizeDeg** (riga 254) — `private static double NormalizeDeg(double deg)`.

### SkyMaskOptions.cs

- **SkyMaskOptions** (riga 22, tipo) — `public sealed record SkyMaskOptions(`. Parametri del cerchio fisheye e dell'eventuale profilo d'orizzonte per Create.


## AllSky.Imaging.Jpeg (src/AllSky.Imaging.Jpeg)

### BaselineJpegEncoder.cs

- **BaselineJpegEncoder** (riga 20, tipo) — `public sealed class BaselineJpegEncoder : IJpegEncoder`. Codificatore JPEG baseline sequenziale (ITU-T T.81), scritto da zero con la sola BCL.
- **Encode** (riga 34) — `public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)`. Codifica l'immagine RGB 8 bit in un file JPEG baseline.
- **ValidateArguments** (riga 56) — `private static void ValidateArguments(ReadOnlySpan<byte> rgb8, int width, int height, int quality)`.
- **WriteHeaders** (riga 81) — `private static void WriteHeaders(JpegBitWriter writer, int width, int height, bool subsample420, int[] lumaQuant, int[] chromaQuant)`. Scrive SOI, APP0 JFIF, DQT (luminanza e crominanza), SOF0, DHT (4 tabelle) e SOS.
- **WriteQuantTable** (riga 142) — `private static void WriteQuantTable(JpegBitWriter writer, int tableId, int[] naturalOrder)`. Scrive un segmento DQT con una tabella a 8 bit, valori in ordine zigzag come richiede la specifica.
- **WriteHuffmanTable** (riga 154) — `private static void WriteHuffmanTable(JpegBitWriter writer, int tableClass, int tableId, byte[] bits, byte[] values)`. Scrive un segmento DHT con una tabella (classe 0 = DC, 1 = AC).
- **WriteScan** (riga 171) — `private static void WriteScan(`. Scrive i dati entropici: MCU in ordine raster, blocchi Y (raster nel MCU), poi Cb e Cr.
- **FillLumaBlock** (riga 219) — `private static void FillLumaBlock(ReadOnlySpan<byte> rgb8, int width, int height, int x0, int y0, Span<float> dst)`. Riempie un blocco 8x8 di luminanza (Y - 128).
- **FillChromaBlock** (riga 240) — `private static void FillChromaBlock(`. Riempie un blocco 8x8 di crominanza.
- **EncodeBlock** (riga 278) — `private static void EncodeBlock(`. DCT, quantizzazione, zigzag e codifica Huffman di un blocco 8x8.
- **ForwardDct** (riga 341) — `private static void ForwardDct(ReadOnlySpan<float> src, Span<float> temp, Span<float> dst)`. DCT-II 8x8 separabile in float: F(u,v) = sum_x sum_y C(u,x) C(v,y) f(x,y), con C(u,x) = c(u)/2 cos((2x+1)u*pi/16).
- **BuildDctBasis** (riga 376) — `private static float[] BuildDctBasis()`.
- **Category** (riga 392) — `private static int Category(int value)`. Categoria di un valore: numero di bit necessari a |value| (0 se value è 0).
- **WriteSymbol** (riga 399) — `private static void WriteSymbol(JpegBitWriter writer, HuffmanCodeTable table, int symbol)`. Scrive il codice Huffman di un simbolo.
- **WriteAmplitude** (riga 405) — `private static void WriteAmplitude(JpegBitWriter writer, int value, int category)`. Scrive i bit di ampiezza: il valore se positivo, valore + 2^cat - 1 se negativo (complemento).

### JpegBitWriter.cs

- **JpegBitWriter** (riga 9, tipo) — `internal sealed class JpegBitWriter : IDisposable`. Scrittore di byte e di bit per il flusso JPEG.
- **JpegBitWriter** (riga 18) — `public JpegBitWriter(int initialCapacity)`. Crea lo scrittore con una capacità iniziale indicativa.
- **WriteByte** (riga 24) — `public void WriteByte(byte value)`. Scrive un byte grezzo (senza bit stuffing), usato per segmenti e marker.
- **WriteUInt16** (riga 31) — `public void WriteUInt16(int value)`. Scrive un valore a 16 bit in big-endian.
- **WriteMarker** (riga 38) — `public void WriteMarker(byte marker)`. Scrive il marker FF xx.
- **PutBits** (riga 45) — `public void PutBits(uint value, int count)`. Accoda `count` bit (1..16) di `value`, MSB-first, con bit stuffing.
- **FlushBits** (riga 65) — `public void FlushBits()`. Completa l'ultimo byte con bit a 1 (come richiesto dalla specifica) e lo scrive.
- **ToArray** (riga 75) — `public byte[] ToArray()`. Copia il contenuto scritto in un array nuovo.
- **Dispose** (riga 78) — `public void Dispose()`. Restituisce il buffer ad ArrayPool.
- **EnsureCapacity** (riga 87) — `private void EnsureCapacity(int extra)`.

### JpegTables.cs

- **JpegTables** (riga 8, tipo) — `internal static class JpegTables`. Tabelle JPEG standard (ITU-T T.81, Annex K) e funzioni di supporto.
- **ScaleQuantTable** (riga 113) — `public static int[] ScaleQuantTable(byte[] baseTable, int quality)`. Scala una tabella base secondo la formula di libjpeg (jpeg_quality_scaling): quality < 50 usa 5000/q, altrimenti 200-2q; ogni valore è (base*scala+50)/100 limitato a 1..255 (baseline).
- **HuffmanCodeTable** (riga 132, tipo) — `internal sealed class HuffmanCodeTable`. Tabella di codici Huffman canonica (ITU-T T.81, Annex C): per ogni simbolo restituisce codice e lunghezza in bit.
- **HuffmanCodeTable** (riga 137) — `private HuffmanCodeTable()`.
- **CodeOf** (riga 142) — `public uint CodeOf(int symbol)`. Codice (allineato a destra) del simbolo.
- **SizeOf** (riga 145) — `public int SizeOf(int symbol)`. Lunghezza in bit del codice del simbolo (0 se il simbolo non è presente nella tabella).
- **Create** (riga 148) — `public static HuffmanCodeTable Create(byte[] bits, byte[] values)`. Costruisce la tabella dai conteggi per lunghezza (bits[0..15] = numero di codici di lunghezza 1..16) e dai simboli.


## AllSky.Imaging.Overlay (src/AllSky.Imaging.Overlay)

### BitmapFont.cs

- **BitmapFont** (riga 22, tipo) — `public static class BitmapFont`. Font bitmap 8x16 incorporato nel codice, a 1 bit per pixel.
- **readonly** (riga 1814) — `private static readonly (char Target, char Source, string[] Rows, int FirstRow)[] Accents =`. Accenti composti sul glifo base: carattere risultante, carattere base, righe dell'accento e riga di partenza.
- **IsSupported** (riga 1834) — `public static bool IsSupported(char c)`. Indica se il carattere ha un glifo proprio (altrimenti si disegna Fallback).
- **GetGlyph** (riga 1841) — `public static ReadOnlySpan<byte> GetGlyph(char c)`. Restituisce le 16 righe del glifo: byte i = riga i, bit 7 = colonna 0.
- **GetPixel** (riga 1850) — `public static bool GetPixel(char c, int x, int y)`. Restituisce se il pixel (x, y) del glifo e' acceso.
- **BuildGlyphs** (riga 1865) — `private static Dictionary<char, byte[]> BuildGlyphs()`.
- **ParseGlyph** (riga 1887) — `private static byte[] ParseGlyph(char symbol, string art)`.
- **RowBits** (riga 1904) — `private static byte RowBits(string row)`.

### ImageResize.cs

- **ImageResize** (riga 7, tipo) — `public static class ImageResize`. Ridimensionamento di immagini RGB a 8 bit.
- **Box** (riga 26) — `public static void Box(ReadOnlySpan<byte> src, int sw, int sh, Span<byte> dst, int dw, int dh)`. Riduzione ad area (media a box ponderata).
- **Bilinear** (riga 72) — `public static void Bilinear(ReadOnlySpan<byte> src, int sw, int sh, Span<byte> dst, int dw, int dh)`. Ingrandimento bilineare.
- **BoxWeights** (riga 102) — `private static (int Index, double Weight)[][] BoxWeights(int srcLen, int dstLen)`. Pesi della riduzione ad area lungo un asse: per ogni indice di destinazione, le coppie (indice sorgente, peso).
- **LinearWeights** (riga 133) — `private static (int I0, int I1, double F)[] LinearWeights(int srcLen, int dstLen)`. Pesi dell'interpolazione lineare lungo un asse: indici dei due campioni e frazione tra loro.
- **ToByte** (riga 149) — `private static byte ToByte(double value)`.
- **ValidateArgs** (riga 154) — `private static void ValidateArgs(long srcLength, int sw, int sh, long dstLength, int dw, int dh)`.

### OverlayAnchor.cs

- **OverlayAnchor** (riga 4, tipo) — `public enum OverlayAnchor`. Angolo dell'immagine a cui ancorare un testo (con margine), usato da DrawTextAnchored.

### OverlayComposer.cs

- **OverlayComposer** (riga 17, tipo) — `public static class OverlayComposer`. Composizione della sovraimpressione di un fotogramma.
- **readonly** (riga 22) — `private static readonly (byte R, byte G, byte B) TextColor = (255, 255, 255);`.
- **Compose** (riga 35) — `public static void Compose(Span<byte> rgb, int w, int h, OverlayInfo info, OverlayOptions opt)`. Scrive la sovraimpressione sul buffer RGB.
- **FormatLocalTime** (riga 85) — `public static string FormatLocalTime(DateTimeOffset localTime)`. Ora locale nel formato 'yyyy-MM-dd HH:mm:ss', senza conversione di fuso.
- **FormatTemperature** (riga 94) — `public static string FormatTemperature(double celsius)`. Temperatura del sensore, per esempio "12.3 °C" (un decimale, punto come separatore).
- **FormatExposureLine** (riga 107) — `public static string? FormatExposureLine(double? exposureMs, double? gain, string language)`. Riga di esposizione e gain, per esempio "exp 8.0 s | gain 250" (IT: "esp 8.0 s | gain 250").
- **NormalizeLanguage** (riga 132) — `public static string NormalizeLanguage(string language)`. Normalizza la lingua a "it" o "en"; qualunque altro valore e' un errore.
- **IsFinite** (riga 148) — `private static bool IsFinite(double? value)`.

### OverlayInfo.cs

- **OverlayInfo** (riga 12, tipo) — `public sealed record OverlayInfo(`. Dati di un singolo fotogramma da scrivere in sovraimpressione.

### OverlayOptions.cs

- **OverlayOptions** (riga 8, tipo) — `public sealed record OverlayOptions(bool ShowTime, bool ShowSensorTemp, string Language, int Scale);`. Opzioni di composizione della sovraimpressione.

### OverlayRenderer.cs

- **OverlayRenderer** (riga 8, tipo) — `public static class OverlayRenderer`. Disegno di testo sopra un'immagine RGB a 8 bit (3 byte per pixel, righe contigue), con il BitmapFont 8x16.
- **readonly** (riga 18) — `private static readonly (byte R, byte G, byte B) ShadowColor = (0, 0, 0);`.
- **MeasureText** (riga 27) — `public static (int Width, int Height) MeasureText(string text, int scale, bool shadow)`. Dimensioni in pixel occupate da un testo a una certa scala.
- **DrawText** (riga 51) — `public static void DrawText(Span<byte> rgb, int w, int h, string text, int x, int y, (byte R, byte G, byte B) color, int scale, bool shadow)`. Disegna il testo con l'angolo in alto a sinistra in (`x`, `y`).
- **DrawTextAnchored** (riga 73) — `public static void DrawTextAnchored(Span<byte> rgb, int w, int h, string text, OverlayAnchor anchor, int margin, (byte R, byte G, byte B) color, int scale, bool`. Disegna il testo ancorato a un angolo dell'immagine, a distanza `margin` dai bordi.
- **ValidateScale** (riga 112) — `internal static void ValidateScale(int scale)`. Valida una scala del testo: intera tra 1 e MaxScale.
- **DrawCore** (riga 120) — `private static void DrawCore(Span<byte> rgb, int w, int h, string text, long x, long y, (byte R, byte G, byte B) color, int scale, bool shadow)`.
- **DrawGlyphs** (riga 130) — `private static void DrawGlyphs(Span<byte> rgb, int w, int h, string text, long x0, long y0, (byte R, byte G, byte B) color, int scale)`.
- **SetPixel** (riga 182) — `private static void SetPixel(Span<byte> rgb, int w, int h, long x, long y, (byte R, byte G, byte B) color)`.
- **ValidateImage** (riga 195) — `private static void ValidateImage(int rgbLength, int w, int h)`.


## AllSky.InfoOverlay (src/AllSky.InfoOverlay)

### Editor/MaskEditorState.cs

- **MaskWidgetDraft** (riga 10, tipo) — `public sealed class MaskWidgetDraft`. Una voce della lista widget dell'editor.
- **MaskWidgetDraft** (riga 12) — `public MaskWidgetDraft(OverlayWidgetKind tipo, bool abilitato, OverlayPosition posizione, OverlayWidgetFormat formato)`.
- **MaskEditorState** (riga 41, tipo) — `public sealed class MaskEditorState`. Stato dell'editor della maschera, senza nessuna dipendenza da WPF.
- **MaskEditorState** (riga 46) — `public MaskEditorState()`. Crea lo stato con il layout predefinito e nessun avviso.
- **Da** (riga 78) — `public static MaskEditorState Da(AppConfig.OverlayConfig config)`. Stato dalla configurazione salvata.
- **ToLayout** (riga 100) — `public OverlayLayout ToLayout()`. Layout da serializzare.
- **Valida** (riga 119) — `public IReadOnlyList<string> Valida()`. Problemi del layout corrente, nel testo della validazione.
- **LoghiUsati** (riga 122) — `public IReadOnlyList<string> LoghiUsati()`. Nomi dei file dei loghi referenziati da almeno una voce logo, anche disabilitata: servono alla pulizia.
- **AggiornaConfig** (riga 133) — `public AppConfig.OverlayConfig AggiornaConfig(AppConfig.OverlayConfig corrente)`. Configurazione aggiornata: copia i tre interruttori di applicazione e sostituisce il layout con il JSON del codec.
- **RipristinaPredefiniti** (riga 151) — `public void RipristinaPredefiniti()`. Ripristina il layout predefinito: DataOra in basso a sinistra, Sole e Luna in basso a destra, maschera a cerchio attiva, gli altri widget spenti.
- **Aggiungi** (riga 157) — `public int Aggiungi(OverlayWidgetKind tipo, OverlayPosition posizione)`. Aggiunge un widget in fondo alla lista.
- **AggiungiLogo** (riga 170) — `public int AggiungiLogo(string nomeFile, OverlayPosition posizione)`. Aggiunge un logo che usa il file indicato, già copiato nella cartella loghi.
- **Rimuovi** (riga 187) — `public bool Rimuovi(int indice)`. Rimuove una voce, ma solo se il tipo è ripetibile (logo, testo libero).
- **SpostaSu** (riga 199) — `public bool SpostaSu(int indice)`. Sposta la voce verso l'alto, scambiandola con la precedente della stessa posizione.
- **SpostaGiu** (riga 206) — `public bool SpostaGiu(int indice)`. Sposta la voce verso il basso, scambiandola con la successiva della stessa posizione.
- **CaricaLayout** (riga 212) — `private void CaricaLayout(OverlayLayout layout)`.
- **IndiceVicino** (riga 235) — `private int IndiceVicino(int indice, int verso)`.
- **Scambia** (riga 254) — `private bool Scambia(int indice, int altro)`.
- **NormalizzaFormato** (riga 268) — `internal static OverlayWidgetFormat NormalizzaFormato(OverlayWidgetKind tipo, OverlayWidgetFormat f)`. Formato con i soli campi previsti dal tipo.
- **Previsto** (riga 287) — `private static bool Previsto(OverlayFormatField previsti, OverlayFormatField campo)`.
- **MaskEditorCatalog** (riga 291, tipo) — `public static class MaskEditorCatalog`. Informazioni sui tipi di widget per l'editor: cosa si può aggiungere, cosa è ancora spento.
- **Multipla** (riga 300) — `public static bool Multipla(OverlayWidgetKind tipo)`. Logo ed etichette libere possono comparire più volte.
- **Disponibile** (riga 306) — `public static bool Disponibile(OverlayWidgetKind tipo)`. Vero se il tipo si può attivare.
- **CampiPrevisti** (riga 309) — `public static OverlayFormatField CampiPrevisti(OverlayWidgetKind tipo)`. Campi di formato previsti per il tipo.

### Editor/MaskInput.cs

- **MaskInput** (riga 9, tipo) — `public static class MaskInput`. Conversione dei valori digitati nell'editor.
- **TryParsePercento** (riga 20) — `public static bool TryParsePercento(string? testo, CultureInfo cultura, double minimoPercento, double massimoPercento, out double frazione)`. Legge una percentuale e la converte in frazione.
- **FormatPercento** (riga 43) — `public static string FormatPercento(double frazione, CultureInfo cultura)`. Frazione come percentuale con al massimo due decimali, nella cultura indicata.

### Editor/MaskLogoStore.cs

- **MaskLogoErrore** (riga 6, tipo) — `public enum MaskLogoErrore`. Motivo per cui un logo non è stato importato.
- **MaskLogoImport** (riga 20, tipo) — `public sealed record MaskLogoImport(MaskLogoErrore Errore, string? NomeFile)`. Esito di un'importazione: il nome del file copiato nella cartella loghi, oppure l'errore.
- **MaskLogoStore** (riga 29, tipo) — `public static class MaskLogoStore`. Gestione dei loghi PNG nella cartella loghi della cartella dati.
- **Importa** (riga 49) — `public static MaskLogoImport Importa(string sorgente, string cartellaLoghi)`. Copia un PNG nella cartella loghi.
- **Elenco** (riga 106) — `public static IReadOnlyList<string> Elenco(string cartellaLoghi)`. Nomi dei PNG nella cartella loghi, in ordine alfabetico.
- **PulisciNonUsati** (riga 126) — `public static IReadOnlyList<string> PulisciNonUsati(string cartellaLoghi, IEnumerable<string> nomiUsati)`. Elimina i PNG della cartella loghi che nessun widget usa più.
- **NomeDisponibile** (riga 158) — `internal static string NomeDisponibile(string cartella, string nomeSorgente)`. Nome base ripulito, senza caratteri non ammessi, con suffisso se il file esiste già.
- **NomeBase** (riga 173) — `public static string NomeBase(string nome)`. Solo lettere, cifre, trattino e underscore.
- **ValutaPng** (riga 191) — `internal static MaskLogoErrore? ValutaPng(byte[] dati)`. Controlla la firma PNG e la dimensione dichiarata nel primo blocco IHDR.
- **LeggiBigEndian** (riga 219) — `private static uint LeggiBigEndian(byte[] dati, int offset)`.
- **Errore** (riga 222) — `private static MaskLogoImport Errore(MaskLogoErrore errore)`.

### OverlayComposer.cs

- **OverlayComposer** (riga 21, tipo) — `public static class OverlayComposer`. Compone la maschera informativa di un frame: dal layout versionato e dal sidecar ricava la lista dei comandi di disegno (OverlayScene).
- **Compose** (riga 42) — `public static OverlayScene Compose(OverlayLayout layout, FrameSidecar sidecar, OverlayFrameSize frame, Language cultura)`. Compone la scena di un frame.
- **Righe** (riga 110) — `private static IReadOnlyList<string> Righe(OverlayWidget widget, FrameSidecar sidecar, Language cultura, DesktopTexts testi)`. Righe di testo di un widget.
- **TestoDataOra** (riga 129) — `private static string TestoDataOra(FrameSidecar sidecar, OverlayWidgetFormat formato, DesktopTexts testi)`.
- **TestoLuna** (riga 149) — `private static string TestoLuna(FrameSidecar sidecar, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)`.
- **RigheSatelliti** (riga 164) — `private static IReadOnlyList<string> RigheSatelliti(FrameSidecar sidecar, OverlayWidgetFormat formato, DesktopTexts testi)`. Intestazione e nomi dei satelliti.
- **NomiSatelliti** (riga 188) — `internal static IReadOnlyList<string>? NomiSatelliti(JsonElement? campo)`. Nomi dei satelliti dal campo grezzo del sidecar.
- **LeggiNome** (riga 214) — `private static string? LeggiNome(JsonElement oggetto)`.
- **TestoEsposizione** (riga 227) — `private static string TestoEsposizione(FrameSidecar sidecar, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)`.
- **Gradi** (riga 245) — `private static string Gradi(double? gradi, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)`.
- **Formato** (riga 257) — `private static string Formato(double valore, string formato, Language cultura)`. Numero con la virgola in italiano; lo zero negativo diventa zero.
- **Finito** (riga 269) — `private static double? Finito(double? valore)`.
- **Tronca** (riga 271) — `private static string Tronca(string testo, int massimo)`.
- **FormatoDi** (riga 274) — `private static OverlayWidgetFormat FormatoDi(OverlayWidget widget)`.
- **AllineamentoDi** (riga 277) — `private static OverlayAllineamento AllineamentoDi(OverlayPosition posizione)`.
- **AggiungiMaschera** (riga 284) — `private static void AggiungiMaschera(OverlayMask maschera, int larghezza, int altezza, List<OverlayComando> comandi)`.
- **AggiungiTesto** (riga 315) — `private static void AggiungiTesto(OverlayWidgetPosizionato pw, IReadOnlyList<string> linee, double corpo, List<OverlayComando> comandi)`.
- **AggiungiLogo** (riga 326) — `private static void AggiungiLogo(OverlayWidgetPosizionato pw, List<OverlayComando> comandi)`.

### OverlayLayout.cs

- **OverlayLayout** (riga 9, tipo) — `public sealed record OverlayLayout(int Versione, OverlayMask Maschera, OverlayStyle Stile, IReadOnlyList<OverlayWidget> Widget);`. Layout della maschera informativa sul timelapse.
- **OverlayLayoutVersions** (riga 12, tipo) — `public static class OverlayLayoutVersions`. Versione corrente del formato di OverlayLayout.
- **OverlayMask** (riga 18, tipo) — `public sealed record OverlayMask(OverlayMaskMode Modo, OverlayCircleMask Cerchio, OverlayBandMask Bande);`. Maschera ai bordi del frame.
- **OverlayCircleMask** (riga 21, tipo) — `public sealed record OverlayCircleMask(double CentroX, double CentroY, double Raggio, OverlayMaskEdge Esterno, double Sfumatura);`. Cerchio del cielo.
- **OverlayBandMask** (riga 24, tipo) — `public sealed record OverlayBandMask(double Superiore, double Inferiore);`. Bande nere sopra e sotto, in frazione dell'altezza del frame.
- **OverlayStyle** (riga 27, tipo) — `public sealed record OverlayStyle(double ScalaTesto, double Margine);`. Stile del testo.
- **OverlayWidget** (riga 30, tipo) — `public sealed record OverlayWidget(OverlayWidgetKind Tipo, bool Abilitato, OverlayPosition Posizione, int Ordine, OverlayWidgetFormat? Formato);`. Un elemento della maschera.
- **OverlayWidgetFormat** (riga 36, tipo) — `public sealed record OverlayWidgetFormat(`. Opzioni di formato di un widget.
- **OverlayMaskMode** (riga 49, tipo) — `public enum OverlayMaskMode`.
- **OverlayMaskEdge** (riga 56, tipo) — `public enum OverlayMaskEdge`.
- **OverlayTimeZone** (riga 62, tipo) — `public enum OverlayTimeZone`.
- **OverlayPosition** (riga 69, tipo) — `public enum OverlayPosition`. Posizione di un widget.
- **OverlayWidgetKind** (riga 79, tipo) — `public enum OverlayWidgetKind`.
- **OverlayMeteoField** (riga 92, tipo) — `public enum OverlayMeteoField`.

### OverlayLayoutCodec.cs

- **OverlayLayoutReadResult** (riga 8, tipo) — `public sealed record OverlayLayoutReadResult(OverlayLayout Layout, int VersioneOriginale, bool Migrata, IReadOnlyList<string> Avvisi);`. Esito della lettura di un layout: il layout usabile, la versione trovata e gli avvisi per l'utente.
- **OverlayLayoutCodec** (riga 14, tipo) — `public static class OverlayLayoutCodec`. Lettura e scrittura del layout in JSON.
- **Serializza** (riga 34) — `public static string Serializza(OverlayLayout layout)`.
- **Leggi** (riga 37) — `public static OverlayLayoutReadResult Leggi(string? json)`.
- **Predefiniti** (riga 94) — `private static OverlayLayoutReadResult Predefiniti(int versioneOriginale, List<string> avvisi)`.
- **LeggiVersione** (riga 97) — `private static int LeggiVersione(JsonObject radice, List<string> avvisi)`.
- **LeggiSezione** (riga 113) — `private static T LeggiSezione<T>(JsonObject radice, string nome, T predefinito, List<string> avvisi)`.
- **LeggiWidget** (riga 133) — `private static IReadOnlyList<OverlayWidget> LeggiWidget(`.
- **OverlayLayoutNormalizer** (riga 173, tipo) — `internal static class OverlayLayoutNormalizer`. Riporta un layout qualsiasi ai limiti: corregge i valori fuori intervallo e scarta ciò che non si può correggere.
- **Normalizza** (riga 175) — `internal static OverlayLayout Normalizza(OverlayLayout layout, List<string> avvisi)`.
- **NormalizzaMaschera** (riga 206) — `private static OverlayMask NormalizzaMaschera(OverlayMask m, OverlayMask predefinito, List<string> avvisi)`.
- **NormalizzaWidget** (riga 242) — `private static OverlayWidget NormalizzaWidget(OverlayWidget w, List<string> avvisi)`.
- **Scegli** (riga 293) — `private static T Scegli<T>(T? valore, Func<T, bool> valido, T predefinito, string nome, List<string> avvisi)`. Valore assente: predefinito in silenzio.
- **ScegliVal** (riga 310) — `private static T ScegliVal<T>(T? valore, Func<T, bool> valido, T predefinito, string nome, List<string> avvisi)`.
- **Limita** (riga 328) — `private static double Limita(double valore, double minimo, double massimo, double predefinito, string nome, List<string> avvisi)`. Porta il valore nell'intervallo, o usa il predefinito se non è un numero finito.

### OverlayLayoutDefaults.cs

- **OverlayLayoutDefaults** (riga 4, tipo) — `public static class OverlayLayoutDefaults`. Layout predefinito: cerchio cielo, DataOra in basso a sinistra, Sole e Luna in basso a destra.
- **Predefinito** (riga 6) — `public static OverlayLayout Predefinito()`.
- **Crea** (riga 27) — `private static OverlayWidget Crea(OverlayWidgetKind tipo, bool abilitato, OverlayPosition posizione, int ordine)`. Un widget con il formato predefinito del suo tipo.

### OverlayLayoutValidator.cs

- **OverlayLayoutValidator** (riga 4, tipo) — `public static class OverlayLayoutValidator`. Controlla un layout senza modificarlo.
- **Valida** (riga 6) — `public static IReadOnlyList<string> Valida(OverlayLayout layout)`.
- **ValidaMaschera** (riga 43) — `private static void ValidaMaschera(OverlayMask maschera, List<string> errori)`.
- **ValidaStile** (riga 67) — `private static void ValidaStile(OverlayStyle stile, List<string> errori)`.
- **ValidaWidget** (riga 80) — `private static void ValidaWidget(OverlayWidget widget, int indice, List<string> errori)`.
- **CampiValidi** (riga 128) — `internal static bool CampiValidi(IReadOnlyList<OverlayMeteoField> campi)`.
- **Entro** (riga 133) — `internal static bool Entro(double valore, double minimo, double massimo)`.

### OverlayMeteo.cs

- **OverlayMeteoFields** (riga 11, tipo) — `public static class OverlayMeteoFields`. Insieme dei campi meteo che il widget Meteo può mostrare.
- **Valida** (riga 20) — `public static IReadOnlyList<string> Valida(IReadOnlyList<OverlayMeteoField>? campi)`. Problemi di un elenco di campi scelto dall'utente.
- **Normalizza** (riga 51) — `public static IReadOnlyList<OverlayMeteoField> Normalizza(IEnumerable<OverlayMeteoField> campi)`. Elenco pulito da una selezione: scarta i valori non validi e i ripetuti, mantiene l'ordine di comparsa.
- **OverlayMeteoText** (riga 63, tipo) — `public static class OverlayMeteoText`. Testo delle righe del widget Meteo.
- **Righe** (riga 69) — `public static IReadOnlyList<string> Righe(WeatherSnapshot? meteo, IReadOnlyList<OverlayMeteoField>? campi, Language lingua)`. Restituisce una riga per campo, nell'ordine dell'elenco.
- **Riga** (riga 89) — `internal static string Riga(WeatherSnapshot? m, OverlayMeteoField campo, Language lingua)`. Una riga: "Etichetta: valore unità".
- **Vento** (riga 115) — `private static string Vento(WeatherSnapshot? m, bool it, string mancante)`.
- **Etichetta** (riga 127) — `private static string Etichetta(string nome, string valore)`.
- **Valore** (riga 130) — `private static string Valore(double? valore, int decimali, string unita, string mancante, bool it)`. Valore con unità, oppure il segnaposto per i dati mancanti (senza unità).
- **Numero** (riga 140) — `private static string Numero(double valore, int decimali, bool it)`.

### OverlayPlacement.cs

- **OverlaySize** (riga 4, tipo) — `public sealed record OverlaySize(double Larghezza, double Altezza);`. Dimensione di un widget in pixel.
- **OverlayMisura** (riga 7) — `public delegate OverlaySize OverlayMisura(OverlayWidget widget, double altezzaTestoPx, int larghezzaFrame, int altezzaFrame);`. Misura di un widget dato il font in pixel e la dimensione del frame.
- **OverlayRect** (riga 10, tipo) — `public sealed record OverlayRect(double X, double Y, double Larghezza, double Altezza)`. Rettangolo in pixel del frame, origine in alto a sinistra.
- **Interseca** (riga 16) — `public bool Interseca(OverlayRect altro)`.
- **OverlayWidgetPosizionato** (riga 20, tipo) — `public sealed record OverlayWidgetPosizionato(OverlayWidget Widget, OverlayRect Area);`.
- **OverlayWidgetOmesso** (riga 22, tipo) — `public sealed record OverlayWidgetOmesso(OverlayWidget Widget, string Motivo);`.
- **OverlayPosizionamento** (riga 25, tipo) — `public sealed record OverlayPosizionamento(`. Esito del posizionamento: i rettangoli dei widget visibili e quelli omessi con il motivo.
- **OverlayPlacer** (riga 37, tipo) — `public static class OverlayPlacer`. Calcolo puro dei rettangoli dei widget per un frame di dimensione data.
- **Dispone** (riga 49) — `public static OverlayPosizionamento Dispone(OverlayLayout layout, int larghezza, int altezza, OverlayMisura? misura = null)`.
- **DisponiAngolo** (riga 87) — `private static void DisponiAngolo(`.
- **DisponiBanda** (riga 149) — `private static void DisponiBanda(`.
- **IsDimensioneValida** (riga 228) — `private static bool IsDimensioneValida(OverlaySize s)`.
- **Contesto** (riga 232, tipo) — `private sealed class Contesto`. Misure derivate dal layout, una sola volta per chiamata.
- **Contesto** (riga 234) — `public Contesto(OverlayLayout layout, int larghezza, int altezza)`.
- **OverlayMisure** (riga 273, tipo) — `public static class OverlayMisure`. Misura approssimata dei widget, in multipli del font.
- **Approssimata** (riga 275) — `public static OverlaySize Approssimata(OverlayWidget widget, double altezzaTestoPx, int larghezzaFrame, int altezzaFrame)`.

### OverlayRules.cs

- **OverlayLimits** (riga 6, tipo) — `public static class OverlayLimits`. Limiti dei valori.
- **OverlayFormatField** (riga 32, tipo) — `public enum OverlayFormatField`. Campi di OverlayWidgetFormat, come flag per indicare quali sono previsti da un tipo.
- **OverlayWidgetKinds** (riga 48, tipo) — `public static class OverlayWidgetKinds`.
- **CampiPrevisti** (riga 51) — `public static OverlayFormatField CampiPrevisti(OverlayWidgetKind tipo)`. Campi di formato validi per ciascun tipo di widget.
- **AmmetteIstanzeMultiple** (riga 65) — `public static bool AmmetteIstanzeMultiple(OverlayWidgetKind tipo)`. Solo loghi ed etichette libere possono comparire più volte.
- **OverlayWidgetFormats** (riga 69, tipo) — `public static class OverlayWidgetFormats`.
- **Predefinito** (riga 72) — `public static OverlayWidgetFormat Predefinito(OverlayWidgetKind tipo)`. Formato completo con i valori predefiniti per il tipo indicato.
- **Presente** (riga 90) — `public static bool Presente(OverlayWidgetFormat formato, OverlayFormatField campo)`.
- **PatternValido** (riga 107) — `public static bool PatternValido(string? pattern)`. Un formato di DataOra è valido se il pattern è lungo al massimo 40 caratteri e .NET lo accetta.
- **FileLogoValido** (riga 129) — `public static bool FileLogoValido(string? nomeFile)`. Il logo è solo il nome di un file PNG, che deve stare nella cartella loghi della cartella dati.
- **TestoValido** (riga 149) — `public static bool TestoValido(string? testo)`.

### OverlayScene.cs

- **OverlayFrameSize** (riga 6, tipo) — `public readonly record struct OverlayFrameSize(int Larghezza, int Altezza);`. Dimensione del frame in pixel.
- **OverlayColore** (riga 9, tipo) — `public readonly record struct OverlayColore(byte R, byte G, byte B, byte A = 255);`. Colore RGBA a 8 bit.
- **OverlayAllineamento** (riga 12, tipo) — `public enum OverlayAllineamento`. Allineamento orizzontale del testo dentro la propria area.
- **OverlayComando** (riga 23, tipo) — `public abstract record OverlayComando;`. Un comando di disegno della maschera.
- **OverlayRiempimento** (riga 26, tipo) — `public sealed record OverlayRiempimento(OverlayRect Area, OverlayColore Colore) : OverlayComando;`. Rettangolo pieno (bande nere, sfondi).
- **OverlayMascheraCerchio** (riga 33, tipo) — `public sealed record OverlayMascheraCerchio(`. Maschera circolare del cielo, in pixel.
- **OverlayTestoPosizionato** (riga 44, tipo) — `public sealed record OverlayTestoPosizionato(`. Una riga di testo nella sua area.
- **OverlayImmagine** (riga 55, tipo) — `public sealed record OverlayImmagine(OverlayRect Area, string NomeFile, double Opacita) : OverlayComando;`. Logo PNG.
- **OverlayScene** (riga 61, tipo) — `public sealed record OverlayScene(`. Esito della composizione: comandi di disegno in ordine, widget omessi con il motivo e il corpo del font usato.


## AllSky.Processing (src/AllSky.Processing)

### FrameProcessor.cs

- **FrameProcessor** (riga 56, tipo) — `public sealed class FrameProcessor : IFrameProcessor`. Pipeline di elaborazione dei frame grezzi.
- **FrameProcessor** (riga 100) — `internal FrameProcessor(AppConfig config, IJpegEncoder encoder, DarkLibrary? darks, SkyMask? skyMask)`. Costruttore per i test: permette di fornire una maschera gia' pronta (per esempio piena).
- **Process** (riga 117) — `public ProcessedFrame Process(RawFrame raw)`. Elabora un frame grezzo con la configurazione corrente.
- **ProcessRawPreview** (riga 178) — `public byte[] ProcessRawPreview(RawFrame raw)`. Anteprima per il confronto: solo demosaico Hamilton-Adams e stretch MTF automatico, senza dark, bilanciamento del bianco o gradiente.
- **UpdateConfig** (riga 193) — `public void UpdateConfig(AppConfig config)`. Sostituisce la configurazione.
- **ApplyDark** (riga 212) — `private void ApplyDark(RawFrame raw, ushort[] work)`.
- **PedestalOf** (riga 228) — `private int PedestalOf(MasterDark master)`.
- **ApplyHotPixels** (riga 239) — `private static void ApplyHotPixels(Geometry g, string phase)`.
- **ComputeGains** (riga 260) — `private (double R, double G, double B) ComputeGains(AppConfig cfg, RgbImage img, Geometry g, double sunDeg)`.
- **ManualGains** (riga 293) — `private static (double R, double G, double B) ManualGains(double[]? gains)`.
- **ApplyGradient** (riga 303) — `private static void ApplyGradient(AppConfig cfg, RgbImage img, Geometry g, string phase)`.
- **DrawOverlay** (riga 331) — `private void DrawOverlay(AppConfig cfg, RawFrame raw, Span<byte> rgb, string phase)`.
- **SharedBlack** (riga 355) — `private static StretchParams SharedBlack(StretchParams p)`. Punto nero comune ai tre canali: media dei tre punti stimati da Estimate.
- **PhaseText** (riga 361) — `private static string PhaseText(Language language, string phase)`.
- **EnsureGeometry** (riga 376) — `private Geometry EnsureGeometry(int width, int height)`.
- **Median** (riga 390) — `private int Median(ushort[] data)`.
- **ValidateFrame** (riga 412) — `private static void ValidateFrame(RawFrame raw)`.
- **ToImagingPattern** (riga 426) — `private static ImgBayer ToImagingPattern(CoreBayer pattern)`.
- **StretchKindOf** (riga 437) — `private static StretchKind StretchKindOf(CoreStretch stretch)`.
- **SaturationOf** (riga 440) — `private static float SaturationOf(double saturation)`.
- **SmoothingAlpha** (riga 443) — `private static double SmoothingAlpha(double smoothing)`.
- **ResolveTimeZone** (riga 453) — `private static TimeZoneInfo ResolveTimeZone(string id)`.
- **Geometry** (riga 466, tipo) — `private sealed class Geometry`. Stato legato alle dimensioni del sensore: ricreato quando cambiano larghezza o altezza.
- **Geometry** (riga 468) — `public Geometry(int width, int height, SkyMask mask)`.


## AllSky.Products (src/AllSky.Products)

### KeogramBuilder.cs

- **KeogramBuilder** (riga 14, tipo) — `public sealed class KeogramBuilder`. Keogram incrementale.
- **KeogramBuilder** (riga 50) — `public KeogramBuilder(int frameWidth, int frameHeight, int expectedFrames, double angleDegrees)`. Crea un keogram che usa un diametro del frame passante per il centro, a un angolo scelto.
- **AddFrame** (riga 89) — `public void AddFrame(ReadOnlySpan<byte> rgb8, DateTimeOffset timestamp)`. Aggiunge la colonna del frame come colonna successiva del keogram.
- **Build** (riga 119) — `public byte[] Build()`. Restituisce il keogram come RGB 8 bit row-major (3 byte per pixel), di dimensione OutputWidth per `frameHeight`.
- **EnsureCapacity** (riga 175) — `private void EnsureCapacity(long needed)`.
- **BuildSamplingTable** (riga 195) — `private static (int[] X, int[] Y) BuildSamplingTable(int width, int height, double angleDegrees)`. Per ogni riga di output calcola il pixel sorgente sul diametro: punto `centro + s * (sin a, cos a)`, con `s = j - (H-1)/2`, arrotondato al pixel più vicino e limitato al frame.
- **Nearest** (riga 213) — `private static int Nearest(double value, int max)`.

### ProductFrameInfo.cs

- **ProductFrameInfo** (riga 8, tipo) — `public sealed record ProductFrameInfo(DateTimeOffset Timestamp, string Phase);`. Metadati di un frame usati dai prodotti: istante di cattura e fase del cielo (per esempio "day", "twilight", "night", come in docs/API.md).

### StartrailsBuilder.cs

- **StartrailsBuilder** (riga 14, tipo) — `public sealed class StartrailsBuilder`. Startrails incrementale: accumulatore "lighten" (massimo per canale e per pixel) su un unico buffer di `W * H * 3` byte.
- **AddFrame** (riga 65) — `public void AddFrame(ReadOnlySpan<byte> rgb8, bool skip = false)`. Accumula un frame: ogni canale di ogni pixel diventa il massimo tra il valore accumulato (dopo il decadimento) e il frame.
- **Build** (riga 91) — `public byte[] Build()`. Restituisce una copia dell'immagine accumulata, RGB 8 bit row-major di `W * H * 3` byte.
- **MaxVectorized** (riga 97) — `private void MaxVectorized(ReadOnlySpan<byte> rgb8)`. Massimo per canale senza decadimento, con Vector<byte> e coda scalare.
- **MaxWithDecay** (riga 118) — `private void MaxWithDecay(ReadOnlySpan<byte> rgb8, byte[] decayTable)`. Decadimento tramite tabella, poi massimo per canale.


## AllSky.Publish (src/AllSky.Publish)

### Contracts.cs

- **PublishInfo** (riga 7, tipo) — `public static class PublishInfo`. Versione del modulo di pubblicazione.
- **Secret** (riga 14, tipo) — `public readonly struct Secret : IEquatable<Secret>`. Valore segreto: ToString non rivela mai il contenuto (quindi nemmeno i record che lo contengono).
- **Reveal** (riga 20) — `public string Reveal()`. Contenuto in chiaro: usarlo solo per l'header di invio e per cifrare su disco.
- **ToString** (riga 21) — `public override string ToString()`.
- **Equals** (riga 22) — `public bool Equals(Secret other)`.
- **Equals** (riga 23) — `public override bool Equals(object? obj)`.
- **GetHashCode** (riga 24) — `public override int GetHashCode()`.
- **PublishSettings** (riga 28, tipo) — `public sealed record PublishSettings`. Impostazioni di pubblicazione.
- **SettingsError** (riga 51, tipo) — `public sealed record SettingsError(string Path, string Message);`. Errore di validazione di un campo (path in camelCase, es.
- **ISecretProtector** (riga 54, tipo) — `public interface ISecretProtector`. Cifra/decifra segreti per il salvataggio su disco.
- **PublishMode** (riga 61, tipo) — `public enum PublishMode`. Modalita' del cielo, serializzata come day | twilight | night.
- **PublishFrame** (riga 64, tipo) — `public sealed record PublishFrame(`. Frame offerto dal motore: RGB 8 bit pulito (senza scritte), righe contigue, 3 byte per pixel.
- **PublishState** (riga 75, tipo) — `public enum PublishState`. Stato del publisher.
- **PublishStatus** (riga 90, tipo) — `public sealed record PublishStatus(`. Stato visibile all'utente.
- **TestResult** (riga 102, tipo) — `public sealed record TestResult(bool Ok, string Code, string Message, int? StatusCode);`. Esito di "Test connessione".
- **SendOutcome** (riga 105, tipo) — `public enum SendOutcome`. Cosa fare dopo un tentativo di invio (classificazione dell'esito HTTP).
- **SendResult** (riga 120, tipo) — `public sealed record SendResult(SendOutcome Outcome, TimeSpan? RetryAfter, string Code, string Reason, int? StatusCode);`. Esito di un invio.
- **QueuedItem** (riga 123, tipo) — `public sealed record QueuedItem(`. Elemento della coda: un'immagine pronta con i suoi metadati.
- **IUplinkSender** (riga 133, tipo) — `public interface IUplinkSender`. Invia un elemento al server.
- **IPublishDelay** (riga 140, tipo) — `public interface IPublishDelay`. Attesa astratta per poter testare backoff e intervalli con un orologio finto.
- **IJitter** (riga 146, tipo) — `public interface IJitter`. Sorgente di casualita' 0..1 (jitter del backoff), iniettabile nei test.
- **IPublisher** (riga 152, tipo) — `public interface IPublisher : IAsyncDisposable`. Il publisher usato dal motore.

### Pipeline/CaptureIdFactory.cs

- **CaptureIdFactory** (riga 13, tipo) — `public static class CaptureIdFactory`. Genera il captureId di uno scatto: UUID deterministico name-based versione 5 (RFC 4122, SHA-1).
- **Create** (riga 30) — `public static string Create(string deviceId, DateTimeOffset capturedAtUtc)`. Calcola il captureId dello scatto.
- **Version5** (riga 45) — `private static string Version5(Guid namespaceId, string name)`. Algoritmo UUID v5: SHA-1 di (namespace in ordine di rete + nome UTF-8), primi 16 byte, versione 5 e variante RFC 4122.

### Pipeline/DiskQueue.cs

- **DiskQueue** (riga 23, tipo) — `public sealed class DiskQueue`. Coda su disco degli elementi da inviare.
- **Enqueue** (riga 103) — `public void Enqueue(QueuedItem item, byte[] jpeg)`. Aggiunge o sovrascrive un elemento.
- **List** (riga 126) — `public IReadOnlyList<QueuedItem> List()`. Elementi in coda, dal piu' vecchio al piu' recente per CapturedAtUtc.
- **ReadImage** (riga 139) — `public byte[]? ReadImage(QueuedItem item)`. Legge il JPEG di un elemento in coda, identificato dal captureId.
- **Remove** (riga 164) — `public bool Remove(string captureId)`. Rimuove un elemento (json prima, poi jpg).
- **TrimToNewest** (riga 182) — `public int TrimToNewest(int keep)`. Elimina i piu' vecchi finche' restano al massimo `keep` elementi.
- **LoadFromDisk** (riga 196) — `private void LoadFromDisk()`. Carica dal disco gli elementi validi ed elimina gli orfani.
- **TrimLocked** (riga 277) — `private int TrimLocked(int keep)`. Elimina i piu' vecchi oltre `keep`.
- **RemoveEntry** (riga 303) — `private bool RemoveEntry(string id)`. Rimuove un elemento: prima il json (cosi' l'elemento scompare), poi il jpg.
- **Touch** (riga 328) — `private void Touch()`.
- **ImagePathFor** (riga 333) — `private string ImagePathFor(string id)`.
- **MetaPathFor** (riga 335) — `private string MetaPathFor(string id)`.
- **SortOldestFirst** (riga 337) — `private static IEnumerable<QueuedItem> SortOldestFirst(IEnumerable<QueuedItem> items)`.
- **WriteAtomic** (riga 341) — `private static void WriteAtomic(string path, byte[] bytes)`. Scrittura atomica: file .tmp, flush su disco, rinomina sopra il file finale.
- **TryDelete** (riga 360) — `private static void TryDelete(string path)`.
- **ValidateCaptureId** (riga 375) — `private static string ValidateCaptureId(string? captureId)`.
- **QueueMeta** (riga 386, tipo) — `private sealed record QueueMeta(`. Metadati su disco: tutti i campi di QueuedItem tranne ImagePath.

### Pipeline/PreparedImage.cs

- **PreparedImage** (riga 10, tipo) — `public sealed record PreparedImage(byte[] Jpeg, int Width, int Height, ulong ContentHash);`. Immagine pronta per l'invio: JPEG codificato, dimensioni effettive e hash del contenuto.

### Pipeline/PublishWorker.cs

- **PublishWorker** (riga 27, tipo) — `public sealed class PublishWorker : IPublisher`. Publisher del modulo.
- **PublishWorker** (riga 101) — `public PublishWorker(PublishSettings initialSettings, IClock clock, IPublishDelay delay, IUplinkSender sender,`. Crea il worker.
- **Offer** (riga 120) — `public void Offer(PublishFrame frame)`.
- **GetStatus** (riga 144) — `public PublishStatus GetStatus()`.
- **ApplySettings** (riga 154) — `public void ApplySettings(PublishSettings settings)`.
- **TestConnectionAsync** (riga 257) — `public async Task<TestResult> TestConnectionAsync(PublishSettings? overrideSettings, CancellationToken ct)`.
- **StartAsync** (riga 300) — `public Task StartAsync(CancellationToken ct)`.
- **StopAsync** (riga 337) — `public async Task StopAsync()`.
- **DisposeAsync** (riga 360) — `public async ValueTask DisposeAsync()`. Equivalente a StopAsync.
- **RunOnceAsync** (riga 366) — `internal async Task RunOnceAsync(CancellationToken ct)`. Esegue UN giro del ciclo: acquisizione se e' il momento, poi svuotamento della coda.
- **RunLoopSafeAsync** (riga 403) — `private async Task RunLoopSafeAsync(CancellationToken ct)`. Ciclo di sfondo con gestione di ogni eccezione: un errore imprevisto viene registrato e il ciclo riprende dopo 5 s.
- **LoopAsync** (riga 419) — `private async Task LoopAsync(CancellationToken ct)`.
- **ComputeWait** (riga 466) — `private TimeSpan ComputeWait()`. Tempo da dormire: il minore fra intervallo, prossimo tick e prossimo tentativo (se in backoff), con minimo 1 s.
- **Acquire** (riga 497) — `private void Acquire(PublishSettings settings)`. Acquisizione: prende l'ultimo frame nuovo, lo prepara e lo accoda.
- **IsAlreadyProcessed** (riga 581) — `private bool IsAlreadyProcessed(PublishFrame frame)`.
- **DrainAsync** (riga 590) — `private async Task DrainAsync(PublishSettings settings, CancellationToken ct)`. Svuotamento: invia gli elementi piu' recenti, dal piu' vecchio al piu' recente, rispettando lo stato e il backoff.
- **IsGated** (riga 655) — `private bool IsGated(DateTimeOffset now)`. True se l'invio non puo' partire adesso: sospeso, o prima del prossimo tentativo di backoff.
- **SendOneAsync** (riga 663) — `private async Task<SendResult> SendOneAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct)`.
- **ApplyResult** (riga 689) — `private bool ApplyResult(QueuedItem item, SendResult result, PublishSettings settings)`. Applica l'esito di un invio.
- **ScheduleRetry** (riga 740) — `private void ScheduleRetry(TimeSpan? retryAfter, string code, string reason, DateTimeOffset now)`.
- **BuildStatusLocked** (riga 761) — `private PublishStatus BuildStatusLocked()`. Costruisce lo stato visibile.
- **SetErrorLocked** (riga 772) — `private void SetErrorLocked(string code, string text, DateTimeOffset now)`.
- **RecordError** (riga 779) — `private void RecordError(string code, string text)`.
- **NotifyIfChanged** (riga 789) — `private void NotifyIfChanged()`. Emette StatusChanged se lo stato visibile e' cambiato dall'ultima emissione.
- **Log** (riga 822) — `private void Log(string message)`.
- **CurrentSettings** (riga 840) — `private PublishSettings CurrentSettings()`.
- **Clean** (riga 849) — `private static string Clean(string? text, PublishSettings settings, string fallback)`. Ripulisce un testo prima di salvarlo o registrarlo: rimuove segreti e URL, toglie i caratteri di controllo, limita la lunghezza.
- **IsActive** (riga 869) — `private static bool IsActive(PublishSettings settings)`.
- **HasCredentialChange** (riga 871) — `private static bool HasCredentialChange(PublishSettings old, PublishSettings updated)`.
- **IntervalOf** (riga 878) — `private static TimeSpan IntervalOf(PublishSettings settings)`.

### Pipeline/RetryPolicy.cs

- **RetryPolicy** (riga 15, tipo) — `public sealed class RetryPolicy`. Ritardi del backoff esponenziale con jitter per i tentativi di invio.
- **GetDelay** (riga 57) — `public TimeSpan GetDelay(int attempt, TimeSpan? retryAfter = null)`. Calcola l'attesa prima del tentativo successivo.

### Pipeline/SystemJitter.cs

- **SystemJitter** (riga 4, tipo) — `public sealed class SystemJitter : IJitter`. Sorgente di jitter basata su Shared: valori uniformi in [0, 1).
- **NextDouble** (riga 7) — `public double NextDouble()`. Restituisce un valore casuale in [0, 1).

### Pipeline/WebImagePreparer.cs

- **WebImagePreparer** (riga 23, tipo) — `public sealed class WebImagePreparer`. Prepara il frame per l'invio: ridimensiona (mai ingrandendo) il lato lungo a LongEdgePx, codifica in JPEG e tiene il peso sotto TargetBytes.
- **WebImagePreparer** (riga 42) — `public WebImagePreparer(IJpegEncoder encoder)`. Crea il preparatore con l'encoder JPEG da usare.
- **Prepare** (riga 54) — `public PreparedImage Prepare(PublishFrame frame, PublishSettings settings)`. Prepara il frame secondo le impostazioni.
- **ValidateFrame** (riga 108) — `private static void ValidateFrame(PublishFrame frame)`.
- **FitLongEdge** (riga 124) — `private static (int Width, int Height) FitLongEdge(int width, int height, int maxLongEdge)`. Dimensioni con lato lungo al massimo `maxLongEdge`, rapporto mantenuto, mai ingrandite.
- **ResizeTo** (riga 139) — `private static byte[] ResizeTo(PublishFrame frame, int width, int height)`. Restituisce i byte RGB alla dimensione richiesta.
- **QualitySequence** (riga 153) — `private static IEnumerable<int> QualitySequence(int startQuality)`. Qualita' da provare: quella iniziale, poi a passi di 5 fino a 40 (o fino alla qualita' iniziale, se e' gia' sotto 40).
- **Fnv1a64** (riga 165) — `private static ulong Fnv1a64(byte[] data)`. FNV-1a 64 bit: offset basis 14695981039346656037, primo 1099511628211, XOR poi moltiplicazione per byte.
- **Candidate** (riga 178, tipo) — `private sealed record Candidate(byte[] Jpeg, int Width, int Height);`. Miglior tentativo finora, usato solo se nessuno rientra nel target.

### Pipeline/WorkerSystemPublishDelay.cs

- **SystemPublishDelay** (riga 4, tipo) — `public sealed class SystemPublishDelay : IPublishDelay`. Attesa reale del ciclo di pubblicazione, basata su Delay(TimeSpan, CancellationToken).
- **DelayAsync** (riga 9) — `public Task DelayAsync(TimeSpan delay, CancellationToken ct)`. Attende il tempo richiesto.

### Settings/BootstrapLoader.cs

- **BootstrapResult** (riga 7, tipo) — `public enum BootstrapResult`. Esito dell'applicazione del file di avvio bootstrap.json.
- **BootstrapLoader** (riga 25, tipo) — `public static class BootstrapLoader`. Applica il file di avvio `bootstrap.json` (campi camelCase: enabled, serverUrl, deviceId, token, profileName, intervalSeconds, longEdgePx, jpegQuality, extraHeaderName, extraHeaderValue).
- **Apply** (riga 33) — `public static BootstrapResult Apply(string bootstrapPath, PublishSettingsStore store, Action<string> log)`. Applica il file di avvio, se presente.
- **Merge** (riga 86) — `private static PublishSettings Merge(PublishSettings current, BootstrapFileDto f)`.
- **OverwriteAndDelete** (riga 101) — `private static void OverwriteAndDelete(string path, Action<string> log)`. Sovrascrive il file con spazi (best effort) e poi lo cancella.
- **Emit** (riga 133) — `private static void Emit(Action<string> log, string message)`.
- **BootstrapFileDto** (riga 147, tipo) — `internal sealed class BootstrapFileDto`. Forma del file bootstrap.json.

### Settings/DpapiSecretProtector.cs

- **DpapiSecretProtector** (riga 16, tipo) — `public sealed class DpapiSecretProtector : ISecretProtector`. Protezione dei segreti con DPAPI, ambito utente corrente (crypt32.dll: CryptProtectData / CryptUnprotectData).
- **DataBlob** (riga 23, tipo) — `private struct DataBlob`. DATA_BLOB (CRYPTOAPI_BLOB): cbData (DWORD) e pbData (BYTE*).
- **CryptProtectData** (riga 31) — `private static extern bool CryptProtectData(`.
- **CryptUnprotectData** (riga 42) — `private static extern bool CryptUnprotectData(`.
- **LocalFree** (riga 52) — `private static extern IntPtr LocalFree(IntPtr hMem);`.
- **Protect** (riga 58) — `public byte[] Protect(byte[] plain)`. Cifra i byte con DPAPI per l'utente corrente.
- **Unprotect** (riga 91) — `public byte[] Unprotect(byte[] protectedData)`. Decifra i byte prodotti da Protect per l'utente corrente.
- **EnsureWindows** (riga 121) — `private static void EnsureWindows()`.
- **CopyToUnmanaged** (riga 127) — `private static IntPtr CopyToUnmanaged(byte[] data)`.
- **CopyFromBlob** (riga 134) — `private static byte[] CopyFromBlob(DataBlob blob)`.
- **ZeroAndFree** (riga 143) — `private static void ZeroAndFree(IntPtr ptr, int length)`. Azzera il buffer non gestito (che puo' contenere testo in chiaro) e lo libera.
- **FreeBlob** (riga 154) — `private static void FreeBlob(DataBlob blob)`. Azzera il blocco restituito da DPAPI e lo libera con LocalFree.

### Settings/PublishLog.cs

- **PublishLog** (riga 17, tipo) — `public sealed class PublishLog`. Logger a file del modulo di pubblicazione, thread-safe.
- **Info** (riga 46) — `public void Info(string text)`. Scrive una riga di livello INFO.
- **Warn** (riga 49) — `public void Warn(string text)`. Scrive una riga di livello WARN.
- **Error** (riga 52) — `public void Error(string text, Exception? ex = null)`. Scrive una riga di livello ERROR; con `ex` aggiunge tipo e messaggio dell'eccezione.
- **AsInfo** (riga 55) — `public Action<string> AsInfo()`. Restituisce il log come callback Action<string> (livello INFO).
- **Write** (riga 57) — `private void Write(string level, string text, Exception? ex)`.
- **Clean** (riga 92) — `private string Clean(string? value)`. Applica la sanificazione e rende il testo una riga sola.
- **RotateOldFiles** (riga 112) — `private void RotateOldFiles(DateTime today)`. Elimina i publish-YYYYMMDD.log piu' vecchi di RetentionDays rispetto a `today`.

### Settings/PublishSettingsStore.cs

- **PublishSettingsStore** (riga 16, tipo) — `public sealed class PublishSettingsStore`. Archivio delle impostazioni di pubblicazione in un file JSON (camelCase, indentato).
- **PublishSettingsStore** (riga 31) — `public PublishSettingsStore(string filePath, ISecretProtector protector)`.
- **Load** (riga 45) — `public PublishSettings Load()`. Legge le impostazioni.
- **Save** (riga 89) — `public void Save(PublishSettings s)`. Salva le impostazioni in modo atomico, cifrando i segreti.
- **ProtectSecret** (riga 111) — `private string? ProtectSecret(Secret secret)`.
- **UnprotectSecret** (riga 133) — `private Secret UnprotectSecret(string? stored)`.
- **ToSettings** (riga 157) — `private PublishSettings ToSettings(SettingsFileDto f)`.
- **WriteAtomic** (riga 176) — `private void WriteAtomic(string json)`. Scrive su .tmp e poi sposta sul file definitivo (sostituzione del file esistente).
- **QuarantineCorruptFile** (riga 202) — `private void QuarantineCorruptFile()`.
- **TryDelete** (riga 214) — `private static void TryDelete(string path)`.
- **SettingsFileDto** (riga 229, tipo) — `internal sealed class SettingsFileDto`. Forma del file JSON delle impostazioni.

### Settings/SettingsValidator.cs

- **SettingsValidator** (riga 14, tipo) — `public static class SettingsValidator`. Validazione delle impostazioni di pubblicazione.
- **Validate** (riga 26) — `public static IReadOnlyList<SettingsError> Validate(PublishSettings s)`. Valida le impostazioni e restituisce l'elenco degli errori (vuoto se valide).
- **ValidateServerUrl** (riga 48) — `private static void ValidateServerUrl(PublishSettings s, List<SettingsError> errors)`.
- **ValidateDeviceId** (riga 107) — `private static void ValidateDeviceId(PublishSettings s, List<SettingsError> errors)`.
- **ValidateToken** (riga 121) — `private static void ValidateToken(PublishSettings s, List<SettingsError> errors)`.
- **ValidateProfileName** (riga 134) — `private static void ValidateProfileName(PublishSettings s, List<SettingsError> errors)`.
- **ValidateExtraHeader** (riga 146) — `private static void ValidateExtraHeader(PublishSettings s, List<SettingsError> errors)`.
- **AddIfOutOfRange** (riga 171) — `private static void AddIfOutOfRange(List<SettingsError> errors, string path, int value, int min, int max, string message)`.
- **HasUserInfo** (riga 178) — `private static bool HasUserInfo(string url)`. True se nella parte di autorita' dell'URL compare una '@' (credenziali incorporate).
- **IsLocalHost** (riga 190) — `private static bool IsLocalHost(string host)`.
- **ContainsControl** (riga 196) — `private static bool ContainsControl(string value)`.
- **Add** (riga 198) — `private static void Add(List<SettingsError> errors, string path, string message)`.

### Transport/ErrorClassifier.cs

- **ErrorClassifier** (riga 14, tipo) — `public static class ErrorClassifier`. Traduce l'esito HTTP (o l'eccezione di rete) in un SendResult: cosa fare del frame e quando riprovare.
- **Classify** (riga 23) — `public static SendResult Classify(int statusCode, string? body, string? retryAfter, IClock clock, Redactor redactor)`. Classifica una risposta HTTP.
- **ClassifyTimeout** (riga 80) — `public static SendResult ClassifyTimeout(Redactor redactor)`. Timeout dell'invio: riprovare con backoff.
- **ClassifyNetworkError** (riga 88) — `public static SendResult ClassifyNetworkError(Redactor redactor)`. Errore di rete (DNS, connessione, TLS, I/O): riprovare con backoff.
- **ClassifyInvalidRequest** (riga 96) — `public static SendResult ClassifyInvalidRequest(Redactor redactor)`. Richiesta non costruibile (URL o impostazioni non utilizzabili): non riprovare.
- **ParseRetryAfter** (riga 108) — `internal static TimeSpan? ParseRetryAfter(string? value, IClock clock)`. Interpreta Retry-After: secondi interi (negativi -> null) oppure data HTTP, relativa a `clock`.
- **Make** (riga 133) — `private static SendResult Make(SendOutcome outcome, TimeSpan? retryAfter, string code, string reason, int? statusCode, Redactor redactor)`.

### Transport/Redactor.cs

- **Redactor** (riga 13, tipo) — `public sealed class Redactor`. Rende sicuro per log, messaggi e report un testo che potrebbe contenere segreti o URL con credenziali.
- **Redactor** (riga 43) — `public Redactor(Func<IReadOnlyCollection<string>> secrets)`. Crea il sanificatore.
- **Sanitize** (riga 49) — `public string Sanitize(string? text)`. Restituisce il testo sanificato.
- **ReplaceSecrets** (riga 75) — `private string ReplaceSecrets(string text)`. Sostituisce ogni segreto non vuoto, in forma grezza e URL-encoded.
- **CleanControlCharacters** (riga 96) — `private static string CleanControlCharacters(string text)`. Tab, a capo e simili diventano spazi; gli altri caratteri di controllo vengono rimossi.
- **Truncate** (riga 110) — `private static string Truncate(string value)`. Tronca a MaxLength caratteri senza spezzare una coppia di surrogati.

### Transport/UplinkHttpClient.cs

- **UplinkHttpClient** (riga 14, tipo) — `public sealed class UplinkHttpClient : IUplinkSender`. Client HTTP del protocollo Uplink: invio dei frame (SendAsync) e verifica della connessione.
- **SendAsync** (riga 47) — `public async Task<SendResult> SendAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct)`. Invia un frame.
- **TestConnectionAsync** (riga 97) — `public async Task<TestResult> TestConnectionAsync(PublishSettings settings, CancellationToken ct)`. Verifica la connessione con GET {ServerUrl}/api/uplink/health, stesso Bearer e header extra, stesso timeout.
- **BuildRequestOrNull** (riga 142) — `private static HttpRequestMessage? BuildRequestOrNull(Func<HttpRequestMessage> factory)`. Costruisce la richiesta; null se l'URL o le impostazioni non sono utilizzabili.
- **ReadPrefixAsync** (riga 159) — `private async Task<string?> ReadPrefixAsync(HttpContent content, CancellationToken callerCt, CancellationToken linkedCt)`. Legge al massimo MaxBodyBytes byte del corpo.
- **RetryAfterOf** (riga 186) — `private static string? RetryAfterOf(HttpResponseMessage response)`. Valore grezzo di Retry-After dalla risposta, se presente (il parsing e' in ErrorClassifier).
- **Test** (riga 193) — `private TestResult Test(bool ok, string code, string message, int? statusCode)`.

### Transport/UplinkJson.cs

- **UplinkJson** (riga 14, tipo) — `public static class UplinkJson`. Serializzazione della parte "meta" e lettura della risposta 200 del protocollo Uplink.
- **SerializeMeta** (riga 23) — `public static string SerializeMeta(UplinkMeta meta)`. Serializza i metadati.
- **TryParseResponse** (riga 58) — `public static bool TryParseResponse(string? body, out bool stored, out bool duplicate, out bool visible)`. Legge la risposta 200 {"stored":..,"duplicate":..,"visible":..}.
- **FormatCapturedAt** (riga 89) — `public static string FormatCapturedAt(DateTimeOffset capturedAt)`. Formatta un istante in UTC con millisecondi, cultura invariante.
- **FormatMode** (riga 93) — `public static string FormatMode(PublishMode mode)`. Modalita' in minuscolo: day, twilight, night.
- **WriteOptionalNumber** (riga 101) — `private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)`.
- **WriteNullableNumber** (riga 108) — `private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)`.
- **ReadBool** (riga 117) — `private static bool ReadBool(JsonElement root, string name)`.

### Transport/UplinkMeta.cs

- **UplinkMeta** (riga 16, tipo) — `public sealed record UplinkMeta(`. Metadati della parte "meta" del multipart verso il server (schema v1).

### Transport/UplinkRequestFactory.cs

- **UplinkHttpRequest** (riga 10, tipo) — `internal sealed class UplinkHttpRequest : HttpRequestMessage`. Richiesta HTTP del protocollo Uplink.
- **UplinkHttpRequest** (riga 12) — `public UplinkHttpRequest(HttpMethod method, Uri uri) : base(method, uri)`.
- **ToString** (riga 15) — `public override string ToString()`. Solo metodo e URL: nessun header, nessun contenuto.
- **UplinkRequestFactory** (riga 22, tipo) — `public static class UplinkRequestFactory`. Costruisce le richieste del protocollo Uplink: invio del frame (POST multipart) e verifica della connessione (GET).
- **Create** (riga 48) — `public static HttpRequestMessage Create(QueuedItem item, byte[] jpeg, PublishSettings s, string? deviceProfileName = null)`. Crea la POST multipart verso {ServerUrl}/api/uplink/allsky con le parti "meta" (application/json) e "image" (image/jpeg).
- **CreateHealth** (riga 84) — `public static HttpRequestMessage CreateHealth(PublishSettings s)`. Crea la GET di verifica connessione verso {ServerUrl}/api/uplink/health (endpoint ipotetico, vedi HealthPath).
- **BuildUri** (riga 97) — `internal static Uri BuildUri(string serverUrl, string relativePath)`. Unisce base e percorso senza doppie barre, usando Uri; un eventuale percorso nel ServerUrl viene conservato.
- **ApplyCredentials** (riga 109) — `internal static void ApplyCredentials(HttpRequestMessage request, PublishSettings s)`. Header di autenticazione (Bearer) e header extra facoltativo, sulla sola richiesta indicata.


## AllSky.Satellites (src/AllSky.Satellites)

### CelesTrakTleSource.cs

- **CelesTrakTleSource** (riga 20, tipo) — `public sealed class CelesTrakTleSource : ITleSource, ITleFetcher`. Sorgente TLE da CelesTrak, con cache su disco e blocco di 24 ore dopo un errore.
- **GetAsync** (riga 61) — `public async Task<IReadOnlyList<TleSet>> GetAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)`.
- **FetchAsync** (riga 68) — `public async Task<TleFetchResult> FetchAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)`. Come GetAsync, ma restituisce anche lo stato e il messaggio per l'utente.
- **FetchCoreAsync** (riga 83) — `private async Task<TleFetchResult> FetchCoreAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken ct)`.
- **Attempt** (riga 181, tipo) — `private sealed record Attempt(TleSet? Set, string? FormatError, string? HaltReason);`. Esito di un singolo download: un TLE valido, un rifiuto di formato o un motivo di arresto.
- **DownloadAsync** (riga 183) — `private async Task<Attempt> DownloadAsync(SatelliteTarget target, DateTimeOffset now, CancellationToken ct)`.
- **Halt** (riga 224) — `private static Attempt Halt(string reason)`.
- **IsBlockActive** (riga 226) — `private static bool IsBlockActive(BlockState block, DateTimeOffset now)`.
- **IsFresh** (riga 229) — `private bool IsFresh(DateTimeOffset fetchedUtc, DateTimeOffset now)`.
- **BlockedMessage** (riga 235) — `private static string BlockedMessage(BlockState block)`.
- **CacheMeta** (riga 241, tipo) — `private sealed record CacheMeta(DateTimeOffset FetchedUtc);`.
- **ReadCache** (riga 243) — `private TleSet? ReadCache(SatelliteTarget target)`.
- **TrySaveCache** (riga 269) — `private bool TrySaveCache(TleSet set)`.
- **BlockState** (riga 285, tipo) — `private sealed record BlockState(DateTimeOffset UntilUtc, string Reason);`.
- **ReadBlock** (riga 287) — `private BlockState? ReadBlock()`.
- **TryWriteBlock** (riga 300) — `private bool TryWriteBlock(BlockState block)`.
- **WriteAtomic** (riga 314) — `private static void WriteAtomic(string path, string content)`. Scrittura atomica: file temporaneo nella stessa cartella, poi spostamento sul file definitivo.
- **TlePath** (riga 331) — `private string TlePath(int noradId)`.
- **MetaPath** (riga 333) — `private string MetaPath(int noradId)`.

### EarthShadow.cs

- **EarthShadow** (riga 12, tipo) — `public static class EarthShadow`. Illuminazione del satellite dal Sole, con modello di ombra cilindrica della Terra.
- **IsSunlit** (riga 23) — `public static bool IsSunlit(EciKm satEci, EciKm sunEci)`. Indica se il satellite è illuminato dal Sole.

### OrbitPropagation.cs

- **EciKm** (riga 10, tipo) — `public readonly record struct EciKm(double X, double Y, double Z)`. Posizione di un oggetto nel sistema ECI (geocentrico, quasi inerziale), in chilometri.
- **Dot** (riga 16) — `public double Dot(EciKm other)`. Prodotto scalare con un altro vettore.
- **SatObservation** (riga 28, tipo) — `public sealed record SatObservation(`. Osservazione di un satellite da un osservatore a terra in un istante.
- **IOrbitPropagator** (riga 44, tipo) — `public interface IOrbitPropagator`. Propagatore di un singolo satellite.
- **IOrbitPropagatorFactory** (riga 55, tipo) — `public interface IOrbitPropagatorFactory`. Crea un propagatore partendo dagli elementi orbitali di un satellite.

### PassFinder.cs

- **PassOptions** (riga 8, tipo) — `public sealed record PassOptions`. Opzioni della ricerca dei passaggi.
- **Validate** (riga 20) — `internal void Validate()`. Controlla i valori e lancia ArgumentOutOfRangeException se non sono validi.
- **PassPoint** (riga 43, tipo) — `public sealed record PassPoint(DateTimeOffset Utc, double AzimuthDeg, double ElevationDeg);`. Un istante di un passaggio, con azimut ed elevazione.
- **SatellitePass** (riga 56, tipo) — `public sealed record SatellitePass(`. Un passaggio: intervallo continuo in cui l'elevazione è almeno quella minima.
- **PassFinder** (riga 79, tipo) — `public static class PassFinder`. Ricerca dei passaggi di un satellite sopra un osservatore.
- **Find** (riga 93) — `public static IReadOnlyList<SatellitePass> Find(`. Trova i passaggi nella finestra [from, from + window).
- **CheckObserver** (riga 177) — `private static void CheckObserver(double latDeg, double lonDeg, double altM)`.
- **Sample** (riga 196, tipo) — `private sealed record Sample(DateTimeOffset Utc, SatObservation Obs);`. Campione: istante e osservazione del satellite.
- **Scanner** (riga 199, tipo) — `private sealed class Scanner`. Esegue le osservazioni e costruisce i passaggi, con i dati dell'osservatore.
- **Scanner** (riga 207) — `public Scanner(IOrbitPropagator propagator, double latDeg, double lonDeg, double altM, PassOptions options)`.
- **Observe** (riga 216) — `public Sample Observe(DateTimeOffset utc)`.
- **IsAbove** (riga 221) — `public bool IsAbove(Sample s)`.
- **Edge** (riga 227) — `public Sample Edge(Sample above, Sample below)`. Bisezione del passaggio sopra/sotto soglia.
- **Build** (riga 255) — `public SatellitePass Build(Sample startSample, bool startTruncated, List<Sample> inPass, Sample endSample, bool endTruncated)`. Costruisce il passaggio dai campioni sopra soglia.
- **Refine** (riga 306) — `private Sample Refine(DateTimeOffset lo, DateTimeOffset hi)`. Massimo dell'elevazione nell'intervallo [lo, hi] con la sezione aurea.
- **ElevationAt** (riga 344) — `private double ElevationAt(DateTimeOffset lo, double seconds)`.
- **ToPoint** (riga 349) — `private static PassPoint ToPoint(Sample s)`.

### SatelliteModels.cs

- **SatelliteTarget** (riga 6, tipo) — `public sealed record SatelliteTarget(int NoradId, string Name);`. Satellite da seguire, identificato dal numero di catalogo NORAD.
- **TleSet** (riga 9, tipo) — `public sealed record TleSet(int NoradId, string Name, string Line1, string Line2, DateTimeOffset FetchedUtc);`. Elementi orbitali TLE di un satellite, con l'istante del download (UTC).
- **TleFetchStatus** (riga 12, tipo) — `public enum TleFetchStatus`. Esito di una richiesta di TLE.
- **TleFetchResult** (riga 31, tipo) — `public sealed record TleFetchResult(IReadOnlyList<TleSet> Sets, TleFetchStatus Status, string? Message);`. Risultato di una richiesta di TLE con stato e messaggio per l'utente.
- **ITleSource** (riga 34, tipo) — `public interface ITleSource`. Sorgente degli elementi orbitali.
- **ITleFetcher** (riga 44, tipo) — `public interface ITleFetcher`. Sorgente che restituisce anche lo stato e il messaggio per l'utente (blocco, dati rifiutati).
- **ICelesTrakClock** (riga 51, tipo) — `public interface ICelesTrakClock`. Orologio della sorgente TLE, sostituibile nei test.
- **SystemCelesTrakClock** (riga 58, tipo) — `public sealed class SystemCelesTrakClock : ICelesTrakClock`. Orologio di sistema.
- **SatelliteCatalog** (riga 65, tipo) — `public static class SatelliteCatalog`. Bersagli predefiniti dell'applicazione.

### SunEphemeris.cs

- **SunEphemeris** (riga 14, tipo) — `public static class SunEphemeris`. Posizione approssimata del Sole in ECI, in km.
- **SunEci** (riga 21) — `public static EciKm SunEci(DateTimeOffset utc)`. Posizione del Sole in ECI nell'istante indicato, in km.
- **ToRad** (riga 58) — `private static double ToRad(double deg)`.

### TleParser.cs

- **TleParser** (riga 8, tipo) — `internal static class TleParser`. Lettura e validazione del formato TLE (2 o 3 righe).
- **TryParse** (riga 21) — `public static bool TryParse(string? text, SatelliteTarget target, DateTimeOffset fetchedUtc, out TleSet? set, out string? error)`. Valida un blocco TLE.
- **Checksum** (riga 95) — `internal static int Checksum(string line)`. Checksum modulo 10: cifre sommate, il segno meno vale 1, il resto 0 (prime 68 colonne).
- **HasValidChecksum** (riga 114) — `private static bool HasValidChecksum(string line)`.
- **ParseCatalogNumber** (riga 117) — `private static int ParseCatalogNumber(string line)`.


## AllSky.Satellites.Sgp4 (src/AllSky.Satellites.Sgp4)

### SgpNetPropagator.cs

- **SgpNetPropagator** (riga 20, tipo) — `public sealed class SgpNetPropagator : IOrbitPropagator`. Propagatore di un satellite con SGP.NET: posizione ECI e osservazione topocentrica.
- **SgpNetPropagator** (riga 25) — `public SgpNetPropagator(Satellite satellite)`. Crea il propagatore per un satellite già costruito con SGP.NET.
- **Observe** (riga 31) — `public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)`.
- **NormalizeDeg** (riga 54) — `private static double NormalizeDeg(double deg)`.

### SgpNetPropagatorFactory.cs

- **SgpNetPropagatorFactory** (riga 22, tipo) — `public sealed class SgpNetPropagatorFactory : IOrbitPropagatorFactory`. Crea propagatori SGP4 con la libreria SGP.NET.
- **Create** (riga 25) — `public IOrbitPropagator Create(TleSet tle)`.


## AllSky.Sky (src/AllSky.Sky)

### Calibration/SkyCalibrationResult.cs

- **SkyCalibrationResult** (riga 12, tipo) — `public record SkyCalibrationResult(`. Esito della calibrazione del modello fisheye dalle posizioni del Sole.

### Calibration/SunCalibrationCollector.cs

- **SunCalibrationCollector** (riga 17, tipo) — `public sealed class SunCalibrationCollector`. Raccoglie coppie (posizione del Sole in pixel, posizione del Sole in cielo) e stima il modello fisheye.
- **Add** (riga 52) — `public bool Add(DateTimeOffset utc, double sunX, double sunY, double sunAltDeg, double sunAzDeg)`. Aggiunge un punto.
- **TryFit** (riga 81) — `public bool TryFit(out SkyCalibrationResult? r)`. Esegue il fit.
- **CoverageProblem** (riga 143) — `private static string? CoverageProblem(double[] alt, double[] az, bool[]? mask)`.
- **AzimuthSpread** (riga 177) — `private static double AzimuthSpread(List<double> az)`.
- **CountTrue** (riga 188) — `private static int CountTrue(bool[] mask)`.
- **Format** (riga 201) — `private static string Format(double v)`.
- **SunObservation** (riga 203, tipo) — `private readonly record struct SunObservation(DateTimeOffset Utc, double X, double Y, double AltDeg, double AzDeg);`.

### Calibration/SunCalibrationSolver.cs

- **SunCalibrationSolver** (riga 18, tipo) — `internal static class SunCalibrationSolver`. Fit ai minimi quadrati non lineari del modello fisheye sulle posizioni osservate del Sole.
- **Solution** (riga 30, tipo) — `internal sealed record Solution(`. Esito del fit per un valore di Mirrored.
- **Solve** (riga 44) — `internal static Solution? Solve(`. Stima il modello per un valore fisso di Mirrored.
- **LinearInitialGuess** (riga 129) — `private static (double Cx, double Cy, double S)? LinearInitialGuess(double[] x, double[] y, double[] alt)`.
- **InitialRotationDeg** (riga 175) — `private static double InitialRotationDeg(double[] x, double[] y, double[] az, double cx, double cy, bool mirrored)`.
- **Minimize** (riga 197) — `private static double[]? Minimize(`.
- **Jacobian** (riga 301) — `private static double[,]? Jacobian(`.
- **Residual** (riga 343) — `private static double[]? Residual(`.
- **RadialErrors** (riga 367) — `private static double[]? RadialErrors(double[] p, bool mirrored, double[] x, double[] y, double[] alt, double[] az)`.
- **RejectOutliers** (riga 392) — `private static bool[]? RejectOutliers(double[] err, bool[] inlier)`.
- **BuildModel** (riga 425) — `private static FisheyeModel? BuildModel(double[] p, bool mirrored)`.
- **IsValid** (riga 435) — `private static bool IsValid(double[] p)`.
- **Unpack** (riga 451) — `private static (double Cx, double Cy, double S, double Th, double K2) Unpack(double[] p)`.
- **Indices** (riga 454) — `private static int[] Indices(bool[] mask)`.
- **Median** (riga 467) — `private static double Median(List<double> values)`.
- **Dot** (riga 474) — `private static double Dot(double[] a, double[] b)`.
- **SolveLinear** (riga 485) — `private static double[]? SolveLinear(double[,] a, double[] b)`.
- **NormalizeDeg** (riga 547) — `private static double NormalizeDeg(double deg)`.

### Calibration/SunDetector.cs

- **SunDetection** (riga 10, tipo) — `public readonly record struct SunDetection(double X, double Y, double AreaPx, double PeakLuma);`. Sole individuato in un frame in luminanza.
- **SunDetector** (riga 31, tipo) — `public static class SunDetector`. Individua il Sole in un frame in luminanza a 8 bit.
- **TryFindSun** (riga 46) — `public static bool TryFindSun(byte[] luma, int width, int height, out SunDetection d)`. Cerca il Sole usando la soglia di saturazione di default.
- **TryFindSun** (riga 56) — `public static bool TryFindSun(byte[] luma, int width, int height, byte saturationThreshold, out SunDetection d)`. Cerca il Sole nel frame.
- **Circularity** (riga 186) — `private static double Circularity(int area, int bw, int bh)`. Circolarita' approssimata: (area / bbox) normalizzato su pi/4, per il rapporto di aspetto min/max della bounding box.

### FisheyeModel.cs

- **FisheyeModel** (riga 45, tipo) — `public record FisheyeModel(`. Modello della lente fisheye all-sky con proiezione equidistante, con correzione radiale K2.
- **TryAltAzToPixel** (riga 90) — `public bool TryAltAzToPixel(double altDeg, double azDeg, out double x, out double y)`. Proietta un punto del cielo sull'immagine.
- **TryPixelToAltAz** (riga 130) — `public bool TryPixelToAltAz(double x, double y, out double altDeg, out double azDeg)`. Inverte la proiezione: dal pixel ricava altezza e azimut (Nord = 0, Est = 90).
- **IsInsideSky** (riga 177) — `public bool IsInsideSky(double x, double y)`. True se il punto e' dentro o sul bordo del cerchio dell'orizzonte.
- **PixelScaleArcsecPerPx** (riga 194) — `public double PixelScaleArcsecPerPx(double altDeg)`. Scala sul cielo, in arcosecondi per pixel, nella direzione radiale a una data altezza.
- **RadiusForZenithDistance** (riga 210) — `private double RadiusForZenithDistance(double zDeg)`. Raggio in pixel per una distanza zenitale data in gradi.
- **ZenithDistanceForRadius** (riga 217) — `private double ZenithDistanceForRadius(double rPx)`. Distanza zenitale in gradi per un raggio in pixel, con Newton sulla variabile t = z / 90.
- **RequireFinite** (riga 236) — `private static double RequireFinite(double value, string name)`.
- **RequirePositiveFinite** (riga 248) — `private static double RequirePositiveFinite(double value, string name)`.
- **RequireK2** (riga 261) — `private static double RequireK2(double value)`.
- **NormalizeDeg** (riga 274) — `private static double NormalizeDeg(double deg)`.
- **Show** (riga 284) — `private static string Show(double value)`.


## AllSky.Timelapse (src/AllSky.Timelapse)

### EncoderPolicy.cs

- **EncoderPolicy** (riga 13, tipo) — `public sealed record EncoderPolicy`. Regole per scegliere l'encoder H.264 tra quelli disponibili in ffmpeg.
- **EncoderSelection** (riga 30, tipo) — `public sealed record EncoderSelection(string? Encoder, bool IsH264, string? Message)`. Esito della scelta dell'encoder.

### EncoderProbe.cs

- **WorkingEncoders** (riga 10, tipo) — `public sealed record WorkingEncoders(IReadOnlyList<EncoderSelection> Encoders, string? Message)`. Encoder che hanno superato la codifica di prova, in ordine di preferenza.
- **EncoderProbe** (riga 22, tipo) — `public static class EncoderProbe`. Sceglie l'encoder H.264 leggendo l'output di `ffmpeg -hide_banner -encoders`.
- **ParseVideoEncoders** (riga 38) — `public static IReadOnlySet<string> ParseVideoEncoders(string encodersOutput)`. Estrae i nomi degli encoder video dall'output di `ffmpeg -encoders`.
- **Select** (riga 72) — `public static EncoderSelection Select(string encodersOutput, EncoderPolicy policy)`. Sceglie l'encoder dato l'output di `ffmpeg -encoders`, senza eseguire nulla.
- **ProbeAsync** (riga 91) — `public static async Task<EncoderSelection> ProbeAsync(string? ffmpegPath, EncoderPolicy policy, CancellationToken ct = default)`. Esegue `ffmpeg -encoders` e sceglie il primo candidato (senza prova).
- **GetWorkingAsync** (riga 113) — `public static Task<WorkingEncoders> GetWorkingAsync(string? ffmpegPath, EncoderPolicy policy, CancellationToken ct = default)`. Restituisce gli encoder che superano una codifica di prova (5 frame 128x72), in ordine di preferenza.
- **Candidates** (riga 135) — `internal static IReadOnlyList<EncoderSelection> Candidates(IReadOnlySet<string> available, EncoderPolicy policy, out bool gplBlocked)`. Candidati nell'ordine di preferenza: encoder della policy presenti in ffmpeg (libx264 solo con AllowGpl), poi mpeg4 come ultima voce se AllowFallbackMpeg4.
- **ComputeWorkingAsync** (riga 172) — `private static async Task<WorkingEncoders> ComputeWorkingAsync(string ffmpegPath, EncoderPolicy policy)`.
- **TrialEncodeAsync** (riga 211) — `private static async Task<bool> TrialEncodeAsync(string ffmpegPath, string encoder, CancellationToken ct)`. Esegue la codifica di prova di un encoder.
- **ListEncodersAsync** (riga 271) — `private static async Task<(string? Output, string? Error)> ListEncodersAsync(string? ffmpegPath, CancellationToken ct)`. Esegue `ffmpeg -hide_banner -encoders`.
- **UnavailableMessage** (riga 338) — `private static string UnavailableMessage(bool gplBlocked)`.
- **PolicyKey** (riga 351) — `private static string PolicyKey(EncoderPolicy policy)`.
- **IsH264** (riga 357) — `private static bool IsH264(string name)`. True se il nome di encoder produce H.264.

### FfmpegArguments.cs

- **FfmpegArguments** (riga 9, tipo) — `public static class FfmpegArguments`. Costruzione pura degli argomenti di ffmpeg per il timelapse.
- **OutputSize** (riga 29) — `public static (int Width, int Height) OutputSize(int width, int height, int? maxWidth)`. Dimensioni di output: scala in giù a `maxWidth` se la larghezza è maggiore, poi arrotonda entrambe le dimensioni al pari inferiore (come `trunc(iw/2)*2`), minimo 2.
- **BitrateFor** (riga 49) — `public static int BitrateFor(int width, int height, int fps)`. Bitrate per gli encoder a bitrate: 0,1 bit per pixel per frame, limitato a [MinBitrate, MaxBitrate].
- **Build** (riga 62) — `public static IReadOnlyList<string> Build(TimelapseOptions options, string outputPath, int width, int height)`. Argomenti completi di ffmpeg.
- **ValidateOptions** (riga 111) — `public static void ValidateOptions(TimelapseOptions options)`. Controlla i parametri.

### FfmpegLocator.cs

- **FfmpegLocator** (riga 12, tipo) — `public static class FfmpegLocator`. Cerca l'eseguibile ffmpeg in quest'ordine: percorso configurato, cartella `tools/ffmpeg` accanto all'eseguibile, PATH di sistema.
- **Locate** (riga 26) — `public static string? Locate(string? configuredPath = null, string? baseDirectory = null, string? pathVariable = null)`. Restituisce il percorso completo di ffmpeg, oppure null se non è stato trovato.

### IFrameOverlay.cs

- **IFrameOverlay** (riga 9, tipo) — `public interface IFrameOverlay`. Maschera informativa applicata a un frame prima del montaggio.
- **NullFrameOverlay** (riga 19, tipo) — `public sealed class NullFrameOverlay : IFrameOverlay`. Implementazione nulla: restituisce il JPEG invariato.
- **ApplyAsync** (riga 25) — `public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)`.
- **FrameOverlayPlan** (riga 40, tipo) — `public sealed record FrameOverlayPlan(`. Come applicare la maschera a un montaggio: l'overlay, il sidecar del frame di indice N (frame N, orario N) e il gestore degli errori per frame.
- **FrameOverlayStep** (riga 46, tipo) — `public static class FrameOverlayStep`. Passo del montaggio: produce i byte JPEG da inviare a ffmpeg per un frame.
- **ComposeAsync** (riga 57) — `public static async Task<byte[]> ComposeAsync(string jpegPath, int index, FrameOverlayPlan plan, CancellationToken ct)`. Legge il JPEG e lo compone con la maschera.

### ITimelapseBuilder.cs

- **ITimelapseBuilder** (riga 4, tipo) — `public interface ITimelapseBuilder`. Costruisce un MP4 timelapse da una sequenza di JPEG.

### JpegSize.cs

- **JpegSize** (riga 4, tipo) — `public static class JpegSize`. Lettura delle dimensioni di un JPEG dal solo header (marker SOFn), senza decodificarlo.
- **TryRead** (riga 9) — `public static bool TryRead(string path, out int width, out int height)`. Legge larghezza e altezza dal primo marker SOF.
- **IsStartOfFrame** (riga 89) — `private static bool IsStartOfFrame(int marker)`. SOF0..SOF15 esclusi DHT (C4), JPG (C8) e DAC (CC).

### ProcessSafety.cs

- **ProcessSafety** (riga 7, tipo) — `internal static class ProcessSafety`. Utilità per terminare i processi ffmpeg senza eccezioni.
- **KillTree** (riga 13) — `internal static void KillTree(Process process)`. Termina l'intero albero di processi se il processo non è ancora uscito, e attende la fine.
- **RequestKill** (riga 34) — `internal static void RequestKill(Process process)`. Richiede la terminazione senza attendere.

### TimelapseBuilder.cs

- **TimelapseBuilder** (riga 19, tipo) — `public sealed class TimelapseBuilder : ITimelapseBuilder`. Costruisce il timelapse MP4 lanciando ffmpeg come processo esterno e inviandogli i JPEG su stdin.
- **BuildAsync** (riga 34) — `public async Task BuildAsync(`.
- **ResolveEncodersAsync** (riga 100) — `private async Task<IReadOnlyList<string>> ResolveEncodersAsync(TimelapseOptions options, CancellationToken ct)`. Encoder da provare in ordine: quello esplicito, oppure i candidati verificati con la prova.
- **RunAttemptAsync** (riga 117) — `private static async Task<AttemptResult> RunAttemptAsync(`. Un tentativo di codifica con un solo encoder.
- **FeedFramesAsync** (riga 208) — `private static async Task<bool> FeedFramesAsync(`. Copia i JPEG su stdin, uno alla volta.
- **CopyFrameAsync** (riga 260) — `private static async Task<bool> CopyFrameAsync(Stream stdin, string path, byte[] buffer, CancellationToken ct)`. Copia un JPEG su disco verso stdin a blocchi.
- **WriteComposedAsync** (riga 291) — `private static async Task<bool> WriteComposedAsync(Stream stdin, byte[] jpeg, CancellationToken ct)`. Invia un JPEG gia' composto in memoria.
- **ReadTailAsync** (riga 305) — `private static async Task ReadTailAsync(StreamReader reader, Queue<string> tail)`. Legge stderr fino alla fine, conservando solo le ultime righe.
- **TailText** (riga 319) — `private static string TailText(Queue<string> tail)`.
- **DeleteQuietly** (riga 324) — `private static void DeleteQuietly(string path)`.
- **AttemptResult** (riga 343, tipo) — `private sealed record AttemptResult(bool Success, string Detail, string Tail)`. Esito di un tentativo: successo, oppure motivo e ultime righe di stderr.
- **Failed** (riga 347) — `public static AttemptResult Failed(string detail, string tail)`.

### TimelapseException.cs

- **TimelapseException** (riga 4, tipo) — `public sealed class TimelapseException : Exception`. Errore nella costruzione del timelapse (ffmpeg assente, terminato con errore, file non prodotto).
- **TimelapseException** (riga 8) — `public TimelapseException(string message)`. Crea l'eccezione con un messaggio.
- **TimelapseException** (riga 17) — `public TimelapseException(string message, string stderrTail)`. Crea l'eccezione con un messaggio e le ultime righe di stderr di ffmpeg.
- **TimelapseException** (riga 26) — `public TimelapseException(string message, Exception inner)`. Crea l'eccezione con un messaggio e un'eccezione interna.

### TimelapseOptions.cs

- **TimelapseOptions** (riga 4, tipo) — `public sealed record TimelapseOptions`. Parametri del timelapse MP4.


## AllSky.Transits (src/AllSky.Transits)

### PassTrackProjector.cs

- **ObserverSite** (riga 11, tipo) — `public sealed record ObserverSite(double LatDeg, double LonDeg, double AltM);`. Posizione dell'osservatore, necessaria per calcolare alt/az del satellite.
- **TrackProjectionOptions** (riga 14, tipo) — `public sealed record TrackProjectionOptions`. Opzioni della proiezione di un passaggio.
- **TrackPixel** (riga 27, tipo) — `public sealed record TrackPixel(DateTimeOffset Utc, double X, double Y);`. Punto della traccia prevista nel frame, con l'istante a cui corrisponde.
- **ProjectedTrackRun** (riga 31, tipo) — `public sealed record ProjectedTrackRun(IReadOnlyList<TrackPixel> Points);`. Tratto continuo della traccia visibile: sopra l'orizzonte e dentro il cerchio del cielo.
- **ProjectedPassTrack** (riga 37, tipo) — `public sealed record ProjectedPassTrack(SatellitePass Pass, bool Calibrated, IReadOnlyList<ProjectedTrackRun> Runs)`. Traccia di un passaggio proiettata nei pixel, già tagliata sull'orizzonte.
- **Distance** (riga 57) — `private static double Distance(TrackPixel a, TrackPixel b)`.
- **PassTrackProjector** (riga 74, tipo) — `public static class PassTrackProjector`. Proietta un passaggio di un satellite nei pixel dell'immagine, con il modello fisheye calibrato.
- **Project** (riga 82) — `public static ProjectedPassTrack Project(`. Proietta il passaggio nei pixel.
- **SampleTimes** (riga 132) — `private static IEnumerable<DateTimeOffset> SampleTimes(DateTimeOffset from, DateTimeOffset to, TimeSpan step)`. Istanti da `from` a `to` con passo fisso, estremo finale incluso.

### PredictedTrackProviderAdapter.cs

- **TrackedSatellite** (riga 10, tipo) — `public sealed record TrackedSatellite(string Name, IOrbitPropagator Propagator);`. Satellite da includere nelle tracce previste, con il suo propagatore.
- **PredictedTrackOptions** (riga 13, tipo) — `public sealed record PredictedTrackOptions`. Opzioni dell'adattatore delle tracce previste.
- **PredictedTrackProviderAdapter** (riga 39, tipo) — `public sealed class PredictedTrackProviderAdapter : IPredictedTrackProvider`. Fornisce al StreakClassifier le tracce previste in pixel, a partire dai passaggi.
- **PredictedTrackProviderAdapter** (riga 52) — `public PredictedTrackProviderAdapter(`. Crea l'adattatore.
- **GetTracks** (riga 71) — `public IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc)`.
- **EnsureCache** (riga 103) — `private CachedPasses EnsureCache(DateTimeOffset utc)`.
- **CachedPasses** (riga 136, tipo) — `private sealed class CachedPasses`. Passaggi proiettati nell'intervallo [From, To), con il nome del satellite di ciascuno.
- **CachedPasses** (riga 138) — `public CachedPasses(DateTimeOffset from, DateTimeOffset to, List<(string Name, ProjectedPassTrack Track)> passes)`.

### TransitConfirmation.cs

- **TransitVerdict** (riga 4, tipo) — `public enum TransitVerdict`. Esito della conferma di un transito nelle immagini.
- **TransitConfirmationOptions** (riga 23, tipo) — `public sealed record TransitConfirmationOptions`. Soglie e dimensioni della ricerca lungo la traccia.
- **Validate** (riga 58) — `internal void Validate()`. Controlla i valori e lancia ArgumentOutOfRangeException se non sono validi.
- **RequireRange** (riga 69) — `private static void RequireRange(double value, double min, double max, string name)`.
- **TransitConfirmation** (riga 84, tipo) — `public sealed record TransitConfirmation(`. Esito della conferma di un passaggio.

### TransitConfirmer.cs

- **TransitConfirmer** (riga 16, tipo) — `public sealed class TransitConfirmer`. Conferma se un passaggio previsto è visibile come scia nei frame della sua finestra.
- **Confirm** (riga 34) — `public TransitConfirmation Confirm(ProjectedPassTrack track, IEnumerable<LumaFrame> frames)`. Conferma il passaggio sui frame indicati.
- **ScoreFrame** (riga 86) — `private double ScoreFrame(LumaFrame frame, IReadOnlyList<TrackSample> samples)`. Punteggio del frame: frazione dei campioni della traccia con eccesso sopra soglia.
- **AddFlank** (riga 146) — `private static void AddFlank(LumaFrame frame, TrackSample s, int d, List<double> flank)`.
- **TryPixel** (riga 154) — `private static bool TryPixel(LumaFrame frame, double x, double y, out double value)`.
- **Median** (riga 168) — `private static double Median(List<double> values)`.
- **TrackSample** (riga 177, tipo) — `private readonly record struct TrackSample(double X, double Y, double NormalX, double NormalY);`. Campione della traccia con la normale unitaria alla direzione locale.
- **Resample** (riga 180) — `private static IReadOnlyList<TrackSample> Resample(IReadOnlyList<ProjectedTrackRun> runs, double spacing)`. Ricampiona le poligonali dei tratti a passo fisso, con la normale del segmento che contiene ciascun punto.


## AllSky.Tray (src/AllSky.Tray)

### Program.cs

- **Program** (riga 14, tipo) — `internal static class Program`. Punto di ingresso del Tray di AllSky (legacy: l'exe pubblicato è ora AllSky.Desktop).
- **Main** (riga 24) — `private static int Main(string[] args)`. Avvia All Sky Brain.
- **BuildStartupMessage** (riga 97) — `private static string BuildStartupMessage(int port, string logPath, Exception ex)`. Testo dell'errore mostrato quando l'host non parte.
- **ReportCrash** (riga 105) — `private static void ReportCrash(AppPaths paths, Exception ex)`. Registra un'eccezione non gestita nel crash log e mostra un messaggio (una sola volta alla volta).

### TrayApp.cs

- **TrayContext** (riga 20, tipo) — `public sealed class TrayContext : ApplicationContext`. Contesto senza finestre: icona nell'area di notifica con menu, apertura del pannello nel browser, avvio automatico con Windows (chiave HKCU Run) e chiusura ordinata dell'host.
- **TrayContext** (riga 48) — `public TrayContext(AllSkyHostRuntime runtime, AppPaths paths, bool english, bool openPanelOnStart)`. Crea l'icona nell'area di notifica e il relativo menu.
- **OpenPanel** (riga 99) — `private void OpenPanel()`. Apre l'indirizzo del pannello nel browser predefinito.
- **IsAutostartEnabled** (riga 113) — `private static bool IsAutostartEnabled()`. Restituisce true se la voce di avvio automatico esiste già nel registro.
- **SetAutostart** (riga 129) — `private void SetAutostart(bool enable)`. Scrive o rimuove la voce di avvio automatico.
- **OnCheckForUpdatesClick** (riga 162) — `private async void OnCheckForUpdatesClick(object? sender, EventArgs e)`. Voce "Controlla aggiornamenti".
- **DownloadUpdateAsync** (riga 204) — `private async Task DownloadUpdateAsync()`. Scarica l'aggiornamento e, a download completato, chiede se installarlo subito.
- **InstallAndRestart** (riga 223) — `private void InstallAndRestart()`. Programma l'installazione e chiude AllSky.
- **ErrorText** (riga 236) — `private string ErrorText(UpdateStatus status)`. Testo dell'errore di aggiornamento nella lingua del menu.
- **ShowMessage** (riga 239) — `private static void ShowMessage(string text, MessageBoxIcon icon)`.
- **ChooseWithButtons** (riga 246) — `private static bool ChooseWithButtons(string text, string confirmText, string cancelText)`. Finestra con due pulsanti dal testo scelto: MessageBox non permette etichette personalizzate.
- **ExitApplication** (riga 261) — `private void ExitApplication()`. Chiude l'applicazione dal menu "Esci".
- **OnApplicationExit** (riga 268) — `private void OnApplicationExit(object? sender, EventArgs e)`. Gestisce la chiusura di WinForms (se avviene per altri motivi).
- **StopAll** (riga 277) — `private void StopAll()`. Arresta l'host (timeout 10 s) e rimuove l'icona dall'area di notifica.
- **StopHost** (riga 300) — `private void StopHost()`. Ferma e libera l'host.
- **MenuTexts** (riga 333, tipo) — `private sealed record MenuTexts(`. Etichette del menu e messaggi dell'icona, in italiano o in inglese.

### Updates/VelopackBackend.cs

- **VelopackBackend** (riga 14, tipo) — `internal sealed class VelopackBackend : IUpdateBackend`. Backend degli aggiornamenti basato su Velopack (licenza MIT).
- **VelopackBackend** (riga 23) — `private VelopackBackend(UpdateManager manager)`.
- **Create** (riga 32) — `public static IUpdateBackend Create()`. Crea il backend.
- **CheckAsync** (riga 52) — `public async Task<string?> CheckAsync(CancellationToken cancellationToken)`.
- **DownloadAsync** (riga 65) — `public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)`.
- **ApplyAndRestartLater** (riga 76) — `public void ApplyAndRestartLater()`.
- **Pending** (riga 81) — `private UpdateInfo Pending()`.


## AllSky.Updates (src/AllSky.Updates)

### UpdateContracts.cs

- **IUpdateBackend** (riga 7, tipo) — `public interface IUpdateBackend`. Sorgente degli aggiornamenti.
- **IUpdateClock** (riga 30, tipo) — `public interface IUpdateClock`. Orologio del servizio, sostituibile nei test.
- **SystemUpdateClock** (riga 37, tipo) — `public sealed class SystemUpdateClock : IUpdateClock`. Orologio di sistema.
- **UnavailableUpdateBackend** (riga 46, tipo) — `public sealed class UnavailableUpdateBackend : IUpdateBackend`. Backend per le installazioni che non possono aggiornarsi.
- **CheckAsync** (riga 55) — `public Task<string?> CheckAsync(CancellationToken cancellationToken)`.
- **DownloadAsync** (riga 58) — `public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)`.
- **ApplyAndRestartLater** (riga 62) — `public void ApplyAndRestartLater()`.

### UpdateModels.cs

- **UpdateState** (riga 4, tipo) — `public enum UpdateState`. Stato dell'aggiornamento, letto dal Tray e dalla UI.
- **UpdateStatus** (riga 42, tipo) — `public sealed record UpdateStatus(`. Istantanea dello stato.
- **UpdateErrorCodes** (riga 52, tipo) — `public static class UpdateErrorCodes`. Codici di ErrorCode.

### UpdateService.cs

- **UpdateService** (riga 19, tipo) — `public sealed class UpdateService : IDisposable`. Controllo, download e installazione degli aggiornamenti di AllSky.
- **UpdateService** (riga 53) — `public UpdateService(IUpdateBackend backend, IUpdateClock clock, bool autoCheck)`. Crea il servizio.
- **GetStatus** (riga 90) — `public UpdateStatus GetStatus()`. Istantanea dello stato.
- **SetAutoCheck** (riga 100) — `public void SetAutoCheck(bool enabled)`. Cambia il controllo automatico.
- **Start** (riga 109) — `public void Start()`. Avvia il timer del controllo automatico.
- **Dispose** (riga 128) — `public void Dispose()`. Ferma il timer.
- **CheckNowAsync** (riga 146) — `public async Task<UpdateStatus> CheckNowAsync(CancellationToken cancellationToken = default)`. Controllo richiesto dall'utente (o dal controllo automatico).
- **DownloadAsync** (riga 205) — `public async Task<UpdateStatus> DownloadAsync(CancellationToken cancellationToken = default)`. Scarica la versione trovata dall'ultimo controllo.
- **ApplyLater** (riga 240) — `public UpdateStatus ApplyLater()`. Programma l'installazione dell'aggiornamento scaricato: avviene quando AllSky esce, poi l'app si riavvia.
- **TickAsync** (riga 269) — `internal async Task TickAsync()`. Controllo programmato: se è il momento e il controllo automatico è attivo, controlla e, se trova una versione, la scarica.
- **OnTimer** (riga 299) — `private void OnTimer(object? state)`.
- **DownloadCoreAsync** (riga 305) — `private async Task DownloadCoreAsync(CancellationToken cancellationToken)`. Corpo del download.
- **Fail** (riga 325) — `private void Fail(Exception ex, bool clearAvailableVersion)`. Classifica l'eccezione e la registra nello stato con un messaggio fisso.
- **Describe** (riga 342) — `private static (string Code, string Message) Describe(Exception ex)`. Codice e messaggio per un'eccezione del backend.
- **Update** (riga 349) — `private void Update(Func<UpdateStatus, UpdateStatus> change)`.
- **ReadSafely** (riga 357) — `private static T ReadSafely<T>(Func<T> read, T fallback)`.
- **CallbackProgress** (riga 371, tipo) — `private sealed class CallbackProgress(Action<int> report) : IProgress<int>`. Riporta l'avanzamento in modo sincrono, senza SynchronizationContext.
- **Report** (riga 373) — `public void Report(int value)`.


## AllSky.Weather (src/AllSky.Weather)

### AscomComSupport.cs

- **ComWorker** (riga 11, tipo) — `internal sealed class ComWorker : IDisposable`. Thread dedicato alle chiamate COM (STA su Windows, come `AscomComThread` di AllSky.Cameras.Ascom).
- **ComWorker** (riga 20) — `public ComWorker(string nome)`. Crea il thread e lo avvia.
- **RunAsync** (riga 34) — `public Task<T> RunAsync<T>(Func<T> lavoro, TimeSpan timeout, CancellationToken ct)`. Esegue `lavoro` sul thread COM e restituisce il risultato entro `timeout`.
- **AttendiAsync** (riga 73) — `private static async Task<T> AttendiAsync<T>(Task<T> operazione, TimeSpan timeout, CancellationToken ct)`.
- **Dispose** (riga 89) — `public void Dispose()`. Chiude la coda: le operazioni già accodate vengono eseguite, poi il thread termina.
- **EseguiCoda** (riga 99) — `private void EseguiCoda()`.
- **ComObservingConditionsFactory** (riga 112, tipo) — `public sealed class ComObservingConditionsFactory : IObservingConditionsFactory`. Fabbrica COM: crea il driver con GetTypeFromProgID(string), imposta `Connected = true` e restituisce un lettore late-bound.
- **Create** (riga 115) — `public IObservingConditionsReader Create(string progId)`.
- **Rilascia** (riga 144) — `private static void Rilascia(object driver)`.
- **ComObservingConditionsReader** (riga 167, tipo) — `internal sealed class ComObservingConditionsReader : IObservingConditionsReader, IDisposable`. Lettore late-bound sul driver COM.
- **ComObservingConditionsReader** (riga 171) — `public ComObservingConditionsReader(object driver)`.
- **CloudCover** (riga 176) — `public double CloudCover()`.
- **DewPoint** (riga 178) — `public double DewPoint()`.
- **Humidity** (riga 180) — `public double Humidity()`.
- **Pressure** (riga 182) — `public double Pressure()`.
- **RainRate** (riga 184) — `public double RainRate()`.
- **SkyQuality** (riga 186) — `public double SkyQuality()`.
- **SkyTemperature** (riga 188) — `public double SkyTemperature()`.
- **Temperature** (riga 190) — `public double Temperature()`.
- **WindDirection** (riga 192) — `public double WindDirection()`.
- **WindGust** (riga 194) — `public double WindGust()`.
- **WindSpeed** (riga 196) — `public double WindSpeed()`.
- **Dispose** (riga 199) — `public void Dispose()`. Disconnette il driver e rilascia l'oggetto COM.

### AscomObservingConditionsSource.cs

- **IObservingConditionsReader** (riga 7, tipo) — `public interface IObservingConditionsReader`. Lettura delle proprietà di un driver ASCOM ObservingConditions.
- **IObservingConditionsFactory** (riga 47, tipo) — `public interface IObservingConditionsFactory`. Crea il lettore per un ProgID.
- **AscomObservingConditionsMapper** (riga 57, tipo) — `internal static class AscomObservingConditionsMapper`. Conversione pura da IObservingConditionsReader a WeatherSnapshot.
- **Leggi** (riga 60) — `private static double? Leggi(Func<double> lettura, double min = double.NegativeInfinity, double max = double.PositiveInfinity)`. Legge una proprietà.
- **Leggi** (riga 92) — `internal static WeatherSnapshot Leggi(IObservingConditionsReader r, DateTimeOffset istante, string sorgente)`. Costruisce la snapshot.
- **HaDati** (riga 111) — `internal static bool HaDati(WeatherSnapshot s)`. Vero se almeno un campo misurato è disponibile.
- **AscomObservingConditionsSource** (riga 133, tipo) — `public sealed class AscomObservingConditionsSource : IWeatherSource, IAsyncDisposable`. Sorgente ASCOM ObservingConditions con binding tardivo (COM via `dynamic`, nessun pacchetto NuGet).
- **ReadAsync** (riga 171) — `public async Task<WeatherSnapshot?> ReadAsync(CancellationToken ct)`.
- **LeggiSulThread** (riga 195) — `private WeatherSnapshot? LeggiSulThread()`. Eseguito sul thread COM: crea il lettore al primo uso e legge tutte le proprietà.
- **DisposeAsync** (riga 222) — `public async ValueTask DisposeAsync()`. Rilascia il lettore sul thread COM e chiude il thread.

### OpenMeteoSource.cs

- **OpenMeteoSource** (riga 32, tipo) — `public sealed class OpenMeteoSource : IWeatherSource`. Sorgente meteo Open-Meteo, endpoint `forecast` con il blocco `current`.
- **ReadAsync** (riga 128) — `public async Task<WeatherSnapshot?> ReadAsync(CancellationToken ct)`.
- **LeggiAsync** (riga 141) — `private async Task<WeatherSnapshot?> LeggiAsync(CancellationToken ct)`.
- **RegistraErrore** (riga 211) — `private WeatherSnapshot? RegistraErrore(DateTimeOffset adesso, string motivo)`. Registra un errore e calcola il backoff.
- **UrlRichiesta** (riga 224) — `private string UrlRichiesta()`.
- **Analizza** (riga 236) — `internal static WeatherSnapshot? Analizza(string body, DateTimeOffset istante, string sorgente)`. Analizza il JSON.
- **Valore** (riga 281) — `private static double? Valore(JsonElement current, JsonElement? unita, string nome, double min = double.NegativeInfinity, double max = double.PositiveInfinity)`. Legge un valore numerico di `current`, controllandone l'unità e l'intervallo.

### WeatherModels.cs

- **WeatherSnapshot** (riga 9, tipo) — `public sealed record WeatherSnapshot(`. Una lettura meteo e della qualità del cielo.
- **IWeatherSource** (riga 25, tipo) — `public interface IWeatherSource`. Sorgente di dati meteo.
- **IWeatherClock** (riga 35, tipo) — `public interface IWeatherClock`. Orologio iniettabile, per cache, backoff e timestamp.
- **SystemWeatherClock** (riga 42, tipo) — `public sealed class SystemWeatherClock : IWeatherClock`. Orologio di sistema.

### WeatherSnapshotProvider.cs

- **IWeatherSnapshotProvider** (riga 7, tipo) — `public interface IWeatherSnapshotProvider`. Fornisce al momento della cattura l'ultima lettura meteo utile, per il sidecar del frame.
- **CachedWeatherSnapshotProvider** (riga 23, tipo) — `public sealed class CachedWeatherSnapshotProvider : IWeatherSnapshotProvider`. Provider con cache dell'ultimo valore valido.
- **CachedWeatherSnapshotProvider** (riga 48) — `public CachedWeatherSnapshotProvider(`. Crea il provider.
- **GetAsync** (riga 78) — `public async Task<WeatherSnapshot?> GetAsync(CancellationToken ct)`.
- **UltimoValidoSeRecente** (riga 126) — `private WeatherSnapshot? UltimoValidoSeRecente()`.
- **SegnalaErrore** (riga 137) — `private void SegnalaErrore(string motivo, Exception? causa)`.
- **SegnalaRipresa** (riga 148) — `private void SegnalaRipresa()`.


## AllSky.YouTube (src/AllSky.YouTube)

### DescriptionTemplate.cs

- **DescriptionTemplate** (riga 7, tipo) — `public sealed class DescriptionTemplate`. Modello della descrizione di un video, con gli stessi segnaposto del titolo (vedi TitleTemplate).
- **Parse** (riga 14) — `public static TemplateParseResult<DescriptionTemplate> Parse(string template)`. Analizza il modello e restituisce tutti gli errori trovati, con la posizione di ciascuno.
- **Render** (riga 29) — `public string Render(TitleContext ctx)`. Descrizione finale: righe normalizzate e ritagliata a DescriptionMaxBytes byte UTF-8.
- **Preview** (riga 37) — `public static TemplatePreview Preview(string template, TitleContext sample)`. Anteprima della descrizione con un contesto di esempio: parse e render in un solo passo.

### OAuth/GoogleOAuth.cs

- **GoogleOAuth** (riga 7, tipo) — `public static class GoogleOAuth`. Endpoint e scope del flusso "Desktop app" di Google.
- **BuildAuthorizationUrl** (riga 20) — `public static Uri BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge)`. URL di autorizzazione con i soli parametri elencati nella guida: client_id, redirect_uri, response_type=code, scope, state, code_challenge, code_challenge_method=S256.

### OAuth/GoogleOAuthClient.cs

- **GoogleOAuthClient** (riga 8, tipo) — `public sealed class GoogleOAuthClient`. Chiamate HTTP agli endpoint OAuth di Google: scambio del codice, refresh del token d'accesso e revoca.
- **ExchangeCodeAsync** (riga 23) — `public Task<OAuthTokens> ExchangeCodeAsync(`. Scambio code -> token (grant_type=authorization_code).
- **RefreshAsync** (riga 48) — `public Task<OAuthTokens> RefreshAsync(`. Rinnovo dell'access token con il refresh token (grant_type=refresh_token).
- **RevokeAsync** (riga 71) — `public async Task RevokeAsync(OAuthSecret token, CancellationToken cancellationToken = default)`. Revoca di un token (POST su oauth2.googleapis.com/revoke).
- **AddClientSecret** (riga 89) — `private static void AddClientSecret(List<KeyValuePair<string, string>> form, OAuthClientCredentials credentials)`.
- **PostTokenRequestAsync** (riga 95) — `private async Task<OAuthTokens> PostTokenRequestAsync(`.
- **ErrorFrom** (riga 112) — `private static OAuthException ErrorFrom(string body, int status)`. Errore dal corpo della risposta: solo il codice (o http_STATO), mai il corpo intero.

### OAuth/LoopbackCallbackListener.cs

- **LoopbackCallback** (riga 10, tipo) — `public sealed record LoopbackCallback(OAuthSecret Code, string? Error)`. Esito ricevuto dal browser sul redirect loopback.
- **ToString** (riga 12) — `public override string ToString()`.
- **LoopbackCallbackListener** (riga 21, tipo) — `public sealed class LoopbackCallbackListener : IDisposable`. Listener HTTP loopback per il solo login OAuth.
- **LoopbackCallbackListener** (riga 29) — `private LoopbackCallbackListener(HttpListener listener, string redirectUri)`.
- **Start** (riga 43) — `public static LoopbackCallbackListener Start()`. Avvia il listener su una porta libera di 127.0.0.1.
- **WaitForCallbackAsync** (riga 67) — `public async Task<LoopbackCallback> WaitForCallbackAsync(`. Attende il redirect del browser.
- **Dispose** (riga 101) — `public void Dispose()`. Chiude il listener e libera la porta.
- **TryHandle** (riga 117) — `private static bool TryHandle(HttpListenerContext context, string expectedState, out LoopbackCallback? callback)`.
- **FindFreeLoopbackPort** (riga 163) — `private static int FindFreeLoopbackPort()`.
- **StopQuietly** (riga 177) — `private static void StopQuietly(HttpListener listener)`.
- **Respond** (riga 190) — `private static void Respond(HttpListenerContext context, HttpStatusCode status, string html)`.
- **FixedTimeEquals** (riga 223) — `private static bool FixedTimeEquals(string a, string b)`.
- **Truncate** (riga 230) — `private static string Truncate(string value, int max)`.
- **Pages** (riga 233, tipo) — `private static class Pages`. Pagine fisse: nessun input dell'utente viene riflesso nell'HTML.

### OAuth/OAuthClientCredentials.cs

- **OAuthClientCredentials** (riga 7, tipo) — `public sealed record OAuthClientCredentials`. Client ID e client secret inseriti dall'utente: sono le credenziali "Desktop app" del SUO progetto Google Cloud.
- **OAuthClientCredentials** (riga 9) — `public OAuthClientCredentials(string clientId, OAuthSecret clientSecret)`.

### OAuth/OAuthModels.cs

- **OAuthException** (riga 8, tipo) — `public sealed class OAuthException : Exception`. Errore del flusso OAuth.
- **OAuthTokens** (riga 31, tipo) — `public sealed record OAuthTokens(`. Token ottenuti dallo scambio o dal refresh.
- **OAuthTokenParser** (riga 42, tipo) — `internal static class OAuthTokenParser`. Lettura della risposta JSON del token endpoint.
- **Parse** (riga 44) — `public static OAuthTokens Parse(string json, DateTimeOffset nowUtc)`.
- **ReadErrorCode** (riga 84) — `public static string? ReadErrorCode(string body)`. Codice d'errore dal corpo JSON, accettato solo se e' un identificatore semplice (es.
- **ReadString** (riga 104) — `private static string? ReadString(JsonElement root, string name)`.

### OAuth/OAuthPkce.cs

- **OAuthPkce** (riga 10, tipo) — `public static class OAuthPkce`. PKCE (RFC 7636, metodo S256, come chiede la guida Google "OAuth 2.0 for Mobile & Desktop Apps") e state anti-CSRF.
- **NewVerifier** (riga 19) — `public static string NewVerifier()`. Nuovo code_verifier.
- **NewState** (riga 22) — `public static string NewState()`. Nuovo state per la verifica del redirect.
- **ComputeChallenge** (riga 26) — `public static string ComputeChallenge(string verifier)`. code_challenge = BASE64URL(SHA256(ASCII(code_verifier))).
- **IsValidVerifier** (riga 36) — `public static bool IsValidVerifier(string? verifier)`. True se il verifier ha 43-128 caratteri del set non riservato della RFC 7636.
- **Base64Url** (riga 51) — `private static string Base64Url(byte[] bytes)`.

### OAuth/OAuthSecret.cs

- **OAuthSecret** (riga 7, tipo) — `public readonly struct OAuthSecret`. Valore segreto (client secret, token).
- **OAuthSecret** (riga 11) — `public OAuthSecret(string? value)`.
- **Reveal** (riga 20) — `public string Reveal()`. Contenuto in chiaro: solo per l'header o il corpo della richiesta e per la cifratura.
- **ToString** (riga 22) — `public override string ToString()`.

### OAuth/YouTubeAuthStore.cs

- **ISecretProtector** (riga 11, tipo) — `public interface ISecretProtector`. Cifratura dei segreti su disco.
- **StoredYouTubeAuth** (riga 18, tipo) — `public sealed record StoredYouTubeAuth(string ClientId, OAuthSecret ClientSecret, OAuthSecret RefreshToken);`. Credenziali YouTube salvate: client ID in chiaro, client secret e refresh token solo cifrati nel file.
- **YouTubeAuthStore** (riga 24, tipo) — `public sealed class YouTubeAuthStore`. Archivio JSON delle credenziali YouTube.
- **YouTubeAuthStore** (riga 37) — `public YouTubeAuthStore(string filePath, ISecretProtector protector)`.
- **Load** (riga 48) — `public StoredYouTubeAuth? Load()`. Legge le credenziali.
- **Save** (riga 84) — `public void Save(StoredYouTubeAuth auth)`. Salva le credenziali in modo atomico, cifrando i segreti.
- **Delete** (riga 100) — `public void Delete()`. Rimuove il file delle credenziali, se esiste.
- **Protect** (riga 106) — `private string? Protect(OAuthSecret secret)`.
- **Unprotect** (riga 127) — `private OAuthSecret Unprotect(string? stored)`.
- **WriteAtomic** (riga 150) — `private void WriteAtomic(string json)`.
- **TryDelete** (riga 176) — `private static void TryDelete(string path)`.
- **AuthFileDto** (riga 189, tipo) — `private sealed class AuthFileDto`.

### OAuth/YouTubeOAuthService.cs

- **YouTubeOAuthService** (riga 7, tipo) — `public sealed class YouTubeOAuthService`. Flusso OAuth completo per l'utente: login (loopback + PKCE + state), rinnovo dell'access token e logout con revoca.
- **YouTubeOAuthService** (riga 12) — `public YouTubeOAuthService(GoogleOAuthClient client, YouTubeAuthStore store)`.
- **LoginAsync** (riga 25) — `public async Task<OAuthTokens> LoginAsync(`. Login: avvia il listener loopback (solo 127.0.0.1, chiuso al termine), passa l'URL di autorizzazione a `openBrowser`, attende il redirect entro `timeout`, verifica lo state, scambia il codice e salva il refresh token cifrato.
- **RefreshAccessTokenAsync** (riga 67) — `public async Task<OAuthTokens> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)`. Rinnova l'access token con il refresh token salvato.
- **LogoutAsync** (riga 87) — `public async Task LogoutAsync(CancellationToken cancellationToken = default)`. Logout: revoca il refresh token presso Google e rimuove il file locale.

### TemplateEngine.cs

- **TemplateEngine** (riga 10, tipo) — `internal static class TemplateEngine`. Parser, renderer e normalizzazione comuni ai modelli con segnaposto (titolo e descrizione).
- **Kind** (riga 13, tipo) — `internal enum Kind`. Tipi di segnaposto riconosciuti.
- **Token** (riga 16, tipo) — `internal abstract record Token;`. Elemento del modello: testo letterale oppure segnaposto.
- **LiteralToken** (riga 18, tipo) — `internal sealed record LiteralToken(string Text) : Token;`.
- **PlaceholderToken** (riga 21, tipo) — `internal sealed record PlaceholderToken(Kind Kind, string? Format) : Token;`. Segnaposto; Format e null quando si usa il formato di default.
- **Tokenize** (riga 46) — `internal static List<Token> Tokenize(string template, List<TemplateError> errors)`. Converte il testo in token.
- **Render** (riga 96) — `internal static string Render(IReadOnlyList<Token> tokens, TitleContext ctx)`. Renderizza i token senza normalizzare il testo: la normalizzazione è compito del modello.
- **NormalizeLine** (riga 113) — `internal static string NormalizeLine(string raw)`. Normalizzazione di una riga (titolo): caratteri di controllo e separatori di riga diventano spazi, '<' e '>' vengono rimossi, gli spazi multipli si riducono a uno e il testo si ritaglia.
- **NormalizeMultiline** (riga 119) — `internal static string NormalizeMultiline(string raw)`. Normalizzazione multilinea (descrizione): mantiene le righe, normalizza fine riga in '\n', applica NormalizeLine a ogni riga e lascia al massimo una riga vuota consecutiva.
- **TruncateUtf16** (riga 134) — `internal static string TruncateUtf16(string text, int maxLength)`. Ritaglia a `maxLength` unità UTF-16 aggiungendo '…', senza spezzare una coppia surrogata.
- **TruncateUtf8** (riga 145) — `internal static string TruncateUtf8(string text, int maxBytes)`. Ritaglia a `maxBytes` byte UTF-8 aggiungendo '…', per code point interi.
- **FlushLiteral** (riga 162) — `private static void FlushLiteral(StringBuilder literal, List<Token> tokens)`.
- **ParsePlaceholder** (riga 170) — `private static void ParsePlaceholder(string inner, int position, List<Token> tokens, List<TemplateError> errors)`.
- **AcceptsFormat** (riga 220) — `private static bool AcceptsFormat(Kind kind)`.
- **IsValidFormat** (riga 223) — `private static bool IsValidFormat(Kind kind, string format, out string? reason)`. Valida un formato .NET con cultura invariante: deve essere accettato e non produrre caratteri di controllo.
- **Value** (riga 245) — `private static string Value(PlaceholderToken placeholder, TitleContext ctx)`.
- **FormatDuration** (riga 259) — `private static string FormatDuration(TimeSpan duration)`. Durata in mm:ss; i minuti oltre 59 restano nei minuti (es.
- **ReplaceLoneSurrogates** (riga 263) — `private static string ReplaceLoneSurrogates(string text)`. Sostituisce le unità surrogate isolate con U+FFFD, così ogni coppia valida resta intatta.
- **CollapseLine** (riga 287) — `private static string CollapseLine(string line)`. Una riga: rimuove '<' e '>', tratta i controlli come spazi e collassa gli spazi.
- **Validate** (riga 310) — `internal static void Validate(TitleContext ctx)`. Controlla il contesto: valori nulli o negativi sono errori del chiamante, quindi lanciano.
- **AddError** (riga 320) — `private static void AddError(List<TemplateError> errors, int position, string message)`.

### TemplateResults.cs

- **TemplateError** (riga 4, tipo) — `public sealed record TemplateError(int Position, string Message);`. Errore di un modello.
- **TemplateParseResult** (riga 7, tipo) — `public sealed record TemplateParseResult<T>(T? Template, IReadOnlyList<TemplateError> Errors) where T : class`. Esito di Parse.
- **TemplatePreview** (riga 14, tipo) — `public sealed record TemplatePreview(string? Text, IReadOnlyList<TemplateError> Errors)`. Esito di Preview.

### TitleContext.cs

- **TitleContext** (riga 14, tipo) — `public sealed record TitleContext(`. Dati della ripresa usati per riempire i segnaposto di titolo e descrizione.

### TitleTemplate.cs

- **TitleTemplate** (riga 12, tipo) — `public sealed class TitleTemplate`. Modello del titolo di un video, con segnaposto.
- **Parse** (riga 19) — `public static TemplateParseResult<TitleTemplate> Parse(string template)`. Analizza il modello e restituisce tutti gli errori trovati, con la posizione di ciascuno.
- **Render** (riga 37) — `public string Render(TitleContext ctx)`. Titolo finale: testo normalizzato (spazi ridotti, controlli e '<' '>' rimossi) e ritagliato a TitleMaxLength unità UTF-16.
- **Preview** (riga 45) — `public static TemplatePreview Preview(string template, TitleContext sample)`. Anteprima del titolo con un contesto di esempio: parse e render in un solo passo.
- **Failed** (riga 53) — `private static TemplateParseResult<TitleTemplate> Failed(TemplateError error)`.

### Upload/AtomicFile.cs

- **AtomicFile** (riga 4, tipo) — `internal static class AtomicFile`. Scrittura atomica: file temporaneo nella stessa cartella, poi spostamento sul file definitivo.
- **WriteAllBytes** (riga 6) — `public static void WriteAllBytes(string path, byte[] bytes)`.
- **TryDelete** (riga 31) — `private static void TryDelete(string path)`.

### Upload/IAccessTokenSource.cs

- **IAccessTokenSource** (riga 4, tipo) — `public interface IAccessTokenSource`. Fornisce un access token valido per le chiamate di upload.
- **OAuthAccessTokenSource** (riga 14, tipo) — `public sealed class OAuthAccessTokenSource : IAccessTokenSource`. Adattatore verso YouTubeOAuthService: mantiene in memoria l'access token fino a pochi minuti dalla scadenza.
- **OAuthAccessTokenSource** (riga 24) — `public OAuthAccessTokenSource(YouTubeOAuthService oauth, IClock clock)`.
- **GetAccessTokenAsync** (riga 30) — `public async Task<OAuthSecret> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)`.

### Upload/NightUploader.cs

- **NightUploader** (riga 13, tipo) — `public sealed class NightUploader`. Carica un timelapse notturno in modo idempotente.
- **NightUploader** (riga 29) — `public NightUploader(`.
- **StartAsync** (riga 46) — `public async Task<UploadOutcome> StartAsync(LatestTimelapse night, YouTubeUploadSettings settings, CancellationToken cancellationToken)`. Avvia il caricamento di una notte nuova.
- **ResumeAsync** (riga 104) — `public async Task<UploadOutcome> ResumeAsync(`. Riprende un caricamento InProgress.
- **ContinueAsync** (riga 141) — `private async Task<UploadOutcome> ContinueAsync(NightUploadRecord record, VideoMetadata? metadata, CancellationToken ct)`.
- **UploadBytesAsync** (riga 200) — `private async Task<UploadOutcome> UploadBytesAsync(NightUploadRecord record, Uri session, UploadProgress progress, CancellationToken ct)`. Invia i chunk dall'offset indicato fino al completamento.
- **WithRetryAsync** (riga 249) — `private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)`.
- **IsTransient** (riga 264) — `private static bool IsTransient(Exception ex, CancellationToken ct)`.
- **IsAuthPermanent** (riga 280) — `private static bool IsAuthPermanent(OAuthException ex)`.
- **Describe** (riga 283) — `private static string Describe(Exception ex)`.
- **Complete** (riga 291) — `private UploadOutcome Complete(NightUploadRecord record, string videoId)`.
- **HandleQuota** (riga 312) — `private UploadOutcome HandleQuota(NightUploadRecord record)`.
- **HandleAuth** (riga 326) — `private UploadOutcome HandleAuth(NightUploadRecord record)`.
- **Transient** (riga 339) — `private UploadOutcome Transient(NightUploadRecord record, string reason)`.
- **Fail** (riga 354) — `private UploadOutcome Fail(NightUploadRecord record, string reason)`.
- **Review** (riga 368) — `private UploadOutcome Review(NightUploadRecord record, string reason)`.
- **TrySave** (riga 382) — `private bool TrySave(NightUploadRecord record)`.
- **TrySave** (riga 385) — `private bool TrySave(Func<UploadJournalState, UploadJournalState> change)`. Salvataggio nel registro.
- **WithRecord** (riga 398) — `private static UploadJournalState WithRecord(UploadJournalState state, NightUploadRecord record)`.
- **ExtractVideoId** (riga 407) — `private static string? ExtractVideoId(NightUploadRecord record)`.
- **TryBuildMetadata** (riga 410) — `private bool TryBuildMetadata(LatestTimelapse night, YouTubeUploadSettings settings, DateTimeOffset now, out VideoMetadata? metadata, out string? error)`. Titolo, descrizione e impostazioni validati.
- **TryFileLength** (riga 489) — `private static bool TryFileLength(string path, out long length, out string? error)`.

### Upload/ResumableUploadClient.cs

- **ResumableUploadClient** (riga 17, tipo) — `public sealed class ResumableUploadClient`. Client del protocollo di upload resumable di YouTube Data API v3 (fonte: guida ufficiale "Resumable upload", letta il 2026-10-22).
- **StartSessionAsync** (riga 48) — `public async Task<Uri> StartSessionAsync(VideoMetadata metadata, long contentLength, CancellationToken cancellationToken)`. Apre una sessione resumable.
- **QueryAsync** (riga 76) — `public Task<UploadProgress> QueryAsync(Uri session, long totalLength, CancellationToken cancellationToken)`. Chiede a Google fino a dove ha ricevuto i byte (PUT vuoto con "bytes */total").
- **SendChunkAsync** (riga 84) — `public Task<UploadProgress> SendChunkAsync(Uri session, byte[] chunk, long offset, long totalLength, CancellationToken cancellationToken)`. Invia un chunk che inizia a `offset`.
- **SendPutAsync** (riga 97) — `private async Task<UploadProgress> SendPutAsync(`.
- **SendAuthorizedAsync** (riga 138) — `private async Task<HttpResponseMessage> SendAuthorizedAsync(`. Invia la richiesta costruita da `build`.
- **SendOnceAsync** (riga 152) — `private async Task<HttpResponseMessage> SendOnceAsync(`.
- **ParseNextOffset** (riga 164) — `internal static long ParseNextOffset(HttpResponseHeaders headers, long totalLength)`. Byte ricevuti secondo l'header Range ("bytes=0-N" → N+1).
- **ReadVideoId** (riga 182) — `internal static string? ReadVideoId(string json)`.
- **ErrorAsync** (riga 199) — `private static async Task<YouTubeApiException> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)`.
- **VideoResource** (riga 211) — `private static object VideoResource(VideoMetadata metadata)`. Risorsa `video` inviata all'inizializzazione.

### Upload/UploadCalendar.cs

- **UploadCalendar** (riga 6, tipo) — `internal sealed class UploadCalendar`. Conversioni di fuso orario per giorno locale, ora di caricamento e mezzanotte.
- **UploadCalendar** (riga 10) — `public UploadCalendar(TimeZoneInfo zone)`.
- **ToLocal** (riga 15) — `public DateTimeOffset ToLocal(DateTimeOffset utc)`.
- **LocalDate** (riga 17) — `public DateOnly LocalDate(DateTimeOffset utc)`.
- **Key** (riga 20) — `public static string Key(DateOnly date)`. Chiave del giorno locale, "yyyy-MM-dd".
- **SlotUtc** (riga 23) — `public DateTimeOffset SlotUtc(DateOnly date, TimeOnly time)`. Istante UTC dell'ora di caricamento in un dato giorno locale.
- **NextMidnightUtc** (riga 30) — `public DateTimeOffset NextMidnightUtc(DateTimeOffset utc)`. Inizio del giorno locale successivo, come istante UTC.
- **Format** (riga 37) — `public string Format(DateTimeOffset utc)`. Data e ora locali per i messaggi all'utente.

### Upload/UploadJournal.cs

- **NightUploadState** (riga 7, tipo) — `public enum NightUploadState`.
- **NightUploadRecord** (riga 25, tipo) — `public sealed record NightUploadRecord(`. Stato di un caricamento notturno.
- **UploadJournalState** (riga 38, tipo) — `public sealed record UploadJournalState(`. Stato complessivo del pianificatore: un record per notte e i contatori del giorno.
- **UploadJournal** (riga 59, tipo) — `public sealed class UploadJournal`. Registro su file (JSON, scrittura atomica) dei caricamenti.
- **UploadJournal** (riga 73) — `public UploadJournal(string filePath, ISecretProtector protector)`.
- **Update** (riga 86) — `public void Update(Func<UploadJournalState, UploadJournalState> change)`. Applica `change` e salva.
- **Read** (riga 94) — `private UploadJournalState Read()`.
- **Write** (riga 153) — `private void Write(UploadJournalState state)`.
- **Protect** (riga 180) — `private string? Protect(OAuthSecret secret)`.
- **Unprotect** (riga 200) — `private OAuthSecret Unprotect(string stored)`.
- **JournalDto** (riga 219, tipo) — `private sealed class JournalDto`.
- **NightDto** (riga 228, tipo) — `private sealed class NightDto`.

### Upload/UploadModels.cs

- **IClock** (riga 7, tipo) — `public interface IClock`. Sorgente dell'ora UTC, iniettabile per i test.
- **YouTubePrivacy** (riga 13, tipo) — `public enum YouTubePrivacy`. Privacy del video caricato.
- **YouTubePrivacyApi** (riga 21, tipo) — `public static class YouTubePrivacyApi`. Valori di `status.privacyStatus` nell'API.
- **ToApiValue** (riga 23) — `public static string ToApiValue(this YouTubePrivacy privacy)`.
- **YouTubeUploadSettings** (riga 40, tipo) — `public sealed record YouTubeUploadSettings(`. Impostazioni del caricamento automatico.
- **LatestTimelapse** (riga 68, tipo) — `public sealed record LatestTimelapse(`. Timelapse dell'ultima notte completata, fornito da ILatestTimelapseProvider.
- **ILatestTimelapseProvider** (riga 78, tipo) — `public interface ILatestTimelapseProvider`. Fonte del timelapse da caricare.
- **VideoMetadata** (riga 85, tipo) — `public sealed record VideoMetadata(`. Metadati inviati nella risorsa `video` all'avvio della sessione.
- **UploadProgress** (riga 97, tipo) — `public sealed record UploadProgress(long NextOffset, string? VideoId, TimeSpan? RetryAfter)`. Avanzamento di una richiesta di upload.
- **RetryPolicy** (riga 103, tipo) — `public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)`. Retry con backoff esponenziale senza jitter: attesa = Base * 2^(tentativo-1), limitata a MaxDelay.
- **DelayAfter** (riga 109) — `public TimeSpan DelayAfter(int attempt)`. Attesa prima del tentativo successivo a quello indicato (1-based).
- **UploadOutcome** (riga 117, tipo) — `public sealed record UploadOutcome(UploadOutcomeKind Kind, string Message, string? VideoId = null);`. Esito di un tentativo di caricamento o di tick del pianificatore.
- **UploadOutcomeKind** (riga 119, tipo) — `public enum UploadOutcomeKind`.

### Upload/UploadSchedule.cs

- **UploadSchedule** (riga 7, tipo) — `public static class UploadSchedule`. Prossimo istante utile del caricamento automatico, per la vista.
- **NextAttemptUtc** (riga 18) — `public static DateTimeOffset NextAttemptUtc(`. Istante UTC del prossimo tentativo del pianificatore.

### Upload/YouTubeApiException.cs

- **YouTubeApiException** (riga 9, tipo) — `public sealed class YouTubeApiException : Exception`. Risposta di errore dell'API di upload.
- **YouTubeApiException** (riga 23) — `public YouTubeApiException(int? httpStatus, string reason, string message)`.
- **ReadReason** (riga 46) — `internal static string ReadReason(string body)`. Estrae il motivo da `error.errors[0].reason`.

### Upload/YouTubeUploadScheduler.cs

- **YouTubeUploadScheduler** (riga 15, tipo) — `public sealed class YouTubeUploadScheduler`. Decide quando caricare il timelapse dell'ultima notte completata.
- **YouTubeUploadScheduler** (riga 25) — `public YouTubeUploadScheduler(`.
- **TickAsync** (riga 42) — `public async Task<UploadOutcome> TickAsync(CancellationToken cancellationToken)`. Un passo del pianificatore.
- **UploadLatestNowAsync** (riga 60) — `public async Task<UploadOutcome> UploadLatestNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken)`. Carica subito la notte piu' recente completata.
- **TickCoreAsync** (riga 96) — `private async Task<UploadOutcome> TickCoreAsync(CancellationToken cancellationToken)`.
- **QuotaBlocked** (riga 142) — `private UploadOutcome QuotaBlocked(DateTimeOffset until)`.
- **MarkSlot** (riga 145) — `private void MarkSlot(string dayKey)`.
- **TryUpdate** (riga 151) — `private void TryUpdate(Func<UploadJournalState, UploadJournalState> change)`.
- **WithoutRecord** (riga 163) — `private static IReadOnlyDictionary<string, NightUploadRecord> WithoutRecord(UploadJournalState state, string nightId)`.

### YouTubeLimits.cs

- **YouTubeLimits** (riga 13, tipo) — `public static class YouTubeLimits`. Limiti dei campi di snippet di una risorsa video di YouTube Data API v3.



## Da verificare

Per le voci web (`web/js`, `web/mock`) il comportamento è ricavato dal codice. Restano da verificare solo le precisioni dei commenti astronomici: `MoonPosition.Altitude` (0.5°–1°), `SolarPosition.Altitude` (0.01°–0.1°) e `sunAltitude` in `time.js` (pochi centesimi di grado). Il commento di `MoonPosition` dichiara che l'intervallo di validità non è stato verificato.

Per i moduli del ramo desktop v1 il comportamento è ricavato dal codice, ma restano non verificati questi punti (fonte: `docs/ralph/NOTES.md`, voci del 22/10/2026):

- **UI WPF**: non è mai stata eseguita né vista su schermo. La CI Windows compila e lancia i test; non esegue l'interfaccia.
- **Rilevamento** (`detection.*`): soglie tarate solo su dati sintetici. Il confermatore dei transiti può dare falsi positivi quando il bordo di una nuvola è allineato alla traccia prevista. Clip provate con libx264 su dati sintetici; non provate con `h264_mf` su Windows né con immagini reali.
- **Calibrazione**: i punti raccolti dal Sole non persistono al riavvio. Il rilevamento del Sole per la calibrazione è stato provato solo su dati sintetici.
- **Satelliti** (`AllSky.Satellites.Sgp4`, SGP.NET 1.6.0): la propagazione della ISS non è stata confrontata con una fonte esterna [NV]. Gli oggetti deep-space (modello SDP4) non vengono rifiutati.
- **Posizione della Luna**: errore atteso di circa 1 grado sull'azimut, dall'angolo orario geocentrico.
- **Modalità transito**: il tempo di lettura della camera (`capture.transitMode.readoutSeconds`, 2 s) è stimato.
- **Maschera**: il budget del renderer (sotto 30 ms a 1920×1080) non è misurato. La maschera sul timelapse reale non è stata verificata: la prova di notte simulata non l'ha applicata.
- **Meteo**: il fornitore è collegato all'Engine e scrive il campo `weather` del sidecar. Le stringhe `current_units` di Open-Meteo non sono confermate da una risposta reale. Il driver ASCOM non è stato provato con una stazione reale. L'attribuzione Open-Meteo non viene ancora scritta nel sidecar.
- **Widget**: `satelliti` riceve i nomi dal campo `satellites` del sidecar, ma il risultato a schermo non è verificato. `meteo` riceve dati solo con una sorgente configurata. `bussola` è omesso dal compositore.
- **YouTube**: ID categoria `28` [NV]; effetto della privacy per i progetti API non verificati: la documentazione dice che i loro video non sono limitati alla visualizzazione privata, l'effetto reale per questo progetto è [NV]; `selfDeclaredMadeForKids` è sempre `false`; motivi esatti dei 403 di quota non verificati; durata della sessione resumable non verificata.
- **Accesso dal browser**: `HttpListener` su `127.0.0.1` senza privilegi amministrativi non provato su Windows. La password della rete locale viaggia in HTTP Basic, non cifrato.
- **Archivio**: il `keogram` dopo un riavvio può coprire meno frame dell'archivio; oltre 4096 frame le colonne sono a blocchi.

---

## Riepilogo dei conteggi

| File | Funzioni documentate |
|---|---|
| AllSky.Core (C#) | 14 |
| AllSky.Astro (C#) | 25 |
| AllSky.Imaging (C#) | 41 |
| web/js/api.js | 19 |
| web/js/app.js | 33 |
| web/js/i18n.js | 18 |
| web/js/time.js | 12 |
| web/js/form-kit.js | 52 |
| web/js/views/camera.js | 7 |
| web/js/views/site.js | 7 |
| web/js/views/wizard.js | 7 |
| web/js/views/color.js | 29 |
| web/js/views/live.js | 43 |
| web/js/views/nights.js | 55 |
| web/mock/server.mjs | 36 |
| **Totale** | **398** |

### Inventario generato (sezione «Inventario generato delle funzioni C#»)

| Progetto | Funzioni, costruttori e tipi documentati |
|---|---|
| AllSky.Archive | 44 |
| AllSky.Astro | 1 |
| AllSky.Cameras.Alpaca | 50 |
| AllSky.Cameras.Ascom | 41 |
| AllSky.Cameras.Sim | 21 |
| AllSky.Capture | 44 |
| AllSky.Core | 373 |
| AllSky.Desktop | 593 |
| AllSky.Detection | 38 |
| AllSky.Engine | 213 |
| AllSky.Events | 89 |
| AllSky.Host | 218 |
| AllSky.Imaging | 59 |
| AllSky.Imaging.Jpeg | 32 |
| AllSky.Imaging.Overlay | 36 |
| AllSky.InfoOverlay | 141 |
| AllSky.Processing | 24 |
| AllSky.Products | 13 |
| AllSky.Publish | 175 |
| AllSky.Satellites | 60 |
| AllSky.Satellites.Sgp4 | 6 |
| AllSky.Sky | 44 |
| AllSky.Timelapse | 52 |
| AllSky.Transits | 30 |
| AllSky.Tray | 27 |
| AllSky.Updates | 28 |
| AllSky.Weather | 51 |
| AllSky.YouTube | 180 |
| **Totale generato** | **2683** |
