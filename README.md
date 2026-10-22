# All Sky Brain

All Sky Brain è un'applicazione per Windows che controlla una camera all-sky. Cattura le immagini del cielo, le elabora e le archivia notte per notte. Dalle immagini crea timelapse, keogram e startrails. Mostra lo stato della notte in corso, i passaggi dei satelliti e, se attivati, le meteore, i bolidi e i transiti rilevati.

> Questo documento descrive la versione 0.3.0, che è in preparazione. Fino alla pubblicazione della 0.3.0, valgono le istruzioni per lo zip della sezione «Installazione».

## Cosa fa

- **Cattura continua** dalla camera, con esposizione adattata a giorno, crepuscolo e notte.
- **Elaborazione** di ogni frame: bilanciamento del bianco, rimozione del gradiente di cielo, stretch e scritta opzionale (ora, temperatura del sensore).
- **Archivio per notte**: una notte va da mezzogiorno locale a mezzogiorno locale.
- **Prodotti**: timelapse MP4, keogram e startrails per ogni notte.
- **Applicazione desktop** con finestra e icona nell'area di notifica (vedi «Viste dell'app»).
- **Maschera informativa** sui bordi del timelapse, con widget attivabili (vedi «Maschera sul timelapse»).
- **Previsione dei passaggi** di ISS e di altri satelliti, facoltativa (vedi «Satelliti e transiti»).
- **Rilevamento di meteore, bolidi e transiti**, facoltativo (vedi «Meteore e bolidi»).
- **Caricamento del timelapse su YouTube**, facoltativo (vedi «YouTube»).
- **Camere**: simulatore incluso, camere ASCOM (driver COM) e ASCOM Alpaca raggiungibili in rete.
- **Pubblicazione periodica** (facoltativa) dell'ultima immagine verso un server web. Vedi [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md).

## Requisiti

- Windows 10 o Windows 11, 64 bit (x64).
- **ffmpeg** per il timelapse e per le clip degli eventi. Va copiato in `tools\ffmpeg\ffmpeg.exe` accanto ad `AllSkyBrain.exe`, oppure installato e raggiungibile dal `PATH` di Windows.
- Per le camere ASCOM: il driver ASCOM della camera installato sul computer.

## Installazione

### Con l'installer (dalla 0.3.0)

