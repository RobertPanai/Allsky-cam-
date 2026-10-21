# Pubblicazione (protocollo Uplink)

Versione del modulo: 0.1.0.

La pubblicazione invia periodicamente l'ultima immagine della camera a un server web scelto dall'utente. Il server riceve un JPEG ridimensionato e i suoi metadati. AllSky Brain fa solo richieste HTTPS in uscita verso l'indirizzo configurato: non apre porte e non espone nulla in rete.

## Cos'è

- Ogni intervallo (per default 60 secondi) il motore prende il frame più recente, lo ridimensiona, lo codifica in JPEG e lo invia.
- Il frame è l'immagine pulita, senza scritte (overlay). Il publisher tiene solo l'ultimo frame ricevuto: quelli intermedi non vengono inviati.
- Se il server non risponde, le immagini restano in una coda su disco e vengono inviate più tardi, con un limite (vedi «Comportamento»).

## Protocollo

### Richiesta

```
POST {serverUrl}/api/uplink/allsky
Authorization: Bearer <token>
[<nome-header-extra>: <valore>]        ; facoltativo
Content-Type: multipart/form-data; boundary=...
```

Il corpo multipart ha due parti:

- `meta` (`application/json`): i metadati dello scatto.
- `image` (`image/jpeg`): l'immagine ridimensionata.

Esempio di `meta` (valori di esempio):

```json
{
  "schemaVersion": 1,
  "deviceId": "allsky-1",
  "profileName": "Cielo di prova",
  "captureId": "3f0c2a7e-9b1d-5c4a-8e2f-6d1b0a9c7e35",
  "capturedAt": "2026-10-21T21:30:00.000Z",
  "exposureMs": 175,
  "gain": 0,
  "mode": "night",
  "temperatureC": null
}
```

Campi:

| Campo | Significato |
|---|---|
| `schemaVersion` | Versione del protocollo: 1. |
| `deviceId` | Identificativo del dispositivo, dall'impostazione omonima. |
| `profileName` | Nome mostrato dal server. Assente se non impostato. |
| `captureId` | UUID dello scatto, stabile (vedi sotto). |
| `capturedAt` | Istante di cattura in UTC, `yyyy-MM-ddTHH:mm:ss.fffZ`. |
| `exposureMs`, `gain` | Esposizione in millisecondi e guadagno della camera. |
| `mode` | `day`, `twilight` o `night`. |
| `temperatureC` | Temperatura del sensore in gradi Celsius, oppure `null`. |

### Risposta

Con successo il server risponde `200` con:

```json
{ "stored": true, "duplicate": false, "visible": true }
```

`stored` e `duplicate` valgono entrambi come successo: se il server risponde `duplicate`, il frame è considerato consegnato. Una risposta `200` senza `stored` o `duplicate` a `true`, o con un corpo illeggibile, viene trattata come errore temporaneo.

### Errori e comportamento del client

| Risposta del server | Codice nel client | Cosa fa il client |
|---|---|---|
| `200` con corpo valido | `stored` / `duplicate` | Frame considerato consegnato. |
| `200` con corpo illeggibile | `bad_response` | Riprova con backoff. |
| `401` o `403` | `auth` | Sospende l'invio. Riprende quando si salvano credenziali nuove. |
| `400`, `415` e altri `4xx` | `rejected` | Scarta il frame. Non riprova. |
| `408` | `server_error` | Riprova con backoff. |
| `413` | `too_large` | Scarta il frame. Non riprova. |
| `429` | `rate_limited` | Riprova dopo il `Retry-After` del server. |
| `5xx` | `server_error` | Riprova con backoff. |
| Rete non raggiungibile | `network` | Riprova con backoff. |
| Nessuna risposta entro 30 secondi | `timeout` | Riprova con backoff. |

## Comportamento

- **Intervallo**: 60 secondi per default, minimo 15 secondi.
- **Immagine**: ridimensionata con il lato lungo massimo di 1200 pixel (configurabile tra 200 e 2000), codificata in JPEG a qualità 75 (configurabile tra 30 e 95). Se il JPEG supera circa 300 KB, il client riduce prima la qualità e poi le dimensioni. Non accetta un risultato sopra 900 KB.
- **Nessun invio inutile**: un'immagine identica all'ultima accodata non viene reinviata. Senza nuovi frame dalla camera (per esempio con la camera ferma) non parte alcun invio.
- **Coda su disco**: al massimo 20 immagini in attesa. Quando la coda è piena vengono eliminate le più vecchie.
- **Dopo un'interruzione**: alla ripresa vengono inviate solo le ultime 3 immagini, dalla più vecchia alla più recente. Le altre vengono scartate e contate in `discardedTotal`.
- **`captureId` stabile**: è un UUID v5 calcolato da `deviceId` e dall'istante di cattura in UTC. Lo stesso scatto ha sempre lo stesso identificativo, anche dopo un riavvio, così il server riconosce i duplicati.
- **Orari**: tutti gli orari nel protocollo e nello stato sono in UTC.
- **Backoff**: dopo un errore temporaneo il client attende 30 secondi, poi raddoppia l'attesa fino a un massimo di 15 minuti. Se il server indica un `Retry-After` più lungo, il client attende quel tempo (al massimo 24 ore). Un `Retry-After` più corto viene ignorato.

## Impostazioni

Le impostazioni si modificano nella pagina Pubblicazione della UI, da questo computer.

