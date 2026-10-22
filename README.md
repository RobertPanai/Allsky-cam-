# All Sky Brain

All Sky Brain è un'applicazione per Windows che controlla una camera all-sky. Cattura le immagini del cielo, le elabora e le archivia notte per notte. Per ogni notte produce timelapse, keogram e startrails. Mostra l'andamento della notte in corso e, se attivati, i passaggi dei satelliti, le meteore, i bolidi e i transiti rilevati.

Questo documento descrive la **versione 0.3.0**, pubblicata come release stabile il 22/10/2026.

## Cosa fa

- **Cattura continua** dalla camera, con esposizione adattata a giorno, crepuscolo e notte.
- **Elaborazione** di ogni frame: bilanciamento del bianco, rimozione del gradiente di cielo, stretch e scritta opzionale (ora, temperatura del sensore).
- **Archivio per notte**: una notte va da mezzogiorno locale a mezzogiorno locale.
- **Prodotti** per ogni notte: timelapse MP4, keogram e startrails.
- **Maschera informativa** sui bordi del timelapse, con widget attivabili (vedi «Maschera sul timelapse»).
- **Previsione dei passaggi** di ISS e di altri satelliti, facoltativa (vedi «Satelliti e transiti»).
- **Rilevamento di meteore, bolidi e transiti**, facoltativo (vedi «Meteore e bolidi»).
- **Meteo** da un driver ASCOM o da Open-Meteo, facoltativo (vedi «Meteo»).
- **Caricamento del timelapse su YouTube**, facoltativo (vedi «YouTube»).
- **Notte rossa** per non disturbare l'adattamento al buio (vedi «Notte rossa»).
- **Camere**: simulatore incluso, camere ASCOM (driver COM) e ASCOM Alpaca raggiungibili in rete.
- **Pubblicazione periodica** (facoltativa) dell'ultima immagine verso un server web. Vedi [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md).

In arrivo, fuori dalla 0.3.0: integrazione con NINA, invio a un bot Telegram e monitoraggio pubblico sul sito.

## Requisiti

- Windows 10 o Windows 11, 64 bit (x64).
- **ffmpeg** per il timelapse e per le clip degli eventi. Va copiato in `tools\ffmpeg\ffmpeg.exe` accanto ad `AllSkyBrain.exe`, oppure installato e raggiungibile dal PATH di sistema.
- Per le camere ASCOM: il driver ASCOM della camera installato sul computer.
- Per il meteo da driver ASCOM: il driver ObservingConditions installato sul computer (facoltativo).
- Per il meteo da Open-Meteo: connessione a Internet (facoltativo).

## Installazione

Le release si trovano nella pagina **Releases** del repository. La release stabile documentata è la **v0.3.0**, che contiene due file:

| File | Uso |
|---|---|
| `AllSkyBrain-win-Setup.exe` | Installazione. Abilita l'aggiornamento automatico. |
| `AllSkyBrain-win-Portable.zip` | Copia portatile, senza installazione né aggiornamento automatico. |

### Installazione con il file Setup

1. Scaricare `AllSkyBrain-win-Setup.exe`.
2. Eseguire il file e seguire la procedura.
3. Avviare All Sky Brain. L'icona compare nell'area di notifica, accanto all'orologio.

Il file non è firmato digitalmente. Windows può mostrare un avviso per un programma non riconosciuto.

### Copia portatile

1. Scaricare `AllSkyBrain-win-Portable.zip`.
2. Estrarre lo zip in una cartella dell'utente, per esempio `C:\AllSkyBrain`.
3. Avviare `AllSkyBrain.exe`.

La copia portatile non riceve aggiornamenti: l'interfaccia lo indica. Per aggiornarla, scaricare lo zip della nuova versione ed estrarlo di nuovo. La release v0.3.0 non include file di controllo SHA-256.

### Aggiornamento automatico

Vale per l'installazione con il file Setup (aggiornamenti Velopack), che controlla le nuove release nella pagina Releases del repository.

- Il primo controllo parte circa un minuto dopo l'avvio, poi ogni 24 ore.
- Il controllo è attivo per predefinito. Si cambia in **Impostazioni**, con la casella «Controlla e scarica gli aggiornamenti automaticamente», oppure con `autoCheck` nel file `updates.json` della cartella dati.
- Con il controllo attivo, il download avviene in automatico. L'installazione avviene solo su richiesta dell'utente.
- Dal menu dell'icona, la voce **Controlla aggiornamenti** avvia un controllo subito.

## Primo avvio

