# Richiesta di modifica del contratto Uplink: monitoraggio dell'all-sky

Stato: bozza del 23/10/2026, task P12.2. Solo documento: nessuna fixture e nessun file del sito sono stati toccati.
La richiesta segue la procedura (d) dell'agente `uplink-expert` e si appoggia a `UPLINK-SPEC.md` (nel Project «codici astrofotografia», `claude/UPLINK-SPEC.md`; nel repository del sito dovrebbe stare in `docs/UPLINK-SPEC.md`, ma nel clone locale usato da questa sessione quel file non c'è).

Convenzioni: **[V]** verificato con fonte e data; **[NV]** non verificato; **[S]** scelta o stima mia.
Per la specifica Uplink, dove una cifra non è stata confrontata con il codice del sito, vale il rimando alla sezione citata.

## 1. Motivazione

All Sky Brain deve mostrare ai visitatori del sito, in sola lettura e senza login (decisione di Robert del 22/10/2026, `docs/ACCESSO-REMOTO.md`), questi dati:

- immagine all-sky, con mirino e maschera se attivi;
- fase della notte, altezza di Sole e Luna;
- soggetto e stato di park di NINA, distanza dalla Luna, grafico dell'altezza del soggetto;
- ultimo timelapse, keogram e startrails.

L'elenco chiuso è in `docs/ralph/IMPLEMENTATION_PLAN.md`, P12.1, e nel tipo `MonitorSnapshot` (`src/AllSky.Publish/Monitor/`).

Che cosa il contratto attuale già copre e che cosa no:

| Dato | Copertura attuale | Fonte |
|---|---|---|
| Immagine all-sky | Coperta: `POST /api/uplink/allsky`, JPEG ≤ 1 MB, una nuova immagine ogni 10 s per dispositivo | UPLINK-SPEC §4.7 |
| Mirino e maschera sull'immagine | Coperta: sono pixel nel JPEG, nessun campo nuovo | P12.3 |
| Fase della notte | Parziale: `mode` (`day`, `twilight`, `night`) nella meta dell'immagine | §4.7 |
| Altezza di Sole e Luna, soggetto, park, distanza dalla Luna, grafico | **Non coperta** | — |
| Keogram e startrails | **Non coperta** (l'immagine allsky è una sola, l'ultima) | §4.7, «solo l'ultima immagine per dispositivo» |
| Timelapse | **Non coperto** | — |

## 2. Tipo di modifica

**Compatibile.** Si aggiungono due route nuove e nessun campo esistente cambia (UPLINK-SPEC §8: «aggiungere un campo facoltativo, aggiungere un campo di risposta, aggiungere un valore di `error` documentato» resta in `schemaVersion: 1`; una route nuova è additiva). Nessun client in produzione (plugin NINA, software allsky attuale) viene toccato. Non serve `schemaVersion: 2`.

Alternativa scartata [S]: estendere la `meta` dell'immagine allsky. Il limite della meta è 4 096 byte (§4.7) e il grafico dell'altezza da solo ne richiede circa 4 300 con 96 punti; inoltre lo stato ha ritmo e vincoli diversi da quelli dell'immagine (limite di 10 s per dispositivo).

## 3. Proposta di contratto

### 3.1 `PUT /api/uplink/monitor` (stato corrente)

Sostituisce lo stato precedente del dispositivo (stessa logica di `streams`: ultimo stato vince). `deviceId` al livello principale, come per activity, allsky e allsky-link (§2).

Header: `Authorization: Bearer <token>`, `Content-Type: application/json`. Gzip accettato.

| Campo | Tipo | Obbligatorio | Regola proposta |
|---|---|---|---|
| `schemaVersion` | numero | sì | `1` |
| `deviceId` | stringa | sì | `^[A-Za-z0-9._-]{1,64}$`, uguale al token |
| `profileName` | stringa | no | come §4.9 |
| `snapshotId` | stringa | sì | `^[A-Za-z0-9-]{1,64}$`, identifica lo stato (idempotenza, R11) |
| `capturedAt` | stringa | sì | ISO 8601 in UTC con `Z` |
| `nightPhase` | stringa | no | `day`, `twilight`, `night` (stessi valori di `mode` in §4.7) |
| `sunAltitudeDeg`, `moonAltitudeDeg` | numero finito | no | gradi, arrotondati a 0,1 dal client |
| `targetName` | stringa | no | 1-80 caratteri dopo la pulizia; non può contenere `://`, `tok=`, `bearer` (come `message` in §4.4) |
| `parkState` | stringa | no | `parked`, `tracking`, `slewing`, `stopped`, `unknown`, `disconnected` |
| `moonSeparationDeg` | numero finito | no | gradi, 0 a 180 |
| `targetAltitudeChart` | array | no | 0-96 punti `{ "t": ISO UTC, "altDeg": numero }` |

