# Configurare il caricamento su YouTube

Questa guida spiega come collegare All Sky Brain al proprio account YouTube, per caricare automaticamente il timelapse di ogni notte. Le fonti dei fatti sono in [YOUTUBE-OAUTH.md](YOUTUBE-OAUTH.md), che resta il riferimento tecnico (flusso OAuth, endpoint, scope).

Legenda: **[V]** verificato su una fonte ufficiale, letta il 22/10/2026 · **[NV]** non verificato: controllare nella Console Google prima di fidarsi.

Ogni utente usa un proprio progetto Google Cloud. All Sky Brain non contiene credenziali.

---

## Prima di iniziare

- Un account Google con il canale YouTube su cui caricare i video.
- All Sky Brain con la sezione YouTube (versione 0.3.0 o successiva).
- Connessione a Internet sul PC: il caricamento parte solo con rete disponibile.

Il caricamento richiede due valori dalla Google Cloud Console: **client ID** e **client secret** di una credenziale di tipo "Desktop app". All Sky Brain richiede entrambi.

---

## Passo 1: progetto Google Cloud

1. Aprire la Google Cloud Console e scegliere un progetto, oppure crearne uno nuovo.
2. Procedura di creazione del progetto: [NV]. Non è stata letta in una fonte ufficiale per questo documento.

## Passo 2: abilitare la YouTube Data API v3

1. Dalla Console, aprire la **Library** (libreria) e cercare **YouTube Data API v3**. [V]
2. Premere **Enable** (abilita). [V] per il concetto: "Any application that calls Google APIs needs to enable those APIs in the API Console." Il nome esatto del pulsante è [NV].

## Passo 3: schermata di consenso (Google Auth platform)

Aprire **Google Auth platform** e completare le sezioni del menu. Le voci seguono la guida "Configure OAuth consent" letta il 22/10/2026. [V]

1. **Branding**: se compare "Google Auth platform not configured yet", premere **Get Started**. In **App Information** inserire nome dell'app ed email di supporto.
2. **Audience**: scegliere il tipo di utente. Con **External**, aggiungere in **Test users** l'email del proprio account Google e premere **Save**.
3. **Contact Information**: inserire un'email per le comunicazioni.
4. **Finish**: accettare la Google API Services User Data Policy, poi **Continue** e **Create**.
5. **Data Access**: premere **Add or Remove Scopes**, selezionare lo scope `https://www.googleapis.com/auth/youtube.upload` ("Manage your YouTube videos") e premere **Save**.

Nota: una volta configurata, la schermata di consenso non si può rimuovere. [V]

## Passo 4: creare la credenziale "Desktop app"

1. Aprire **Google Auth platform > Clients**.
2. Premere **Create Client**.
3. In **Application type** scegliere **Desktop app**.
4. Inserire un **Name**: serve solo a riconoscere la credenziale nella Console.
5. Premere **Create**. La credenziale compare in "OAuth 2.0 Client IDs".

Copiare il **client ID** e il **client secret**. Dove la Console mostra il secret di una "Desktop app" e come copiarlo: [NV]. Se il secret non compare, All Sky Brain non accetta il collegamento: la guida Google lo indica come opzionale, ma All Sky Brain lo richiede.

## Passo 5: stato "Testing" o "In production"

Questo passo decide se il collegamento resta valido nel tempo.

| Stato | Cosa succede | Stato della fonte |
|---|---|---|
| **Testing** | Il refresh token scade dopo **7 giorni**. Dopo la scadenza il caricamento notturno si ferma e il collegamento va rifatto. | [V] |
| **In production** | Il passaggio va fatto dalla Console, in Google Auth platform, sezione Audience. Il comportamento del refresh token in questo stato, per questo progetto, non è verificato: [NV]. La regola dei 7 giorni è quella dello stato Testing. | Passaggio: [V] come indicato in YOUTUBE-OAUTH.md; pulsante e procedura non letti: [NV]. |

Altri punti verificati:

- Con l'app in Testing, Google può mostrare la schermata "unverified app". Per rimuoverla occorre una richiesta di verifica. [V]
- Per ogni account e ogni client ID c'è un limite di 100 refresh token. Un nuovo collegamento invalida il più vecchio. [V]
- Se lo scope `youtube.upload` richieda una verifica dell'app prima del passaggio a In production, e in quanto tempo: [NV].
- Il comportamento dei refresh token in In production va confermato nella Console prima del rilascio: [NV].

All Sky Brain mostra un avviso in Testing, con il rimando a questa guida. [V]

## Passo 6: collegare l'account dall'app

1. Aprire **Pubblicazione** e scorrere alla sezione **YouTube**.
2. Inserire **Client ID** e **Client secret** copiati al passo 4.
3. Premere **Collega account**. Il browser apre la pagina di autorizzazione di Google.
4. Scegliere l'account e concedere il permesso di caricare video.
5. Tornare nell'app: il messaggio "Account collegato. Il caricamento automatico è attivo." conferma il collegamento.

Cosa succede in background [V] (flusso della guida Google "OAuth 2.0 for Mobile & Desktop Apps"):

