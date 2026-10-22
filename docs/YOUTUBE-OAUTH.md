# Collegamento YouTube (OAuth «Desktop app»)

Questa pagina è il riferimento tecnico del collegamento che All Sky Brain usa per caricare i timelapse su YouTube. Per i passi da seguire nella Console Google vedi [GUIDA-YOUTUBE.md](GUIDA-YOUTUBE.md).

Ogni utente usa il proprio progetto Google Cloud: l'applicazione non contiene credenziali incorporate.

Legenda: **[V]** verificato su una pagina Google, letta il 23/10/2026 (elenco in fondo) · **[NV]** non verificato. Il comportamento dell'applicazione è descritto dal codice e non ha marcatura.

---

## 1. Come funziona il login

Il flusso è quello della guida Google «OAuth 2.0 for Mobile & Desktop Apps» (vedi Fonti):

- **Redirect loopback.** Il redirect va a `http://127.0.0.1:PORTA`, con un listener HTTP su una porta libera scelta dal sistema. La guida dice di usare un indirizzo di loopback e una porta casuale disponibile. [V] All Sky Brain usa `127.0.0.1`.
- **`localhost`.** La guida lo cita come alternativa a `127.0.0.1`, e avverte che può causare problemi con i firewall. [V]
- **Schemi URI personalizzati.** Non sono supportati, per il rischio di impersonificazione dell'app. [V]
- **PKCE.** La guida dice «Always use PKCE» (sempre). Il metodo raccomandato è `S256`. Se il parametro manca, il metodo è `plain`. [V] All Sky Brain usa `S256`.
- **Parametro `state`.** È raccomandato e serve a mantenere lo stato tra richiesta e risposta, e a prevenire CSRF. [V]
- **Endpoint.** Autorizzazione `https://accounts.google.com/o/oauth2/v2/auth`, token `https://oauth2.googleapis.com/token`, revoca `https://oauth2.googleapis.com/revoke`. [V]
- **`access_type`.** La pagina non lo cita. [V] All Sky Brain non lo invia.
- **`prompt`.** La pagina cita `prompt=consent` per ottenere un nuovo refresh token quando ne esiste già uno. [V] All Sky Brain non lo invia. Il comportamento in questo caso non è stato provato: [NV].
- **Client secret.** Nello scambio del codice il `client_secret` è indicato come opzionale. La guida dice che «Desktop apps are public clients and cannot keep secrets confidential». [V] All Sky Brain lo richiede e lo invia (vedi la guida utente, passo 4).
- **Refresh token.** La guida dice che «refresh tokens are always returned for installed applications». [V]
- **Scope.** `https://www.googleapis.com/auth/youtube.upload`, descritto da Google come «Manage your YouTube videos». [V]
- Il listener ascolta solo su `127.0.0.1` e solo durante il login, poi viene chiuso. Il login scade dopo 5 minuti. (Valore del codice.)
- Il refresh token è salvato cifrato con DPAPI nel file `youtube-auth.json` della cartella dati. Il client secret è cifrato allo stesso modo. Il client ID è in chiaro.

## 2. Credenziali «Desktop app»

Procedura dalla guida «Create credentials» [V]: **Google Auth platform > Clients**, **Create Client**, **Application type** = **Desktop app**, **Name**, **Create**. La credenziale compare in «OAuth 2.0 Client IDs».

Non verificato:

- se la Console mostra il client secret per le app desktop, e come copiarlo: [NV]. La pagina Google non lo dice.
- La richiesta del secret è una scelta di All Sky Brain. La guida Google lo indica come opzionale. [V]

## 3. Schermata di consenso

Procedura dalla guida «Configure OAuth consent» [V]:

1. **Branding**: se compare la configurazione non ancora completata, **Get Started**. In **App Information** nome dell'app e email di supporto.
2. **Audience**: tipo di utente.
3. **Contact Information**: email per le comunicazioni.
4. **Finish**: accettare la policy, poi **Continue** e **Create**.
5. **Audience > Test users** (solo External): **Add users**, poi **Save**.
6. **Data Access**: **Add or Remove Scopes**, poi **Save**.

La pagina dice che, per ragioni di sicurezza, la schermata di consenso non si può eliminare una volta configurata. [V]

## 4. Abilitare la YouTube Data API v3