Dimensione massima del body: **16 384 byte** [S: 96 punti circa 4,3 KB; il margine copre i testi]. Nessuna posizione dell'osservatorio, nessun percorso, nessun indirizzo, nessun token: il tipo del client non li contiene (test di contratto in `tests/AllSky.Publish.Tests/Monitor/`).

Risposta 200 proposta: `{ "stored": true, "duplicate": false, "visible": true }` (stessa forma di §4.7).

Errori proposti: gli stessi di §4.8 (400, 401, 403, 413, 415, 500, 503) più `429 rate_limited` con `Retry-After` se arrivano più aggiornamenti di uno ogni **60 secondi** per dispositivo [S].

Richiesta al sito: se arriva uno stato con `capturedAt` precedente a quello già salvato, lo scarta con `200` e `stored: false` (evita il problema del punto 7 di §12: un arretrato che sostituisce il dato più recente).

### 3.2 `POST /api/uplink/monitor-product` (keogram, startrails)

`multipart/form-data` come §4.7, due parti: `meta` (JSON, ≤ 4 096 byte) e `image` (`image/jpeg`, ≤ 1 048 576 byte, primi byte `FF D8 FF`). Nessun gzip.

`meta`: `schemaVersion` (1), `deviceId`, `profileName` (facoltativo), `productId` (`^[A-Za-z0-9-]{1,64}$`, idempotenza), `capturedAt` (UTC, fine della notte), `kind` (`keogram` o `startrails`).

Il sito tiene **l'ultimo prodotto per tipo e dispositivo** (due immagini per dispositivo), come l'immagine allsky tiene solo l'ultima. La notte la calcola il sito (§4.1: il client non invia `nightId`).

### 3.3 Timelapse: nessun caricamento del video in v1 [S]

Un MP4 notturno pesa decine di MB; il Blob Hobby include 1 GB di archiviazione [V, sotto] e il tetto della richiesta di una funzione Vercel è 4,5 MB [V, piano v1 §2.4]. Proposta: il timelapse esce già su YouTube (P7). Il client invia in `PUT /monitor` un campo facoltativo `timelapseUrl` (https, host in una allowlist configurata sul sito, per esempio `www.youtube.com` e `youtu.be`), e il sito mostra il link o l'incorporamento. **Da decidere con il team del sito**, perché richiede una allowlist di host come per le dirette (§4.3). Se la decisione è «niente timelapse sul sito», il campo si omette.

### 3.4 `GET /api/uplink/monitor` (pubblico)

Senza Bearer, `?device=<deviceId>` facoltativo (formato errato: `400 invalid_device`, come §4.5). Restituisce lo stato corrente senza `deviceId` né dati tecnici, con un campo `stale` (vero se `capturedAt` è più vecchio di 20 minuti, come §4.10 [S]) e gli URL degli ultimi prodotti.

## 4. Fixture da creare o cambiare

Nuove, in `contract/` (copie identiche nei client):

- `monitor-v1.request.json`, `monitor-v1.response.json`, `monitor-v1.get.json`;
- `monitor-product-v1.meta.json`, `monitor-product-v1.response.json`.

Fixture esistenti: **nessuna da cambiare.** Resta aperta la discrepanza già nota di `health-v1.json` (`maxFramesPerBatch` 25 contro 100 del server, UPLINK-SPEC §12 punto 1): non riguarda questa richiesta.

## 5. Impatto per repository

