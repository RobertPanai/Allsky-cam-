# Agenti e chat: collegare All Sky Brain (server MCP locale)

Questo documento spiega come un agente (per esempio la chat di Claude) può leggere lo stato del PC di Robert, e come si abilita il server. Dettagli tecnici e specifica: `docs/MCP-LOCALE.md`.

## Cosa si può leggere

Il server MCP espone quattro strumenti, tutti di sola lettura:

- `engine_status`: stato del motore (fase del giorno, altezza di Sole e Luna, camera, ultimo frame, disco, lavori).
- `nina_state`: ultimo stato letto da NINA (mount, parcheggio, soggetto).
- `target_marker`: posizione attesa del soggetto sul frame all-sky.
- `view_png`: immagine di una vista. **Non disponibile in questa versione**: il render non è ancora collegato (P17.3).

Non si scrive nulla, non si inviano comandi alla camera o al motore, non si leggono file. Le risposte non contengono percorsi, token, né le coordinate esatte dell'osservatorio.

## Come si abilita

Il server è **spento di default**. Quando è spento non si apre nessuna porta.

1. Aprire il file `config.json` nella cartella dati di All Sky Brain (di norma `%LOCALAPPDATA%\AllSky`, vedi `src/AllSky.Core/AppPaths.cs`).
2. Aggiungere o modificare la sezione:

   ```json
   "mcp": {
     "enabled": true,
     "port": 8765
   }
   ```

   `port` va da 1024 a 65535; il valore predefinito è 8765.
3. Riavviare All Sky Brain. Una modifica a mano del file non è applicata al volo: l'avviso di cambio configurazione scatta solo dalle modifiche fatte dall'app, quindi serve il riavvio. Il log dell'Host dice se il server è partito (`Server MCP in ascolto su 127.0.0.1:8765 (sola lettura).`) oppure che l'avvio non è riuscito (per esempio porta occupata).

[NV] Nel repo non ho trovato un interruttore nell'interfaccia dell'app: l'abilitazione è per ora solo da file.

Per spegnerlo: `"enabled": false`, poi riavvio (o salvataggio dall'app, che applica subito).

## Come si collega la chat al PC

Fatti noti:

- Il collegamento è quello che l'app desktop chiama **«Link to this computer»** (dato di Robert; la stringa non è nel repo, quindi [NV] sul comportamento esatto).
- Il server ascolta solo su `127.0.0.1` del PC. Un agente che gira su un altro computer non lo raggiunge da solo: serve un collegamento che porti la chiamata fino al PC.

Non noto [NV]:

- Se la sessione della chat può raggiungere `http://127.0.0.1:8765/mcp` tramite il collegamento «Link to this computer», e in quale forma il tool compare alla chat.
- Se l'app desktop registra il server in automatico o va configurato a mano.

Quando il collegamento è attivo e il server è acceso, l'URL da usare è **`http://127.0.0.1:8765/mcp`** (con 127.0.0.1, non `localhost`: vedi `docs/MCP-LOCALE.md`, deviazione 2).

## Sicurezza: da leggere prima di accendere

- **Nessuna autenticazione.** Chiunque sul PC può chiamare il server e leggere lo stato, il cielo e (quando ci sarà) le viste. Per questo è spento di default e ascolta solo in locale.
- **Nessuna porta sulla LAN.** Il server non ascolta su 0.0.0.0 né su indirizzi di rete.
- **Protezione dal browser.** Le richieste con `Origin` non locale vengono rifiutate, e anche quelle con `Host` estraneo (un sito che tenta un DNS rebinding verso 127.0.0.1 non passa).
- **Limite del corpo.** 64 KB. Oltre, la richiesta viene rifiutata.
