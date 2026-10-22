# Accesso remoto e monitoraggio pubblico: decisione

Stato: decisione presa il 22/10/2026. Riferimento nel piano: P12 (`docs/ralph/IMPLEMENTATION_PLAN.md`).

## Decisione di Robert

Robert vuole solo il **monitoraggio** dell'all-sky da un sito web. Non servono comandi, login o altre interazioni: chiunque apra la pagina è un visitatore e vede lo stesso contenuto, in sola lettura. Sulla scelta tecnica ha delegato.

## Strada adottata (A): invio in uscita

L'app invia in uscita, via HTTPS e con il protocollo Uplink, uno stato e le immagini al sito di Robert. Il sito li mostra ai visitatori.

- La connessione parte sempre dal PC dell'osservatorio verso il sito.
- Sul PC non si apre nessuna porta in ascolto per il monitoraggio.
- Non esiste un canale per scrivere sul PC dal sito o da Internet.
- La pubblicazione è spenta di default e si accende con un interruttore generale; ogni voce pubblicata ha un interruttore proprio.

I vincoli di sicurezza di §0 del piano restano invariati: nessun nuovo listener, nessuna modifica ai listener già ammessi.

## Strade scartate

**B. Tunnel di terzi (per esempio un tunnel di un fornitore esterno).** Aggiunge un fornitore con termini d'uso propri per immagini e video, ancora da leggere [NV]. Per Cloudflare il piano v1 registra come verificato [V] che lo streaming video è vietato sui piani gratuiti.

**C. Porta aperta sul router con DDNS.** Espone direttamente il PC a Internet. Per il solo monitoraggio non porta vantaggi rispetto all'invio in uscita.

## Cosa non esiste in questa fase

- Nessun login, account o ruolo utente.
- Nessun comando verso l'app: non si può avviare né fermare la cattura, cambiare la configurazione, muovere il telescopio o inviare richieste a NINA.
- Nessuna porta in ascolto per il monitoraggio e nessun tunnel.
- Nessuna scrittura sul PC da parte del sito.

I listener ammessi dal piano restano quelli già previsti e spenti di default: SafetyMonitor Alpaca (P9.5, solo `127.0.0.1`), login OAuth (P7.2, solo loopback e solo durante l'accesso), «Accesso dal browser» (P1.8). Il monitoraggio pubblico non ne aggiunge.

## Cosa non viene mai pubblicato

Posizione esatta dell'osservatorio, percorsi di file, indirizzi IP, log di NINA, token e credenziali. L'elenco completo e chiuso dei dati pubblicabili è in P12.1.

## Se in futuro servirà l'interazione

Sarà una fase nuova, con un modello di sicurezza a parte (login, ruoli, coda dei comandi). Non fa parte di questa fase.

## Stato dei lavori

- P12.0: questo documento.
- P12.1: elenco dei dati pubblici e interruttori.
- P12.2: richiesta di modifica del contratto Uplink (solo documento).
- P12.3: invio dello stato dall'app.
- P12.4: lato sito, solo se Robert lo autorizza, su un ramo dedicato.
- P12.5: verifica indipendente.