| Repository | Cosa cambia | Da rilasciare prima di |
|---|---|---|
| Sito (`sito`) | `src/lib/uplink/`: validatori `monitor-schema.ts`, `monitor-product-schema.ts`, handler e route in `src/app/api/uplink/monitor` e `monitor-product`; tabella o chiave di stato per dispositivo (`migrations.ts`); storage dei due prodotti; test in `tests/uplink/`; `docs/UPLINK-SPEC.md` (sezioni 3, 4, 5, 6); `docs/uplink-api.md`; pagina pubblica (P12.4) | il client |
| Kit di conformità | `tests/uplink/conformance-core.ts`: nuovi tipi `monitor` e `monitor-product-meta`; `--all-fixtures` copre le nuove fixture | il client |
| Plugin NINA | **Nessuna modifica.** Il monitoraggio parte da All Sky Brain con un `deviceId` proprio (per esempio quello della camera all-sky). | — |
| Software allsky attuale | Nessuna modifica: l'immagine usa già `POST /allsky`. | — |
| All Sky Brain | P12.3: compositore dello snapshot, invio dalla coda esistente (`AllSky.Publish`), fixture copiate identiche, test con le fixture | dopo sito e kit |

## 6. Ordine di rilascio

1. Fixture nuove e kit di conformità (con i loro test).
2. Sito: validatori, route, storage, documentazione, pagina pubblica.
3. All Sky Brain (P12.3). Nessun client viene aggiornato prima che le fixture siano pronte (UPLINK-SPEC §8).

## 7. Stima dei volumi contro i limiti

### Limiti correnti letti sulla documentazione ufficiale

| Risorsa | Limite del piano gratuito | Fonte e data |
|---|---|---|
| Vercel Blob, archiviazione | 1 GB | [V] `vercel.com/docs/vercel-blob/usage-and-pricing`, aggiornata il 23/09/2026 |
| Vercel Blob, operazioni avanzate | **2 000 al mese**; un caricamento, una copia o un elenco contano come una; **le cancellazioni sono gratuite** | [V] stessa pagina |
| Vercel Blob, operazioni semplici | 10 000 al mese; gli accessi con cache HIT non contano | [V] stessa pagina |
| Vercel Blob, trasferimento | 10 GB | [V] stessa pagina |
| Vercel Blob, al superamento | nessun costo, ma Blob non è più accessibile per 30 giorni | [V] stessa pagina |
| Vercel Blob, frequenza | 15 operazioni avanzate al secondo | [V] stessa pagina |
| Funzioni Vercel, invocazioni | 1 000 000 al mese | [V] `vercel.com/docs/plans/hobby`, aggiornata il 14/09/2026 |
| Funzioni Vercel, body della richiesta | 4,5 MB | [V] piano v1 §2.4 (`vercel.com/docs/functions/limitations`) |
| Piano Hobby, uso | «non commerciale, solo personale» (fair use) | [V] `vercel.com/docs/plans/hobby` |
| Neon Free, compute | 100 CU-ore al mese; si sospende dopo 5 minuti di inattività | [V] `neon.com/docs/introduction/plans`, letta il 23/10/2026 (testo riassunto dallo strumento di lettura) |
| Neon Free, storage | 1 GB per progetto | [V] stessa pagina |

Il sito usa questi servizi (Blob per le immagini, Neon per i dati) secondo UPLINK-SPEC §4.7 e il brief allsky.

### Calcolo per l'immagine all-sky (un caricamento = una operazione avanzata)

Ipotesi [S]: 30 notti al mese, notte di 12 ore, pubblicazione solo di notte.

| Intervallo | Caricamenti per notte | Operazioni al mese | Quota del limite (2 000) |
|---|---|---|---|
| 1 min, tutto il giorno | 1 440 al giorno | 43 200 | 2 160 % |
| 5 min | 144 | 4 320 | 216 % |
| 10 min | 72 | 2 160 | 108 % |
| 15 min | 48 | 1 440 | 72 % |
| 20 min | 36 | 1 080 | 54 % |
| 30 min | 24 | 720 | 36 % |
| 60 min | 12 | 360 | 18 % |

Conseguenze:

1. **Il valore predefinito di 60 s indicato nel brief allsky (minimo 15 s) non è sostenibile su Hobby.** Anche un caricamento ogni 5 minuti di notte supera il limite.
2. Il sito segna l'immagine come «ferma» oltre 20 minuti (UPLINK-SPEC §4.10). Un intervallo di 20 minuti o più la mostra sempre ferma. L'intervallo utile è quindi **sotto i 20 minuti**; 15 minuti usa il 72 % del limite di Blob, senza contare altro.
3. Il limite di 2 000 operazioni è **condiviso con le miniature del plugin NINA** (`frames[].thumbnail`, UPLINK-SPEC §4.1), che usano lo stesso storage Blob [NV: lo storage delle miniature è Blob? non confermato dal codice]. Quante operazioni consumi oggi non lo so: va letto nella sezione Observability di Vercel [NV].
4. Keogram e startrails aggiungono 2 caricamenti per notte, cioè 60 al mese, trascurabili.

