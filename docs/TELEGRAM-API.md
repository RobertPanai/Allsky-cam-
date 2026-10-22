# Telegram Bot API: fatti per l'invio dei prodotti (P15.1)

Letto il 23/10/2026 sulla documentazione ufficiale. Convenzioni: **[V]** letto sulla fonte indicata; **[NV]** non letto, da verificare prima di scrivere codice che dipende dal valore.

Gli strumenti di lettura usati riassumono le pagine: le frasi tra virgolette sono quelle riportate dallo strumento. Per ogni valore che decide un comportamento del codice (limiti di dimensione) conviene rileggere la fonte prima di P15.2.

## Versione letta

- Bot API **10.3**, data in cima alla pagina **24 agosto 2026** [V] https://core.telegram.org/bots/api

## Limiti di dimensione dei file

| Voce | Valore | Stato | Fonte |
|---|---|---|---|
| File inviati da un bot (qualsiasi tipo) | fino a **50 MB**; la FAQ avverte che il limite «may be changed in the future» | [V] | https://core.telegram.org/bots/faq |
| File scaricati da un bot con `getFile` | fino a **20 MB** | [V] | https://core.telegram.org/bots/faq |
| Server Bot API locale: invio | fino a **2000 MB** | [V] | https://core.telegram.org/bots/api (sezione sul server locale) |
| Server Bot API locale: download | «without a size limit» | [V] | https://core.telegram.org/bots/api |
| Limite specifico delle foto (`sendPhoto`) | non letto | [NV] | la parte dei metodi della pagina non era leggibile con lo strumento |
| Somma di larghezza e altezza della foto, rapporto | non letto | [NV] | idem |
| Formato richiesto per i video (`sendVideo`) | non letto | [NV] | idem |
| Lunghezza massima del testo di un messaggio e di una didascalia | non letto | [NV] | idem |
| Limiti di invio da URL | non letto | [NV] | idem |

Conseguenza per il piano [S]: un timelapse notturno in MP4 di decine di MB può superare i 50 MB del server pubblico. Le due strade di P15.2 (ricodificare a una dimensione bersaglio, oppure inviare keogram e startrails con un link al video) restano valide. Il server Bot API locale porterebbe il limite a 2000 MB ma richiede di far girare un programma sul PC dell'osservatorio: non è nella v1, e va confrontato con la regola di non aggiungere listener.

## Frequenza di invio

Fonte: https://core.telegram.org/bots/faq [V].

| Situazione | Limite |
|---|---|
| Stessa chat | evitare più di **un messaggio al secondo**; brevi picchi sono ammessi, poi arrivano errori 429 |
| Gruppi | non più di **20 messaggi al minuto** |
| Invii massivi (broadcast) | circa **30 messaggi al secondo** |
| Broadcast a pagamento | fino a 1000 messaggi al secondo (richiede @BotFather, saldo in Stars e utenti attivi: non rilevante per questo progetto) |

La FAQ **non** descrive il comportamento da tenere dopo un 429 e **non** cita il parametro `retry_after` [V: assenti nella FAQ]. Se la risposta di errore lo contiene, la Bot API lo riporta nei `parameters` dell'errore [NV]: P15.2 deve leggerlo se c'è e usare un backoff crescente se manca.

## `getUpdates` (long polling)

Fonte: https://core.telegram.org/bots/api [V].

- Restituisce un array di oggetti `Update`.
- `timeout`: secondi di long polling; predefinito `0`, cioè short polling.
- `offset`: identificativo del primo aggiornamento da restituire; deve essere maggiore di uno rispetto al più alto già ricevuto. Un valore negativo recupera gli aggiornamenti a partire dalla fine della coda.
- «This method will not work if an outgoing webhook is set up»: se il bot ha un webhook configurato, `getUpdates` non funziona.
- Sono richieste solo in uscita, quindi compatibili con la regola di non aprire porte.

## Cosa resta da leggere prima di P15.2

1. Limiti di `sendPhoto`, `sendVideo`, `sendDocument` e `sendMediaGroup` (dimensioni, formati, lunghezza di testo e didascalia).
2. Forma della risposta di errore 429 (`retry_after`).
3. Come passare il file: caricamento diretto (`multipart/form-data`) e relativi limiti.

La fonte da leggere è la sezione «Sending files» e le sezioni dei singoli metodi di https://core.telegram.org/bots/api; lo strumento di lettura usato in questa sessione arriva fino agli oggetti `Poll*` e non raggiunge i metodi.

## Fonti

- https://core.telegram.org/bots/api (Bot API 10.3, 24 agosto 2026)
- https://core.telegram.org/bots/faq
