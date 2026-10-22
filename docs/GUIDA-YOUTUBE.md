# Configurare il caricamento su YouTube

Questa guida spiega come collegare All Sky Brain al proprio account YouTube, per caricare automaticamente il timelapse di ogni notte. Il riferimento tecnico (flusso OAuth, endpoint, scope) è in [YOUTUBE-OAUTH.md](YOUTUBE-OAUTH.md).

Legenda:

- **[V]** verificato su una pagina ufficiale Google. Le pagine lette e la data sono nell'elenco «Fonti» in fondo.
- **[NV]** non verificato: controllare nella Console Google prima di fidarsi.
- Il comportamento dell'applicazione è descritto dal codice, non da una fonte Google, e non ha questa marcatura.

Ogni utente usa un proprio progetto Google Cloud. All Sky Brain non contiene credenziali.

---

## Prima di iniziare

- Un account Google con il canale YouTube su cui caricare i video.
- All Sky Brain versione 0.3.0 o successiva, con la sezione YouTube.
- Connessione a Internet sul PC: il caricamento parte solo con rete disponibile.

Il collegamento richiede due valori dalla Google Cloud Console: **client ID** e **client secret** di una credenziale di tipo «Desktop app». All Sky Brain richiede entrambi.

---

## Passo 1: progetto Google Cloud

1. Aprire la Google Cloud Console e scegliere un progetto esistente, oppure crearne uno nuovo.
2. La procedura di creazione del progetto e i nomi dei campi non sono stati letti in una fonte ufficiale: [NV].

## Passo 2: abilitare la YouTube Data API v3

1. Nella Console, aprire **Menu > APIs & Services > Library** e cercare **YouTube Data API v3**. [V] per il percorso del menu e per la pagina Library, dalla documentazione Google.
2. Premere **Enable**. [V]

Google richiede che l'applicazione che chiama le API abiliti le API nel proprio progetto. [V]

## Passo 3: schermata di consenso (Google Auth platform)

Aprire **Google Auth platform** e completare le sezioni del menu nell'ordine seguente. Le voci e i pulsanti sono dalla guida Google «Configure OAuth consent». [V]

1. **Branding**: se compare il messaggio di configurazione non ancora completata, premere **Get Started**. In **App Information** inserire il nome dell'app e l'email di supporto, poi premere **Next**.
2. **Audience**: scegliere il tipo di utente. Per un account Google personale si sceglie **External**. [V] per la scelta del tipo di utente.
3. **Contact Information**: inserire un'email per le comunicazioni, poi premere **Next**.
4. **Finish**: accettare la Google API Services User Data Policy, poi premere **Continue** e **Create**.
5. **Audience**, sezione **Test users** (solo con External): premere **Add users**, inserire il proprio indirizzo Google e premere **Save**.
6. **Data Access**: premere **Add or Remove Scopes**, selezionare lo scope `https://www.googleapis.com/auth/youtube.upload` (descrizione Google: «Manage your YouTube videos») e premere **Save**. [V]

Una volta configurata, la schermata di consenso non si può più eliminare. [V]

## Passo 4: creare la credenziale «Desktop app»

1. Nella Console, aprire **Google Auth platform > Clients**. [V]
2. Premere **Create Client**. [V]
3. In **Application type** scegliere **Desktop app**. [V]
4. Inserire un **Name**. È visibile solo nella Console. [V]
5. Premere **Create**. La credenziale compare in «OAuth 2.0 Client IDs». [V]

Se la Console propone l'opzione per un agente AI, non va selezionata: la pagina Google la presenta come opzione per agenti AI, e All Sky Brain non lo è. [V] per la descrizione dell'opzione.

Copiare il **client ID** e il **client secret**. La pagina Google non dice se la Console mostra il secret per le app desktop, né come copiarlo: [NV]. Google indica il secret come opzionale per le app desktop, che definisce «public clients». All Sky Brain però lo richiede, e senza secret la schermata di collegamento non accetta il campo vuoto (messaggio «Inserisci client ID e client secret.»). [V] per la guida Google; il requisito del secret è una scelta di All Sky Brain.

## Passo 5: stato «Testing» o «In production»

Questo passo decide se il collegamento resta valido nel tempo. Lo stato si cambia da **Audience**. [V]

| Stato | Cosa dice Google | Conseguenza per All Sky Brain |
|---|---|---|
| **Testing** | Il refresh token scade dopo **7 giorni**. Le autorizzazioni degli utenti di test scadono 7 giorni dopo il consenso. Gli utenti di test sono al massimo 100. [V] | Dopo 7 giorni il caricamento notturno si ferma e il collegamento va rifatto. |
| **In production** | Il progetto è disponibile a qualunque account Google. Il passaggio si fa con il pulsante **Publish app**. [V] La regola dei 7 giorni è indicata per lo stato Testing. [V] | Il collegamento non scade per la regola dei 7 giorni. Il token può comunque smettere di funzionare: in quel caso All Sky Brain chiede di collegare di nuovo l'account. Google avverte di prevedere questo caso nel codice. [V] |

