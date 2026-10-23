# All Sky Brain — Piano di implementazione "desktop v1" (per ralph loop)

Nome ufficiale del prodotto: **All Sky Brain** (con gli spazi) in tutto ciò che l'utente vede.
Branch di lavoro: `ralph/desktop-v1`, partito da `main` @ 885535f. Si lavora sempre sul branch; su `main` si va **solo** nel task finale P8.4.
Il piano prevede due rilasci automatici, ciascuno solo se il suo cancello di verifica è verde:
- **0.3.0** in P8.4, con le funzioni di base (P0–P7), così il PC che ha la 0.2.x riceve presto la richiesta di aggiornamento;
- **0.4.0** in P10.2, con le funzioni avanzate di P9.
- **0.5.0** in P16.2, con la fase 3 (P11–P15). **Non avviare la fase 3 senza il via esplicito di Robert.**

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
11. **Fase 3** (richiesta del 22/10/2026, **da avviare solo col via di Robert**), in P11–P16: integrazione opzionale con NINA (soggetto puntato con mirino, grafico altezza, distanza dalla Luna, stato di park, log di NINA collegati ai frame), monitoraggio pubblico sul sito in sola lettura, studio di montaggio per materiale vecchio, colore del video, invio a un bot Telegram; rilascio 0.5.0.

Vincoli che restano in vigore:
- solo richieste HTTPS in uscita e nessun server esposto;
- segreti cifrati con DPAPI e mai nei log;
- interfaccia in italiano e inglese;
- nessuna dipendenza GPL;
- la pubblicazione Uplink esistente non va rotta.
- La fase 3 non cambia questi vincoli: il monitoraggio pubblico (P12) è solo invio in uscita; le letture da NINA (P11) sono richieste locali verso il PC di NINA (HTTP o WebSocket su localhost o in LAN), da elencare tra le connessioni previste.

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
- [x] **P0.1 [S] Baseline.** Build e test su `main`/branch; annota in NOTES.md numero test, falliti preesistenti (noto: `CaptureLoopTests.FrameMedian_UpdatesExposureForNextCapture` intermittente). *Accettazione:* NOTES.md con esito.
- [x] **P0.2 [H] `--data-folder`.** Argomento da riga di comando (e variabile `ALLSKY_DATA_FOLDER`) per scegliere la cartella dati; default invariato. Mutex di istanza singola derivato dalla cartella dati (due cartelle = due istanze ammesse). *Test:* parsing argomenti. *Dip.:* P0.1.

- [x] **P0.3 [H] Nome "All Sky Brain".** Sostituisci "AllSky Brain" e il vecchio nome del prodotto nei testi visibili, rispettando la lista di invarianti in §2. *Accettazione:* `grep -rn "AllSky Brain"`, escludendo `.git`, `bin`/`obj` e `docs/ralph`, non trova più nulla; test verdi; YAML validi. *Dip.:* P0.1.

