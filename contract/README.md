# Fixture del contratto di pubblicazione

| File | Cosa descrive |
|---|---|
| `allsky-v1.meta.json` | Parte `meta` (application/json) della richiesta `POST {serverUrl}/api/uplink/allsky` |
| `allsky-v1.response.json` | Risposta 200 |

**Stato: PROVVISORIE.** Sono ricavate parola per parola dalla descrizione del contratto fornita dal committente.
La fonte di verita' sono le fixture `contract/allsky-v1.*.json` del server: appena disponibili vanno copiate qui
identiche, sostituendo questi file. I test del contratto leggono questa cartella e segnalano ogni differenza.

Punti da confermare con la fonte:
- `temperatureC` (numero o `null`) e' presente nella descrizione del contratto ma non in tutte le sintesi.
- Endpoint della verifica "Test connessione": si usa `GET {serverUrl}/api/uplink/health` con lo stesso Bearer.
