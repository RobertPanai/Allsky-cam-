# Contratto API locale (UI web <-> Host)

Base: `http://127.0.0.1:{port}` (default porta 5810, configurabile). JSON UTF-8, camelCase. Nessuna chiamata verso Internet.
Il contratto è la sola dipendenza fra la UI e il motore: la UI si sviluppa contro `web/mock/server.mjs`.

## Stato e eventi
- `GET /api/status` ->
```json
{
  "version": "0.1.0",
  "state": "idle | capturing | error",
  "phase": "day | twilight | night",
  "sunAltitudeDeg": -12.4,
  "moonAltitudeDeg": 31.0,
  "camera": { "id": "sim-1", "name": "Simulatore ASCOM", "kind": "sim | ascom-com | ascom-alpaca", "connected": true },
  "lastFrame": { "id": "2026-10-19T23:41:07Z", "timestamp": "2026-10-19T23:41:07Z", "exposureMs": 8000, "gain": 250, "medianAdu": 4210 },
  "disk": { "freeGb": 212.4, "usedGb": 8.1 },
  "uptimeS": 5321,
  "error": null
}
```
- `GET /api/events` — Server-Sent Events locali; evento `status` con lo stesso JSON di `/api/status`, evento `frame` con `{id,timestamp}`. (Solo per la UI locale; non è streaming verso siti esterni.)
- `GET /api/frame/latest.jpg` — ultimo frame elaborato.
- `GET /api/frame/latest.jpg?stage=raw` — stesso frame senza bilanciamento colore né rimozione del gradiente (solo debayer + stretch automatico). Serve alla schermata Colore per il confronto prima/dopo.

## Camere
- `GET /api/cameras/discover` -> `[{ "id": "sim-1", "name": "...", "kind": "sim|ascom-com|ascom-alpaca", "detail": "ProgID o host:porta" }]`
- `POST /api/camera/connect` body `{ "id": "sim-1" }` -> `{ "ok": true }` oppure `{ "ok": false, "error": "testo comprensibile" }`

## Configurazione
- `GET /api/config`, `PUT /api/config` (intero oggetto, validato lato Host; risposta `{ok, errors:[{path,message}]}`)
```json
{
  "schemaVersion": 1,
  "language": "it | en",
  "site": { "name": "Osservatorio", "latitude": 45.07, "longitude": 7.69, "elevationM": 240, "timezone": "Europe/Rome" },
  "camera": { "id": "sim-1", "gain": 250, "maxExposureMs": 30000, "dayExposureMs": 2, "binning": 1 },
  "color": { "whiteBalance": "auto | manual", "manualGains": [1.0, 1.0, 1.0], "smoothing": 0.85, "gradientRemoval": 0.5, "stretch": "mtf | asinh", "saturation": 1.1 },
  "mask": { "enabled": false, "imagePath": null },
  "overlay": { "enabled": true, "showTime": true, "showSensorTemp": true, "label": "" },
  "capture": { "intervalSeconds": 30, "dayCapture": true },
  "products": { "timelapse": true, "keogram": true, "startrails": true, "timelapseFps": 25 },
  "storage": { "folder": "C:\\Users\\...\\AllSky", "keepNights": 30 },
  "ui": { "theme": "auto | day | night | red", "port": 5810, "allowLan": false, "webEnabled": false, "lanPasswordHash": null }
}
```

`ui.webEnabled` (predefinito `false`) avvia il server per il browser: con `false` l'host non apre alcuna porta HTTP.
`ui.allowLan` ha effetto solo con `webEnabled` attivo e con `lanPasswordHash` impostato (hash PBKDF2, mai la password in chiaro);
le richieste dalla rete locale usano HTTP Basic. Modifiche a questi valori si applicano al riavvio.