Opzioni per il team del sito, da scegliere con Robert [S]:

- **A.** Restare su Hobby: intervallo 15 minuti, solo notte e crepuscolo, e misurare prima il consumo attuale di Blob.
- **B.** Passare al piano Pro di Vercel (a pagamento): toglie questi limiti. Attenzione al punto sul fair use: Hobby è per uso non commerciale, e il sito ha anche una sezione negozio di stampe.
- **C.** Spostare le sole immagini del monitoraggio su un altro storage, per esempio Cloudflare R2. Il piano v1 §2.4 registra per R2: 10 GB gratuiti, 1 milione di operazioni di Classe A al mese, uscita gratuita [V]; che il caricamento conti come Classe A è [NV].
- **D.** Salvare l'immagine nel database invece che in Blob: elimina le operazioni di Blob, ma tiene sveglio il compute di Neon (vedi sotto).

### Calcolo per lo stato (`PUT /monitor`), se salvato nel database

Neon si sospende dopo 5 minuti senza attività [V]. Un compute da 0,25 CU [NV: la taglia minima non è stata verificata] acceso 12 ore per 30 notti consuma 0,25 × 360 = **90 CU-ore**, il 90 % del limite di 100, prima di qualunque lettura dei visitatori e delle scritture del plugin NINA [S: stima mia]. Con aggiornamenti ogni 15 minuti il compute resta sveglio circa 5-6 minuti per ogni scrittura: circa 33 CU-ore al mese [S: stima mia]. Quindi, anche per lo stato, **l'intervallo di 15 minuti** è un valore ragionevole e una scrittura al minuto non lo è.

### Invocazioni di funzioni

Con 15 minuti: circa 48 immagini + 48 stati + 3 prodotti per notte, cioè circa 3 000 invocazioni al mese, lo 0,3 % del milione incluso [V per il limite, S per il calcolo]. Le letture dei visitatori non sono stimabili da qui [NV].

## 8. Rischi

- **Superamento dei limiti di Blob:** blocco per 30 giorni, che colpirebbe anche le miniature del plugin NINA (stesso store, se è lo stesso) [NV].
- **Immagine «ferma» sul sito** con intervalli di 20 minuti o più (soglia di §4.10).
- **Dato vecchio che sostituisce quello nuovo** (UPLINK-SPEC §12 punto 7): il client invia solo l'ultimo stato e l'ultima immagine dopo un'interruzione; il sito dovrebbe confrontare `capturedAt`.
- **Privacy:** la lettura è pubblica. L'elenco chiuso di P12.1 esclude posizione, percorsi, indirizzi IP, log e token; il nome del soggetto è testo libero (80 caratteri) e viene mostrato così com'è.
- **Retrocompatibilità:** nessuna, perché le route sono nuove; i client in produzione non cambiano.
- **Dati già salvati:** nessuno viene modificato.
- **Fair use di Hobby:** vedi opzione B.

## 9. Domande aperte per il team del sito e per Robert

1. Dove si conserva lo stato: database o file JSON in Blob?
2. Timelapse: link a YouTube con allowlist (§3.3) oppure niente sul sito?
3. Limite di frequenza dello stato e soglia di «ferma» per il monitoraggio: valgono 60 s e 20 minuti proposti?
4. Piano: restare su Hobby con intervallo di 15 minuti (opzione A), o cambiare (B, C, D)?
5. Il `deviceId` del monitoraggio è lo stesso della camera all-sky già usata per `POST /allsky`?

## 10. Fonti

- Project «codici astrofotografia»: `claude/UPLINK-SPEC.md` (commit `aa18177` del sito, 21/10/2026), `claude/allsky-uplink-brief.md`, `claude/uplink-expert-agent.md` (procedura (d)).
- Vercel Blob, prezzi e limiti: https://vercel.com/docs/vercel-blob/usage-and-pricing (aggiornata il 23/09/2026).
- Piano Vercel Hobby: https://vercel.com/docs/plans/hobby (aggiornata il 14/09/2026).
- Piani Neon: https://neon.com/docs/introduction/plans (letta il 23/10/2026).
- Piano di progetto: `docs/ralph/IMPLEMENTATION_PLAN.md` §2.4, P12.