- La guida YouTube dice: «Any application that calls Google APIs needs to enable those APIs in the API Console» e «Use the Library page to find and enable the YouTube Data API». [V]
- Il pulsante per abilitare un'API è **Enable**. [V] dalla guida «Enable APIs», con percorso **Menu > APIs & Services > Library**.

## 5. Stato di pubblicazione: Testing e In production

Fatti verificati [V]:

- **Testing.** Il refresh token scade dopo 7 giorni. La pagina dice: «is issued a refresh token expiring in 7 days». La regola non si applica se gli scope richiesti sono solo name, email e profilo: `youtube.upload` non rientra in quel gruppo. Con l'app in Testing il login va rifatto ogni 7 giorni.
- **Utenti di test.** Un'app in Testing è limitata agli utenti di test elencati nella schermata di consenso, al massimo 100. Le autorizzazioni di un utente di test scadono 7 giorni dopo il consenso. [V] da «Manage App Audience».
- **Passaggio a In production.** Il pulsante è **Publish app**, nella gestione dello stato di pubblicazione della pagina **Audience**. [V] Dopo il passaggio, il progetto è disponibile a qualunque account Google. [V]
- **Regola dei 7 giorni.** La pagina di scadenza la riferisce allo stato Testing. [V] Per lo stato In production la pagina non dice che il token scade a 7 giorni. Google avverte che il codice deve prevedere il caso di un refresh token che smette di funzionare. [V] All Sky Brain gestisce questo caso chiedendo di collegare di nuovo l'account.
- **Limite dei token.** Ci sono al massimo 100 refresh token per account e client ID. Un nuovo token invalida il più vecchio, senza avviso. [V]
- **Schermata «unverified app».** Con l'app non verificata compare durante i test. Per rimuoverla occorre inviare una richiesta di verifica. [V]
- **Scope sensibili.** Una richiesta di scope sensibili o riservati, prima della verifica, mostra un avviso. Le app non verificate che lo mostrano hanno un limite di 100 nuovi utenti nel corso della vita del progetto. [V]

Non verificato [NV]:

- se `youtube.upload` sia classificato come sensibile o riservato. La pagina degli scope letta non riporta la riga YouTube.
- se e quando lo stato In production richieda una verifica dell'app per questo scope, e i tempi.

## 6. Interfaccia (comportamento dell'applicazione)

- Con lo stato Testing la UI indica che il collegamento scade dopo 7 giorni e rimanda alla guida per il passaggio a In production.
- Se il refresh viene rifiutato, la UI chiede di collegare di nuovo l'account. Il nome dell'errore che Google restituisce in questo caso (`invalid_grant`) non è stato verificato in una pagina Google: [NV].

## 7. Revoca e logout

- Il logout invia una POST a `https://oauth2.googleapis.com/revoke` con il token come parametro. [V] per l'endpoint; il metodo POST è quello dell'esempio della pagina Google. La pagina mostra il token nella query string; All Sky Brain lo invia nel corpo della richiesta, in forma `application/x-www-form-urlencoded`. Che Google accetti questa forma: [NV].
- Il file locale viene rimosso anche se la revoca fallisce, e l'errore viene comunque segnalato. (Comportamento dell'applicazione, dal codice.)

---

## Fonti (lette il 23/10/2026)

- OAuth 2.0 for Mobile & Desktop Apps: https://developers.google.com/identity/protocols/oauth2/native-app
- OAuth 2.0 expiration (refresh token, limiti): https://developers.google.com/identity/protocols/oauth2#expiration
- OAuth 2.0 for Mobile & Desktop Apps, guida YouTube: https://developers.google.com/youtube/v3/guides/auth/installed-apps
- Create credentials: https://developers.google.com/workspace/guides/create-credentials
- Configure OAuth consent: https://developers.google.com/workspace/guides/configure-oauth-consent
- Enable APIs: https://developers.google.com/workspace/guides/enable-apis
- Manage App Audience: https://support.google.com/cloud/answer/15549945
- Publishing status (pagina Audience): https://support.google.com/cloud/answer/10311615
- OAuth 2.0 Scopes (sensibilità di `youtube.upload`, non letta): https://developers.google.com/identity/protocols/oauth2/scopes

Non letta: la pagina di creazione del progetto Google Cloud (https://cloud.google.com/resource-manager/docs/creating-managing-projects), bloccata durante la lettura.
