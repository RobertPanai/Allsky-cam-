# All Sky Brain

All Sky Brain è un'applicazione per Windows che cattura le immagini di una camera all-sky, le elabora e le archivia notte per notte. Dalle immagini crea timelapse, keogram e startrails. Si controlla da una pagina web che gira sul computer, con un'icona nell'area di notifica.

## Cosa fa

- **Cattura continua** dalla camera, con esposizione adattata a giorno, crepuscolo e notte.
- **Elaborazione** di ogni frame: bilanciamento del bianco, rimozione del gradiente di cielo, stretch dell'immagine e scritte opzionali (ora, temperatura, esposizione).
- **Archivio per notte**: una notte va da mezzogiorno locale a mezzogiorno locale.
- **Prodotti**: timelapse MP4, keogram e startrails per ogni notte.
- **Camere**: simulatore incluso, oppure camere ASCOM (driver COM) e ASCOM Alpaca raggiungibili in rete.
- **Interfaccia web locale**: sezioni Live, Notti, Colore, Camera, Sito e Pubblicazione, con layout adatto anche al telefono.
- **Icona nell'area di notifica**: apre il pannello, permette di avviare All Sky Brain con Windows e chiude l'applicazione.
- **Pubblicazione periodica** (facoltativa): invia l'ultima immagine a un server web. Vedi [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md).

## Requisiti

- Windows 10 o Windows 11, 64 bit (x64).
- Per il timelapse: **ffmpeg**. Va copiato in `tools\ffmpeg\ffmpeg.exe` accanto ad `AllSkyBrain.exe`, oppure installato e raggiungibile dal `PATH` di Windows.
- Per le camere ASCOM: il driver ASCOM della camera installato sul computer.

## Installazione

1. Scaricare `AllSkyBrain-win-x64.zip` e il file `AllSkyBrain-win-x64.zip.sha256` dalla pagina **Releases** del repository.
2. Verificare il file. In PowerShell, dalla cartella del download:

   ```powershell
   Get-FileHash .\AllSkyBrain-win-x64.zip -Algorithm SHA256
   Get-Content .\AllSkyBrain-win-x64.zip.sha256
   ```

   Il valore di `Hash` deve essere uguale al primo campo del file `.sha256`.
3. Estrarre lo zip in una cartella dell'utente, per esempio `C:\AllSkyBrain`.
4. Avviare `AllSkyBrain.exe`.

L'applicazione non è firmata digitalmente. Windows SmartScreen può quindi mostrare un avviso: scegliere **Ulteriori informazioni** e poi **Esegui comunque**. Il controllo SHA-256 del punto 2 permette di verificare che lo zip sia quello pubblicato.

## Primo avvio

All'avvio All Sky Brain crea l'icona nell'area di notifica (accanto all'orologio). Doppio clic sull'icona, oppure la voce **Apri pannello**, apre la UI nel browser all'indirizzo `http://127.0.0.1:5810`. Se la porta è occupata, All Sky Brain usa la successiva libera e apre la UI su quella.

Al primo avvio viene creato il file di configurazione con valori predefiniti e la UI apre la procedura guidata. Dalla UI:

1. Scegliere la camera (il simulatore serve per una prova senza hardware).
2. Inserire il sito: latitudine, longitudine, quota e fuso orario. Servono per sapere quando è giorno e quando è notte.
3. Collegare la camera dalla sezione Camera e controllare la sezione Live.

## Dove stanno i dati

Tutti i dati sono in `%LocalAppData%\AllSky`, cioè `C:\Users\<utente>\AppData\Local\AllSky`:

- `config.json`: configurazione.
- `nights\`: archivio delle notti (frame, keogram, startrails, timelapse).
- `logs\`: log giornalieri dell'applicazione (`allsky-AAAAMMGG.log`) e della pubblicazione (`publish-AAAAMMGG.log`).
- `publish.json`, `publish-queue\`: impostazioni e coda della pubblicazione.

## Pubblicazione

La pubblicazione è facoltativa e spenta di default. Richiede un server che accetti il protocollo Uplink. Descrizione completa, impostazioni, sicurezza e risoluzione dei problemi in [docs/PUBBLICAZIONE.md](docs/PUBBLICAZIONE.md). Il riferimento delle rotte HTTP locali è in [docs/API.md](docs/API.md).

---

## Quick start

1. Download `AllSkyBrain-win-x64.zip` and `AllSkyBrain-win-x64.zip.sha256` from the **Releases** page of the repository.
2. Check the file: `Get-FileHash .\AllSkyBrain-win-x64.zip -Algorithm SHA256` must match the first field of the `.sha256` file.
3. Extract the zip to a folder and run `AllSkyBrain.exe`. SmartScreen warns because the file is not code-signed: choose **More info**, then **Run anyway**.
4. Open the panel from the tray icon (it opens `http://127.0.0.1:5810`), then choose the camera and enter the site coordinates and time zone.
5. Timelapse needs ffmpeg: place `ffmpeg.exe` in `tools\ffmpeg` next to `AllSkyBrain.exe`, or add it to `PATH`.
6. Data is stored in `%LocalAppData%\AllSky`. Publishing is optional: see `docs/PUBBLICAZIONE.md`.
