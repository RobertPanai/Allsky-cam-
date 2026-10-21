# All Sky Brain — Piano di implementazione "desktop v1" (per ralph loop)

Nome ufficiale del prodotto: **All Sky Brain** (con gli spazi) in tutto ciò che l'utente vede.
Branch di lavoro: `ralph/desktop-v1`, partito da `main` @ 885535f. Si lavora sempre sul branch; su `main` si va **solo** nel task finale P8.4.
Il piano prevede due rilasci automatici, ciascuno solo se il suo cancello di verifica è verde:
- **0.3.0** in P8.4, con le funzioni di base (P0–P7), così il PC che ha la 0.2.x riceve presto la richiesta di aggiornamento;
- **0.4.0** in P10.2, con le funzioni avanzate di P9.

Legenda stato: `[ ]` da fare · `[x]` fatto (test verdi) · `[B]` bloccato (motivo in NOTES.md) · `[~]` parziale.
Legenda modello: **[H]** codice scritto da subagent Haiku 5.5 (`model: "haiku"`) con contratto e criteri dati dal loop ·
**[S]** fatto dal loop stesso con Sonnet 5.5 (parti delicate: architettura, concorrenza, matematica orbitale, OAuth, revisione).
Ogni task [H] è comunque **rivisto** dal loop (diff + test) prima di segnarlo `[x]`.

Legenda fatti: **[V]** verificato con fonte (21/10/2026) · **[NV]** da verificare nel task prima di usarlo · **[S-scelta]** scelta progettuale.

---

## 0. Obiettivi (richiesta di Robert, 21/10/2026)

1. **Applicazione desktop nativa**, non più web app nel browser.
2. **Caricamento automatico del timelapse su YouTube**, a un orario configurabile, periodico, con titolo da modello con data variabile.
3. **Riconoscimento dei transiti della ISS e di altri satelliti importanti**: se il transito viene identificato, si salva il video del transito.
4. **Riconoscimento di bolidi e meteore.**
5. **Grafica a tema nero, molto curata e futuristica.**
6. **Maschera informativa ai bordi del timelapse**, con questi elementi, ognuno attivabile e disattivabile dall'utente: data e ora, satelliti in transito in quel momento, altezza del Sole, altezza della Luna, loghi, meteo.
7. **Nome ufficiale "All Sky Brain"** ovunque sia visibile.
8. **Immagini e stile futuristico** in tutta l'app, curati come farebbe un esperto di UI/UX con competenze di astronomia (vedi §5).
9. **Rilascio automatico** a fine implementazione (P8.4 e P10.2), così l'altro PC riceve l'aggiornamento.
10. **Funzioni avanzate** (richiesta del 21/10 alle 23:17), in P9: SafetyMonitor Alpaca, SQM stimato, allerte aurora, calibrazione fotometrica sulle stelle note, mappa di costellazioni e pianeti, report via Telegram o email, rete di camere, archivio ricercabile.

Vincoli che restano in vigore:
- solo richieste HTTPS in uscita e nessun server esposto;
- segreti cifrati con DPAPI e mai nei log;
- interfaccia in italiano e inglese;
- nessuna dipendenza GPL;
- la pubblicazione Uplink esistente non va rotta.

## 1. Fatti verificati che guidano il piano