1. Aprire la pagina **Releases** del repository e scaricare il file di installazione `AllSkyBrain-win-Setup.exe` dell'ultima release stabile.
2. Eseguire il file e seguire la procedura.
3. Avviare All Sky Brain. Compare l'icona nell'area di notifica (accanto all'orologio).

L'installer non è firmato digitalmente. Windows SmartScreen può mostrare un avviso: scegliere **Ulteriori informazioni** e poi **Esegui comunque**.

### Con lo zip (copia portatile)

1. Scaricare `AllSkyBrain-win-x64.zip` e `AllSkyBrain-win-x64.zip.sha256` dalla pagina **Releases**.
2. Verificare il file. In PowerShell, dalla cartella del download:

   ```powershell
   Get-FileHash .\AllSkyBrain-win-x64.zip -Algorithm SHA256
   Get-Content .\AllSkyBrain-win-x64.zip.sha256
   ```

   Il valore di `Hash` deve essere uguale al primo campo del file `.sha256`.
3. Estrarre lo zip in una cartella dell'utente, per esempio `C:\AllSkyBrain`.
4. Avviare `AllSkyBrain.exe`.

La copia portatile non riceve aggiornamenti automatici: l'interfaccia lo segnala. Per aggiornarla, scaricare un nuovo zip.

### Aggiornamento automatico

Con l'installazione (installer), All Sky Brain controlla le nuove release nella pagina Releases del repository.

- Il primo controllo parte circa un minuto dopo l'avvio, poi ogni 24 ore.
- Il controllo è attivo per predefinito. Si cambia in **Impostazioni > Aggiornamenti**, con la voce "Controlla e scarica gli aggiornamenti automaticamente", oppure nel file `updates.json` (`autoCheck`).
- Il download avviene in automatico se l'opzione è attiva. L'installazione richiede sempre una conferma.
- Dall'icona nell'area di notifica, la voce **Controlla aggiornamenti** avvia un controllo subito.

## Primo avvio

1. Avviare All Sky Brain. Alla prima apertura viene creato il file di configurazione con i valori predefiniti.
2. In **Impostazioni > Osservatorio** inserire latitudine, longitudine, quota e fuso orario. Servono per sapere quando è giorno e quando è notte, e per calcolare Sole e Luna.
3. In **Camera** cercare la camera e collegarla. Il simulatore serve per una prova senza hardware.
4. Controllare **Live**: l'immagine compare dopo il primo frame.

Dal menu dell'icona nell'area di notifica: **Apri All Sky Brain** apre la finestra; **Avvia con Windows** registra l'app nell'avvio dell'utente corrente; **Esci** chiude l'applicazione.

## Viste dell'app

La barra laterale contiene, dall'alto in basso: **Stanotte**, **Live**, **Camera**, **Notti**, **Eventi**, **Pubblicazione**, **Impostazioni**, **Log**. Con **Ctrl+1** … **Ctrl+7** si aprono le viste Live, Camera, Notti, Pubblicazione, Impostazioni, Log ed Eventi, in quest'ordine. **Stanotte** non ha una scorciatoia numerica: si raggiunge con il mouse, con le frecce o con Tab. **Ctrl+R** attiva o disattiva la notte rossa.

- **Stanotte**: l'immagine del cielo della notte in corso. Una linea temporale da tramonto ad alba, con le fasce dei crepuscoli civile, nautico e astronomico e le curve di altezza di Sole e Luna (altezze geometriche, senza rifrazione atmosferica). Carte con la fase della Luna, il prossimo transito previsto e l'ultima meteora registrata, e lo stato della cattura. Il meteo non è ancora disponibile (vedi «Limiti noti»).
- **Live**: l'ultimo frame, con zoom e spostamento. Commutazione tra immagine grezza ed elaborata, pannello con i dati del frame (esposizione, guadagno, mediana, temperatura). Overlay di griglia alt/az e tracce dei satelliti: disponibili dopo la calibrazione ottica.
- **Camera**: ricerca e collegamento della camera (simulatore, ASCOM, Alpaca), impostazioni di cattura, messaggi d'errore leggibili, calibrazione ottica (dal Sole o inserita a mano).
- **Notti**: elenco delle notti, keogram come barra di scorrimento temporale, startrails, riproduzione del timelapse, pulsante **Rigenera timelapse**.
- **Eventi**: meteore, bolidi e transiti, con filtri per notte e per tipo. Per ogni evento: clip, dati, pulsanti **Conferma** e **Falso positivo** (le etichette servono a tarare le soglie), **Esporta evento** in una cartella compressa.
- **Pubblicazione**: impostazioni Uplink e sezione **YouTube** (collegamento, impostazioni, caricamento manuale).
- **Impostazioni**: osservatorio, lingua (si applica al riavvio), tema, cartella dati (sola lettura), avvio con Windows, aggiornamenti, accesso dal browser, e il pulsante che apre l'editor della maschera.
- **Log**: ultime righe del log, con filtro. Il log maschera i segreti noti prima di mostrarli. Il log non ha livelli di gravità: la gravità è stimata dalle parole nel messaggio.

## Maschera sul timelapse

La maschera è una fascia di informazioni ai bordi del timelapse. Scrive ora, Sole, Luna e altri dati sul frame. Non cambia le immagini archiviate: ogni frame salva un file di metadati (`<frame>.json`) accanto al JPEG, e la maschera si disegna al momento della creazione del timelapse. Con un nuovo layout si rigenera il video dalla vista **Notti**.

Per modificarla: **Impostazioni > Maschera** (editor). L'editor mostra un'anteprima sull'ultimo frame e permette di:

- attivare o disattivare ogni widget, scegliere la posizione (angoli o bande) e l'ordine, e impostare i formati;
- caricare un logo PNG (viene copiato nella cartella `loghi` della cartella dati);
- scegliere se applicare la maschera al **Timelapse**. Le voci "Vista dal vivo" e "Pubblicazione Uplink" sono presenti ma non attive;
- ripristinare i valori predefiniti.

Widget disponibili: **Data e ora** (attivo, in basso a sinistra), **Altezza del Sole** e **Luna: altezza e fase** (attivi, in basso a destra), **Satelliti in transito** (spento), **Meteo** (spento), **Logo**, **Testo libero**, **Esposizione**, **Bussola**. Sono spenti per predefinito tutti tranne i tre indicati come attivi.

Stato dei dati: il widget **Satelliti** e il widget **Meteo** non ricevono ancora dati dall'applicazione, quindi non mostrano valori. Il widget **Bussola** non è ancora disponibile e viene omesso (vedi «Limiti noti»).

Configurazione in `config.json`:

- `overlay.applyToTimelapse`: `true` applica la maschera al timelapse. Predefinito `false`.
- `overlay.layout`: layout della maschera (modalità cerchio/bande, widget, posizioni, formati). `null` usa il layout predefinito.

Nomi, valori e vincoli sono nella sezione «Parametri di config.json» di [docs/FUNZIONI.md](docs/FUNZIONI.md).

## Satelliti e transiti

La previsione dei passaggi è **spenta per predefinito**. Non c'è un interruttore nell'interfaccia: si attiva in `config.json`:

```json
"satellites": { "enabled": true, "targets": [ { "noradId": 25544, "name": "ISS (ZARYA)" } ], "windowHours": 24, "minElevationDeg": 10, "sunMaxAltDeg": -6 }
```

Funzionamento:

- I dati orbitali (elementi TLE) vengono scaricati da CelesTrak, per numero NORAD, al massimo una volta ogni 2 ore per satellite, e salvati nella cartella `satellites`. Se CelesTrak risponde con un errore diverso da "200 OK", i download si fermano per 24 ore e l'applicazione lo segnala.
- Il calcolo usa SGP.NET 1.6.0 (licenza MIT). Sono previsti i passaggi nella finestra indicata (24 ore), con elevazione massima sopra la soglia (10°). Un passaggio è **visibile** solo se il satellite è illuminato dal Sole e il cielo è buio (Sole sotto -6°).
- I passaggi compaiono nella vista **Stanotte**. Le tracce previste compaiono nella vista **Live** per i passaggi in corso o entro 15 minuti.
- Il **satellite di default è la ISS** (NORAD 25544, verificato sul sito di CelesTrak). Si possono aggiungere altri numeri NORAD in `satellites.targets` (massimo 20). Per i numeri a 6 cifre non esistono TLE.

Conferma nelle immagini: con `detection.enabled` attivo (vedi sotto) e una calibrazione ottica accettata, un transito viene confermato se nei frame compare una scia lungo la traccia prevista. Il transito confermato salva una clip, un'immagine composita e i metadati nella vista **Eventi**. Senza calibrazione, il transito non viene confermato. Con `detection.saveAllVisiblePasses` vengono salvati anche i passaggi non confermati, con il verdetto registrato.

Modalità transito (facoltativa, spenta): durante un passaggio previsto riduce l'intervallo tra le pose, per avere più frame nel video. Si attiva con `capture.transitMode.enabled`. L'intervallo non scende sotto il minimo della camera.

## Meteore e bolidi

Il rilevamento è **spento per predefinito**. Si attiva in `config.json`:

```json
"detection": { "enabled": true }
```

(Per le altre chiavi, vedi «Parametri di config.json» in [docs/FUNZIONI.md](docs/FUNZIONI.md).)

Funzionamento:

- Lavora solo di notte, su una versione ridotta del frame (lato lungo 960 px), in un thread separato. Non blocca la cattura. Se il lavoro è in ritardo, il frame più vecchio viene scartato e la cattura prosegue.
- Confronta ogni frame con il fondo medio dei frame precedenti. Cerca scie lunghe e sottili.
- Scarta i frame con variazioni globali alte (nuvole, fulmini, fari) e le scie che si ripetono allineate su più frame (aerei o satelliti).
- Classifica le scie come **meteora** o **bolide**. Il bolide è una scia molto luminosa o molto lunga. Le soglie sono iniziali: vanno tarate sul cielo reale.
- Salva un evento nella vista **Eventi**: ritaglio ingrandito, frame intero con la scia evidenziata, mini-clip dei frame vicini (richiede ffmpeg) e metadati.

Le soglie sono nella sezione `detection.streak` e `detection.transit` di `config.json`. I valori predefiniti non sono stati tarati su immagini reali: le etichette **Conferma** e **Falso positivo** nella vista Eventi servono a valutarle.

## YouTube

Il caricamento automatico è **spento per predefinito**. Si attiva collegando l'account Google dalla sezione **YouTube** della vista **Pubblicazione**.

Le istruzioni passo passo, con la Google Cloud Console (progetto, YouTube Data API v3, schermata di consenso, credenziale "Desktop app", stato Testing o In production), sono in [docs/GUIDA-YOUTUBE.md](docs/GUIDA-YOUTUBE.md).

Funzionamento, con i valori predefiniti:

- Ogni giorno alle **12:30** (ora locale) carica il timelapse dell'ultima notte completata. Una notte viene caricata una sola volta.
- Se il PC era spento all'ora prevista, il caricamento parte all'avvio successivo, entro 12 ore.
- La privacy predefinita è **privato** (`youtube.privacy`). Il titolo e la descrizione derivano da modelli con segnaposto (`{notte:...}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`).
- Un caricamento consuma quota giornaliera di YouTube. In caso di errore temporaneo il caricamento riprova; in caso di errore di autorizzazione chiede di collegare di nuovo l'account.
- Il client secret e i token non stanno in `config.json`: sono nel file `youtube-auth.json`, cifrati con DPAPI.

Con l'app OAuth in stato **Testing** di Google, il collegamento scade dopo 7 giorni. Vedi la guida per passare in **In production**.

## Meteo

Il codice per due sorgenti meteo è presente nel progetto `AllSky.Weather`:

- **ASCOM ObservingConditions**: legge una stazione meteo o un sensore ASCOM installato sul computer.
- **Open-Meteo** (opt-in, spento): dati di previsione online. Il servizio è gratuito per **uso non commerciale**. I dati sono di Open-Meteo, licenza CC BY 4.0, e l'attribuzione va mostrata dove compaiono.

**In questa versione le due sorgenti non sono collegate all'applicazione**: non esiste una chiave di configurazione per attivarle e l'Engine non le utilizza. Di conseguenza il meteo non compare in Stanotte, nella maschera né nel sidecar dei frame. La sezione va letta come stato del codice, non come funzione disponibile.

## Notte rossa

La notte rossa riduce la luce dell'interfaccia per non disturbare l'adattamento al buio. La sua palette usa solo il canale rosso. Si attiva con **Ctrl+R** oppure in **Impostazioni > Tema > Notte rossa**, che salva la scelta in `ui.theme` con valore `"red"`. Non è stata verificata sullo schermo di un PC reale (vedi «Limiti noti»).

## Accesso dal browser

L'interfaccia web è **spenta per predefinito** (`ui.webEnabled: false`). L'applicazione desktop non ne ha bisogno, e con l'interfaccia spenta non si apre alcuna porta HTTP.

Per attivarla: **Impostazioni > Accesso dal browser**, oppure `ui.webEnabled` in `config.json`. La modifica vale dal riavvio. Con l'interfaccia attiva:

- il server ascolta solo su `127.0.0.1` (il computer stesso), sulla porta `ui.port` (predefinito 5810). Se la porta è occupata, si usa la successiva libera;
- l'accesso dalla rete locale (`ui.allowLan`) richiede una password di almeno 8 caratteri, impostata in Impostazioni. La password è salvata come hash PBKDF2. Con HTTP Basic il traffico non è cifrato: usarlo solo su reti di fiducia.

Chi aggiorna dalla versione 0.2.x trova l'interfaccia web **spenta**. Per usarla dal browser, attivarla come sopra.

## Dove stanno i dati

Cartella dati: `%LocalAppData%\AllSky`, cioè `C:\Users\<utente>\AppData\Local\AllSky`. Si cambia con l'argomento `--data-folder <cartella>` o con la variabile d'ambiente `ALLSKY_DATA_FOLDER`.

- `config.json`: configurazione. Un file non valido viene rinominato in `config.json.bad` e si usano i valori predefiniti.
- `nights\`: archivio delle notti (frame, keogram, startrails, timelapse). Gli eventi stanno in `nights\<notte>\events\`.
- `satellites\`: cache dei dati orbitali.
- `loghi\`: logo della maschera.
- `youtube-auth.json`, `updates.json`, `publish.json`, `publish-queue\`: collegamento YouTube (cifrato), impostazioni degli aggiornamenti, pubblicazione Uplink.
- `logs\`: log giornalieri (`allsky-AAAAMMGG.log`), log di avvio (`startup.log`) e log degli arresti anomali (`crash.log`).

Il percorso dell'archivio delle notti è `storage.folder`: al primo avvio coincide con la cartella dati. Un file di configurazione già esistente mantiene il suo valore.

Il file di configurazione è descritto in [docs/FUNZIONI.md](docs/FUNZIONI.md) e le chiavi dell'interfaccia locale in [docs/API.md](docs/API.md).

## Documentazione

- [docs/FUNZIONI.md](docs/FUNZIONI.md): parametri di `config.json` e inventario delle funzioni.
- [docs/API.md](docs/API.md): contratto dell'interfaccia locale e delle chiavi di configurazione.
- [docs/GUIDA-YOUTUBE.md](docs/GUIDA-YOUTUBE.md): collegamento a YouTube, passo passo.
- [docs/YOUTUBE-OAUTH.md](docs/YOUTUBE-OAUTH.md): dettagli tecnici del collegamento OAuth e fonti.
- [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md): pubblicazione Uplink.
- [docs/DESIGN-DESKTOP.md](docs/DESIGN-DESKTOP.md): linee guida grafiche dell'applicazione desktop.

## Limiti noti

Elenco dello stato al momento della stesura. Le fonti sono nel registro di sviluppo (`docs/ralph/NOTES.md`).

- **Soglie non tarate**: il rilevamento di meteore, bolidi e transiti è stato provato solo su immagini sintetiche (scie inserite artificialmente nel cielo). Le soglie vanno tarate su cielo reale. Il confermatore dei transiti può dare falsi positivi quando il bordo di una nuvola è allineato alla traccia prevista.
- **Interfaccia non vista su schermo**: la finestra desktop è stata compilata e testata in automatico, ma non è mai stata avviata e osservata su un PC Windows. Disposizione, icone, focus, barra di stato e notte rossa vanno controllati a mano.
- **Camera ZWO monocromatica**: il driver ASCOM ASICamera2 riporta la camera come `Monochrome` e All Sky Brain la rifiuta. Il modello e il tipo di sensore delle camere ZWO vanno confermati sul PC reale. I limiti di esposizione ZWO (correzione della 0.2.1) non sono stati provati con la camera reale.
- **Meteo non collegato**: vedi la sezione «Meteo». Le sorgenti esistono nel codice, ma non sono attive.
- **Widget non alimentati**: il widget **Satelliti** della maschera non riceve i nomi dei passaggi; il widget **Meteo** non riceve dati; il widget **Bussola** è sempre omesso.
- **Calibrazione ottica**: i punti raccolti dal Sole non vengono salvati al riavvio. La rilevazione del Sole è stata provata su dati sintetici.
- **Posizione della Luna**: l'azimut della Luna nei metadati ha un errore atteso di circa 1°.
- **Tempo di lettura della camera**: il valore di 2 s per la modalità transito è una stima, non una misura.
- **Satelliti**: la propagazione della ISS non è stata confrontata con una fonte esterna. Gli oggetti in orbita profonda (che usano il modello SDP4) non vengono rifiutati. CSS (Tianhe, 48274) e Hubble (20580) non sono inclusi tra i bersagli predefiniti: le pagine CelesTrak con i loro dati non sono state lette.
- **Clip degli eventi**: non sono state provate con un ffmpeg reale su immagini reali.
- **YouTube**:
  - l'ID categoria predefinito `28` non è verificato su una fonte ufficiale;
  - Google può limitare a privato i video di progetti API non verificati: l'effetto reale per questo progetto va controllato al primo caricamento;
  - il flag "destinato ai minori" è sempre `false`.
- **Accesso dal browser**: il listener su `127.0.0.1` senza privilegi amministrativi non è stato provato su Windows. Con la rete locale attiva, la password usa HTTP Basic, non cifrato.
- **Aggiornamenti**: il percorso di installer e aggiornamento automatico non è ancora stato provato su un'installazione reale della 0.3.0.
- **Firma del codice**: il file eseguibile non è firmato. Windows può mostrare un avviso.
- **Licenza**: il repository non contiene ancora un file di licenza. La licenza del codice e del marchio va definita.
- **Messaggi**: i messaggi di validazione del server Uplink sono solo in italiano.
- **Keogram**: dopo un riavvio il keogram può coprire meno frame dell'archivio. Oltre 4096 frame le colonne sono raggruppate a blocchi.
