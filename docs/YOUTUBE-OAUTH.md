# Collegamento YouTube (OAuth "Desktop app")

Questa pagina spiega come preparare le credenziali Google che All Sky Brain usa per caricare i timelapse su YouTube (P7.2 / P7.3).
Ogni utente usa il PROPRIO progetto Google Cloud: l'applicazione non contiene credenziali incorporate.

Legenda: **[V]** verificato sulla pagina Google indicata, con data di lettura · **[NV]** non verificato, da controllare nella Console prima di usarlo.

Data di lettura di tutte le pagine: **22/10/2026**.

---

## 1. Come funziona il login

- Il flusso è quello della guida Google "OAuth 2.0 for Mobile & Desktop Apps" ([link](https://developers.google.com/identity/protocols/oauth2/native-app), letta il 22/10/2026):
  - redirect verso l'indirizzo loopback `http://127.0.0.1:PORTA`, con l'HTTP listener su una porta libera scelta dal sistema. La guida dice: "Query your platform for the relevant loopback IP address and start an HTTP listener on a random available port." [V]
  - la guida cita `localhost` come alternativa a `127.0.0.1` e avverte che può causare problemi di firewall. All Sky Brain usa `127.0.0.1`. [V]
  - gli schemi URI personalizzati non sono supportati per le app desktop. [V]
  - PKCE è obbligatorio: "Always use PKCE (Proof Key for Code Exchange) when requesting authorization codes." Il metodo consigliato è `S256`. [V]
  - il parametro `state` è raccomandato e serve a prevenire CSRF. [V]
  - endpoint: autorizzazione `https://accounts.google.com/o/oauth2/v2/auth`, token `https://oauth2.googleapis.com/token`, revoca `https://oauth2.googleapis.com/revoke`. [V]
  - la guida non riporta `access_type` né `prompt` tra i parametri: All Sky Brain non li invia. [V]
  - il client secret è indicato come "optional" nello scambio del codice; la guida dice anche che "Desktop apps are public clients and cannot keep secrets confidential." Se la Console mostra un secret per il client "Desktop app", All Sky Brain lo accetta e lo invia. [V] per la guida; la presenza del secret nella Console è [NV].
  - la guida dice che "refresh tokens are always returned for installed applications". [V]
- Scope richiesto: `https://www.googleapis.com/auth/youtube.upload`, descritto come "Manage your YouTube videos". [V] pagina [YouTube OAuth installed apps](https://developers.google.com/youtube/v3/guides/auth/installed-apps), letta il 22/10/2026.
- Il listener ascolta solo su `127.0.0.1` e solo durante il login, poi viene chiuso. Il login scade dopo un tempo limite. Se l'utente non conclude, il listener si chiude.
- Il refresh token è salvato cifrato con DPAPI (protettore della pubblicazione), nel file `youtube-auth.json` della cartella dati. Il client secret è cifrato allo stesso modo. Il client ID è in chiaro.

## 2. Creare le credenziali "Desktop app" nella Google Cloud Console

Procedura dalla pagina [Create credentials](https://developers.google.com/workspace/guides/create-credentials), letta il 22/10/2026:

1. Apri il menu della Google Cloud Console e vai su **Google Auth platform > Clients**. [V]
2. Clicca **Create Client**. [V]
3. Sotto **Application type** seleziona **Desktop app**. [V]
4. Inserisci un **Name** per la credenziale: compare solo nella Console. [V]
5. Clicca **Create**. La credenziale compare sotto "OAuth 2.0 Client IDs". [V]

Non verificato sulla pagina:

- se la Console mostra il client secret per le app "Desktop app" e come scaricarlo [NV]. Se il secret non c'è, All Sky Brain funziona comunque, perché la guida lo indica come opzionale. [V]

## 3. Configurare la schermata di consenso (OAuth consent screen)

Procedura dalla pagina [Configure OAuth consent](https://developers.google.com/workspace/guides/configure-oauth-consent), letta il 22/10/2026:

1. Vai su **Google Auth platform > Branding**. Se compare "Google Auth platform not configured yet", clicca **Get Started**. [V]
2. In **App Information** inserisci nome dell'app e email di supporto. [V]
3. In **Audience** scegli il tipo di utente. [V]
4. In **Contact Information** inserisci un'email per le comunicazioni. [V]
5. In **Finish** accetta la Google API Services User Data Policy e clicca **Continue** e **Create**. [V]
6. Se scegli **External**, vai su **Audience**, sezione **Test users**, clicca **Add users**, inserisci le email di prova e clicca **Save**. [V]
7. Per app usate fuori dall'organizzazione Google Workspace, vai su **Data Access > Add or Remove Scopes**, seleziona gli scope necessari e clicca **Save**. [V]

Nota: la pagina dice che "For security reasons, you can't remove the OAuth 2.0 consent screen after you've configured it." [V]

## 4. Abilitare la YouTube Data API v3

Dalla pagina [YouTube OAuth installed apps](https://developers.google.com/youtube/v3/guides/auth/installed-apps), letta il 22/10/2026:

- "Any application that calls Google APIs needs to enable those APIs in the API Console." [V]
- "Use the Library page to find and enable the YouTube Data API." [V]

## 5. Stato di pubblicazione: Testing e In production

Fatti verificati [V]:

- Con lo stato **Testing** il refresh token scade dopo 7 giorni. La pagina [OAuth 2.0 expiration](https://developers.google.com/identity/protocols/oauth2#expiration), letta il 22/10/2026, dice: "is issued a refresh token expiring in 7 days". La regola non si applica se gli scope richiesti sono solo name, email e profilo: lo scope `youtube.upload` non rientra in quel gruppo. Quindi con l'app in Testing il login va rifatto ogni 7 giorni, e il caricamento notturno si fermerà.
- Le pagine indicano che è possibile avere un solo refresh token per account e client: "There is currently a limit of 100 refresh tokens per Google Account per OAuth 2.0 client ID." Il nuovo token invalida il più vecchio. [V] stessa pagina.
- Con app non verificata, durante i test compare la schermata "unverified app". La pagina YouTube dice: "If you see unverified app on the screen when testing your application, you must submit a verification request to remove it." [V]
- La pagina consent screen dice che gli scope sensibili e ristretti richiedono verifica aggiuntiva. [V] Non è verificato se `youtube.upload` sia classificato come sensibile. [NV]

Non verificato [NV]:

- il passaggio da **Testing** a **In production**: nelle pagine lette non c'è il pulsante né la procedura. Va fatto dalla Console (Google Auth platform, Audience) e controllato prima del rilascio.
- se il passaggio a "In production" richiede una verifica dell'app per lo scope `youtube.upload`, e i tempi. [NV]
- il comportamento dei refresh token in stato "In production" (non scadono finché non vengono revocati). Questo è scritto in una pagina Google Health API, non YouTube: non è un fatto verificato per questo progetto. [NV]

## 6. Cosa mostra la UI (P7.4)

- Con lo stato Testing la UI deve dire che il collegamento scade dopo 7 giorni e deve mostrare il passaggio a "In production" come azione da fare. [V] sulla scadenza.
- Se il refresh viene rifiutato con `invalid_grant`, la UI chiede di collegare di nuovo l'account. [NV] sul nome dell'errore: è il nome definito nella specifica di progetto, non letto nelle pagine lette.

## 7. Revoca e logout

- Il logout revoca il refresh token con una POST a `https://oauth2.googleapis.com/revoke`, con il token nel corpo della richiesta. [V] per l'endpoint; la forma del corpo è [NV] (la guida non dettaglia la forma della richiesta). Il file locale viene rimosso anche se la revoca fallisce.
- La guida dice che la revoca "removes all OAuth 2.0 scopes previously granted to a project" e invalida i token dei client dello stesso progetto. [V]

## Fonti (lette il 22/10/2026)

- [OAuth 2.0 for Mobile & Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)
- [OAuth 2.0 expiration (refresh token, limiti)](https://developers.google.com/identity/protocols/oauth2#expiration)
- [OAuth 2.0 for Mobile & Desktop Apps (guida YouTube)](https://developers.google.com/youtube/v3/guides/auth/installed-apps)
- [Create credentials](https://developers.google.com/workspace/guides/create-credentials)
- [Configure OAuth consent](https://developers.google.com/workspace/guides/configure-oauth-consent)
