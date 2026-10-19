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
  "ui": { "theme": "auto | day | night | red", "port": 5810, "allowLan": false }
}
```

## Notti e prodotti (timelapse, keogram, startrails)
Una "notte" va da mezzogiorno locale a mezzogiorno locale; id = data di inizio `YYYY-MM-DD`.
- `GET /api/nights` -> `[{ "id":"2026-10-18", "frames":1204, "sizeMb":1840, "firstFrame":"...", "lastFrame":"...", "keogram":true, "startrails":true, "timelapse":"ready | generating | none | failed", "sunBands":[{"phase":"day|twilight|night","from":"ISO","to":"ISO"}] }]`
- `GET /api/nights/{id}/frames?limit=&offset=` -> `[{ "id","timestamp","phase" }]`
- `GET /api/nights/{id}/frames/{frameId}.jpg`, `.../thumb/{frameId}.jpg`
- `GET /api/nights/{id}/keogram.jpg`, `.../startrails.jpg`, `.../timelapse.mp4`
- `POST /api/nights/{id}/timelapse` -> `{ "ok": true }` (avvia la generazione); progresso nello `status` come `jobs:[{kind,nightId,progress}]`.
- `DELETE /api/nights/{id}`

## Errori
HTTP 4xx/5xx con `{ "error": "frase in italiano/inglese che dice cosa è successo e cosa fare" }`.