1. Avviare All Sky Brain. Alla prima apertura viene creato `config.json` nella cartella dati, con i valori predefiniti. La cartella dell'archivio (`storage.folder`) coincide con la cartella dati.
2. In **Impostazioni**, sezione **Osservatorio**, inserire latitudine, longitudine, quota e fuso orario. Servono per calcolare Sole e Luna e per sapere quando è giorno e quando è notte.
3. In **Camera** cercare la camera e collegarla. Il simulatore serve per una prova senza hardware.
4. Aprire **Live**: l'immagine compare dopo il primo frame.

Dal menu dell'icona nell'area di notifica:

- **Apri All Sky Brain** apre la finestra.
- **Avvia con Windows** registra l'app nell'avvio dell'utente corrente.
- **Controlla aggiornamenti** avvia un controllo degli aggiornamenti.
- **Esci** arresta l'applicazione.

## Viste dell'app

La barra laterale contiene, dall'alto in basso: **Stanotte**, **Live**, **Camera**, **Notti**, **Eventi**, **Pubblicazione**, **Impostazioni**, **Log**.

Scorciatoie da tastiera:

- **Ctrl+1** … **Ctrl+7** aprono, in quest'ordine, le viste Live, Camera, Notti, Pubblicazione, Impostazioni, Log ed Eventi.
- **Stanotte** non ha una scorciatoia numerica. Si raggiunge con il mouse, con Tab, oppure con Su e Giù quando il fuoco è su una voce della barra laterale.
- **Ctrl+R** attiva o disattiva la notte rossa.

Descrizione delle viste:

- **Stanotte**: l'immagine del cielo della notte in corso. Una linea temporale da tramonto ad alba mostra le fasce dei crepuscoli civile, nautico e astronomico e le curve di altezza di Sole e Luna (altezze geometriche, senza rifrazione atmosferica). Ci sono inoltre la fase della Luna, l'elenco dei prossimi passaggi dei satelliti, l'ultima meteora registrata e lo stato della cattura. Le righe «Indice di cielo sereno» e «Meteo» mostrano ancora un segnaposto (vedi «Limiti noti»).
- **Live**: l'ultimo frame, con zoom e spostamento. L'interruttore **Grezzo** mostra il frame senza elaborazione. Il pannello laterale riporta i dati del frame (esposizione, guadagno, mediana, temperatura). La griglia alt/az e le tracce previste dei satelliti richiedono una calibrazione ottica accettata. Le tracce compaiono per i passaggi in corso o entro 15 minuti.
- **Camera**: ricerca e collegamento della camera (simulatore, ASCOM, Alpaca), impostazioni di cattura, messaggi d'errore, calibrazione ottica (dal Sole o inserita a mano).
- **Notti**: elenco delle notti, keogram come barra di scorrimento temporale, startrails, riproduzione del timelapse e pulsante **Rigenera timelapse**.
- **Eventi**: meteore, bolidi e transiti, con filtri per notte e per tipo. Per ogni evento: clip, dati, etichette **Conferma** e **Falso positivo** (servono a tarare le soglie) e **Esporta evento**, che salva l'evento in un file zip.
- **Pubblicazione**: impostazioni Uplink e sezione **YouTube** (collegamento, impostazioni, caricamento manuale).
- **Impostazioni**: osservatorio, lingua (si applica al riavvio), tema, cartella dati (sola lettura), avvio con Windows, aggiornamenti, accesso dal browser, e il pulsante che apre l'editor della maschera.
- **Log**: ultime righe del log, con filtro. Il log maschera i segreti noti prima di mostrarli. Il log non ha livelli di gravità: la gravità è stimata dalle parole nel messaggio.

## Maschera sul timelapse

La maschera è una fascia di informazioni ai bordi del timelapse. Scrive ora, Sole, Luna e altri dati sul frame. Non cambia le immagini archiviate: ogni frame salva un file di metadati (`<frame>.json`) accanto al JPEG, e la maschera si disegna quando si crea il timelapse. Dopo una modifica del layout, si rigenera il video dalla vista **Notti**.

Per modificarla: **Impostazioni**, pulsante dell'editor della maschera. L'editor mostra un'anteprima sull'ultimo frame e permette di:

- attivare o disattivare ogni widget, scegliere la posizione (angoli o bande) e l'ordine, e impostare i formati;
- caricare un logo PNG, che viene copiato nella cartella `loghi` della cartella dati;
- scegliere se applicare la maschera al **Timelapse**. Le voci «Vista dal vivo» e «Pubblicazione Uplink» sono presenti ma non ancora attive;
- ripristinare i valori predefiniti.

