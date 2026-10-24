# Server MCP locale di sola lettura (P17.4)

Stato al 23/10/2026. Branch `ralph/desktop-v1`. Questo documento registra cosa è stato letto sulla specifica MCP, cosa decide il codice e cosa resta non verificato. Per come collegare la chat al PC vedi `docs/AGENTI.md`.

## 1. Cosa fa il server

- Ascolta **solo su 127.0.0.1**, sulla porta `mcp.port` (predefinita 8765), e **solo se** `mcp.enabled` è vero (predefinito: spento, nessun listener).
- Endpoint unico `/mcp`, solo `POST`. Corpo JSON-RPC 2.0 di massimo 64 KB.
- Quattro tool, tutti di sola lettura: `engine_status`, `nina_state`, `target_marker`, `view_png`.
- Nessuna scrittura, nessun comando verso il motore, nessuna lettura di file arbitrari, nessuna sessione.
- Gira nel processo dell'Host su task propri. Un errore del server non ferma la cattura.

## 2. Specifica letta (con URL)

Letture fatte il 23/10/2026 con WebFetch. Per ogni punto: **[V]** = letto nella pagina citata; **[NV]** = non letto, non verificato o pagina troncata.

### 2.1 Versione di protocollo

| Punto | Fonte | Esito |
|---|---|---|
| La revisione più recente è `2026-07-28`. Non ha `initialize`, non ha sessioni, ogni richiesta porta `_meta` e l'intestazione `MCP-Protocol-Version` è obbligatoria. | https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http , https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning | [V] |
| La revisione `2025-11-25` ha il ciclo `initialize` → `notifications/initialized`, con sessioni opzionali. | https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle , https://modelcontextprotocol.io/specification/2025-11-25/basic/transports | [V] |

**Decisione:** il server implementa **solo `2025-11-25`**, che è la revisione con il ciclo richiesto nel brief (initialize, notifications/initialized, tools/list, tools/call, ping). La revisione `2026-07-28` non è implementata (vedi §4).

### 2.2 Ciclo di vita e initialize

| Punto | Fonte | Esito | Nel codice |
|---|---|---|---|
| `initialize` ha `params.protocolVersion`, `capabilities`, `clientInfo`; il risultato ha `protocolVersion`, `capabilities`, `serverInfo`, `instructions`. | lifecycle 2025-11-25 | [V] | `McpProtocol.Initialize` |
| Se il server non supporta la versione richiesta, risponde con una versione che supporta (la più recente possibile). | lifecycle 2025-11-25, "Version Negotiation" | [V] | risponde con `2025-11-25` |
| Dopo `initialize` il client manda `notifications/initialized` (notifica, senza `id`). | lifecycle 2025-11-25 | [V] | accettata con 202 |
| Prima di `initialize` il server non deve inviare richieste diverse da ping e logging. | lifecycle 2025-11-25 | [V] | il server non invia richieste |
| Forma di `ping` e della sua risposta `{}`. | pagina ping linkata dal lifecycle, **non letta** | [NV] | risposta `result: {}` (forma comune, non verificata) |

### 2.3 Trasporto Streamable HTTP (2025-11-25)