| Tema | Fatto | Stato |
|---|---|---|
| YouTube upload | `videos.insert`: scope `youtube.upload` (o `youtube`/`force-ssl`); costa **1 unità nel bucket "Video Uploads", 100 chiamate/giorno**; max 256 GB; MIME `video/*` | [V] [videos.insert](https://developers.google.com/youtube/v3/docs/videos/insert) |
| YouTube privacy | La pagina oggi dice: "Videos uploaded from unverified API projects are not restricted to private viewing mode" e che alcuni upload possono essere privati per impostazioni dell'account | [V] testo letto; **effetto reale [NV]**, si conferma con un upload di prova |
| OAuth Google | Con consent screen "External" in stato **"Testing"**, il refresh token **scade dopo 7 giorni** → l'upload notturno si fermerebbe. Serve passare l'app OAuth a "In production" (l'utente lo fa nella sua Google Cloud Console) | [V] [OAuth2 Google](https://developers.google.com/identity/protocols/oauth2) |
| OAuth desktop | Tipo credenziale "Desktop app" | [V]; il flusso loopback `127.0.0.1` è **[NV]** → leggere "OAuth 2.0 for Mobile & Desktop Apps" prima di Y3 |
| Dati orbitali | CelesTrak: GP aggiornati ogni 2 h; scaricare solo ciò che serve e **una volta per aggiornamento**; su qualunque risposta non-200 **fermarsi** e segnalarlo (rischio blocco IP); query `gp.php?GROUP=…&FORMAT=…` | [V] [usage policy](https://celestrak.org/usage-policy.php), [formati](https://celestrak.org/NORAD/documentation/gp-data-formats.php) |
| TLE vs OMM | I TLE non rappresentano numeri di catalogo > 99999; i nuovi oggetti a 6 cifre non hanno TLE. ISS e i satelliti "storici" hanno numeri a 5 cifre | [V] stessa fonte; numeri NORAD dei bersagli **[NV]** (verificarli su CelesTrak) |
| SGP4 | **SGP.NET**: licenza MIT (© 2019 Colby Newman), carica TLE, calcola az/el da stazione a terra e i passaggi | [V] [LICENSE](https://raw.githubusercontent.com/parzivail/SGP.NET/master/LICENSE); versione NuGet e target **[NV]** |
| SGP4 da evitare | Zeptomoby OrbitTools: "non-commercial purposes only" | [V] → **vietato** |
| Meteo online | Open-Meteo: niente API key; **solo uso non commerciale** nella versione gratuita; < 10.000 chiamate/giorno; dati CC-BY 4.0 (serve l'attribuzione) | [V] [termini](https://open-meteo.com/en/terms) |
| Meteo locale | ASCOM ObservingConditions: CloudCover, DewPoint, Humidity, Pressure, RainRate, SkyBrightness, SkyQuality, SkyTemperature, StarFWHM, Temperature, WindDirection, WindGust, WindSpeed | [V] [IObservingConditions](https://ascom-standards.org/library/html/T_ASCOM_Common_DeviceInterfaces_IObservingConditions.htm); **unità [NV]** (vanno lette sulle pagine delle singole proprietà) |
| Ralph loop | `/ralph-loop "<prompt>" --max-iterations N --completion-promise "<testo>"`; per chiudere il loop serve stampare `<promise>testo</promise>`; `/cancel-ralph` lo annulla | [V] [help del plugin](https://gitcode.com/2301_77165880/claude-code/blob/main/plugins/ralph-wiggum/commands/help.md) |

## 2. Architettura target [S-scelta]

```
AllSky.Desktop (NUOVO, WPF net10.0-windows, AssemblyName = AllSkyBrain)  ← sostituisce AllSky.Tray come exe
  ├─ ospita in-process Engine (IAllSkyService) — niente HTTP per la UI
  ├─ icona tray (NotifyIcon) + finestra principale; VelopackApp.Build().Run() prima riga di Main
  ├─ Theme/ (ResourceDictionary con i token di design), Views/, ViewModels/ (MVVM senza librerie)
  └─ Kestrel/web UI: OPZIONALE, spento per default ("Accesso dal browser", solo loopback; LAN opt-in)
AllSky.Sky (NUOVO, net10.0, puro)        geometria fisheye alt/az ↔ pixel, calibrazione
AllSky.Satellites (NUOVO, net10.0)       fonte TLE con cache e policy, propagazione (SGP.NET), passaggi, illuminazione
AllSky.Detection (NUOVO, net10.0, puro)  rilevamento scie: meteore/bolidi, conferma transiti satellite
AllSky.Events (NUOVO, net10.0)           archivio eventi (meteora, bolide, transito): json + immagini + clip
AllSky.Weather (NUOVO, net10.0)          IWeatherSource: OpenMeteo (opt-in) e AscomObservingConditions
AllSky.InfoOverlay (NUOVO)               layout puro (net10.0) + renderer WPF (in Desktop) della maschera informativa
AllSky.YouTube (NUOVO, net10.0)          OAuth desktop, upload resumable, scheduler, modello del titolo
```

**Nome e identificatori**
- Si vede **"All Sky Brain"**: titoli di finestra, tray, installer (`--packTitle "All Sky Brain"`), voce in "App installate", collegamenti, `Product`/`AssemblyTitle`, README e documenti, testi IT/EN, titoli delle release.
- Restano **invariati**, perché la catena di aggiornamento dipende da questi valori:
  - `packId` Velopack `AllSkyBrain`;
  - exe `AllSkyBrain.exe`;
  - cartella dati `%LOCALAPPDATA%\AllSky`;
  - namespace e progetti `AllSky.*`;
  - valore Run `AllSkyBrain`;
  - URL del repository.

**Regole di architettura**
- **Separazione:** la logica pura sta in librerie `net10.0` testabili senza Windows. Il codice Windows (WPF, WIC, DPAPI, COM) resta in `AllSky.Desktop` e negli adapter.
- **Velopack:** `packId` resta `AllSkyBrain` e `mainExe` resta `AllSkyBrain.exe`. Se cambiassero, gli utenti della 0.2.x non riceverebbero l'aggiornamento. Il workflow `release-stable.yml` va cambiato solo nel percorso del progetto pubblicato.
- **Cartella dati:** `%LOCALAPPDATA%\AllSky` resta invariata. Nei test e nello sviluppo si usa un argomento `--data-folder <dir>` (task P0.2), così non si tocca l'istanza installata.
- **Archivio pulito:** i JPEG archiviati non contengono la maschera. Ogni frame salva un **sidecar di metadati**, e la maschera si disegna **al momento della composizione** del timelapse. Così l'utente può cambiare il layout e rigenerare il video.
- **Cattura prima di tutto:** rilevamento, overlay e upload girano fuori dal thread di cattura e non la bloccano mai. Quando superano il budget di tempo, saltano il frame e lo segnalano.
- **Dipendenze NuGet ammesse:** quelle già presenti, più quelle elencate sotto. Ciascuna va verificata (licenza, versione, target) nel task che la introduce, e l'esito si scrive in NOTES.md.
  - `SGP.NET`: MIT [V].
  - `Google.Apis.YouTube.v3` e `Google.Apis.Auth`: licenza Apache-2.0 [NV]. Se la licenza non è confermata, si usa REST con `HttpClient`.
  - Nient'altro senza una nota in NOTES.md che ne motivi l'aggiunta. Nessuna dipendenza GPL o non commerciale.
- **Font dell'interfaccia:** inclusi nell'app con licenza OFL (es. *Inter* e *JetBrains Mono*). Il file di licenza va nel repository e nell'output, licenza [NV] da verificare in D1. Niente font di sistema non ridistribuibili.

## 3. Comandi (sul PC Windows dove gira il loop)

```
dotnet build AllSky.sln -c Release
dotnet test  AllSky.sln -c Release --nologo
dotnet run --project src/AllSky.Desktop -- --data-folder %TEMP%\allsky-dev   (dopo P1.1)
```
Se un comando fallisce per motivi d'ambiente (SDK, rete, NuGet), scrivi il motivo in NOTES.md e marca `[B]`. Non aggirare.

---

## 4. Task (in ordine; prendi il primo `[ ]` con dipendenze soddisfatte)

### P0 — Preparazione
- [ ] **P0.1 [S] Baseline.** Build e test su `main`/branch; annota in NOTES.md numero test, falliti preesistenti (noto: `CaptureLoopTests.FrameMedian_UpdatesExposureForNextCapture` intermittente). *Accettazione:* NOTES.md con esito.
- [ ] **P0.2 [H] `--data-folder`.** Argomento da riga di comando (e variabile `ALLSKY_DATA_FOLDER`) per scegliere la cartella dati; default invariato. Mutex di istanza singola derivato dalla cartella dati (due cartelle = due istanze ammesse). *Test:* parsing argomenti. *Dip.:* P0.1.

- [ ] **P0.3 [H] Nome "All Sky Brain".** Sostituisci "AllSky Brain" e il vecchio nome del prodotto nei testi visibili, rispettando la lista di invarianti in §2. *Accettazione:* `grep -rn "AllSky Brain"`, escludendo `.git`, `bin`/`obj` e `docs/ralph`, non trova più nulla; test verdi; YAML validi. *Dip.:* P0.1.

### P1 — Applicazione desktop nativa (WPF) e tema
- [ ] **P1.1 [S] Progetto `AllSky.Desktop`.** WPF net10.0-windows, AssemblyName `AllSkyBrain`, `VelopackApp.Build().Run()` prima riga, avvio Engine in-process (riusa la costruzione oggi in `AllSkyHost`/Tray estraendo una factory condivisa, senza duplicare), tray icon con menu (Apri, Controlla aggiornamenti, Esci), chiusura ordinata. `AllSky.Tray` resta nel repo ma non è più l'exe pubblicato: cambia il suo `AssemblyName` in `AllSkyBrain.TrayLegacy` (due exe con lo stesso nome nella solution creano confusione) e aggiorna `build.yml` e `release-stable.yml` (solo il percorso del progetto pubblicato; `packId`/`mainExe` invariati). *Accettazione:* build verde; avvio con `--data-folder` mostra finestra vuota con tema base; uscita pulita. *Dip.:* P0.2.
- [ ] **P1.2 [S] Design system** in `Theme/Tokens.xaml` + `docs/DESIGN-DESKTOP.md`. Vedi §5. Stili per Button (primario/secondario/pericolo), TextBox, ComboBox, CheckBox/Toggle, Slider, ScrollBar, ToolTip, DataGrid/ListView, schede "pannello" con bordo luminoso sottile. *Accettazione:* pagina "Galleria componenti" (solo in Debug) che mostra tutti gli stili; contrasto testo ≥ 4.5:1 verificato con calcolo (test unitario sui colori). *Dip.:* P1.1.
- [ ] **P1.3 [H] Shell di navigazione.** Barra laterale con icone (vettoriali, Path XAML, niente immagini raster) + barra di stato in basso (stato camera, fase giorno/crepuscolo/notte, altezza Sole/Luna, ultimo frame, spazio disco, stato pubblicazioni). Navigazione da tastiera, focus visibile. *Dip.:* P1.2.
- [ ] **P1.4 [H] Vista "Live".** Immagine corrente grande (aggiornata su `FrameProduced`, decodifica fuori dal thread UI, nessun leak di bitmap), zoom/pan, toggle "grezzo/elaborato", pannello dati del frame (esposizione, gain, mediana ADU, temperatura). Overlay commutabili: griglia alt/az e punti cardinali (dopo P3), tracce satelliti previste (dopo P4). *Dip.:* P1.3.
- [ ] **P1.5 [H] Vista "Camera".** Ricerca/connessione camere (ASCOM/Alpaca/Sim) con l'API dell'Engine esistente, messaggi d'errore leggibili (anche quelli con i valori dei limiti di esposizione introdotti in 0.2.1). *Dip.:* P1.3.
- [ ] **P1.6 [H] Vista "Notti".** Elenco notti, keogram come barra di scorrimento temporale, startrails, riproduzione timelapse (MediaElement) e pulsante "Rigenera timelapse". *Dip.:* P1.3.
- [ ] **P1.7 [H] Vista "Pubblicazione".** Porta in WPF le impostazioni Uplink esistenti (stessi contratti `/api/publish/*` ma chiamando direttamente i servizi; token mai mostrato, solo "impostato/non impostato"). *Dip.:* P1.3.
- [ ] **P1.8 [H] Vista "Impostazioni" + "Log".** Posizione osservatorio, lingua IT/EN, cartella dati (sola lettura), avvio con Windows, aggiornamenti (AutoCheck), "Accesso dal browser" (Kestrel opzionale, spento). Log: ultime righe con filtro, senza segreti. *Dip.:* P1.3.
- [ ] **P1.10 [S] Identità visiva e immagini** (vedi §5.3). Comprende:
  - **logo e icona originali:** vettoriali in XAML/SVG, esportati in `.ico` multi-risoluzione (16–256 px) con uno strumento di build nel repository, e usati da exe, tray e installer (`--icon`);
  - **splash d'avvio;**
  - **illustrazioni vettoriali originali** per gli stati vuoti: nessuna notte, nessun evento, camera non collegata, YouTube non collegato;
  - **sfondo stellare procedurale e statico** per le schermate senza immagine del cielo, con densità e magnitudini realistiche e una sottile banda della Via Lattea. Viene generato con un seed fisso e non è animato;
  - **immagini promozionali per README e release:** screenshot reali dell'app (P1.9).
  - **Vincolo:** niente immagini di terzi. Foto NASA/ESA solo se la licenza è verificata e annotata in NOTES.md, altrimenti nessuna.
  - **Accettazione:**
    - gli asset sono in `assets/brand/` con un README che descrive origine e licenza (originale, del progetto);
    - l'icona compare in exe, tray e installer;
    - gli stati vuoti sono illustrati in IT ed EN.
  - *Dip.:* P1.2.
- [ ] **P1.11 [S] Vista "Stanotte"**, schermata iniziale (vedi §5.2). Contiene:
  - l'immagine del cielo dal vivo come protagonista;
  - **una linea temporale da tramonto ad alba** con le fasce dei crepuscoli civile, nautico e astronomico e la notte astronomica, le curve di altezza di Sole e Luna, e i passaggi visibili dei satelliti segnati (quando P4 è pronto);
  - **una carta "Condizioni"** con fase e illuminazione della Luna (glifo vettoriale corretto per la fase), meteo (dopo P6) e un indice di cielo sereno (dopo P5: stelle rilevate rispetto all'atteso, oppure non disponibile);
  - **una carta "Prossimi eventi"** con il prossimo transito e il conto alla rovescia, e l'ultima meteora;
  - **uno stato sintetico della cattura.**
  - Le altezze e gli orari dei crepuscoli si calcolano con `AllSky.Astro`. *Test:* gli orari dei crepuscoli vanno confrontati con una fonte indipendente per una data e un luogo noti, e la fonte va annotata. Se non è raggiungibile, segnare [NV].
  - *Dip.:* P1.4.
- [ ] **P1.9 [S] Revisione UI** con la checklist della skill `web-ui-review` adattata a WPF (gerarchia, spaziatura su scala 4/8, contrasto, focus, stati vuoto/caricamento/errore, testi IT più lunghi). Screenshot a 1280×800 e 1920×1080 salvati in `docs/ralph/shots/` (RenderTargetBitmap della finestra), in modalità normale e modalità notte rossa. Correggi ciò che non rispetta §5 prima di segnare `[x]`. *Dip.:* P1.4–P1.8, P1.10, P1.11.

### P2 — Maschera informativa sul timelapse (richiesta 21/10 23:09)
- [ ] **P2.1 [H] Sidecar metadati per frame.** Per ogni frame archiviato `<frame>.json` con: UTC e ora locale, esposizione, gain, temperatura sensore, altezza/azimut Sole e Luna, fase Luna (illuminazione %), fase giorno, snapshot meteo (se disponibile, P6), satelliti visibili in quel momento con alt/az (se disponibile, P4). Scrittura atomica, retrocompatibile (frame vecchi senza sidecar → metadati ricostruiti da timestamp e posizione dove possibile). *Test:* serializzazione, ricostruzione. *Dip.:* P0.1.
- [ ] **P2.2 [S] Modello di layout (puro, `AllSky.InfoOverlay`).** `OverlayLayout` versionato in config: lista di *widget* `{ tipo, abilitato, posizione (TL, TR, BL, BR, BandaAlta, BandaBassa), ordine, formato }`; *maschera*: cerchio cielo (centro/raggio da P3 o manuali) con esterno nero opaco o sfumato, oppure bande nere sopra/sotto; scala testo relativa all'altezza del frame. Tipi di widget: `DataOra` (formato data/ora configurabile, fuso locale/UTC), `Sole` (altezza °), `Luna` (altezza °, fase %), `Satelliti` (nomi in transito in quel momento, max N), `Meteo` (scelta dei campi), `Logo` (file PNG, posizione, altezza max %, opacità), `TestoLibero` (es. nome osservatorio), `Esposizione` (tempo/gain), `Bussola` (N/E/S/W sul bordo del cerchio, dopo P3). Calcolo puro dei rettangoli (nessuna sovrapposizione: se lo spazio manca, i widget in coda vengono omessi e lo si segnala). *Test:* layout su 4:3, 16:9, 1:1; nessuna sovrapposizione; ordine rispettato. *Dip.:* P2.1.
- [ ] **P2.3 [S] Renderer WPF** (in Desktop): disegna maschera + widget con `DrawingVisual` e font OFL, compone sul frame (WIC per PNG dei loghi), restituisce pixel per l'encoder. Budget: < 30 ms/frame a 1920×1080 sul PC (misura e annota). *Test:* golden test su un frame sintetico (confronto con tolleranza) — salvare i golden in `tests/.../Golden/`. *Dip.:* P2.2, P1.2.
- [ ] **P2.4 [H] Integrazione nel timelapse.** Il generatore del timelapse applica la maschera se `overlay.applyToTimelapse = true`; opzioni separate `applyToLive` e `applyToPublish` (Uplink: default **false**, per non cambiare il contratto delle immagini già inviate). Rigenerazione possibile con il layout corrente. *Test:* il timelapse con maschera usa i metadati del frame giusto (frame N → orario N). *Dip.:* P2.3.
- [ ] **P2.5 [H] Editor visivo della maschera** (vista "Maschera" o sezione delle Impostazioni): anteprima live sull'ultimo frame, lista widget con interruttore, posizione (tendina), ordine (su/giù), formati; caricamento loghi (copia nella cartella dati, mai riferimento a percorsi esterni); pulsante "Ripristina predefiniti". Predefinito: DataOra (BL), Sole+Luna (BR), maschera cerchio attiva, altri spenti. *Dip.:* P2.4, P1.3.

### P3 — Geometria del cielo (prerequisito di satelliti, meteore, bussola)
- [ ] **P3.1 [S] Modello fisheye** (`AllSky.Sky`): proiezione equidistante r = k·z (z = distanza zenitale), parametri `cx, cy, raggioOrizzonte, rotazioneNord, specchio` (E-O invertito guardando verso l'alto), con eventuale termine radiale di correzione k2. Conversioni alt/az ↔ pixel con test di andata/ritorno (errore < 0.01 px). *Dip.:* P0.1.
- [ ] **P3.2 [S] Calibrazione dal Sole.** Di giorno, centroide del Sole (pixel saturi, area compatta) a orari diversi + alt/az calcolati (già in `AllSky.Astro`) → fit ai minimi quadrati di cx, cy, raggio, rotazione, specchio. Serve una giornata con Sole visibile: il codice raccoglie i punti in background e propone la calibrazione quando l'errore RMS è sotto soglia (es. < 1% del raggio). Fallback manuale: l'utente clicca centro, bordo orizzonte e il Nord sull'immagine. *Test:* punti sintetici generati con parametri noti → fit li ritrova; rumore e outlier. *Dip.:* P3.1.
- [ ] **P3.3 [H] UI calibrazione** (in vista Camera): stato della raccolta punti, anteprima con griglia alt/az sovrapposta, accetta/rifiuta, modalità manuale. *Dip.:* P3.2, P1.5.

### P4 — Satelliti e transiti (ISS e altri)
- [ ] **P4.1 [S] Fonte elementi orbitali** (`AllSky.Satellites`): download da CelesTrak per numero di catalogo (`CATNR=`) dei bersagli configurati; formato TLE per oggetti a 5 cifre; **una richiesta per bersaglio ogni ≥ 2 h**, cache su disco con data; **su qualunque risposta non-200: stop dei download per 24 h e messaggio all'utente**; nessuna richiesta del gruppo "active"/Starlink. Lista predefinita: ISS, stazione cinese, Hubble — **verifica i numeri NORAD su CelesTrak prima di scriverli** e cita la fonte in NOTES.md. L'utente può aggiungere/togliere numeri NORAD. *Test:* con HttpMessageHandler finto (200, 404, 503, rete giù), rispetto dell'intervallo con orologio finto. *Dip.:* P0.1.
- [ ] **P4.2 [S] Propagazione e passaggi** con SGP.NET (verifica licenza/versione/target e annota): posizione topocentrica alt/az per l'osservatorio; passaggi nelle prossime 24 h con alt > soglia (default 10°); **visibilità ottica**: satellite illuminato dal Sole (fuori dall'ombra terrestre, modello cilindrico) **e** cielo scuro (Sole < −6°, configurabile). Interfaccia `IOrbitPropagator` per poter cambiare libreria. *Test:* coerenza (az/el continui, culminazione unica per passaggio), cambio di giorno, oggetto mai visibile. Validazione esterna: confronta 1 passaggio ISS previsto con una fonte indipendente (es. Heavens-Above) **solo se raggiungibile**; altrimenti annota [NV]. *Dip.:* P4.1.
- [ ] **P4.3 [H] Previsioni in UI:** prossimi passaggi visibili (ora inizio/culmine/fine, alt max, direzione), tracce previste sull'immagine Live (P3.1) e nella barra di stato "prossimo transito tra…". *Dip.:* P4.2, P1.4, P3.1.
- [ ] **P4.4 [S] Conferma del transito nelle immagini** (`AllSky.Detection`): nei frame dentro la finestra del passaggio, cerca una scia lungo la traccia prevista (proiettata in pixel): profilo di luminosità lungo un corridoio di ± N px rispetto al fondo locale; punteggio = frazione del tratto previsto sopra soglia. "Identificato" se punteggio ≥ soglia su almeno un frame. Funziona solo con calibrazione valida (P3.2), altrimenti l'evento è "previsto, non verificato". *Test:* frame sintetici con scia iniettata lungo la traccia (identificato), scia altrove o assente (non identificato), nuvole (rumore diffuso). *Dip.:* P4.2, P3.2.
- [ ] **P4.5 [H] Salvataggio del transito:** se identificato (o se l'utente sceglie "salva tutti i passaggi visibili"), crea evento in `AllSky.Events` con: clip MP4 dei frame [inizio−1, fine+1] (riusa `AllSky.Timelapse`, fps configurabile, con maschera P2 se attiva), immagine composita (massimo dei frame), metadati (satellite, orari, alt max, punteggio). Non blocca la cattura. *Test:* evento completo su disco con frame sintetici. *Dip.:* P4.4, P2.4.
- [ ] **P4.6 [H] Modalità "transito" opzionale** (default spenta): durante un passaggio previsto riduce l'intervallo tra le pose (senza scendere sotto il minimo della camera) per avere più frame nel video; ripristina dopo. *Test:* sequenza di intervalli con orologio finto. *Dip.:* P4.2.

### P5 — Meteore e bolidi
- [ ] **P5.1 [S] Rilevatore di scie** (`AllSky.Detection`, puro): lavora su luminanza ridotta (es. lato lungo 960 px) e solo di notte; differenza con la mediana degli ultimi N frame (fondo), soglia adattiva (k·σ robusta), maschera del cielo (P3) e della Luna (cerchio attorno alla posizione calcolata), componenti connesse, trasformata di Hough sui pixel candidati, segmenti con lunghezza ≥ L e spessore ≤ W. Budget: < 300 ms/frame sul PC (misura e annota), in coda separata con scarto se in ritardo. *Test:* frame sintetici con stelle, rumore, scia iniettata (lunghezze/luminosità varie); obiettivo su set sintetico: richiamo ≥ 90% per scie ≥ 40 px con SNR ≥ 5, falsi positivi ≤ 1 per 100 frame senza scie. *Dip.:* P3.1.
- [ ] **P5.2 [S] Classificazione e filtri**: scarta (a) scie che coincidono con tracce di satelliti previste (P4.2) → etichetta "satellite"; (b) oggetti che si ripetono allineati su ≥ 2 frame consecutivi → "aereo/satellite"; (c) frame con variazione globale alta (nuvole, fulmini, fari) → frame scartato. Meteora = scia singola in un frame; **bolide** = meteora con picco saturo o luminosità integrata sopra soglia alta e/o lunghezza molto grande (soglie configurabili, valori iniziali documentati come da tarare). *Test:* casi sintetici per ciascun ramo. *Dip.:* P5.1, P4.2.
- [ ] **P5.3 [H] Salvataggio evento meteora/bolide**: ritaglio ingrandito, frame intero con scia evidenziata, mini-clip dei frame [N−2, N+2], metadati (ora, posizione alt/az inizio-fine se calibrato, lunghezza, luminosità, classe, punteggio). *Dip.:* P5.2.
- [ ] **P5.4 [H] Vista "Eventi"**: galleria (meteore, bolidi, transiti) con filtri per notte/tipo, dettaglio con clip e dati, pulsanti "Conferma" / "Falso positivo" (etichette salvate per tarare le soglie), esporta evento (cartella zip). *Dip.:* P5.3, P4.5, P1.3.

### P6 — Meteo
- [ ] **P6.1 [H] `IWeatherSource` + adapter ASCOM ObservingConditions** (COM late-bound come la camera, thread dedicato, ogni proprietà letta con try/catch: se non implementata → assente). **Verifica le unità** sulle pagine delle singole proprietà ASCOM e scrivile in NOTES.md. *Test:* fake. *Dip.:* P0.1.
- [ ] **P6.2 [H] Adapter Open-Meteo opt-in** (default spento, testo in UI: "solo uso non commerciale, dati CC-BY 4.0 Open-Meteo" + attribuzione mostrata dove compaiono i dati): una richiesta ogni ≥ 15 min, cache, stop su errori ripetuti. Campi: temperatura, umidità, copertura nuvolosa, vento. **Verifica i nomi dei parametri sulla documentazione** prima di scrivere il codice. *Test:* handler finto. *Dip.:* P6.1.
- [ ] **P6.3 [H] Meteo nel sidecar (P2.1) e nel widget `Meteo`** (P2.2): l'utente sceglie quali campi mostrare. *Dip.:* P6.1, P2.2.

### P7 — Caricamento automatico su YouTube
- [ ] **P7.1 [H] Modello del titolo/descrizione** (puro): segnaposto `{data:yyyy-MM-dd}`, `{notte}` (data di inizio notte), `{ora:HH:mm}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`; formati .NET validati; anteprima; caratteri vietati e lunghezze massime controllate (**limiti di lunghezza YouTube [NV]**: verificali sulla doc della risorsa `video` prima di codificarli). *Test:* esempi con date a cavallo della mezzanotte e del cambio ora legale. *Dip.:* P0.1.
- [ ] **P7.2 [S] Autenticazione OAuth "Desktop app"**: l'utente inserisce *il proprio* client ID/secret (l'app è generica, nessuna credenziale incorporata). Leggi prima la guida Google "OAuth 2.0 for Mobile & Desktop Apps" e annota il flusso consentito; listener solo su `127.0.0.1` e solo durante il login, poi chiuso. Refresh token cifrato con DPAPI (riusa il protettore della pubblicazione), mai nei log. La UI spiega che con l'app OAuth in stato "Testing" il token scade dopo 7 giorni [V] e come passarla "In production". *Test:* store cifrato, parsing risposte token con fake. *Dip.:* P1.8.
- [ ] **P7.3 [S] Upload resumable + pianificazione**: ogni giorno all'ora scelta carica il timelapse dell'ultima notte completata (con maschera se attiva); privacy predefinita **private** (scelta utente: private/unlisted/public), tag, categoria (ID **[NV]**), descrizione dal modello; idempotenza per notte (stato salvato: mai due upload della stessa notte, anche dopo un crash); retry con backoff su 5xx/rete, nessun retry su 4xx (eccetto 401 → refresh una volta); quota: ≤ 1 upload/giorno di default. Se il PC era spento all'ora prevista, carica all'avvio successivo (finestra configurabile). *Test:* orologio finto e handler finto (sessione resumable, interruzione a metà, 401, 403 quota, 500). *Dip.:* P7.1, P7.2.
- [ ] **P7.4 [H] Vista YouTube** (in "Pubblicazione"): login/logout, stato (ultimo upload con link al video, prossimo upload, ultimo errore), impostazioni, "Carica ora la notte scorsa". *Dip.:* P7.3, P1.7.

### P8 — Verifica e rilascio 0.3.0
- [ ] **P8.1 [H] Documentazione**: README (desktop, maschera, satelliti, meteore, YouTube, meteo), `docs/FUNZIONI.md` aggiornato, guida "Configurare YouTube" passo-passo (Google Cloud Console, credenziali Desktop, stato In production), note sui limiti noti.
- [ ] **P8.2 [S] Verifica finale**: build + test completi, smoke test con camera Sim e `--data-folder` temporanea (1 notte simulata accelerata se l'Engine lo consente: timelapse con maschera, almeno un evento sintetico); controllo segreti (`grep` del token di prova su cartella dati e log); revisione del diff complessivo. Scrivi in NOTES.md cosa è verificato e cosa no.
- [ ] **P8.3 [S] Riepilogo per Robert** in NOTES.md: cosa è fatto, cosa è bloccato e perché, decisioni prese da confermare, prossimi passi.
- [ ] **P8.4 [S] Rilascio automatico 0.3.0** (richiesto da Robert). Parte solo se tutte le condizioni del **cancello** sono vere:
  - **Condizioni del cancello:**
    - P8.2 è `[x]`, con build e test verdi a parte il test intermittente noto;
    - lo smoke test con la camera Sim è riuscito;
    - nessun segreto compare in log o file;
    - `packId` è ancora `AllSkyBrain` e `mainExe` è ancora `AllSkyBrain.exe`, verificati nel workflow;
    - P1.1 è `[x]`, cioè l'exe desktop esiste;
    - i task bloccati `[B]` non rompono funzioni già presenti nella 0.2.1: cattura, timelapse, Uplink, aggiornamenti.
  - **Passi:**
    1. Imposta la versione 0.3.0 dove vive: `Directory.Build.props` o le costanti `Version`, coerenti tra loro.
    2. `git checkout main && git pull && git merge --ff-only ralph/desktop-v1`. Se il merge non è fast-forward, fai merge di `main` nel branch, rilancia build e test, e riprova.
    3. `git push origin main`.
    4. `gh workflow run release-stable --ref main -f version=0.3.0`.
    5. Attendi la fine del workflow.
    6. Verifica con `gh api repos/RobertPanai/Allsky-cam-/releases` che la release `v0.3.0` non sia pre-release e contenga `AllSkyBrain-win-Setup.exe`, `releases.win.json` e i `.nupkg` full e delta.
  - **Se il cancello non è soddisfatto o il workflow fallisce:** non rilasciare né riprovare in modo diverso. Scrivi tutto in NOTES.md e marca `[B]`. Un rilascio sbagliato arriverebbe all'altro PC, e Velopack di default non torna indietro: la correzione sarebbe solo una 0.3.1.
  - Dopo P8.4, che sia `[x]` o `[B]` con il motivo, prosegui con P9. Il loop **non** si chiude qui.

### P9 — Funzioni avanzate (dopo P8.4; ogni task include la sua parte di UI secondo §5)
Fatti da verificare prima di ciascun task: tutti [NV] finché non hai letto la fonte ufficiale e non l'hai annotata in NOTES.md.

- [ ] **P9.1 [S] Catalogo stellare e raffinamento astrometrico.**
  - **Catalogo:** stelle luminose fino a circa mag 6, per esempio il Yale Bright Star Catalogue. **Prima verifica e annota la licenza.** Se non è di pubblico dominio o permissiva (CC0, CC-BY, BSD, MIT), cerca un'alternativa permissiva. Niente dati GPL o share-alike senza una nota per Robert.
  - **Dati:** copia ridotta nel repository (RA, Dec, mag V, B−V), con un file che ne documenta fonte e licenza.
  - **Precessione a J2000→data:** usa un algoritmo standard, per esempio di Meeus. Annota la fonte della formula.
  - **Raffinamento:** usa le stelle per rifinire la calibrazione fisheye di P3: identificazione delle stelle vicino alla posizione prevista e fit ai minimi quadrati con un termine radiale.
  - *Test:* stelle sintetiche proiettate con parametri noti, che il fit deve ritrovare.
  - *Dip.:* P3.2.
- [ ] **P9.2 [S] Calibrazione fotometrica sulle stelle note.**
  - Su frame notturni senza nuvole: fotometria d'apertura delle stelle identificate in P9.1, con il fondo misurato in un anello.
  - Fit del **punto zero** in funzione della massa d'aria (estinzione) e dei **termini di colore** per canale da B−V.
  - Ne derivano una **matrice di correzione colore** e i guadagni del bilanciamento del bianco, per colori coerenti tra notti diverse. È opzionale e sostituisce il bilanciamento automatico solo se l'utente lo attiva.
  - Fornisce anche **magnitudine limite** e **indice di cielo sereno**: stelle rilevate diviso stelle attese per settore. Questo indice alimenta la carta "Condizioni" di P1.11.
  - *Test:* campo stellare sintetico con punto zero ed estinzione noti, che il fit deve ritrovare entro la tolleranza.
  - *Dip.:* P9.1.
- [ ] **P9.3 [S] SQM stimato.**
  - Stima la brillanza del fondo allo zenit in mag/arcsec² dalla luminanza del fondo, una volta tolte le stelle.
  - Servono l'area del pixel in arcsec² (dal modello fisheye allo zenit), l'esposizione e il punto zero di P9.2.
  - **Offset di calibrazione** facoltativo, inseribile dall'utente confrontando una sera con un SQM reale.
  - In UI va etichettato "stimato". Mostra un grafico della notte e un valore nel sidecar (P2.1), così la maschera lo può mostrare.
  - *Test:* fondo sintetico con brillanza nota.
  - *Dip.:* P9.2.
- [ ] **P9.4 [H] Mappa di costellazioni e pianeti.**
  - **Pianeti** (Mercurio–Nettuno) con algoritmi a bassa precisione da fonte citata, per esempio Meeus o gli elementi orbitali approssimati JPL. Annota la fonte e la precisione attesa.
  - **Linee delle costellazioni** solo da un dataset con licenza permissiva verificata. Se non lo trovi, fai solo i pianeti e le stelle principali con nome e marca le linee `[B]`.
  - **Overlay sul Live** attivabile con il tasto `C`: nomi, linee, pianeti, eclittica ed equatore opzionali.
  - *Test:* posizioni dei pianeti confrontate con valori di riferimento per una data, da fonte citata. Se la fonte non è raggiungibile, [NV].
  - *Dip.:* P9.1, P1.4.
- [ ] **P9.5 [S] SafetyMonitor Alpaca, opt-in.**
  - **Server Alpaca minimo** come dispositivo SafetyMonitor: endpoint di management e `issafe`, con discovery facoltativa.
  - **Leggi la specifica Alpaca ufficiale** e implementa esattamente: endpoint, parametri `ClientID` e `ClientTransactionID`, formato delle risposte, codici d'errore. Annota la fonte.
  - **Spento per default.** Se attivo, ascolta **solo su 127.0.0.1**. La rete locale si apre solo su scelta esplicita, con un avviso in UI, perché in quel caso l'app espone un servizio.
  - **Regola "sicuro":** configurabile.
    - Default: indice di cielo sereno di P9.2 sopra la soglia per N minuti consecutivi, nessuna pioggia (dal meteo, se disponibile), dati non più vecchi di X minuti.
    - Se mancano i dati, il risultato è **non sicuro** (fail-safe).
  - *Test:* richieste HTTP reali su loopback, regola con dati finti, fail-safe.
  - **Conformità:** se ConformU di ASCOM è disponibile sul PC, eseguilo e annota l'esito; altrimenti [NV].
  - *Dip.:* P9.2.
- [ ] **P9.6 [H] Allerte aurora.**
  - **Indice Kp e previsione** da una fonte ufficiale, per esempio NOAA SWPC. Verifica URL, formato, licenza e frequenza di aggiornamento, e annotali.
  - Al massimo una richiesta ogni 15 minuti, con cache e stop sugli errori.
  - **Soglia dipendente dalla latitudine dell'osservatorio**, configurabile. Alle medie latitudini serve un Kp alto: il valore predefinito va giustificato con una fonte.
  - **Conferma nell'immagine:** eccesso di verde (557,7 nm) o di rosso (630 nm) nel settore verso il polo vicino all'orizzonte, rispetto alle notti precedenti.
  - **Notifica:** nell'app e tramite P9.7.
  - *Test:* dati Kp finti e immagini sintetiche colorate.
  - *Dip.:* P3.1.
- [ ] **P9.7 [H] Report e notifiche via Telegram o email.**
  - **Telegram:** Bot API con token del bot e chat ID dell'utente. Verifica i metodi `sendMessage` e `sendPhoto`/`sendVideo` e il **limite di dimensione** dei file inviati dai bot, e annotalo.
  - **Email:** SMTP con TLS. Verifica sulla documentazione Microsoft se `System.Net.Mail.SmtpClient` è sconsigliato. In quel caso usa MailKit (licenza da verificare) oppure solo Telegram.
  - **Report del mattino** a un'ora configurabile: keogram, startrails, link YouTube se caricato, meteore, transiti, ore di cielo sereno, SQM.
  - **Allerte immediate:** bolide, aurora, ISS confermata.
  - Segreti cifrati con DPAPI, mai nei log. Limite di messaggi al giorno.
  - *Test:* handler finti; nessun segreto nei log.
  - *Dip.:* P5.3, P4.5.
- [ ] **P9.8 [S] Archivio ricercabile.**
  - **Indice locale** di notti, frame (dai sidecar) ed eventi. Deve essere un indice su file o un database embedded con licenza permissiva verificata (es. `Microsoft.Data.Sqlite`), da annotare.
  - **Ricostruibile** da zero dai file e aggiornato in modo incrementale.
  - **Ricerca** con filtri combinabili: intervallo di date, tipo di evento, altezza, luminosità, classe, satellite, indice di cielo sereno, fase della Luna, SQM, meteo. Ricerche salvate.
  - **Vista "Archivio"** con risultati a griglia e anteprime.
  - *Test:* indicizzazione, ricostruzione e query su un archivio sintetico; prestazioni su 10.000 frame da annotare.
  - *Dip.:* P2.1, P5.3.
- [ ] **P9.9 [S] Rete di camere (v1: scambio di file, nessun server).**
  - **Formato di scambio** versionato e documentato in `docs/RETE.md`. Contiene: stazione (ID, lat, lon, quota), qualità della calibrazione (residuo RMS) ed evento, cioè l'istante UTC con precisione al millisecondo e la traccia alt/az nel tempo.
  - **Export e import** di eventi tramite cartella o file.
  - **Abbinamento** degli eventi di due o più stazioni per finestra temporale. La sincronizzazione dell'orologio è da segnalare: mostra lo scarto NTP se misurabile.
  - **Triangolazione:** intersezione dei piani di vista delle tracce con un metodo standard da fonte citata, per ottenere traiettoria, quote di inizio e fine e velocità. Se i dati lo consentono, aggiungi un'estrapolazione **indicativa** della zona di caduta, marcata come tale.
  - **Mappa** semplice disegnata dall'app, senza tile di terzi, con le stazioni e la traccia al suolo.
  - **Sincronizzazione automatica** tramite il sito con Uplink: **non** implementarla, perché il contratto non esiste. Scrivi una proposta in `docs/RETE.md`.
  - *Test:* due stazioni sintetiche che osservano una traiettoria nota; la triangolazione deve ritrovarla entro la tolleranza.
  - *Dip.:* P9.1, P5.3.

### P10 — Verifica e rilascio 0.4.0
- [ ] **P10.1 [S] Verifica finale P9.** Come P8.2, più:
  - **test SafetyMonitor su loopback;**
  - **controllo d'esposizione:** con SafetyMonitor e "Accesso dal browser" spenti e nessun login OAuth in corso, il processo non ha porte in ascolto. Verificalo elencando le porte in ascolto del processo.
  - **riepilogo per Robert** in NOTES.md.
- [ ] **P10.2 [S] Rilascio automatico 0.4.0.** Stesso cancello e stessi passi di P8.4, con la versione 0.4.0. Il cancello include anche P10.1 `[x]`. Una release 0.3.0 già fatta non è un requisito: se P8.4 era `[B]`, questa release porta tutto.

---

## 5. Design: direzione UI/UX con occhio da astronomo [S-scelta]

### 5.1 Principi
1. **Il cielo è il protagonista.** L'immagine all-sky occupa lo spazio maggiore. Il resto dell'interfaccia è uno strumento di bordo: sobrio e leggibile al buio.
2. **Rispetto dell'adattamento al buio.** Sfondo nero vero e luminanza bassa per default. **Modalità notte rossa** a un tasto, globale (scorciatoia `Ctrl+R` e pulsante nella barra di stato), con un regolatore "luminosità interfaccia" dal 20 al 100%. In modalità rossa non c'è alcun pixel con componente blu o verde, comprese immagini, icone e grafici. L'immagine del cielo passa in luminanza rossa.
3. **Dati da strumento.** I numeri sono monospaziati a cifre tabulari, con unità sempre visibili (°, s, ms, %, °C). Gli angoli hanno 1 decimale, gli orari sono HH:mm locali con UTC in tooltip. Gli stati sono espressi con colore **e** con icona e testo.
4. **Futuristico, non decorativo.** Linee sottili luminose, griglie tecniche, contatori. I glow sono solo sull'elemento attivo. Niente effetti senza funzione, niente animazioni continue.
5. **Gerarchia a tre livelli per schermata:**
   - un'informazione dominante (il cielo, o l'evento);
   - un gruppo di dati operativi;
   - le azioni secondarie.

### 5.2 Architettura dell'informazione
Barra laterale, in quest'ordine:
- **Stanotte:** la home, P1.11.
- **Live:** P1.4.
- **Eventi:** meteore, bolidi e transiti, P5.4.
- **Notti:** archivio, timelapse, keogram e startrails, P1.6.
- **Pubblicazione:** Uplink e YouTube.
- **Camera:** connessione e calibrazione.
- **Maschera:** editor della maschera del timelapse.
- **Impostazioni.**
- **Log.**

La barra di stato in basso è sempre visibile e mostra:
- lo stato della camera;
- la fase (giorno, civile, nautico, astronomico, notte) con un glifo;
- Sole e Luna in altezza;
- il prossimo transito;
- lo stato degli upload;
- il disco libero.

### 5.3 Linguaggio visivo
- **Palette (ruoli):**
  - sfondo `#000000`;
  - superficie 1 `#06080C`, superficie 2 `#0B0F17`, superficie 3 `#111723`;
  - bordo `rgba(120,170,255,0.16)`, bordo attivo `rgba(92,200,255,0.55)`;
  - testo `#E6EDF7`, testo secondario `#8C98AB`, testo terziario `#5D6778` (solo per testi non essenziali, comunque ≥ 4.5:1 sulle superfici dove compare);
  - accento `#5CC8FF` (ciano ghiaccio), secondario `#8A7DFF` (violetto aurora);
  - successo `#3DDC97`, avviso `#FFC24B`, errore `#FF5C7A`.
  - Modalità rossa: sfondo `#000000`, testo `#FF3B30` e `#B3261E`, accento `#FF453A`.
  - Il contrasto si verifica con test automatici sui token.
- **Codici di colore semantici astronomici, coerenti in tutta l'app:**
  - Sole `#FFC24B`;
  - Luna `#C9D4E5`;
  - satelliti `#5CC8FF`;
  - meteore `#FF8A3D`;
  - bolidi `#FF5C7A`;
  - fasce dei crepuscoli dal blu profondo al nero: civile `#1B2A4A`, nautico `#111C33`, astronomico `#0A1020`, notte `#000000`.
- **Tipografia:**
  - famiglie OFL incluse nell'app: *Inter* per l'interfaccia e *JetBrains Mono* per i dati;
  - scala: 12 / 14 / 16 / 20 / 28 / 40;
  - grassetto solo per titoli e valori chiave;
  - titoli di sezione in maiuscoletto con spaziatura +0.08em, come etichette di strumentazione.
- **Spazio e forme:**
  - spaziature su scala 4/8/12/16/24/32/48;
  - pannelli con angoli 10 px, bordo 1 px e, all'interno, una sottile linea luminosa in alto (gradiente dal bordo attivo al trasparente);
  - un solo livello di ombra.
- **Icone:** set vettoriale originale (Path XAML) con tratto 1.5 px su griglia 24, stile lineare coerente. Comprende glifi astronomici dedicati:
  - Sole e Luna, con la fase della Luna disegnata;
  - satellite;
  - meteora;
  - bussola;
  - fasi del crepuscolo.
- **Overlay sul cielo:**
  - griglia alt/az a 30° e 60°;
  - cerchio dell'orizzonte;
  - punti cardinali;
  - tracce dei satelliti previste, tratteggiate, con il verso indicato;
  - eventi rilevati come segmenti pieni;
  - ogni overlay attivabile singolarmente, con opacità regolabile.
- **Movimento:**
  - transizioni di 120–200 ms (dissolvenza e piccolo spostamento);
  - contatori numerici senza animazione;
  - rispettare l'impostazione di Windows "Mostra animazioni".
- **Immagini (P1.10):**
  - logo e icona originali: un cerchio fisheye stilizzato con orizzonte, una scia di meteora e un nodo "brain" geometrico, vettoriale e leggibile a 16 px;
  - splash;
  - illustrazioni vettoriali per gli stati vuoti, al tratto, con la palette;
  - sfondo stellare procedurale statico.
  - Le foto del cielo sono solo quelle dell'utente.
- **Testi:** mai stringhe fisse, tutto passa dalle risorse IT/EN. I messaggi d'errore dicono cosa è successo e cosa fare.

### 5.4 Pattern UX
- **Stati sempre previsti:** vuoto, caricamento, errore. I dati non disponibili si mostrano come "—" con tooltip del motivo, mai come 0.
- **Azioni:** le distruttive chiedono conferma. Quelle lunghe (rigenera timelapse, upload) mostrano un avanzamento annullabile.
- **Scorciatoie:**
  - `F11` immagine a schermo intero;
  - `Ctrl+R` modalità notte;
  - `G` griglia;
  - `T` tracce dei satelliti;
  - `←`/`→` frame precedente o successivo nelle viste d'archivio.
- **Tastiera e accessibilità:**
  - focus sempre visibile, con un anello ciano di 2 px;
  - ordine di tab logico;
  - testi descrittivi (`AutomationProperties.Name`) sulle icone senza testo;
  - bersagli cliccabili ≥ 32 px.

## 6. Rischi e mitigazioni
| Rischio | Mitigazione |
|---|---|
| Calibrazione assente → niente transiti/meteore localizzati | P3.2 automatica dal Sole + manuale; eventi "non verificati" invece di falsi "identificati" |
| Esposizioni lunghe (10–60 s) → il "video" del transito ha pochi frame | P4.6 modalità transito; fps del clip configurabile |
| Falsi positivi meteore (aerei, satelliti, nuvole) | filtri P5.2 + etichette utente in P5.4 |
| Token YouTube scaduto dopo 7 giorni | spiegazione in UI + errore chiaro + notifica |
| Blocco IP da CelesTrak | policy rigida P4.1 |
| Regressioni su cattura/pubblicazione | test esistenti sempre verdi; nessun cambio a contratti Uplink |
| Loop che rompe l'istanza installata | `--data-folder` obbligatorio nelle prove (P0.2) |