Altri punti verificati:

- Con l'app in Testing, Google mostra la schermata «unverified app». Per rimuoverla occorre inviare una richiesta di verifica. [V]
- Uno scope sensibile o riservato, richiesto da un'app non verificata, fa mostrare un avviso. In questo caso i nuovi utenti sono limitati a 100 nel corso della vita del progetto. [V]
- Un account può avere al massimo 100 refresh token per ogni client ID. Un nuovo collegamento invalida il token più vecchio. [V]
- Se `youtube.upload` sia classificato come sensibile, e quindi richieda la verifica dell'app, non è stato verificato: [NV]. La pagina degli scope Google letta non riporta la riga di YouTube.
- Il criterio con cui Google decide se l'app va verificata, e i tempi, non sono verificati: [NV].

All Sky Brain mostra un avviso in stato Testing, con il rimando a questa guida. (Comportamento dell'applicazione, dal codice.)

## Passo 6: collegare l'account dall'app

1. Aprire **Pubblicazione** e scorrere alla sezione **YouTube**.
2. Inserire **Client ID** e **Client secret** copiati al passo 4.
3. Premere **Collega account**. Il browser apre la pagina di autorizzazione di Google.
4. Scegliere l'account e concedere il permesso di caricare video.
5. Tornare nell'app. Il messaggio «Account collegato. Il caricamento automatico è attivo.» conferma il collegamento.

Cosa succede in background:

- All Sky Brain apre un ascoltatore temporaneo su `127.0.0.1`, su una porta libera scelta dal sistema. Non ascolta sulla rete. Si chiude a fine login. [V] per il flusso loopback della guida Google.
- Il login usa PKCE con metodo S256 e il parametro `state`. [V] Google raccomanda entrambi.
- Il tempo massimo di attesa del login è di 5 minuti. (Valore del codice.)
- Il refresh token viene salvato nel file `youtube-auth.json` della cartella dati, cifrato con DPAPI. Il client secret è cifrato allo stesso modo. Il secret non viene mostrato né riletto: per cambiarlo si collega di nuovo l'account.

Se si annulla o il tempo scade, la sezione mostra il motivo e si può riprovare.

## Passo 7: impostazioni del caricamento

Nella stessa sezione YouTube. I valori predefiniti sono nel codice.

| Impostazione | Predefinito | Note |
|---|---|---|
| Ora del caricamento | `12:30` | Formato `HH:mm`, ora locale. Predefinito a mezzogiorno perché la notte osservativa finisce a mezzogiorno. |
| Privacy | `private` | Scelte: privato, non in elenco, pubblico. |
| Tag | `timelapse`, `allsky`, `astrofotografia` | Separati da virgola. Nessun tag vuoto. |
| ID categoria | `28` | Solo cifre. Il valore predefinito non è verificato su una fonte ufficiale: [NV]. Controllarlo nella documentazione YouTube prima di usare la sezione. |
| Luogo | `Osservatorio` | Sostituisce `{luogo}` nei modelli. |
| Modello del titolo | `Timelapse notte del {notte:yyyy-MM-dd}` | Segnaposto: `{data:...}`, `{notte:...}`, `{ora:...}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`. Massimo 100 caratteri (limite letto il 22/10 in una pagina Google, non riletto il 23/10: [NV]). |
| Modello della descrizione | vedi `config.json` | Massimo 5000 byte. I simboli `<` e `>` non sono ammessi (limite letto il 22/10, non riletto il 23/10: [NV]). |

Premere **Salva impostazioni**. Se un campo non è valido, viene evidenziato e nulla viene inviato a YouTube.

Il caricamento automatico parte ogni giorno all'ora scelta. Carica il timelapse dell'ultima notte completata, una volta per notte. Se il PC era spento all'ora prevista, il caricamento parte all'avvio successivo, entro la finestra di recupero di 12 ore (configurabile fino a 24).

## Passo 8: prova di caricamento

1. Nella sezione YouTube premere **Carica ora la notte scorsa**.
2. Confermare nella finestra di dialogo.
3. Attendere lo stato **Video caricato**. Il link **Apri il video su YouTube** porta al video.

Il primo video è il controllo più utile: verificare la privacy effettiva. La documentazione YouTube per `videos.insert` dice che i video caricati da progetti API non verificati «non sono limitati alla visualizzazione privata». [V] Il comportamento reale per questo progetto non è verificato: [NV]. Controllare sul canale la privacy del primo video.

Quota: un `videos.insert` costa 1 unità nel bucket «Video Uploads», con un limite di 100 chiamate al giorno. [V] La pagina Google non dice se questo limite si può aumentare su richiesta: [NV].

---

## Risoluzione dei problemi

Messaggi della sezione YouTube e cosa fare. I testi sono quelli dell'applicazione.

| Messaggio nella sezione YouTube | Causa probabile | Cosa fare |
|---|---|---|
| «Accesso non valido o revocato: collega di nuovo l'account.» | Il refresh token è scaduto (Testing, 7 giorni) o è stato revocato. | Premere **Collega account** di nuovo. Se succede spesso, passare l'app a In production (passo 5). |
| «Autorizzazione negata nel browser.» | Il permesso non è stato concesso. | Riprovare e concedere il permesso. |
| «Nessuna risposta dal browser entro il tempo limite. Riprova.» | Il login non è stato completato entro 5 minuti. | Riprovare. |
| «Inserisci client ID e client secret.» | Uno dei due campi è vuoto. | Copiare entrambi dal passo 4. |
| «Collegamento non riuscito. Controlla la connessione e le credenziali.» | Rete assente o credenziali errate. | Controllare la connessione e il client ID e il secret. |
| «La protezione dei segreti non è disponibile su questo sistema.» | Il sistema non fornisce la cifratura DPAPI all'applicazione. Causa e rimedio: [NV]. | Consultare il log (sezione Log) e segnalare il caso. |
| «Quota giornaliera di YouTube esaurita: nuovo tentativo domani.» | Quota del giorno esaurita. | Nessuna azione: riprova il giorno dopo. |
| «Esito incerto: controlla su YouTube prima di ricaricare.» | Un caricamento precedente non ha dato una risposta chiara. | Verificare su YouTube che il video non ci sia già. Ricaricare crea un duplicato. |
| «Impostazioni o modelli non validi: nessuna chiamata a YouTube.» | Un valore delle impostazioni non è valido. | Correggere i campi evidenziati. |

Per il dettaglio tecnico, il log dell'applicazione (sezione **Log**) riporta gli errori senza segreti.

## Scollegare l'account

Premere **Scollega account**. All Sky Brain invia una richiesta di revoca all'endpoint `https://oauth2.googleapis.com/revoke` [V], con il token come parametro in una POST. La pagina Google mostra il token nella query string; la forma con il token nel corpo, usata dall'applicazione, non è verificata: [NV]. Il file locale viene cancellato anche se la revoca fallisce, e il caricamento automatico si spegne. (Comportamento dell'applicazione, dal codice.)

Per revocare l'accesso anche dal lato Google, usare le impostazioni del proprio account Google: procedura non letta in questo documento [NV].

---

## Fonti

Pagine Google lette il 23/10/2026:

- Guida «Configure OAuth consent» (Google Auth platform, Branding, Audience, Data Access): https://developers.google.com/workspace/guides/configure-oauth-consent
- Guida «Create credentials» (Desktop app): https://developers.google.com/workspace/guides/create-credentials
- Guida «Enable APIs» (pulsante Enable, percorso Library): https://developers.google.com/workspace/guides/enable-apis
- OAuth 2.0 for Mobile & Desktop Apps, guida YouTube (abilitazione API, scope youtube.upload, avviso unverified app): https://developers.google.com/youtube/v3/guides/auth/installed-apps
- OAuth 2.0 for Mobile & Desktop Apps (loopback, PKCE, state, client secret, endpoint, revoca): https://developers.google.com/identity/protocols/oauth2/native-app
- OAuth 2.0 expiration (7 giorni in Testing, limite di 100 refresh token): https://developers.google.com/identity/protocols/oauth2#expiration
- Manage App Audience (Publish app, utenti di test, produzione, verifica): https://support.google.com/cloud/answer/15549945
- Publishing status (gestione dalla pagina Audience): https://support.google.com/cloud/answer/10311615
- Quota (100 chiamate al giorno per videos.insert, 1 unità): https://developers.google.com/youtube/v3/determine_quota_cost
- videos.insert (1 unità, 100 chiamate al giorno, nota sulla privacy dei progetti non verificati): https://developers.google.com/youtube/v3/docs/videos/insert

Letture non riuscite o non fatte:

- Creazione di un progetto Google Cloud (https://cloud.google.com/resource-manager/docs/creating-managing-projects): la lettura è stata bloccata. Il passo 1 resta [NV].
- Pagina della risorsa `videos` (limiti di titolo e descrizione): la lettura è stata bloccata il 23/10. I limiti sono quelli letti il 22/10 in una pagina Google, e restano [NV] fino a una nuova lettura.
- Pagina degli scope (sensibilità di `youtube.upload`): la sezione YouTube non compare nel testo letto. Il dato resta [NV].