| Punto | Fonte | Esito | Nel codice |
|---|---|---|---|
| Il server DEVE validare `Origin`; se presente e non valido risponde **403**. Il corpo può essere un errore JSON-RPC senza `id`. | transports 2025-11-25, "Security Warning" | [V] | 403 |
| Il server DOVREBBE ascoltare solo su localhost (127.0.0.1), non su 0.0.0.0. | stesso punto | [V] | ascolto solo su 127.0.0.1 |
| Il client DEVE inviare `Accept` con `application/json` e `text/event-stream`. Il server non verifica `Accept`. | transports 2025-11-25, "Sending Messages" | [V] | non controllato (scelta) |
| Il corpo di un POST è **un solo** messaggio JSON-RPC (richiesta, notifica o risposta). I batch non sono ammessi. | transports 2025-11-25 | [V] | batch rifiutato con 400 e `-32600` |
| Notifica o risposta accettata: **202 Accepted** senza corpo. | transports 2025-11-25 | [V] | 202 |
| Richiesta: risposta `application/json` oppure `text/event-stream`. Il server sceglie. | transports 2025-11-25 | [V] | sempre `application/json` |
| `GET`: il server risponde con `text/event-stream` oppure **405**. | transports 2025-11-25, "Listening for Messages" | [V] | 405 con `Allow: POST` |
| `DELETE`: il server PUÒ rispondere 405 (non permette di terminare sessioni). | transports 2025-11-25, "Session Management" | [V] | 405 |
| `Mcp-Session-Id`: il server PUÒ assegnarlo in `initialize`. Se non lo assegna non c'è sessione. | transports 2025-11-25, "Session Management" | [V] | non assegnato, nessuna sessione |
| `MCP-Protocol-Version` DEVE essere inviato dal client dopo `initialize`. Versione non valida o non supportata: **400**. Assente: il server DOVREBBE assumere `2025-03-26`. | transports 2025-11-25, "Protocol Version Header" | [V] | valore non supportato → 400 e `-32602` con `data.supported`; assente → accettato |
| Codice JSON-RPC per versione non supportata: `-32602` nell'esempio di errore del lifecycle. | lifecycle 2025-11-25, "Error Handling" | [V] (esempio) | `-32602` |

### 2.4 Messaggi e tool

| Punto | Fonte | Esito | Nel codice |
|---|---|---|---|
| Il server che espone tool dichiara la capability `tools`. | server/tools 2025-11-25 | [V] | `capabilities.tools.listChanged = false` |
| `tools/list` risponde con `tools[]` (name, description, inputSchema, annotations). | server/tools 2025-11-25 | [V] | `McpTools.Describe` |
| `tools/call` ha `params.name` e `params.arguments`; il risultato ha `content[]` con `type: "text"` e `isError`. | server/tools 2025-11-25 | [V] | `McpProtocol.CallToolAsync` |
| Tool sconosciuto = **errore di protocollo** JSON-RPC, esempio `-32602` "Unknown tool: ...". | server/tools 2025-11-25, "Error Handling" | [V] | `-32602` "Unknown tool: NOME" |
| Errori di validazione degli argomenti e di esecuzione = **risultato con `isError: true`**, pensato perché il modello corregga. | server/tools 2025-11-25, "Error Handling" | [V] | argomenti non validi → `isError: true` |
| Argomenti che non sono un oggetto JSON: errore di protocollo `-32602`. | server/tools, "malformed requests" | [V] (per "malformed requests") | `-32602` |
| Codici JSON-RPC standard `-32700` (parse), `-32600` (invalid request), `-32601` (method not found), `-32602` (invalid params), `-32603` (internal). Una notifica non riceve risposta. | https://www.jsonrpc.org/specification (letto tramite riassunto) | [V] | come da tabella |
| Immagine nel risultato: `{type: "image", data: <base64>, mimeType}`. | server/tools 2025-11-25, "Image Content" | [V] | `view_png` |
| Il nome del campo di `annotations` per "sola lettura" è `readOnlyHint`. | pagina dello schema 2025-11-25 | **[NV]**: la pagina letta era troncata prima di `ToolAnnotations` | `annotations.readOnlyHint = true` (nome da verificare) |

### 2.5 Regole del server aggiunte da noi (non in specifica)

| Regola | Motivo | Stato |
|---|---|---|
| Host ammesso solo `127.0.0.1:porta` o `localhost:porta`; altri Host rifiutati (anti DNS rebinding). | La specifica parla solo di Origin, non di Host. | [NV] specifica; scelta di sicurezza nostra |
| Content-Type obbligatorio `application/json` (altrimenti 415). | Scelta nostra. | [NV] specifica |
| Corpo oltre 64 KB: 413. | Richiesta del brief P17.4. | scelta nostra |
| Percorso diverso da `/mcp`: 404. Metodo diverso da POST: 405. | Richiesta del brief. | scelta nostra (405 per GET/DELETE è ammesso dalla specifica) |

## 3. Cosa dice il codice, in breve