Widget disponibili: **Data e ora** (attivo, in basso a sinistra), **Altezza del Sole** e **Luna: altezza e fase** (attivi, in basso a destra), **Satelliti in transito** (spento), **Meteo** (spento), **Logo**, **Testo libero**, **Esposizione**, **Bussola** (spenti).

Stato dei dati: il widget **Meteo** mostra valori solo se una sorgente è configurata. Il widget **Satelliti** riceve i nomi dei passaggi, ma il risultato a schermo non è ancora verificato. Il widget **Bussola** non è ancora disponibile e viene omesso.

Configurazione in `config.json`:

- `overlay.applyToTimelapse`: `true` applica la maschera al timelapse. Predefinito `false`.
- `overlay.layout`: layout della maschera (modalità cerchio o bande, widget, posizioni, formati). `null` usa il layout predefinito.

Nomi, valori e vincoli sono nella sezione «Parametri di config.json» di [docs/FUNZIONI.md](docs/FUNZIONI.md).

## Satelliti e transiti

La previsione dei passaggi è **spenta per predefinito**. Non c'è un interruttore nell'interfaccia: si attiva in `config.json`:

```json
"satellites": { "enabled": true, "targets": [ { "noradId": 25544, "name": "ISS (ZARYA)" } ], "windowHours": 24, "minElevationDeg": 10, "sunMaxAltDeg": -6 }
```

Funzionamento:

- I dati orbitali (elementi TLE) vengono scaricati da CelesTrak, per numero NORAD, al massimo una volta ogni 2 ore per satellite, e salvati nella cartella `satellites`. Se CelesTrak risponde con un codice diverso da 200, i download si fermano per 24 ore.
- Il calcolo usa la libreria SGP.NET 1.6.0. Sono previsti i passaggi nella finestra indicata (24 ore), con elevazione massima sopra la soglia (10°). Un passaggio è **visibile** solo se il satellite è illuminato dal Sole e il cielo è buio (Sole sotto -6°).
- I passaggi compaiono nella vista **Stanotte**. Le tracce previste compaiono nella vista **Live** per i passaggi in corso o entro 15 minuti.
- Il satellite predefinito è la ISS (NORAD 25544). Si possono aggiungere altri numeri NORAD in `satellites.targets` (massimo 20). Per i numeri a 6 cifre non esistono TLE.

Conferma nelle immagini: con `detection.enabled` attivo (vedi sotto) e una calibrazione ottica accettata, un transito viene confermato se nei frame compare una scia lungo la traccia prevista. Il transito confermato salva una clip, un'immagine composita e i metadati nella vista **Eventi**. Senza calibrazione, il transito non viene confermato. Con `detection.saveAllVisiblePasses` vengono salvati anche i passaggi non confermati, con il verdetto registrato.

Modalità transito (facoltativa, spenta): durante un passaggio previsto riduce l'intervallo tra le pose, per avere più frame nel video. Si attiva con `capture.transitMode.enabled`. L'intervallo non scende sotto il minimo fisico della camera (esposizione più tempo di lettura).

## Meteore e bolidi

Il rilevamento è **spento per predefinito**. Si attiva in `config.json`:

```json
"detection": { "enabled": true }
```

Per le altre chiavi, vedi «Parametri di config.json» in [docs/FUNZIONI.md](docs/FUNZIONI.md).

Funzionamento:

- Lavora solo di notte, su una versione ridotta del frame (lato lungo 960 px), in un thread separato. Non blocca la cattura. Se il lavoro è in ritardo, il frame più vecchio viene scartato.
- Confronta ogni frame con il fondo medio dei frame precedenti. Cerca scie lunghe e sottili.
- Scarta i frame con variazioni globali alte (nuvole, fulmini, fari) e le scie che si ripetono allineate su più frame (aerei o satelliti).
- Classifica le scie come **meteora** o **bolide**. Il bolide è una scia molto luminosa o molto lunga. Le soglie sono iniziali e non sono tarate su cielo reale.
- Salva un evento nella vista **Eventi**: ritaglio ingrandito, frame intero con la scia evidenziata, mini-clip dei frame vicini (richiede ffmpeg) e metadati.

Le soglie sono nelle sezioni `detection.streak` e `detection.transit` di `config.json`. Le etichette **Conferma** e **Falso positivo** nella vista Eventi servono a valutarle.

## YouTube

Il caricamento automatico è **spento per predefinito**. Si attiva collegando l'account Google dalla sezione **YouTube** della vista **Pubblicazione**.