| Campo | Default | Limiti | Note |
|---|---|---|---|
| `enabled` | `false` | sì / no | Con `true` servono URL, ID dispositivo e token. |
| `serverUrl` | vuoto | `https://...`, oppure `http://` solo per `localhost`, `127.0.0.1` e `::1`; massimo 300 caratteri | Senza credenziali nell'indirizzo. |
| `deviceId` | vuoto | 1-64 caratteri: lettere, numeri, `.`, `_`, `-` | Nessuno spazio. |
| `token` | assente | obbligatorio con `enabled` | Segreto. Cifrato su disco. Mai mostrato. |
| `profileName` | vuoto | fino a 80 caratteri | Facoltativo. |
| `intervalSeconds` | 60 | 15-86400 | Secondi tra due invii. |
| `longEdgePx` | 1200 | 200-2000 | Lato lungo dell'immagine inviata. |
| `jpegQuality` | 75 | 30-95 | Qualità JPEG iniziale. |
| `extraHeaderName` | vuoto | 1-64 caratteri: lettere, numeri, `-` | Header HTTP aggiuntivo. Non può essere `Authorization`, `Host`, `Content-Type` o `Content-Length`. |
| `extraHeaderValue` | assente | senza a capo | Segreto, come il token. |

### Pulsante «Test connessione»

Il pulsante usa i valori del form, anche se non sono ancora salvati, e fa una richiesta `GET {serverUrl}/api/uplink/health` con il token. Non invia immagini e non cambia lo stato della pubblicazione.

> **Da confermare**: il test presuppone che il server esponga `GET /api/uplink/health`. Se il server risponde `404` o `405`, il test segnala che la pagina di verifica manca. Va verificato con il server reale prima dell'uso.

### File di avvio `bootstrap.json`

Per preconfigurare l'applicazione (per esempio su più computer) si può mettere un file `bootstrap.json` nella cartella dati, `%LocalAppData%\AllSky\bootstrap.json`. Al successivo avvio il file viene applicato alle impostazioni di pubblicazione e poi sovrascritto con spazi e cancellato. Un campo assente mantiene il valore corrente. Un file non valido viene lasciato al suo posto e nel log compare il motivo (il campo da correggere, oppure la mancata lettura del JSON).

```json
{
  "enabled": true,
  "serverUrl": "https://example.org",
  "deviceId": "allsky-1",
  "token": "<il-tuo-token>",
  "profileName": "Cielo di prova",
  "intervalSeconds": 60,
  "longEdgePx": 1200,
  "jpegQuality": 75,
  "extraHeaderName": null,
  "extraHeaderValue": null
}
```

## Sicurezza e privacy

- **Token cifrato**: token e valore dell'header extra sono salvati cifrati con DPAPI, legati all'utente Windows che li ha salvati. Su un altro utente o un altro computer il file non si decifra.
- **Mai nei log**: token e header non compaiono nei log, nei messaggi d'errore, nelle risposte della UI né negli eventi.
- **Solo richieste in uscita**: il motore non ascolta nulla per la pubblicazione. Le rotte di scrittura e prova accettano solo richieste dal computer locale.
- **Dati inviati**: solo l'immagine ridimensionata e i metadati elencati nel protocollo. Non vengono inviati altri file, la configurazione, il percorso delle cartelle né l'archivio delle notti.

## File e cartelle

Tutto sta in `%LocalAppData%\AllSky`:

- `publish.json`: impostazioni, con i segreti cifrati.
- `publish-queue`: immagini in attesa di invio, con i rispettivi metadati.
- `bootstrap.json`: solo se presente all'avvio; viene cancellato dopo l'applicazione.
- `logs\publish-YYYYMMDD.log`: registro della pubblicazione (data UTC). I file più vecchi di 14 giorni vengono eliminati.

## Stato visibile

La pagina Pubblicazione mostra lo stato. Gli stati sono:

| Stato | Significato |
|---|---|
| `disabled` | Pubblicazione spenta o impostazioni incomplete. |
| `idle` | Attiva, in attesa del prossimo intervallo. |
| `sending` | Invio in corso. |
| `backoff` | Errore temporaneo: nuovo tentativo alla data indicata in `nextAttemptUtc`. |
| `suspended` | Invio sospeso per credenziali rifiutate. Riprende quando si salvano credenziali nuove. |

Lo stesso stato è disponibile in `GET /api/publish/status` e nell'evento SSE `publish` (vedi `docs/API.md`).

## Risoluzione dei problemi

| Sintomo | Cosa fare |
|---|---|
| `suspended` con codice `auth` | Controllare il token e l'ID dispositivo sul server, poi salvare di nuovo. |
| `backoff` con codice `network` | Controllare la connessione e l'indirizzo del server. Il client riprova da solo. |
| `backoff` con codice `timeout` o `server_error` | Il server è lento o in errore. Il client riprova da solo. |
| Codice `rejected` o `too_large` | Il server non accetta l'immagine. Ridurre il lato lungo o la qualità JPEG, o controllare l'ID dispositivo. |
| Codice `no_health_endpoint` nel test | Il server non espone la pagina di verifica: controllare l'indirizzo. |
| Pubblicazione `disabled` dopo il salvataggio | Attivare «Pubblica sul server» nella pagina Pubblicazione e controllare che token e ID siano presenti. |
| Il file di avvio non viene applicato | Controllare il file di log: indica il motivo. Il file non viene cancellato in quel caso. |
| Nessun invio, stato `idle` | Verificare che la camera stia catturando: senza nuovi frame non parte alcun invio. |

Il file `logs\publish-YYYYMMDD.log` contiene le righe del giorno, senza segreti: è il primo posto dove guardare.

## Versione

0.1.0. Il modulo implementa la versione 1 del protocollo.