### P1 — Applicazione desktop nativa (WPF) e tema
- [x] **P1.1 [S] Progetto `AllSky.Desktop`.** WPF net10.0-windows, AssemblyName `AllSkyBrain`, `VelopackApp.Build().Run()` prima riga, avvio Engine in-process (riusa la costruzione oggi in `AllSkyHost`/Tray estraendo una factory condivisa, senza duplicare), tray icon con menu (Apri, Controlla aggiornamenti, Esci), chiusura ordinata. `AllSky.Tray` resta nel repo ma non è più l'exe pubblicato: cambia il suo `AssemblyName` in `AllSkyBrain.TrayLegacy` (due exe con lo stesso nome nella solution creano confusione) e aggiorna `build.yml` e `release-stable.yml` (solo il percorso del progetto pubblicato; `packId`/`mainExe` invariati). *Accettazione:* build verde; avvio con `--data-folder` mostra finestra vuota con tema base; uscita pulita. *Dip.:* P0.2.
- [x] **P1.2 [S] Design system** in `Theme/Tokens.xaml` + `docs/DESIGN-DESKTOP.md`. Vedi §5. Stili per Button (primario/secondario/pericolo), TextBox, ComboBox, CheckBox/Toggle, Slider, ScrollBar, ToolTip, DataGrid/ListView, schede "pannello" con bordo luminoso sottile. *Accettazione:* pagina "Galleria componenti" (solo in Debug) che mostra tutti gli stili; contrasto testo ≥ 4.5:1 verificato con calcolo (test unitario sui colori). *Dip.:* P1.1.
- [x] **P1.3 [H] Shell di navigazione.** Barra laterale con icone (vettoriali, Path XAML, niente immagini raster) + barra di stato in basso (stato camera, fase giorno/crepuscolo/notte, altezza Sole/Luna, ultimo frame, spazio disco, stato pubblicazioni). Navigazione da tastiera, focus visibile. *Dip.:* P1.2.
- [x] **P1.4 [H] Vista "Live".** Immagine corrente grande (aggiornata su `FrameProduced`, decodifica fuori dal thread UI, nessun leak di bitmap), zoom/pan, toggle "grezzo/elaborato", pannello dati del frame (esposizione, gain, mediana ADU, temperatura). Overlay commutabili: griglia alt/az e punti cardinali (dopo P3), tracce satelliti previste (dopo P4). *Dip.:* P1.3.
- [x] **P1.5 [H] Vista "Camera".** Ricerca/connessione camere (ASCOM/Alpaca/Sim) con l'API dell'Engine esistente, messaggi d'errore leggibili (anche quelli con i valori dei limiti di esposizione introdotti in 0.2.1). *Dip.:* P1.3.
- [x] **P1.6 [H] Vista "Notti".** Elenco notti, keogram come barra di scorrimento temporale, startrails, riproduzione timelapse (MediaElement) e pulsante "Rigenera timelapse". *Dip.:* P1.3.
- [x] **P1.7 [H] Vista "Pubblicazione".** Porta in WPF le impostazioni Uplink esistenti (stessi contratti `/api/publish/*` ma chiamando direttamente i servizi; token mai mostrato, solo "impostato/non impostato"). *Dip.:* P1.3.
- [x] **P1.8 [H] Vista "Impostazioni" + "Log".** Posizione osservatorio, lingua IT/EN, cartella dati (sola lettura), avvio con Windows, aggiornamenti (AutoCheck), "Accesso dal browser" (Kestrel opzionale, spento). Log: ultime righe con filtro, senza segreti. *Dip.:* P1.3.
- [x] **P1.10 [S] Identità visiva e immagini** (vedi §5.3). Comprende:
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
- [x] **P1.11 [S] Vista "Stanotte"**, schermata iniziale (vedi §5.2). Contiene:
  - l'immagine del cielo dal vivo come protagonista;
  - **una linea temporale da tramonto ad alba** con le fasce dei crepuscoli civile, nautico e astronomico e la notte astronomica, le curve di altezza di Sole e Luna, e i passaggi visibili dei satelliti segnati (quando P4 è pronto);
  - **una carta "Condizioni"** con fase e illuminazione della Luna (glifo vettoriale corretto per la fase), meteo (dopo P6) e un indice di cielo sereno (dopo P5: stelle rilevate rispetto all'atteso, oppure non disponibile);
  - **una carta "Prossimi eventi"** con il prossimo transito e il conto alla rovescia, e l'ultima meteora;
  - **uno stato sintetico della cattura.**
  - Le altezze e gli orari dei crepuscoli si calcolano con `AllSky.Astro`. *Test:* gli orari dei crepuscoli vanno confrontati con una fonte indipendente per una data e un luogo noti, e la fonte va annotata. Se non è raggiungibile, segnare [NV].
  - *Dip.:* P1.4.
- [ ] **P1.9 [S] Revisione UI** con la checklist della skill `web-ui-review` adattata a WPF (gerarchia, spaziatura su scala 4/8, contrasto, focus, stati vuoto/caricamento/errore, testi IT più lunghi). Screenshot a 1280×800 e 1920×1080 salvati in `docs/ralph/shots/` (RenderTargetBitmap della finestra), in modalità normale e modalità notte rossa. Correggi ciò che non rispetta §5 prima di segnare `[x]`. *Dip.:* P1.4–P1.8, P1.10, P1.11.

### P2 — Maschera informativa sul timelapse (richiesta 21/10 23:09)
- [x] **P2.1 [H] Sidecar metadati per frame.** Per ogni frame archiviato `<frame>.json` con: UTC e ora locale, esposizione, gain, temperatura sensore, altezza/azimut Sole e Luna, fase Luna (illuminazione %), fase giorno, snapshot meteo (se disponibile, P6), satelliti visibili in quel momento con alt/az (se disponibile, P4). Scrittura atomica, retrocompatibile (frame vecchi senza sidecar → metadati ricostruiti da timestamp e posizione dove possibile). *Test:* serializzazione, ricostruzione. *Dip.:* P0.1.
- [x] **P2.2 [S] Modello di layout (puro, `AllSky.InfoOverlay`).** `OverlayLayout` versionato in config: lista di *widget* `{ tipo, abilitato, posizione (TL, TR, BL, BR, BandaAlta, BandaBassa), ordine, formato }`; *maschera*: cerchio cielo (centro/raggio da P3 o manuali) con esterno nero opaco o sfumato, oppure bande nere sopra/sotto; scala testo relativa all'altezza del frame. Tipi di widget: `DataOra` (formato data/ora configurabile, fuso locale/UTC), `Sole` (altezza °), `Luna` (altezza °, fase %), `Satelliti` (nomi in transito in quel momento, max N), `Meteo` (scelta dei campi), `Logo` (file PNG, posizione, altezza max %, opacità), `TestoLibero` (es. nome osservatorio), `Esposizione` (tempo/gain), `Bussola` (N/E/S/W sul bordo del cerchio, dopo P3). Calcolo puro dei rettangoli (nessuna sovrapposizione: se lo spazio manca, i widget in coda vengono omessi e lo si segnala). *Test:* layout su 4:3, 16:9, 1:1; nessuna sovrapposizione; ordine rispettato. *Dip.:* P2.1.
- [x] **P2.3 [S] Renderer WPF** (in Desktop): disegna maschera + widget con `DrawingVisual` e font OFL, compone sul frame (WIC per PNG dei loghi), restituisce pixel per l'encoder. Budget: < 30 ms/frame a 1920×1080 sul PC (misura e annota). *Test:* golden test su un frame sintetico (confronto con tolleranza) — salvare i golden in `tests/.../Golden/`. *Dip.:* P2.2, P1.2.
- [x] **P2.4 [H] Integrazione nel timelapse.** Il generatore del timelapse applica la maschera se `overlay.applyToTimelapse = true`; opzioni separate `applyToLive` e `applyToPublish` (Uplink: default **false**, per non cambiare il contratto delle immagini già inviate). Rigenerazione possibile con il layout corrente. *Test:* il timelapse con maschera usa i metadati del frame giusto (frame N → orario N). *Dip.:* P2.3.
- [x] **P2.5 [H] Editor visivo della maschera** (vista "Maschera" o sezione delle Impostazioni): anteprima live sull'ultimo frame, lista widget con interruttore, posizione (tendina), ordine (su/giù), formati; caricamento loghi (copia nella cartella dati, mai riferimento a percorsi esterni); pulsante "Ripristina predefiniti". Predefinito: DataOra (BL), Sole+Luna (BR), maschera cerchio attiva, altri spenti. *Dip.:* P2.4, P1.3.

### P3 — Geometria del cielo (prerequisito di satelliti, meteore, bussola)
- [x] **P3.1 [S] Modello fisheye** (`AllSky.Sky`): proiezione equidistante r = k·z (z = distanza zenitale), parametri `cx, cy, raggioOrizzonte, rotazioneNord, specchio` (E-O invertito guardando verso l'alto), con eventuale termine radiale di correzione k2. Conversioni alt/az ↔ pixel con test di andata/ritorno (errore < 0.01 px). *Dip.:* P0.1.
- [x] **P3.2 [S] Calibrazione dal Sole.** Di giorno, centroide del Sole (pixel saturi, area compatta) a orari diversi + alt/az calcolati (già in `AllSky.Astro`) → fit ai minimi quadrati di cx, cy, raggio, rotazione, specchio. Serve una giornata con Sole visibile: il codice raccoglie i punti in background e propone la calibrazione quando l'errore RMS è sotto soglia (es. < 1% del raggio). Fallback manuale: l'utente clicca centro, bordo orizzonte e il Nord sull'immagine. *Test:* punti sintetici generati con parametri noti → fit li ritrova; rumore e outlier. *Dip.:* P3.1.
- [x] **P3.3 [H] UI calibrazione** (in vista Camera): stato della raccolta punti, anteprima con griglia alt/az sovrapposta, accetta/rifiuta, modalità manuale. *Dip.:* P3.2, P1.5.

### P4 — Satelliti e transiti (ISS e altri)
- [x] **P4.1 [S] Fonte elementi orbitali** (`AllSky.Satellites`): download da CelesTrak per numero di catalogo (`CATNR=`) dei bersagli configurati; formato TLE per oggetti a 5 cifre; **una richiesta per bersaglio ogni ≥ 2 h**, cache su disco con data; **su qualunque risposta non-200: stop dei download per 24 h e messaggio all'utente**; nessuna richiesta del gruppo "active"/Starlink. Lista predefinita: ISS, stazione cinese, Hubble — **verifica i numeri NORAD su CelesTrak prima di scriverli** e cita la fonte in NOTES.md. L'utente può aggiungere/togliere numeri NORAD. *Test:* con HttpMessageHandler finto (200, 404, 503, rete giù), rispetto dell'intervallo con orologio finto. *Dip.:* P0.1.
- [x] **P4.2 [S] Propagazione e passaggi** con SGP.NET (verifica licenza/versione/target e annota): posizione topocentrica alt/az per l'osservatorio; passaggi nelle prossime 24 h con alt > soglia (default 10°); **visibilità ottica**: satellite illuminato dal Sole (fuori dall'ombra terrestre, modello cilindrico) **e** cielo scuro (Sole < −6°, configurabile). Interfaccia `IOrbitPropagator` per poter cambiare libreria. *Test:* coerenza (az/el continui, culminazione unica per passaggio), cambio di giorno, oggetto mai visibile. Validazione esterna: confronta 1 passaggio ISS previsto con una fonte indipendente (es. Heavens-Above) **solo se raggiungibile**; altrimenti annota [NV]. *Dip.:* P4.1.
- [x] **P4.3 [H] Previsioni in UI:** prossimi passaggi visibili (ora inizio/culmine/fine, alt max, direzione), tracce previste sull'immagine Live (P3.1) e nella barra di stato "prossimo transito tra…". *Dip.:* P4.2, P1.4, P3.1.
- [x] **P4.4 [S] Conferma del transito nelle immagini** (`AllSky.Detection`): nei frame dentro la finestra del passaggio, cerca una scia lungo la traccia prevista (proiettata in pixel): profilo di luminosità lungo un corridoio di ± N px rispetto al fondo locale; punteggio = frazione del tratto previsto sopra soglia. "Identificato" se punteggio ≥ soglia su almeno un frame. Funziona solo con calibrazione valida (P3.2), altrimenti l'evento è "previsto, non verificato". *Test:* frame sintetici con scia iniettata lungo la traccia (identificato), scia altrove o assente (non identificato), nuvole (rumore diffuso). *Dip.:* P4.2, P3.2.
- [x] **P4.5 [H] Salvataggio del transito:** se identificato (o se l'utente sceglie "salva tutti i passaggi visibili"), crea evento in `AllSky.Events` con: clip MP4 dei frame [inizio−1, fine+1] (riusa `AllSky.Timelapse`, fps configurabile, con maschera P2 se attiva), immagine composita (massimo dei frame), metadati (satellite, orari, alt max, punteggio). Non blocca la cattura. *Test:* evento completo su disco con frame sintetici. *Dip.:* P4.4, P2.4.
- [x] **P4.6 [H] Modalità "transito" opzionale** (default spenta): durante un passaggio previsto riduce l'intervallo tra le pose (senza scendere sotto il minimo della camera) per avere più frame nel video; ripristina dopo. *Test:* sequenza di intervalli con orologio finto. *Dip.:* P4.2.

### P5 — Meteore e bolidi
- [x] **P5.1 [S] Rilevatore di scie** (`AllSky.Detection`, puro): lavora su luminanza ridotta (es. lato lungo 960 px) e solo di notte; differenza con la mediana degli ultimi N frame (fondo), soglia adattiva (k·σ robusta), maschera del cielo (P3) e della Luna (cerchio attorno alla posizione calcolata), componenti connesse, trasformata di Hough sui pixel candidati, segmenti con lunghezza ≥ L e spessore ≤ W. Budget: < 300 ms/frame sul PC (misura e annota), in coda separata con scarto se in ritardo. *Test:* frame sintetici con stelle, rumore, scia iniettata (lunghezze/luminosità varie); obiettivo su set sintetico: richiamo ≥ 90% per scie ≥ 40 px con SNR ≥ 5, falsi positivi ≤ 1 per 100 frame senza scie. *Dip.:* P3.1.
- [x] **P5.2 [S] Classificazione e filtri**: scarta (a) scie che coincidono con tracce di satelliti previste (P4.2) → etichetta "satellite"; (b) oggetti che si ripetono allineati su ≥ 2 frame consecutivi → "aereo/satellite"; (c) frame con variazione globale alta (nuvole, fulmini, fari) → frame scartato. Meteora = scia singola in un frame; **bolide** = meteora con picco saturo o luminosità integrata sopra soglia alta e/o lunghezza molto grande (soglie configurabili, valori iniziali documentati come da tarare). *Test:* casi sintetici per ciascun ramo. *Dip.:* P5.1, P4.2.
- [x] **P5.3 [H] Salvataggio evento meteora/bolide**: ritaglio ingrandito, frame intero con scia evidenziata, mini-clip dei frame [N−2, N+2], metadati (ora, posizione alt/az inizio-fine se calibrato, lunghezza, luminosità, classe, punteggio). *Dip.:* P5.2.
- [x] **P5.4 [H] Vista "Eventi"**: galleria (meteore, bolidi, transiti) con filtri per notte/tipo, dettaglio con clip e dati, pulsanti "Conferma" / "Falso positivo" (etichette salvate per tarare le soglie), esporta evento (cartella zip). *Dip.:* P5.3, P4.5, P1.3.
- [x] **P5.5 [H] Cablaggio del rilevamento nell'Engine** (aggiunto in corso d'opera): per ogni frame, luma ridotto → `StreakDetector` → `StreakClassifier` (con `PredictedTrackProviderAdapter` se la calibrazione è valida) → `EventSaver` (meteore/bolidi, finestra N−2..N+2); pipeline dei transiti (previsioni P4.3 → `TransitConfirmer` → `TransitEventSaver`); adattatore `ITransitWindowSource` per la modalità transito (P4.6); `IPixelToSky` dalla calibrazione. Opt-in: sezione config `detection` spenta di default. Nessun blocco della cattura. *Dip.:* P4.3, P4.5, P5.3, P5.2.

### P6 — Meteo
- [x] **P6.1 [H] `IWeatherSource` + adapter ASCOM ObservingConditions** (COM late-bound come la camera, thread dedicato, ogni proprietà letta con try/catch: se non implementata → assente). **Verifica le unità** sulle pagine delle singole proprietà ASCOM e scrivile in NOTES.md. *Test:* fake. *Dip.:* P0.1.
- [x] **P6.2 [H] Adapter Open-Meteo opt-in** (default spento, testo in UI: "solo uso non commerciale, dati CC-BY 4.0 Open-Meteo" + attribuzione mostrata dove compaiono i dati): una richiesta ogni ≥ 15 min, cache, stop su errori ripetuti. Campi: temperatura, umidità, copertura nuvolosa, vento. **Verifica i nomi dei parametri sulla documentazione** prima di scrivere il codice. *Test:* handler finto. *Dip.:* P6.1.
- [x] **P6.3 [H] Meteo nel sidecar (P2.1) e nel widget `Meteo`** (P2.2): l'utente sceglie quali campi mostrare. *Dip.:* P6.1, P2.2.

### P7 — Caricamento automatico su YouTube
- [x] **P7.1 [H] Modello del titolo/descrizione** (puro): segnaposto `{data:yyyy-MM-dd}`, `{notte}` (data di inizio notte), `{ora:HH:mm}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`; formati .NET validati; anteprima; caratteri vietati e lunghezze massime controllate (**limiti di lunghezza YouTube [NV]**: verificali sulla doc della risorsa `video` prima di codificarli). *Test:* esempi con date a cavallo della mezzanotte e del cambio ora legale. *Dip.:* P0.1.
- [x] **P7.2 [S] Autenticazione OAuth "Desktop app"**: l'utente inserisce *il proprio* client ID/secret (l'app è generica, nessuna credenziale incorporata). Leggi prima la guida Google "OAuth 2.0 for Mobile & Desktop Apps" e annota il flusso consentito; listener solo su `127.0.0.1` e solo durante il login, poi chiuso. Refresh token cifrato con DPAPI (riusa il protettore della pubblicazione), mai nei log. La UI spiega che con l'app OAuth in stato "Testing" il token scade dopo 7 giorni [V] e come passarla "In production". *Test:* store cifrato, parsing risposte token con fake. *Dip.:* P1.8.
- [x] **P7.3 [S] Upload resumable + pianificazione**: ogni giorno all'ora scelta carica il timelapse dell'ultima notte completata (con maschera se attiva); privacy predefinita **private** (scelta utente: private/unlisted/public), tag, categoria (ID **[NV]**), descrizione dal modello; idempotenza per notte (stato salvato: mai due upload della stessa notte, anche dopo un crash); retry con backoff su 5xx/rete, nessun retry su 4xx (eccetto 401 → refresh una volta); quota: ≤ 1 upload/giorno di default. Se il PC era spento all'ora prevista, carica all'avvio successivo (finestra configurabile). *Test:* orologio finto e handler finto (sessione resumable, interruzione a metà, 401, 403 quota, 500). *Dip.:* P7.1, P7.2.
- [x] **P7.4 [H] Vista YouTube** (in "Pubblicazione"): login/logout, stato (ultimo upload con link al video, prossimo upload, ultimo errore), impostazioni, "Carica ora la notte scorsa". *Dip.:* P7.3, P1.7. *Stato:* Host, Core e test verdi (1918/1918 sul runner); `YouTubePanel` (WPF) controllato solo in sintassi, non compilato su Linux: verificare su Windows.