- All Sky Brain apre un ascoltatore temporaneo su `127.0.0.1`, su una porta libera scelta dal sistema. Non ascolta sulla rete. Si chiude a fine login.
- Il login usa PKCE (metodo S256) e il parametro `state`.
- Il refresh token viene salvato nel file `youtube-auth.json` della cartella dati, cifrato con DPAPI. Il client secret è cifrato allo stesso modo. La sezione indica che il client secret non viene mostrato né riletto: per cambiarlo si collega di nuovo l'account.
- Il tempo massimo di attesa del login: [NV] (il valore esatto non è documentato nell'app).

Se si annulla o il tempo scade, la sezione mostra il motivo e si può riprovare.

## Passo 7: impostazioni del caricamento

Nella stessa sezione YouTube:

| Impostazione | Predefinito | Note |
|---|---|---|
| Ora del caricamento | `12:30` | Formato `HH:mm`, ora locale. Predefinito a mezzogiorno perché la notte osservativa finisce a mezzogiorno. |
| Privacy | `private` | Scelte: privato, non in elenco, pubblico. |
| Tag | `timelapse`, `allsky`, `astrofotografia` | Separati da virgola. Nessun tag vuoto. |
| ID categoria | `28` | Solo cifre. Il valore `28` è il predefinito del programma: non è verificato su una fonte ufficiale [NV]. |
| Luogo | `Osservatorio` | Sostituisce `{luogo}` nei modelli. |
| Modello del titolo | `Timelapse notte del {notte:yyyy-MM-dd}` | Segnaposto: `{data:...}`, `{notte:...}`, `{ora:...}`, `{luogo}`, `{fotogrammi}`, `{durata}`, `{meteore}`, `{transiti}`. Massimo 100 caratteri. |
| Modello della descrizione | vedi `config.json` | Massimo 5000 byte. Il simbolo `<` e `>` non sono ammessi. [V] |

Premere **Salva impostazioni**. Se un campo non è valido, viene evidenziato e nulla viene inviato a YouTube.

Il caricamento automatico parte ogni giorno all'ora scelta. Carica il timelapse dell'ultima notte completata, una volta per notte. Se il PC era spento all'ora prevista, il caricamento parte all'avvio successivo, entro la finestra di recupero di 12 ore (configurabile fino a 24).

## Passo 8: prova di caricamento

1. Nella sezione YouTube premere **Carica ora la notte scorsa**.
2. Confermare nella finestra di dialogo.
3. Attendere lo stato **Video caricato**. Il link **Apri il video su YouTube** porta al video.

Il primo video è il controllo più utile: verificare la privacy effettiva. Google può limitare a privato i video caricati da progetti API non verificati [V, la pagina dice "not restricted to private viewing mode"; l'effetto reale per questo progetto è [NV]].

Il caricamento consuma una quota giornaliera. Il costo del singolo `videos.insert` è di 1 unità nel bucket "Video Uploads", con 100 chiamate al giorno. [V]

---

## Risoluzione dei problemi

| Messaggio nella sezione YouTube | Causa probabile | Cosa fare |
|---|---|---|
| "Accesso non valido o revocato: collega di nuovo l'account." | Il refresh token è scaduto (Testing, 7 giorni) o è stato revocato. | Premere **Collega account** di nuovo. Se succede spesso, passare l'app a In production (passo 5). |
| "Autorizzazione negata nel browser." | Il permesso non è stato concesso. | Riprovare e concedere il permesso. |
| "Nessuna risposta dal browser entro il tempo limite. Riprova." | Il login non è stato completato in tempo. | Riprovare. |
| "Inserisci client ID e client secret." | Uno dei due campi è vuoto. | Copiare entrambi dal passo 4. |
| "Collegamento non riuscito. Controlla la connessione e le credenziali." | Rete assente o credenziali errate. | Controllare la connessione e il client ID/secret. |
| "La protezione dei segreti non è disponibile su questo sistema." | Il sistema non fornisce la cifratura DPAPI all'applicazione. Causa e rimedio: [NV]. | Consultare il log (sezione Log) e segnalare il caso. |
| "Quota giornaliera di YouTube esaurita: nuovo tentativo domani." | Quota del giorno esaurita. | Nessuna azione: riprova il giorno dopo. |
| "Esito incerto: controlla su YouTube prima di ricaricare." | Un caricamento precedente non ha dato una risposta chiara. | Verificare su YouTube che il video non ci sia già. Ricaricare crea un duplicato. |
| "Impostazioni o modelli non validi: nessuna chiamata a YouTube." | Un valore delle impostazioni non è valido. | Correggere i campi evidenziati. |

Per il dettaglio tecnico: il log dell'applicazione (sezione **Log**) riporta gli errori senza segreti.

## Scollegare l'account

Premere **Scollega account**. All Sky Brain revoca il token con l'endpoint di revoca di Google (`https://oauth2.googleapis.com/revoke`) [V] e disattiva il caricamento automatico. Il file locale viene cancellato anche se la revoca fallisce (comportamento dell'applicazione, come indicato in YOUTUBE-OAUTH.md, sezione 7).

Per revocare l'accesso anche dal lato Google, usare le impostazioni del proprio account Google: procedura non letta in questo documento [NV].