- `src/AllSky.Mcp/McpServer.cs`: listener `System.Net.HttpListener` su `http://127.0.0.1:porta/`. Controlli HTTP nell'ordine Host, Origin, percorso, metodo, Content-Type, dimensione. Start/Stop idempotenti; Stop chiude il listener e libera la porta.
- `src/AllSky.Mcp/McpProtocol.cs`: JSON-RPC 2.0, versione `2025-11-25`.
- `src/AllSky.Mcp/McpTools.cs`: i quattro tool. Ogni risposta è costruita campo per campo: nessun `StatusDto` o `NinaState` viene serializzato intero. Camera senza identificativo (può contenere un indirizzo di rete). Coordinate del sito mai esposte.
- `src/AllSky.Mcp/McpHttpPolicy.cs`: regole pure (Host, Origin, Content-Type) e filtro `McpRedaction`: testi con percorsi, IP, URL o parole come token/password sono sostituiti con un messaggio generico.
- `src/AllSky.Host/Mcp/McpServerHost.cs`: legge `config.Mcp` e fa Start/Stop. Collegato all'Host in `AllSkyHost` tramite `AllSkyEngine.ConfigApplied`, che scatta a ogni `UpdateConfig` riuscito.
- `AppConfig.McpConfig` (`mcp`): `enabled` (false), `port` (8765, validato 1024..65535 sempre, come `ui.port`). Un file senza sezione `mcp` legge il server spento.

## 4. Deviazioni dalla specifica o dal brief

1. **Revisione 2026-07-28 non implementata.** Un client che la usa riceve 400 con `-32602` (non `-32022` della revisione nuova). Secondo la specifica 2026, un client così fa fallback a `initialize`: quindi dovrebbe funzionare, ma **non è provato**.
2. **`localhost` non è instradato.** `HttpListener` manda al codice solo le richieste il cui Host corrisponde al prefisso `127.0.0.1`. Con `Host: localhost:porta` il sistema risponde 404 e il codice non lo vede. Il prefisso `localhost` non si apre di proposito, perché potrebbe associare anche `::1` e il vincolo è solo 127.0.0.1. Il client deve usare `http://127.0.0.1:porta/mcp`. Il controllo `IsAllowedHost` accetta comunque `localhost` (per coerenza col brief), ma non è raggiungibile. Questo vale per l'`HttpListener` gestito di Linux. **Misurato sulla CI Windows il 24/10/2026:** con http.sys `Host: localhost:porta` viene instradato e la politica lo accetta (200); l'ascolto resta comunque solo su 127.0.0.1. Il test `Host_Localhost_RispondeSoloSeLaPiattaformaLoInstrada` ammette entrambi i comportamenti. Per sicurezza il client resta 127.0.0.1.
3. **Host estranei: 404 o 403.** Con Host che non corrisponde al prefisso la risposta è 404 del sistema; con Host corretto e porta diversa è 403 del codice. In entrambi i casi nessuna lettura.
4. **`view_png` senza render.** Nell'Host il renderer è `null`: il tool risponde «rendering non disponibile». Il render WPF non è collegato (P17.3, ancora parziale nel piano).
5. **Porta del server 1..65535, configurazione 1024..65535.** Il server accetta anche porte basse per i test; la configurazione le rifiuta.

## 5. Dubbi aperti

- **Età massima delle coordinate NINA** (`target_marker`): default 60 secondi, scelto dall'autore del codice. **Non giustificato da una misura**: va confermato con Robert (il piano chiede di non imporre valori senza giustificazione).
- **Nome `readOnlyHint`**: vedi §2.4, da verificare sulla pagina dello schema intera.
- **Ping**: forma della risposta `{}` non letta sulla pagina dedicata (§2.2).
- **Windows**: i test girano sul runner Linux. `HttpListener` su Windows (http.sys) instrada probabilmente allo stesso modo, ma **non provato**. Il progetto Windows (test con xunit e pacchetti NuGet) non è stato compilato qui.
- **Desktop (WPF)** non è stato toccato né compilato. La modifica ad `AllSkyEngine` (evento `ConfigApplied`) è additiva.
- **Collegamento chat-PC**: vedi `docs/AGENTI.md`, parte [NV].

## 6. Cosa non è stato fatto

- Nessun commit, nessun push, `docs/ralph/IMPLEMENTATION_PLAN.md` non toccato, `NOTES.md` non aggiornato.
- Nessun render WPF (P17.3).
- Nessuna revisione 2026-07-28, nessun SSE, nessuna sessione.
- Nessun test su Windows.