Le istruzioni passo passo, con la configurazione del progetto Google Cloud, sono in [docs/GUIDA-YOUTUBE.md](docs/GUIDA-YOUTUBE.md). I dettagli tecnici del collegamento OAuth sono in [docs/YOUTUBE-OAUTH.md](docs/YOUTUBE-OAUTH.md).

Funzionamento, con i valori predefiniti:

- Ogni giorno alle **12:30** (ora locale) carica il timelapse dell'ultima notte completata. Una notte viene caricata una sola volta.
- Se il PC era spento all'ora prevista, il caricamento parte all'avvio successivo, entro 12 ore (`youtube.catchUpWindowHours`, fino a 24).
- La privacy predefinita è **privato** (`youtube.privacy`). Il titolo e la descrizione derivano da modelli con segnaposto (`{notte:...}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`).
- Ogni caricamento consuma una unità della quota di YouTube per `videos.insert`, con un limite di 100 chiamate al giorno. In caso di errore temporaneo il caricamento riprova; in caso di errore di autorizzazione chiede di collegare di nuovo l'account.
- Il client secret e i token non stanno in `config.json`: sono nel file `youtube-auth.json`, cifrati con DPAPI.

Con l'app OAuth in stato **Testing** di Google, il collegamento scade dopo 7 giorni, e il caricamento notturno si ferma. La guida spiega come passare lo stato a **In production**.

## Meteo

La sezione `weather` di `config.json` sceglie la sorgente (`weather.source`). Non c'è un'interfaccia per questa sezione: la modifica si fa nel file.

- `none` (predefinito): nessuna sorgente meteo.
- `ascom`: legge un driver ASCOM ObservingConditions installato sul computer. Richiede `weather.ascomProgId`, il nome ProgID del driver.
- `openmeteo` (spento): dati di previsione online di Open-Meteo, usando latitudine e longitudine di `site`. Il servizio è gratuito per **uso non commerciale**: la sorgente parte solo con `weather.openMeteoAcknowledgedNonCommercial: true`.

Il fornitore viene ricreato quando cambia la sezione, senza riavviare l'app. I valori finiscono nel sidecar dei frame (campo `weather`) e nel widget **Meteo** della maschera, se attivo.

Stato della verifica:

- La riga **Meteo** della vista Stanotte non legge ancora questi dati e mostra il segnaposto.
- Il driver ASCOM non è stato provato con una stazione reale.
- Le unità dei campi restituiti da Open-Meteo non sono confermate su una risposta reale.
- L'attribuzione richiesta da Open-Meteo (CC BY 4.0) è definita nel codice. Non viene ancora scritta nei frame né mostrata: va collegata prima di usare la sorgente `openmeteo` (vedi «Limiti noti»).

## Notte rossa

La notte rossa riduce la luce dell'interfaccia. La sua palette usa solo il canale rosso. Si attiva con **Ctrl+R**, oppure nel campo **Tema** delle **Impostazioni**, con la voce «Notte rossa». La scelta è salvata in `ui.theme` con valore `"red"`. Non è stata verificata sullo schermo di un PC Windows (vedi «Limiti noti»).

## Accesso dal browser

L'interfaccia web è **spenta per predefinito** (`ui.webEnabled: false`). L'applicazione desktop non ne ha bisogno, e con l'interfaccia spenta non si apre alcuna porta HTTP.

Per attivarla: **Impostazioni**, sezione «Accesso dal browser», oppure `ui.webEnabled` in `config.json`. La modifica vale dal riavvio. Con l'interfaccia attiva:

- il server ascolta solo su `127.0.0.1` (il computer stesso), sulla porta `ui.port` (predefinito 5810). Se la porta è occupata, l'host prova le successive fino alla 5820;
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

La cartella dell'archivio è `storage.folder`. Al primo avvio coincide con la cartella dati. Un file di configurazione già esistente mantiene il suo valore.

Il file di configurazione è descritto in [docs/FUNZIONI.md](docs/FUNZIONI.md) e le chiavi dell'interfaccia locale in [docs/API.md](docs/API.md).

## Documentazione

- [docs/FUNZIONI.md](docs/FUNZIONI.md): parametri di `config.json` e inventario delle funzioni.
- [docs/API.md](docs/API.md): contratto dell'interfaccia locale e delle chiavi di configurazione.
- [docs/GUIDA-YOUTUBE.md](docs/GUIDA-YOUTUBE.md): configurazione di YouTube, passo passo.
- [docs/YOUTUBE-OAUTH.md](docs/YOUTUBE-OAUTH.md): dettagli tecnici del collegamento OAuth e fonti.
- [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md): pubblicazione Uplink.
- [docs/DESIGN-DESKTOP.md](docs/DESIGN-DESKTOP.md): linee guida grafiche dell'applicazione desktop.

