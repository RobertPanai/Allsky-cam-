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
  "overlay": { "version": 1, "enabled": true, "showTime": true, "showSensorTemp": true, "label": "", "applyToTimelapse": false, "applyToLive": false, "applyToPublish": false, "layout": null },
  "capture": { "intervalSeconds": 30, "dayCapture": true, "transitMode": { "version": 1, "enabled": false, "intervalFraction": 0.5, "restoreMarginSeconds": 5, "readoutSeconds": 2 } },
  "products": { "timelapse": true, "keogram": true, "startrails": true, "timelapseFps": 25 },
  "storage": { "folder": "C:\\Users\\...\\AllSky", "keepNights": 30 },
  "ui": { "theme": "auto | day | night | red", "port": 5810, "allowLan": false, "webEnabled": false, "lanPasswordHash": null },
  "optics": null,
  "youtube": { "version": 1, "enabled": false, "clientId": null, "dailyTime": "12:30", "catchUpWindowHours": 12, "privacy": "private", "tags": ["timelapse", "allsky", "astrofotografia"], "categoryId": "28", "location": "Osservatorio", "titleTemplate": "Timelapse notte del {notte:yyyy-MM-dd}", "descriptionTemplate": "Timelapse della notte del {notte:yyyy-MM-dd} da {luogo}: {fotogrammi} fotogrammi, durata {durata}, meteore {meteore}, transiti {transiti}." },
  "satellites": { "version": 1, "enabled": false, "targets": [ { "noradId": 25544, "name": "ISS (ZARYA)" } ], "windowHours": 24, "minElevationDeg": 10, "sunMaxAltDeg": -6 },
  "detection": { "version": 1, "enabled": false, "saveAllVisiblePasses": false, "analysisLongSidePx": 960, "clipFps": 25, "streak": { "backgroundFrames": 8, "thresholdSigma": 3.0, "minDifferenceLuma": 6.0, "minAreaPx": 15, "minLengthPx": 25.0, "maxWidthPx": 6.0, "minElongation": 6.0, "maxChangedFraction": 0.02, "trackDistancePx": 12.0, "trackAngleDeg": 8.0, "repeatShiftPx": 30.0, "repeatAngleDeg": 8.0, "maxFrameGapSeconds": 30.0, "bolideSaturatedPeakLuma": 200.0, "bolideIntegratedLuma": 4000.0, "bolideLengthPx": 300.0 }, "transit": { "corridorHalfWidthPx": 4.0, "flankWidthPx": 6.0, "sampleSpacingPx": 1.0, "sigmaMultiplier": 5.0, "minExcessLevels": 10.0, "scoreThreshold": 0.6, "minVisibleTrackPx": 40.0 } }
}
```

`ui.webEnabled` (predefinito `false`) avvia il server per il browser: con `false` l'host non apre alcuna porta HTTP.
`ui.allowLan` ha effetto solo con `webEnabled` attivo e con `lanPasswordHash` impostato (hash PBKDF2, mai la password in chiaro);
le richieste dalla rete locale usano HTTP Basic. Modifiche a questi valori si applicano al riavvio.

### Sezioni aggiunte nel ramo desktop v1

Le sezioni seguenti sono opzionali: una configurazione precedente le legge con i valori predefiniti. Nomi, tipi, predefiniti, vincoli e stato di ogni chiave sono nella sezione «Parametri di config.json» di `docs/FUNZIONI.md`. Gli errori di validazione di `satellites` e `detection` usano i percorsi `satellites.*` e `detection.*`.

- `optics`: calibrazione ottica accettata, scritta dalla vista Camera. Vale `null` finché non viene accettata. Non va modificata a mano.
- `youtube`: caricamento automatico su YouTube. Il client secret e i token non sono in questa sezione: stanno in `youtube-auth.json`, cifrati con DPAPI. `enabled` lo impostano collegamento e scollegamento dell'account.
- `satellites`: previsione dei passaggi. Con `enabled: false` non parte nessuna richiesta a CelesTrak.
- `detection`: rilevamento di meteore, bolidi e transiti. Con `enabled: false` non parte alcun worker di rilevamento.
- `capture.transitMode`: modalità transito, spenta di default.
- `overlay.applyToTimelapse`: applica la maschera informativa ai frame del timelapse. `overlay.layout` contiene il layout della maschera (`null` = layout predefinito). `applyToLive` e `applyToPublish` sono riservati e per ora non hanno effetto.

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