### P8 — Verifica e rilascio 0.3.0
- [x] **P8.1 [H] Documentazione** (fatto 23/10/2026: README riscritto per la v0.3.0, FUNZIONI allineato, `docs/GUIDA-YOUTUBE.md` e `docs/YOUTUBE-OAUTH.md` con fonti Google lette il 23/10; punti [NV] elencati nelle guide. **Da fare, trovato dalla revisione:** l'attribuzione Open-Meteo (licenza CC BY) è costruita ma non viene scritta nel sidecar): README (desktop, maschera, satelliti, meteore, YouTube, meteo), `docs/FUNZIONI.md` aggiornato, guida "Configurare YouTube" passo-passo (Google Cloud Console, credenziali Desktop, stato In production), note sui limiti noti.
- [x] **P8.2 [S] Verifica finale** (completata il 22/10/2026 con limiti dichiarati in NOTES.md: mask informativa del timelapse e encoder Windows `h264_mf` non verificati su Linux): build + test completi, smoke test con camera Sim e `--data-folder` temporanea (1 notte simulata accelerata se l'Engine lo consente: timelapse con maschera, almeno un evento sintetico); controllo segreti (`grep` del token di prova su cartella dati e log); revisione del diff complessivo. Scrivi in NOTES.md cosa è verificato e cosa no.
- [ ] **P8.3 [S] Riepilogo per Robert** in NOTES.md: cosa è fatto, cosa è bloccato e perché, decisioni prese da confermare, prossimi passi. Includi la sezione **"Da provare sul PC con la ZWO"** (il loop gira con il solo simulatore, vedi PROMPT.md).
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

## FASE 3 — Integrazione NINA, accesso remoto, studio di montaggio, colore, Telegram (P11–P16)

> **NON PARTIRE.** Questa fase è scritta il 22/10/2026 su richiesta di Robert ma **non è avviata**. Il loop la inizia solo quando Robert lo scrive esplicitamente ("via alla fase 3"). Le decisioni di Robert sono registrate qui sotto.
> Rilascio finale: **0.5.0** in P16.2, con lo stesso cancello di P8.4 (nessuna eccezione senza richiesta esplicita di Robert).

### Richieste di Robert (22/10/2026) che questa fase realizza

**NINA (opzionale, pagina «Integrazioni → NINA» a parte, spenta di default; chi non la spunta non vede né usa nulla):**
1. L'utente collega il software a NINA.
2. Il software riceve quale soggetto sta puntando il telescopio.
3. Un **mirino** sull'immagine all-sky segna il punto puntato e ne scrive il nome.
4. Un **grafico** con altezza del soggetto nella notte (e quanto altro serve a decidere).
5. Gli **eventi del log di NINA** collegati per timestamp al momento del timelapse e al frame preciso, per fare diagnostica anche visuale (la all-sky di Robert sta sotto il telescopio, che quindi si vede nelle immagini).
6. La **distanza angolare del soggetto dalla Luna**.
7. Se il telescopio è in **posizione di park**, letto da NINA.

**Monitoraggio remoto:** i dati dell'osservatorio (immagine, stato, soggetto, park, grafico, prodotti) visibili sul **sito** da chiunque, **senza VPN**, in **sola lettura**; decisione di Robert del 22/10/2026: nessun login e nessuna interazione.

**Studio di montaggio:** selezionare **video vecchi o serie di immagini vecchie** della all-sky, **editarli**, montare timelapse e keogram, **caricarli su YouTube** o **salvarli in locale**.

**Colore:** curare meglio il **bilanciamento colore del video**, oggi poco curato.

**Telegram:** inviare a un **bot Telegram** timelapse, keogram e gli altri prodotti.

### Fatti verificati e da verificare per la fase 3

| Tema | Fatto | Stato |
|---|---|---|
| Plugin ninaAPI | Plugin della comunità per NINA (autore christian-photo, licenza MPL-2.0): API web con percorso `v2/api` e WebSocket (pagina «Websocket-V2» nella wiki) | [V] [GitHub](https://github.com/christian-photo/ninaAPI) (README, 22/10/2026) |
| ninaAPI: dettagli | Porta predefinita, autenticazione, elenco endpoint, nomi esatti dei campi del mount (`parked`, RA/Dec, alt/az, tracking), endpoint della sequenza e del soggetto, endpoint dei log, eventi del WebSocket | **[NV]**: la documentazione in `christian-photo.github.io/github-page/projects/ninaAPI/v2/doc/api` è una pagina generata via JavaScript e non si legge con un fetch semplice. Fonti secondarie (wrapper di terzi) dicono che le informazioni del mount comprendono RA/Dec, alt/az, tracking, parcheggiato/home, slew e coordinate del sito, e usano la porta `1888` nell'esempio di configurazione: **non è una conferma** del valore predefinito. |
| Cartella dei log di NINA | `%localappdata%\NINA\logs`; livelli Error, Warning, Info, Debug, Trace | [V] [documentazione NINA](https://nighttime-imaging.eu/docs/develop/site/tabs/options/general); **formato delle righe, nome dei file e rotazione [NV]** |
| NINA di Robert | Versione `3.2.0.9001`, target `net8.0-windows7.0` | [V] dal passaggio di sessione UP Link del 19/10/2026 (documento del Project), non riverificato |
| Agente esperto UP Link | Conosce il **protocollo del sito** (`docs/UPLINK-SPEC.md`, fixture in `contract/`), non le API interne di NINA: le note `docs/nina-api-notes.md` erano ancora da scrivere il 19/10/2026 | [V] dal passaggio di sessione; quindi **non è una fonte sui campi di NINA** |
| Principio Uplink | Il client Uplink è solo in uscita (push), non apre porte né ospita server | [V] UPLINK-SPEC §1 |
| Cloudflare | Sui piani Free/Pro/Business anche via Tunnel lo streaming video è vietato; MJPEG non citato | [V] dal piano v1 (policy Cloudflare); **rilettura [NV]** prima di usarlo |
| Telegram Bot API | Metodi `sendMessage`, `sendPhoto`, `sendVideo`, `sendDocument`; limiti di dimensione dei file; `getUpdates` in long polling (solo richieste in uscita); server Bot API locale | **[NV]**: tutto da leggere sulla documentazione ufficiale in P15.1 |

### Decisioni di Robert (22/10/2026)

1. **Accesso remoto:** solo **monitoraggio** sul sito, tutti visitatori, **nessun login e nessuna interazione**. La scelta tecnica è stata delegata e adottata: strada A (invio in uscita al sito), vedi P12.
2. **NINA:** gira sullo **stesso PC** della all-sky. Se il plugin ninaAPI sia installato **non è noto**: da verificare sul PC di Robert (in NINA, elenco dei plugin installati) o con la «Prova connessione» di P11.1. Questa sessione non ha un computer collegato, quindi non può controllarlo.
3. **Telegram:** ok a un solo bot con una lista di chat autorizzate.
4. **Idee:** campo inquadrato, capitoli YouTube, allerta di cielo che peggiora e condensa sono **nel piano** (P11.9–P11.12). L'«indice di efficienza della notte» è **scartato** da Robert.

### P11 — Integrazione con NINA (opzionale)

Regole comuni a P11: tutto è **spento di default** (`nina.enabled=false`); la pagina «Integrazioni → NINA» spiega cosa viene letto; nessun dato di NINA lascia il PC se Robert non lo chiede (la pubblicazione sul sito è P12, a parte); un errore o un NINA spento **non** deve mai rallentare o fermare la cattura; riconnessione con backoff; sola lettura (nessun comando a NINA in v1).

- [x] **P11.0 [S] Fatti su NINA e API.** (fatto 22/10/2026: `docs/NINA-API.md`, fatti dal sorgente di ninaAPI commit 353a853; campi JSON di mount e sequenza ancora [NV] fino al test su NINA reale) Leggi la documentazione ufficiale di ninaAPI (README, wiki «Websocket-V2», specifica `v2/api`, anche dal sorgente del repository se la pagina web non si legge) e scrivi `docs/NINA-API.md`: porta predefinita, autenticazione, endpoint e **nomi esatti dei campi** per mount (parcheggiato, RA/Dec e la loro epoca J2000 o JNow, alt/az, tracking, slew), sequenza e soggetto in corso (nome, coordinate), eventi del WebSocket, eventuale endpoint dei log. Dai a ogni riga [V] con fonte o [NV]. Verifica anche il formato delle righe di `%localappdata%\NINA\logs` e la rotazione dei file (da un log reale se Robert ne mette uno a disposizione, anonimizzato). Fai **rivedere** da un agente con le istruzioni di `uplink-expert` (Project «codici astrofotografia», `claude/uplink-expert-agent.md`, da lanciare come agente generico in sola lettura sul sito `/home/claude/sito`) solo le parti che toccano il protocollo del sito, non i fatti di NINA. Conclusione: scegli la fonte (ninaAPI, lettura dei log, oppure entrambe) e annota perché. Verifica anche se il plugin ninaAPI risulta installato sul PC di Robert (serve il computer collegato, oppure lo dice lui) e annota come si installa dal gestore dei plugin di NINA **[NV]**.
  - *Test:* nessun codice; la verifica è che ogni campo usato dai task successivi abbia fonte [V].
  - *Dip.:* nessuna.
- [~] **P11.1 [S] Configurazione e pagina «NINA».** Parte fatta (22/10): sezione `nina` in `AppConfig`, validazione (errori solo con `enabled=true`, avvisi con `enabled=false`), 9 test in `NinaConfigTests`. **Ancora da fare:** la pagina «Integrazioni → NINA», il pulsante «Prova connessione», il collegamento degli avvisi al log, DPAPI (non serve: nessun segreto). Sezione `nina` in `AppConfig` (spenta; host, porta, chiave se prevista da P11.0, cartella dei log, modalità fonte) con validazione. Nuova vista **Integrazioni → NINA** nella UI (IT/EN, notte rossa): interruttore, campi, pulsante «Prova connessione» con esito comprensibile (distingue «NINA non risponde», «plugin ninaAPI non trovato» con la guida all'installazione, «connesso»), stato in barra di stato. Segreti con DPAPI.
  - *Test:* validazione, round-trip, upgrade da config senza la sezione, vista renderizzata (come i test UI esistenti).
  - *Dip.:* P11.0.
- [x] **P11.2 [H] Client NINA e modello di stato.** (fatto 22/10: `src/AllSky.Nina`, sola lettura, contenitore `Response` verificato dal sorgente; 38 test; nomi dei campi JSON ancora [NV], vedi P11.13) `INinaSource` con implementazione per ninaAPI (REST + WebSocket) e, se P11.0 lo prevede, per la lettura dei log. Modello `NinaState` (connesso, mount: parcheggiato/in inseguimento/in slew, coordinate, soggetto in corso, sequenza attiva) con timestamp UTC di lettura. Server finto in-process per i test.
  - *Test:* risposte finte, disconnessioni, riconnessione con backoff, dati incompleti, nessun blocco del chiamante, nessun segreto nei log.
  - *Dip.:* P11.0, P11.1.
- [~] **P11.3 [S] Mirino e nome del soggetto sull'immagine.** Logica fatta 22/10 (`TargetMarker`, 13 test; GMST da Meeus, epoca J2000 assunta [NV]). **Ancora da fare:** disegno WPF nella vista Live, scelta della sorgente mount/sequenza, widget nel timelapse, render 1280×800 e 1920×1080. Proietta le coordinate puntate sul frame con il modello fisheye e la calibrazione ottica già presenti (`AllSky.Sky`); disegna un mirino con il nome nella vista Live e, se l'utente lo spunta, come widget nella maschera informativa del timelapse (`applyToTimelapse`). Gestisci: calibrazione assente (messaggio con invito a calibrare), oggetto sotto l'orizzonte o fuori dal cerchio, differenza tra **punto puntato dal mount** e **soggetto in sequenza** (due marcatori distinti, l'utente sceglie quali mostrare), epoca delle coordinate (precessione, da P11.0), ritardo di lettura. **Parallasse:** la all-sky sta vicino al telescopio, quindi per oggetti celesti la direzione coincide; annotalo e non correggere.
  - *Test:* coordinate note → pixel attesi con tolleranza dichiarata; casi limite; render del mirino a 1280×800 e 1920×1080 in modo normale e rosso notte.
  - *Dip.:* P11.2, P3.1 e P3.2 (modello fisheye e calibrazione), P1.4 (vista Live), P2.4 (overlay timelapse).
- [~] **P11.4 [H] Grafico dell'altezza del soggetto.** Dati fatti 22/10 (`TargetCurve`, transito, ore sopra soglia, Luna; 21 casi di test). **Ancora da fare:** disegno, cursore «adesso», orizzonte configurabile, confronto con fonte esterna (scelta di Robert). Curva altezza/ora della notte con fasce dei crepuscoli, altezza della Luna, transito al meridiano, linea dell'orizzonte utile configurabile, cursore «adesso» e ore rimaste sopra una soglia scelta dall'utente (nessun valore imposto di default senza giustificarlo). Disegno con le primitive WPF già usate, senza librerie nuove. Visibile nella vista Stanotte (P1.11) e nella webapp.
  - *Test:* curva di un oggetto noto contro un riferimento esterno [NV: indicare la fonte usata]; epoca delle coordinate; cambio soggetto a notte in corso.
  - *Dip.:* P11.2, `AllSky.Astro`.
- [~] **P11.5 [S] Distanza dalla Luna.** Logica fatta 22/10 (`MoonSeparation`, 13 test). La Luna è in alt/az da `AllSky.Astro`, non in RA/Dec: la separazione è calcolata tra le direzioni, errore dichiarato 1° [NV]. **Ancora da fare:** UI, avviso configurabile in Stanotte, verifica contro un riferimento esterno. Separazione angolare tra il soggetto e la Luna (altezza, fase e illuminazione incluse) nel grafico, in Stanotte e come widget. Avviso configurabile (soglie scelte dall'utente, nessun valore imposto di default senza fonte). Dichiara l'**accuratezza** del calcolo della posizione lunare del modulo `AllSky.Astro` misurandola contro un riferimento esterno [NV].
  - *Test:* casi con separazioni note, Luna sotto l'orizzonte, soggetto a pochi gradi.
  - *Dip.:* P11.2, P11.4.
- [~] **P11.6 [H] Stato di park.** Logica fatta 22/10 (`ParkState`, transizioni, 27 casi di test; nome `AtPark` [NV]). **Ancora da fare:** indicatore in Stanotte e barra di stato, campo nel sidecar, widget della maschera. Indicatore «Parcheggiato / In inseguimento / In slew / Non collegato» in Stanotte, barra di stato, sidecar del frame (`nina.parked`) e widget della maschera. Ogni cambio di stato entra nella cronologia di P11.7.
  - *Test:* transizioni, valore sconosciuto, NINA scollegato.
  - *Dip.:* P11.2.
- [~] **P11.7 [S] Cronologia eventi NINA collegata ai frame.** Logica fatta 22/10 (`NinaEvent`, `NinaEventLog` JSONL per notte, collegamento per timestamp, pulizia messaggi, correzione orologio, parser del log **ipotetico** [NV]; 180 test verdi nel modulo). **Ancora da fare:** client WebSocket e lettura incrementale dei log, misura dello scarto d'orologio col server NINA, collegamento ai sidecar e a NightArchive. Acquisisci gli eventi (dal WebSocket o dalla lettura incrementale dei log, come da P11.0) e salvali in `nights/<notte>/nina-events.jsonl` con **timestamp UTC**, livello, sorgente, messaggio, categoria dedotta (messa a fuoco, meridian flip, dither, guida, errore, slew, cambio filtro: solo se P11.0 le conferma). Collegamento a frame e timelapse **per timestamp**: dato un frame, gli eventi entro ±N secondi; dato un evento, il frame più vicino. **Scarto d'orologio:** se NINA gira su un altro PC, misura e mostra l'offset (con l'orario del server NINA ricevuto dall'API) e permetti una correzione manuale. **Privacy:** passa i messaggi da `LogSecretMask` e rimuovi percorsi, indirizzi IP e nomi utente; nessuna pubblicazione di default.
  - *Test:* log sintetici (righe di vario formato, righe spezzate, rotazione dei file), ricostruzione dell'indice, offset di orologio, nessun segreto.
  - *Dip.:* P11.2, P2.1 (sidecar per frame).
- [ ] **P11.8 [H] Vista «Diagnostica» (visiva).** Nel player della notte e nel dettaglio frame, pannello con gli eventi NINA attorno al frame mostrato; clic su un evento = salta al frame; clic su un frame = elenca gli eventi vicini; **marcatori sulla linea temporale** della notte per tipo (filtri). Opzione per mostrare nel timelapse esportato l'ultimo evento come widget della maschera (spenta). Ricerca di testo.
  - *Test:* navigazione evento↔frame, filtri, render in rosso notte, notti senza eventi NINA.
  - *Dip.:* P11.7, P1.6 (vista Notti).
- [~] **P11.9 [S] Campo inquadrato del telescopio e controllo dell'allineamento.** Logica fatta 22/10 (`FieldOfView`, 21 test; focale e sensore solo dall'utente, nessun default). **Ancora da fare:** disegno del contorno e del centro, input nella pagina NINA, widget nel timelapse, render in rosso notte. Disegna sulla all-sky il **rettangolo del campo** del telescopio (focale, dimensioni del sensore o pixel, angolo del rotatore). I dati di NINA su camera, focale e rotatore sono **[NV]** (P11.0): se l'API non li dà, l'utente li inserisce a mano nella pagina NINA. Il rettangolo è un widget attivabile, anche nel timelapse. **Controllo dell'allineamento (sperimentale):** confronta il punto puntato dal mount con la posizione del telescopio visibile nell'immagine (Robert tiene la all-sky sotto il telescopio) e mostra lo scarto, utile a validare la calibrazione ottica; non correggere nulla in automatico. L'esito va dichiarato **[NV]** finché non c'è una prova su immagini reali, con soglie annotate dopo la misura.
  - *Test:* campo atteso per focale e sensore noti, rotazione, oggetto fuori dal cerchio; render in modo normale e rosso notte.
  - *Dip.:* P11.3.
- [~] **P11.10 [H] Capitoli automatici su YouTube.** Logica fatta (`YouTubeChapters.cs`, 19 test; regole YouTube [NV]; segnaposto `{capitoli}` da collegare al parser di P7.1 prima dell'integrazione; scala tempi valida solo con un frame al secondo di cattura, da verificare sull'archivio reale). Dagli eventi NINA (cambio soggetto, messa a fuoco, meridian flip, e le altre categorie confermate da P11.0) costruisci l'elenco dei capitoli nella descrizione del video, con i tempi calcolati dalla posizione del frame nel timelapse (indice del frame diviso gli fps). Nuovo segnaposto del modello di titolo e descrizione di P7.1 (per esempio `{capitoli}`). Le **regole di YouTube sui capitoli** (formato dei tempi, primo capitolo a 0:00, numero e durata minimi) vanno lette sulla guida ufficiale e annotate **[NV]**: non applicarle a memoria. Se mancano i requisiti, nessun capitolo.
  - *Test:* eventi sintetici → elenco atteso; troppo pochi eventi; tempi oltre la durata; nessun dato privato (percorsi, indirizzi) nei capitoli.
  - *Dip.:* P11.7, P7.1, P7.3.
- [ ] **P11.11 [S] Allerta di cielo che peggiora, verso NINA.** Calcola la **tendenza** dell'indice di cielo sereno (P9.2 e P9.5) e dai un avviso configurabile quando il cielo sta peggiorando; con il SafetyMonitor Alpaca di P9.5 NINA può riceverlo come condizione «non sicuro». Scrivi una guida passo-passo per collegare il SafetyMonitor di All Sky Brain a NINA. Verifica sulla documentazione di NINA come si aggiunge un monitor di sicurezza e annotalo **[NV]**. Soglie e finestra di tendenza scelte dall'utente, con un valore predefinito **giustificato** da una misura sulla notte simulata o da una fonte. L'avviso passa anche da Telegram (P15) se attivo.
  - *Test:* serie sintetiche di nuvolosità crescente, stabile e oscillante (niente falsi allarmi per una nuvola breve); unità del SafetyMonitor su loopback.
  - *Dip.:* P9.2, P9.5, P11.2.
- [ ] **P11.12 [S] Rugiada e condensa sulla cupola (sperimentale).** Misura la perdita di nitidezza in una zona fissa dell'immagine (per esempio il bordo della cupola o l'orizzonte dentro la maschera) e rilevane il calo rispetto alla base della notte. Se è disponibile un sensore ASCOM ObservingConditions (P6.1–P6.3), confronta temperatura e punto di rugiada come controllo incrociato. Lo stato di park di NINA è solo un dato di contesto da mostrare, non una condizione. Tutto è **[NV]**: la soglia si ricava da immagini reali con e senza condensa, quindi richiede dati di Robert; fino ad allora la funzione resta spenta e marcata come sperimentale.
  - *Test:* immagini sintetiche sfocate con grado crescente; nessun falso allarme al passaggio giorno-notte.
  - *Dip.:* P2.1, P6.1–P6.3 (meteo).
- [ ] **P11.13 [S] Verifica P11.** Test con server NINA finto; controllo che con `nina.enabled=false` non parta nessuna connessione né thread; elenco «Da provare con NINA reale» (campi reali, epoca delle coordinate, formato dei log, scarto d'orologio, allineamento del campo, SafetyMonitor in NINA) in NOTES.md.
  - *Dip.:* P11.1–P11.12.

### P12 — Monitoraggio pubblico sul sito (sola lettura, nessun login, nessuna porta aperta)

> **Decisione di Robert (22/10/2026):** al momento interessa **solo il monitoraggio**. **Zero interazione**: tutti sono visitatori, **nessun login**, nessun comando. Sulla scelta tecnica Robert ha delegato: è adottata la **strada A** (sotto).
> **Strada A:** l'app **invia in uscita** (HTTPS, protocollo Uplink) uno stato e l'immagine al sito di Robert, e il sito li mostra. Nessuna porta si apre sull'osservatorio e non esiste alcun canale per scrivere sul PC: i vincoli di §0 restano **invariati**.
> **Strade scartate:** B, tunnel di terzi (aggiunge un fornitore esterno con termini d'uso per immagini e video **[NV]**; per Cloudflare lo streaming video è vietato nei piani gratuiti **[V]** dal piano v1) e C, porta aperta sul router con DDNS (superficie d'attacco diretta, nessun vantaggio per il solo monitoraggio).
> Se in futuro servirà l'interazione, sarà una fase nuova con un modello di sicurezza a parte (login, ruoli, coda comandi): **non** è in questa fase.

- [x] **P12.0 [S] Registra la decisione.** (fatto 23/10/2026: `docs/ACCESSO-REMOTO.md`) Scrivi `docs/ACCESSO-REMOTO.md` con la decisione di Robert, la strada A, le due alternative scartate e il perché, e l'elenco di ciò che **non** esiste (login, comandi, porte). Nessuna modifica ai vincoli di §0.
  - *Dip.:* nessuna.
- [~] **P12.1 [S] Dati pubblici e privacy.** Logica fatta 23/10 (sezione `monitor` spenta di default con 10 voci; `MonitorSnapshot` con soli campi ammessi e `MonitorSnapshotBuilder`; 43 test Publish, 8 Core; nessuna posizione nel payload). **Ancora da fare:** interruttori e anteprima «cosa vede il visitatore» nell'app; le liste chiuse di fasi e di stati di park sono copie (Publish non dipende da Astro né da Nina) e nessun test le allinea. Elenco **chiuso** di ciò che può uscire: immagine all-sky (con mirino e maschera se attivi), fase della notte, altezza di Sole e Luna, soggetto e stato di park di NINA, grafico altezza e distanza dalla Luna, ultimo timelapse, keogram e startrails, ora dell'ultimo aggiornamento. **Mai pubblicati:** posizione esatta dell'osservatorio, percorsi, indirizzi IP, log di NINA, token. Interruttore generale «Pubblica il monitoraggio» **spento di default**, un interruttore per ogni voce, e un'anteprima nell'app di «cosa vede il visitatore». Ricorda che la lettura è pubblica per tutti: le cose sensibili non vanno nell'elenco.
  - *Test:* il payload non contiene mai campi fuori dall'elenco (test sul tipo e su un JSON di prova); posizione assente o arrotondata come deciso.
  - *Dip.:* P12.0, P11.x per le voci NINA.
- [x] **P12.2 [S] Richiesta di contratto Uplink (solo documento).** (fatto 23/10/2026: `docs/ralph/UPLINK-MONITOR-RICHIESTA.md`; limiti Vercel e Neon letti sulla documentazione corrente. Esito principale: la frequenza di 60 s indicata nel brief allsky non regge il limite di 2 000 operazioni avanzate di Blob su Hobby; intervallo consigliato 15 min, di notte; decisioni aperte per Robert in §9 del documento.) Con le istruzioni di `uplink-expert` (procedura «preparare una richiesta di modifica del contratto»), scrivi `docs/ralph/UPLINK-MONITOR-RICHIESTA.md`: nuovi dati di stato e miniature di prodotti, compatibile o no (preferibile campo facoltativo o nuova rotta), fixture proposte, limiti di dimensione e di frequenza, impatto per ogni repository, ordine di rilascio (fixture e sito prima, app dopo). Stima i **volumi** (richieste e scritture al mese) contro i limiti di Vercel Hobby, Blob e Neon letti sulla documentazione corrente **[NV]**; l'immagine all-sky usa già il limite di una nuova immagine ogni 10 secondi **[V]** (UPLINK-SPEC §4.7). **Non** modificare né le fixture né il sito: lo fa Robert con il suo team.
  - *Dip.:* P12.1.
- [ ] **P12.3 [H] Pubblicazione dello stato dall'app.** Compositore dello snapshot (secondo l'elenco di P12.1 e le fixture concordate), invio nella coda Uplink già presente con backoff e idempotenza, frequenza configurabile (valore predefinito scelto dopo la stima dei volumi di P12.2), immagine pubblicata con **overlay** quando `overlay.applyToPublish` è attivo (oggi il flag esiste ma non viene letto: va collegato qui, con mirino e widget scelti dall'utente). Un errore di rete non deve fermare né rallentare la cattura.
  - *Test:* server HTTP finto, rete assente, risposta 429 con `Retry-After`, doppio invio, nessun segreto nei log.
  - *Dip.:* P12.2 e fixture concordate, P2.4.
- [ ] **P12.4 [S] Lato sito (repository `sito`, solo se Robert lo autorizza).** Pagina pubblica di monitoraggio in sola lettura e route di ricezione conformi alle fixture. Si lavora su un **branch dedicato**, mai sul ramo di produzione senza un via esplicito, e seguendo le regole del sito (`AGENTS.md` non si tocca; leggere `node_modules/next/dist/docs/` prima di modificare routing o cache; `graphify update .` dopo le modifiche; testi editoriali con le skill robertizzazione e humanizer). Il codice lo scrivono agenti Haiku con i criteri qui, rivisti dal loop. Verifica con il kit `scripts/uplink-conformance.mjs --all-fixtures`.
  - *Test:* conformità sulle fixture, pagina a 390 px senza scorrimento orizzontale, visitatore senza strumenti di scrittura, richiesta di scrittura senza Bearer rifiutata con 401.
  - *Dip.:* P12.2.
- [ ] **P12.5 [S] Verifica indipendente.** Un agente che non ha scritto il codice controlla: con la pubblicazione spenta nessuna richiesta in uscita verso il sito; con la pubblicazione accesa nessuna **porta in ascolto** del processo; nessun dato fuori dall'elenco di P12.1; nessun segreto nei log né nei payload; kit di conformità in sola lettura (senza `--write`, che lascerebbe un evento di prova pubblico per 12 ore **[V]**). Esito in NOTES.md con [V]/[NV].
  - *Dip.:* P12.3, P12.4.

### P13 — Studio di montaggio (materiale vecchio, editing, esportazione)

Obiettivo: partire da **archivi già esistenti** (cartelle di immagini o video della all-sky) e produrre timelapse, keogram e startrails, salvandoli in locale o caricandoli su YouTube. Le operazioni sono **non distruttive** (i file originali non si toccano) e i lavori pesanti girano a **bassa priorità, senza rallentare la cattura**.

- [x] **P13.1 [S] Progetti e importazione.** (fatto 23/10/2026, solo logica: `src/AllSky.Studio`, 80 test; ora ambigua in Europe/Rome = 02:30 del 25/10 e non 01:30; estrazione video provata solo con un esecutore finto, ffmpeg reale non verificato [NV]; EXIF dei PNG non letto, quei file vanno su nome o data del file.) Formato di progetto JSON versionato (`.asbproject`: sorgenti, elenco di operazioni, parametri). Sorgenti supportate in v1 [S-scelta]: notti dell'archivio di All Sky Brain, **cartelle di JPEG/PNG** di altri programmi (data e ora da EXIF, da un modello del nome file scelto dall'utente, oppure dalla data del file, con anteprima del risultato) e **video** (frame estratti con `ffmpeg` esterno con le stesse regole di licenza del modulo Timelapse). RAW e FITS: solo se esiste una libreria con licenza permissiva verificata **[NV]**, altrimenti fuori da v1 e annotato.
  - *Test:* cartelle sintetiche con nomi e date in vari formati, file corrotti, ordinamento, fusi orari, video breve.
  - *Dip.:* P2.1.
- [~] **P13.2 [H] Operazioni di montaggio.** (logica fatta 23/10/2026 in `src/AllSky.Studio/{Operations,Montage}`, 127 test nuovi: parser tipizzato, pianificatore deterministico con impronta, ritaglio/rotazione/specchio su RGB8, schede titolo e dissolvenze come descrittori. **Non fatto:** rilevazione nuvole (avviso esplicito), infoMask e colorCorrection (solo parser), UI e rendering. Scelte del subagente da confermare: speed.factor 0,01..100, rotazione 90 = oraria, position/kind obbligatori, MAD senza 1,4826.) Intervallo di tempo, rimozione manuale e automatica di frame (esposizione anomala, nuvole, frame vuoti), velocità e fps, ritaglio, rotazione e specchio, correzione del colore (P14), maschera informativa (riusa `AllSky.InfoOverlay`: se mancano i sidecar l'app chiede coordinate e calcola Sole e Luna dall'orario; satelliti solo con dati orbitali disponibili), schede titolo di testo, dissolvenze. Ogni operazione ha anteprima su un frame.
  - *Test:* ogni operazione su sequenze sintetiche con risultato atteso; il progetto salvato e ricaricato dà lo stesso output.
  - *Dip.:* P13.1, P14.2.
- [~] **P13.3 [S] Prodotti da serie arbitrarie.** (logica fatta 23/10/2026 in `src/AllSky.Studio/Products`, 16 test: keogram su cerchio (centro e raggio, modello ottico o inscritto con avviso), startrails, anteprima per sottocampionamento, annullamento senza buffer parziali, decoder iniettato `IFrameDecoder`. **Non fatto:** decoder JPEG reale (WPF su Windows), selettore del cerchio, timelapse da serie, coda di rendering. Coordinate del cerchio riferite all'immagine dopo le operazioni sui pixel se attive: interpretazione del subagente. Nessun frame reale provato.) Timelapse (riusa `AllSky.Timelapse`), **keogram** e **startrails** da qualsiasi serie: per il keogram servono centro e raggio del cerchio, scelti con un selettore sull'immagine o dal modello ottico se presente. Anteprima veloce a risoluzione ridotta, rendering finale in coda.
  - *Test:* serie sintetica con cerchio noto; keogram atteso; annullamento a metà.
  - *Dip.:* P13.2.
- [ ] **P13.4 [H] Esportazione.** Destinazioni: **file locale** (cartella, modello del nome, preset di risoluzione e qualità), **YouTube** (finestra con titolo, descrizione e privacy da modello, riuso del flusso OAuth e dell'upload di P7, pulsante «Carica ora»), **Telegram** (P15). Coda lavori con avanzamento, annullamento e ripresa.
  - *Test:* destinazioni finte, annullamento, errore di rete a metà, nessun segreto nei log.
  - *Dip.:* P13.3, P7.2, P15.2.
- [ ] **P13.5 [H] Vista «Studio».** Selezione sorgente, timeline minimale con anteprima a scorrimento, pannello operazioni, coda lavori. Notte rossa, IT/EN, stati vuoto/caricamento/errore (§5).
  - *Test:* rendering come per le altre viste; flusso completo con sorgente sintetica.
  - *Dip.:* P13.2–P13.4.
- [ ] **P13.6 [S] Verifica P13.** Misura l'impatto sulla cattura: con un montaggio in corso, la latenza del ciclo di cattura non deve peggiorare oltre una soglia dichiarata e misurata (annotare i numeri). Prova con una vera cartella di immagini vecchie di Robert ([NV] finché non c'è).
  - *Dip.:* P13.1–P13.5.

### P14 — Bilanciamento colore del video

Premessa: Robert segnala che il colore del timelapse è poco curato. **Prima si misura, poi si corregge**: non assumere la causa.

- [x] **P14.1 [S] Diagnosi misurata.** (fatto 23/10/2026 sulla notte SIMULATA 2026-10-22: `src/AllSky.ColorMetrics` + CLI, 37 test, report `docs/ralph/COLORE-DIAGNOSI.md`. Numeri: flicker 1,53 % (soglia provvisoria 1 %), bande ai cambi di esposizione 2,0 % contro 0,18 % altrove (rapporto 11), deriva cromatica sotto soglia; video h264 con matrice `bt470bg` su 1280x960 e trasferimento/primari non dichiarati. I valori non dicono nulla sul colore reale: vanno ripetuti sui frame veri di Robert [NV]; le soglie sono provvisorie; il rivelatore di stelle non separa le stelle dal cielo luminoso di giorno e al crepuscolo.) Leggi la catena attuale (`AllSky.Processing`, `AllSky.Imaging`, `AllSky.Timelapse`) e scrivi uno strumento di metriche: **flicker di luminanza** (deviazione della media per frame dopo aver tolto il trend lento), **deriva cromatica** del fondo cielo (R/G e B/G nel tempo), **continuità giorno/crepuscolo/notte**, bande agli scatti di esposizione automatica, colore delle stelle. Applicalo alla notte simulata (cartella `smoke2`) e, appena disponibili, a frame reali di Robert. Elenca i difetti **con i numeri**. Verifica anche cosa fa l'encoder dei metadati colore del video (matrice, intervallo, formato dei pixel) con `ffprobe` [NV].
  - *Test:* le metriche danno valori noti su sequenze sintetiche con difetti iniettati.
  - *Dip.:* P2.x, notte simulata di P8.2.
- [~] **P14.2 [S] Correzioni.** (prima parte fatta 23/10/2026: smoothing a costante di tempo legata a dt reale e altezza del Sole, compensazione dei cambi di esposizione/guadagno sullo stretch, preset naturale/neutro/profondo, tutto in `color.corrections` e spento di default; 2452 test verdi, output identico con la config di default. Misura su scena sintetica proporzionale: salto medio ai cambi 0,83 spente contro 0,00002 accese; scenario estremo, non rappresenta il cielo reale. **Limite aperto:** con un bias di 800 ADU che non scala con l'esposizione la compensazione non basta (0,559 contro 0,759): ipotesi non verificata, il bias entra nella riscalatura; decidere prima di attivarla su dati reali. **[NV]** gain ZWO = 0,1 dB/unità. **Restano:** alte luci e nuvole di giorno, neutralizzazione del fondo con le stelle, rumore temporale, misura sulla notte simulata smoke2.) In base a P14.1: livellamento temporale dei guadagni del bilanciamento e dello stretch con costanti di tempo legate all'altezza del Sole; compensazione dei cambi di esposizione e guadagno (i valori sono noti dai sidecar); riduzione del flicker residuo senza cancellare meteore e stelle; gestione di alte luci e nuvole di giorno; trasferimento morbido giorno→notte; neutralizzazione del fondo cielo con le stelle come controllo; rumore temporale opzionale solo dove non c'è movimento. Preset **[S-scelta]** «Naturale», «Neutro», «Profondo». Ogni correzione ha un parametro e si disattiva.
  - *Test:* le metriche di P14.1 migliorano di una soglia dichiarata sulle sequenze sintetiche e sulla notte simulata; nessuna regressione sulle immagini singole (golden test esistenti).
  - *Dip.:* P14.1.
- [x] **P14.3 [H] Metadati colore e codifica.** (fatto 23/10/2026 in `AllSky.Timelapse/FfmpegArguments.cs`, 54 test: conversione BT.709 a intervallo limitato nel filtro scale più tag matrice/primari/trasferimento/intervallo e filtro `h264_metadata` per gli encoder H.264. **Correzione alla lettura di P14.1:** misurato con ffmpeg 6.1.1 il file di prima era già coerente (matrice 601 dichiarata e usata); il miglioramento è la completezza dei tag (mancavano primari, trasferimento e box colr). Errore medio per canale su 10 colori piatti: prima 0,53, dopo 0,93 su 255 (costo dell'arrotondamento del passaggio a 709). Scegliere 709 per un video 1280x960 è convenzione, non una misura. **[NV]:** `h264_mf` su Windows, build ffmpeg di Windows con `h264_metadata`, browser e YouTube reali.) Il video esce con matrice, intervallo e formato dei pixel dichiarati in modo coerente con la sorgente (es. BT.709, formato 4:2:0, dichiarati nel contenitore) così che browser, YouTube e lettori mostrino gli stessi colori. Verifica sulla documentazione di FFmpeg e sul comportamento dell'encoder `h264_mf` su Windows **[NV]**.
  - *Test:* decodifica di un frame e confronto con la sorgente entro una differenza di colore dichiarata (metrica annotata); `ffprobe` mostra i metadati attesi.
  - *Dip.:* P14.1.
- [ ] **P14.4 [H] Pannello «Colore del timelapse».** Confronto prima/dopo su frame scelti, cursori (forza dello smoothing, temperatura, tinta, saturazione, rimozione del gradiente), preset salvabili, usato anche dallo Studio (P13).
  - *Test:* render in modo normale e rosso notte; i preset si salvano e si ricaricano.
  - *Dip.:* P14.2.

### P15 — Invio a un bot Telegram

Estende P9.7 (report e allerte): qui si inviano **i prodotti** (timelapse, keogram, startrails, clip di meteore e transiti). Se P9.7 non è ancora fatto, i due task condividono il client Telegram.

- [~] **P15.1 [S] Fatti su Telegram.** (letto 23/10/2026: `docs/TELEGRAM-API.md`; verificati limite di 50 MB per i bot, 20 MB in download, 2000 MB con server locale, frequenza e `getUpdates`. **Ancora [NV]:** limiti di foto, video e testo e risposta 429 con `retry_after`: lo strumento di lettura non arriva ai metodi di invio; da rileggere prima di P15.2.) Leggi la documentazione ufficiale della Bot API e annota con fonte: metodi per foto, video, documenti e messaggi; **limiti di dimensione** per ogni tipo e per l'invio da parte dei bot; `getUpdates` in long polling (richieste solo in uscita); server Bot API locale e suoi limiti; limiti di frequenza. Tutto è [NV] finché non è letto.
  - *Dip.:* nessuna.
- [ ] **P15.2 [H] Invio dei prodotti.** Configurazione (token del bot e chat autorizzate, cifrati con DPAPI, mai nei log), pagina «Integrazioni → Telegram» con prova di invio, invio **automatico** a fine notte dei prodotti scelti e invio **manuale** da Eventi, Notti e Studio. Se il file supera il limite: ricodifica a una dimensione bersaglio, oppure invia keogram e startrails e un link al video, secondo la scelta dell'utente. Coda con ritentativi e limite giornaliero.
  - *Test:* handler HTTP finto, file sopra e sotto il limite, errore 429, nessun segreto nei log.
  - *Dip.:* P15.1, P9.7 (client condiviso), P5.3.
- [ ] **P15.3 [H] Comandi di sola lettura (facoltativo).** Con `getUpdates` in long polling (nessuna porta aperta) il bot risponde a `/stato`, `/ultimo` (ultimo frame), `/keogram`, solo alle chat autorizzate; nessun comando che modifichi l'osservatorio.
  - *Test:* chat non autorizzata ignorata, comandi sconosciuti, flood.
  - *Dip.:* P15.2.

### P16 — Verifica finale e rilascio 0.5.0
- [ ] **P16.1 [S] Verifica finale fase 3.** Come P8.2, più: con NINA, Telegram e pubblicazione del monitoraggio **spenti** nessuna porta in ascolto e nessuna connessione in uscita oltre a quelle già previste; con il monitoraggio acceso, ancora nessuna porta in ascolto; verifica P12.5 verde; test del montaggio con serie sintetica; riepilogo per Robert in NOTES.md con la sezione **«Da provare con NINA reale e sul PC con la ZWO»**.
- [ ] **P16.2 [S] Rilascio automatico 0.5.0.** Stesso cancello e stessi passi di P8.4, con la versione 0.5.0. Il cancello include P16.1 `[x]`. Il lato sito (P12.4) è un rilascio separato di Robert: non fa parte della release dell'app.

### P17 — Verifica visiva e strumenti per gli agenti (senza il PC di Robert)

> Obiettivo: poter concludere il software senza che Robert debba fare verifiche a mano. Dove possibile la verifica gira in CI Windows e gli agenti leggono i risultati dagli artifact; il server MCP locale serve solo quando il PC è collegato e resta opt-in.

- [x] **P17.1 [S] Immagini dei test UI come artifact.** Il workflow `build-windows` carica i PNG di `TestResults/shots` (artifact `ui-render-shots`). Verificato 22/10 sulla run 39791593321: l'artifact esiste (1,8 MB). **Bloccante:** il download dal container è rifiutato dal proxy di policy (403 sull'host di storage degli artifact di GitHub); non si aggira. Vedi P17.2.
- [ ] **P17.2 [S] Revisione visiva dagli artifact.** **Bloccata** dal 22/10: il container non scarica gli artifact (403 di policy). Sbloccabile in uno di questi modi, scelta di Robert 22/10: **(b) PC collegato** (P17.5). Alternativa non scelta: (a) la CI pubblica i PNG su un ramo dedicato via API GitHub (richiede di cambiare i permessi del workflow da sola lettura a scrittura su un ramo, con una decisione esplicita); (b) il PC collegato alla chat (P17.5), **scelto**: in attesa che Robert colleghi la chat dall'app desktop. Un agente scarica `ui-render-shots` con `gh`, guarda i PNG e riporta per ogni vista: elementi fuori schermo, testo tagliato, contrasto in rosso notte, mirino nel punto atteso. Esito in NOTES.md con [V]/[NV].
  - *Dip.:* P17.1, primo run CI con gli screenshot.
- [ ] **P17.3 [H] Render delle viste NINA nei test.** Mirino, grafico, distanza Luna, stato di park e vista Diagnostica (P11.8) rendono un PNG per tema (normale e rosso notte) con dati sintetici fissi, così P17.2 li può guardare.
  - *Test:* ogni vista produce il PNG; nessun crash con `LatestNinaState` nullo.
  - *Dip.:* P11.3–P11.6, P11.8.
- [ ] **P17.4 [H] Server MCP locale di sola lettura (opt-in).** Nel processo dell'app, solo su `127.0.0.1`, disattivato di default, con tool: stato del motore, `LatestNinaState`, posizione attesa del mirino in pixel, PNG di una vista su richiesta. Nessuna scrittura, nessun comando. Il server non entra nel cammino di cattura né apre porte sulla LAN.
  - *Test:* tool elencati; con l'opzione spenta nessun listener; nessuna scrittura accettata.
  - *Dip.:* P11.2 (fatto), P17.3.
- [ ] **P17.5 [S] Collegamento al PC (solo quando serve).** Il server MCP è raggiungibile dalla sessione solo se la chat è collegata al PC tramite l'app desktop. Documentare il passo per Robert in `docs/AGENTI.md`. Senza collegamento, P17.1–P17.3 bastano per la verifica visiva.
  - *Dip.:* P17.4.

### Idee valutate
- Implementate nel piano: campo inquadrato (P11.9), capitoli YouTube (P11.10), allerta di cielo che peggiora con SafetyMonitor per NINA (P11.11), condensa sulla cupola, sperimentale (P11.12).
- **Scartata da Robert:** indice di efficienza della notte (ore di cielo utile contro ore di integrazione).

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
