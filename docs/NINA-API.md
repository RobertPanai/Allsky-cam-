# NINA e ninaAPI: fatti per l'integrazione (P11.0)

Stato: documento di lavoro del 22/10/2026. Ogni riga ha lo stato **[V]** (verificato con fonte letta) oppure **[NV]** (da verificare prima dell'uso).

## 1. Fonte usata

- Plugin **ninaAPI** (christian-photo), repository `https://github.com/christian-photo/ninaAPI`, commit letto `353a853` ("update changelog"), licenza **MPL-2.0** (file `LICENSE`). [V]
- Lettura fatta sul sorgente C# (`ninaAPI/WebService/API.cs`, `WebService/V2/*.cs`, `Properties/Settings.settings`). La pagina della documentazione `christian-photo.github.io/github-page/projects/ninaAPI/v2/doc/api` non si legge con un fetch (è generata via JavaScript): **nessun dato è preso da lì**. [V]
- Il sorgente di NINA stesso non è nel repository del plugin: i tipi `TelescopeInfo`, `ITelescopeMediator` ecc. sono nel core di NINA. Per questo i nomi dei campi JSON di alcune risposte restano [NV]. [V]

## 2. Connessione

| Cosa | Valore | Stato |
|---|---|---|
| Porta predefinita | **1888** (`Settings.settings`, chiave `Port`) | [V] |
| Porta per profilo | opzione `ProfileDependentPort`, predefinita **off** | [V] |
| Porta occupata | il plugin cerca la prima porta libera a partire da quella impostata (`CoreUtility.GetNearestAvailablePort`) | [V] |
| Base REST | `http://<host>:<porta>/v2/api/...` (`WithWebApi("/v2/api")`) | [V] |
| WebSocket | `ws://<host>:<porta>/v2/socket` (`WithModule(new WebSocketV2("/v2/socket"))`) | [V] |
| Ascolto | `http://*:<porta>` (su tutte le interfacce) | [V] |
| Autenticazione | **nessuna** trovata nel codice: nessun controllo di token o password. Solo CORS con header `Authorization` consentito. | [V] |
| Versione minima NINA | "Minimum application version was increased to NINA 3.2" (Changelog). Robert ha **3.2.0.9001**: compatibile. | [V] changelog; la riga esatta della release va letta |

**Conseguenza di sicurezza [V]:** l'API non ha autenticazione e ascolta su tutte le interfacce. L'app deve connettersi solo a `127.0.0.1` (NINA sullo stesso PC, come confermato da Robert) e non deve mai esporla; non usare ninaAPI come punto di accesso da fuori.

## 2b. Formato delle risposte (verificato dal sorgente)

Ogni risposta REST è un contenitore JSON, da `ninaAPI/Utility/CoreUtility.cs`, classe `HttpResponse`:
`{"Response": <dati>, "Error": "", "StatusCode": 200, "Success": true, "Type": "API"}`. I dati veri stanno dentro `Response`. [V]
Gli endpoint `sequence/json` e `sequence/state` restituiscono un array (lista di oggetti) dentro `Response`. [V]
Gli errori usano `StatusCode` (es. 409 per "Sequence is not initialized") e `Success: false`. [V]
Il client deve scartare il contenitore prima di leggere i campi.

## 3. Endpoint letti dal sorgente (solo GET, nessun POST necessario)

| Cosa serve a All Sky Brain | Endpoint | Stato |
|---|---|---|
| Mount: stato (incluso parcheggio) | `GET /v2/api/equipment/mount/info` → `MountInfo` (eredita `TelescopeInfo`) | endpoint [V]; **nomi dei campi JSON [NV]** (tipo in NINA core). Il codice del plugin usa `mount.GetInfo().AtPark` e `.Slewing` e `TrackingMode`: quindi lo stato di park esiste nel modello. |
| Soggetto in sequenza (nome, coordinate) | `GET /v2/api/sequence/json` (albero della sequenza, con target) | endpoint [V]; **campi del target [NV]** (il codice imposta `TargetName` e `InputCoordinates.Coordinates` con epoca J2000, `Epoch.J2000`) |
| Stato della sequenza | `GET /v2/api/sequence/state` (409 se la sequenza non è inizializzata) | [V] |
| Log di NINA (ultime righe) | `GET /v2/api/application/logs?lineCount=N&level=...` | [V]: legge l'ultimo file in `CoreUtil.APPLICATIONTEMPPATH/Logs`, filtra per livello. **Formato delle righe [NV]**. |
| Versione del plugin e di NINA | `GET /v2/api/version`, `GET /v2/api/version/nina` | [V] endpoint |
| Profilo corrente | `GET /v2/api/profile/show` | [V] endpoint |
| Cambio di soggetto impostato da NINA | `GET /v2/api/sequence/set-target` (scrive: **non usare**, la v1 è sola lettura) | [V] |
| Distanza dalla Luna | `GET /v2/api/astro-util/moon-separation` | [V] endpoint; **parametri e campi [NV]**. Per la distanza usiamo comunque il nostro calcolo (P11.5), che è indipendente. |

Endpoint **da non usare in v1** (scrivono o muovono hardware): `mount/park`, `unpark`, `slew`, `flip`, `sync`, `dome/*`, `sequence/start|stop|load|skip`, `camera/capture`, `focuser/auto-focus`, `filterwheel/change-filter`.

## 4. Eventi WebSocket (`/v2/socket`)

Ogni messaggio ha `Event` (nome) e i dati dell'evento, con un timestamp di invio a `DateTime.Now` del PC di NINA. [V] (`WebSocketV2.SendAndAddEvent`).

Eventi che servono a P11.7 (cronologia), letti dal sorgente [V]:
- `MOUNT-PARKED`, `MOUNT-UNPARKED`, `MOUNT-BEFORE-FLIP`, `MOUNT-AFTER-FLIP`, `MOUNT-HOMED`, `MOUNT-CONNECTED`, `MOUNT-DISCONNECTED`
- `SEQUENCE-STARTING`, `SEQUENCE-FINISHED`, `SEQUENCE-ENTITY-FAILED`
- `AUTOFOCUS-STARTING`, `AUTOFOCUS-POINT-ADDED`, `AUTOFOCUS-FINISHED`
- `GUIDER-START`, `GUIDER-STOP`, `GUIDER-DITHER`
- `CAMERA-CONNECTED`, `CAMERA-DISCONNECTED`, `CAMERA-DOWNLOAD-TIMEOUT`, `API-CAPTURE-FINISHED`
- `PROFILE-CHANGED`, `STACK-UPDATED`, `STACK-STATUS`
- `SAFETY-CONNECTED`, `SAFETY-DISCONNECTED`, `WEATHER-CONNECTED`, `WEATHER-DISCONNECTED`

Non ci sono eventi di cambio soggetto né di cambio filtro nell'elenco: P11.10 (capitoli) va ridotto a ciò che è confermato, oppure il cambio soggetto va dedotto da `sequence/json` [NV].

## 5. Log di NINA (lettura diretta del file, alternativa)

- Cartella: `%localappdata%\NINA\logs` (documentazione NINA). [V] da `nighttime-imaging.eu`.
- Livelli: Error, Warning, Info, Debug, Trace. [V] stessa fonte.
- Formato delle righe e rotazione dei file: **[NV]**. Il plugin ninaAPI legge il file più recente da `APPLICATIONTEMPPATH/Logs` (cartella diversa da quella della documentazione): da chiarire su un PC reale.

## 6. Plugin installato sul PC di Robert?

**Non verificato**: questa sessione non ha il computer di Robert collegato. Modo di verifica per Robert: in NINA, menu Plugin, cercare "ninaAPI" / "Advanced API"; oppure aprire `http://127.0.0.1:1888/v2/api/version` nel browser del PC con NINA aperto (se risponde, il plugin è attivo). [V] per l'URL, [NV] per il risultato.

## 7. Cosa resta da verificare su NINA reale (lista per P11.13)

1. Nomi dei campi di `mount/info` (in particolare `AtPark`, RA/Dec e la loro epoca, alt/az, tracking).
2. Forma di `sequence/json` per trovare il target corrente (nome, RA/Dec) quando la sequenza è in corso.
3. Formato delle righe del log e rotazione.
4. Se il plugin è installato e attivo sul PC di Robert; porta effettiva (1888 o successiva libera).
5. Scarto d'orologio tra PC di NINA e PC all-sky (stesso PC: atteso ~0, da misurare).