## Limiti noti

Stato al 23/10/2026. Le fonti dettagliate sono nel registro di sviluppo (`docs/ralph/NOTES.md`).

- **Interfaccia non vista su Windows.** Le viste sono compilate e testate in automatico, ma l'applicazione non è mai stata avviata e osservata su un PC Windows. Avvio dell'exe con la camera simulata, disposizione, icone, focus, barra di stato, notte rossa ed editor della maschera vanno controllati a mano.
- **Maschera sul timelapse non verificata.** La prova con una notte simulata non ha applicato la maschera: la maschera è disegnata dal Desktop, e la prova girava senza interfaccia. L'effetto sul timelapse reale non è stato controllato.
- **Encoder video su Windows.** L'encoder previsto per il prodotto è `h264_mf`. Le prove sono state fatte con libx264, solo in locale. Il comportamento di `h264_mf`, in edizione Windows N e in edizione normale, non è stato provato.
- **Camera reale.** Nessuna prova con una camera ZWO reale. Il driver ASCOM ASICamera2 riporta la camera come `Monochrome` e All Sky Brain la rifiuta: modello e tipo di sensore vanno confermati sul PC reale. I limiti di esposizione ZWO (correzione della 0.2.1) non sono stati provati con la camera reale.
- **Soglie non tarate.** Il rilevamento di meteore, bolidi e transiti è stato provato solo su immagini sintetiche, con scie inserite artificialmente. Le soglie vanno tarate sul cielo reale. Il confermatore dei transiti può dare falsi positivi quando il bordo di una nuvola è allineato alla traccia prevista.
- **Clip degli eventi.** Le clip sono state create con libx264 su una scia sintetica. Non sono state provate con ffmpeg su immagini reali.
- **Calibrazione ottica.** La rilevazione del Sole è stata provata su dati sintetici. I punti raccolti dal Sole non vengono salvati al riavvio.
- **Posizione della Luna.** L'azimut della Luna nei metadati ha un errore atteso di circa 1°.
- **Tempo di lettura della camera.** Il valore di 2 s per la modalità transito è una stima, non una misura.
- **Satelliti.** La propagazione della ISS non è stata confrontata con una fonte esterna. Gli oggetti in orbita profonda (modello SDP4) non vengono rifiutati. CSS (48274) e Hubble (20580) non sono tra i bersagli predefiniti. Il widget Satelliti della maschera non è stato verificato a schermo.
- **Meteo.** Il driver ASCOM non è stato provato con una stazione reale. Le unità dei campi Open-Meteo non sono confermate su una risposta reale. L'attribuzione CC BY 4.0 di Open-Meteo non viene ancora scritta nei frame né mostrata: va collegata prima di usare la sorgente `openmeteo`. Il widget Bussola è sempre omesso. La riga Meteo della vista Stanotte non legge ancora i dati.
- **YouTube.**
  - Il collegamento e il caricamento reali verso YouTube non sono stati provati con un account e un progetto reali. I test usano risposte finte.
  - L'ID categoria predefinito `28` non è verificato su una fonte ufficiale.
  - La documentazione YouTube dice che i video caricati da progetti API non verificati non sono limitati alla visualizzazione privata. L'effetto reale per questo progetto va controllato al primo caricamento.
  - Il flag «destinato ai minori» è sempre `false`.
- **Accesso dal browser.** Il listener su `127.0.0.1` senza privilegi amministrativi non è stato provato su Windows. Con la rete locale attiva, la password usa HTTP Basic, non cifrato.
- **Aggiornamenti.** L'aggiornamento automatico dalla 0.2.1 alla 0.3.0 non è stato provato su un PC reale.
- **Firma del codice e licenza.** Il file eseguibile non è firmato. Il repository non contiene ancora un file di licenza.
- **Log.** Il log non ha livelli: la gravità è stimata dalle parole nel messaggio.
- **Keogram.** Dopo un riavvio il keogram può coprire meno frame dell'archivio. Oltre 4096 frame le colonne sono raggruppate a blocchi.
- **Uplink.** I messaggi di validazione del server di pubblicazione sono solo in italiano.
- **SafetyMonitor Alpaca.** Non è ancora implementato.