## Notti e prodotti (timelapse, keogram, startrails)
Una "notte" va da mezzogiorno locale a mezzogiorno locale; id = data di inizio `YYYY-MM-DD`.
- `GET /api/nights` -> `[{ "id":"2026-10-18", "frames":1204, "sizeMb":1840, "firstFrame":"...", "lastFrame":"...", "keogram":true, "startrails":true, "timelapse":"ready | generating | none | failed", "sunBands":[{"phase":"day|twilight|night","from":"ISO","to":"ISO"}] }]`
- `GET /api/nights/{id}/frames?limit=&offset=` -> `[{ "id","timestamp","phase" }]`
- `GET /api/nights/{id}/frames/{frameId}.jpg`, `.../thumb/{frameId}.jpg`
- `GET /api/nights/{id}/keogram.jpg`, `.../startrails.jpg`, `.../timelapse.mp4`
- `POST /api/nights/{id}/timelapse` -> `{ "ok": true }` (avvia la generazione); progresso nello `status` come `jobs:[{kind,nightId,progress}]`.
- `DELETE /api/nights/{id}`

## Pubblicazione (Uplink)
Invio periodico dell'ultima immagine verso un server web configurato dall'utente. Il motore fa solo richieste in uscita verso l'indirizzo configurato e non espone nulla in rete. Il token e il valore dell'header extra non escono mai dall'Host: le risposte dicono solo se sono impostati (`hasToken`, `hasExtraHeaderValue`). Le rotte che scrivono o provano la connessione accettano solo richieste dal computer locale (indirizzo di loopback); altrimenti rispondono `403` con `{ "error": "..." }`. La lettura è sempre consentita.

- `GET /api/publish/settings` ->
```json
{
  "enabled": true,
  "serverUrl": "https://example.org",
  "deviceId": "allsky-1",
  "hasToken": true,
  "profileName": "Cielo di prova",
  "intervalSeconds": 60,
  "longEdgePx": 1200,
  "jpegQuality": 75,
  "extraHeaderName": null,
  "hasExtraHeaderValue": false
}
```
- `PUT /api/publish/settings` body: gli stessi campi (senza `hasToken` e `hasExtraHeaderValue`), più i segreti facoltativi `token` e `extraHeaderValue` (stringa non vuota sostituisce il valore salvato; assente, `null` o vuota lascia invariato) e i flag `clearToken` e `clearExtraHeaderValue` (`true` cancella). Un campo assente mantiene il valore corrente. Risposta `{ "ok": true }`, oppure `{ "ok": false, "errors": [{ "path": "serverUrl", "message": "..." }] }` con HTTP 200: in caso di errori non si salva nulla. Se il salvataggio su disco fallisce, la risposta è `ok:false` con un errore sul percorso `token`.
- `GET /api/publish/status` ->
```json
{
  "state": "disabled | idle | sending | backoff | suspended",
  "lastSuccessUtc": "2026-10-21T21:30:00.000Z",
  "lastErrorCode": "auth",
  "lastError": "Il server ha rifiutato il token: controlla token e ID dispositivo nelle impostazioni.",
  "lastErrorUtc": "2026-10-21T21:31:00.000Z",
  "queueCount": 0,
  "sentTotal": 12,
  "discardedTotal": 0,
  "nextAttemptUtc": null
}
```
Le date sono UTC in formato `yyyy-MM-ddTHH:mm:ss.fffZ`, oppure `null`. Senza servizi di pubblicazione (avvio fallito) lo stato è `disabled`.
- `POST /api/publish/test` body: come il PUT, con i valori del form anche se non salvati; i segreti non inviati prendono quelli salvati. Il test non modifica impostazioni né stato. Risposta `{ "ok": true, "code": "ok", "message": "...", "statusCode": 200 }`. Con impostazioni non valide: `{ "ok": false, "code": "invalid_settings", "message": "<primo errore>", "statusCode": null }` e nessuna richiesta di rete. Codici possibili: `ok`, `auth` (token o ID rifiutati), `no_health_endpoint` (il server non espone la verifica), `server_error`, `timeout`, `network`, `invalid_settings`.
- Evento SSE `publish` su `GET /api/events`: il payload è lo stesso JSON di `GET /api/publish/status`. Arriva all'apertura della connessione e a ogni cambio di stato.

## Errori
HTTP 4xx/5xx con `{ "error": "frase in italiano/inglese che dice cosa è successo e cosa fare" }`.
