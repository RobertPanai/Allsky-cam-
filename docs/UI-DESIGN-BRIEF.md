# Brief di design — interfaccia web

**Soggetto:** pannello di controllo di una camera all-sky su un PC dell'osservatorio. **Utenti:** astrofili e club, non tecnici; a volte la usano di notte, con occhi adattati al buio. **Compito principale:** vedere il cielo adesso, capire se tutto funziona, rivedere le notti passate (timelapse, keogram, startrails) e configurare in pochi minuti.

## Idea (una sola cosa memorabile)
**Il cielo è l'interfaccia.** L'eroe è la vista circolare del cielo (fisheye) con un anello di tacche e punti cardinali sottili; sotto, **il "nastro della notte"**: una fascia orizzontale colorata dalla reale altezza del Sole (giorno, crepuscolo civile/nautico/astronomico, notte) che fa anche da barra di scorrimento dei frame e del timelapse. Tutto il resto è quieto e disciplinato.

## Token
- **Colori (tema Notte, predefinito di notte):** Zenit `#0A0F1E` (sfondo), Orizzonte `#141C33` (superfici), Foschia `#9AA7C7` (testo secondario), Brina `#EEF2FA` (testo principale), Cometa `#7C9CFF` (azione/selezione), Aria `#5FD3B2` (stato ok), Alba `#FFB86B` (avviso), Meteora `#FF6B7A` (errore).
- **Tema Giorno:** sfondo `#F3F6FB`, superfici `#FFFFFF`, testo `#0F1730`, stessi accenti ma scuriti per contrasto AA.
- **Tema Rosso (osservatorio):** solo sfumature di rosso su nero (`#0B0000` → `#C0392B`), nessun blu/verde, per non rovinare l'adattamento al buio. Tema `auto` segue la fase del Sole (giorno→Giorno, notte→Notte); `red` è sempre manuale.
- **Tipografia:** nessun font da Internet (l'app deve funzionare offline). Stack di sistema: `"Segoe UI Variable Display","Segoe UI",system-ui,sans-serif`, numeri tabulari per esposizioni/ADU/orari. Scala: 12 / 14 / 16 / 20 / 28 / 44; peso 400/600; interlinea 1.5 nel corpo; righe sotto gli 70 caratteri. Titolo grande solo nella schermata Live (l'ora locale o lo stato in una frase, es. "Notte, cielo in cattura").
- **Forma:** raggio 14px per i pannelli grandi, 8px per i controlli, cerchio perfetto per la vista del cielo. Un solo livello di ombra, molto morbido; i bordi sono 1px a bassa opacità. Niente gradienti decorativi (l'unico gradiente è il nastro della notte, che è un dato).
- **Movimento:** un solo momento orchestrato, all'apertura: la vista del cielo si accende (dissolvenza dal nero in ~600ms) e il nastro si disegna da sinistra. Poi solo movimento che risponde ad azioni (cambio frame, apertura di un pannello). Rispetta `prefers-reduced-motion`.

## Layout
```
┌────┬──────────────────────────────────────────────┐
│Rail│  Notte, cielo in cattura          ● Camera ok │
│    │          ╭────────╮      Esposizione  8 s     │
│Live│          │  cielo │      Guadagno     250     │
│Notti│         │  (cerchio)│    Mediana      4210   │
│Colore│        ╰────────╯      Disco libero 212 GB │
│Camera│  ▓▓▓▓▒▒░░░░░░░░░░░▒▒▓▓▓▓▓▓▓  nastro notte  │
│Sito │                                               │
└────┴──────────────────────────────────────────────┘
```
- Navigazione: barra laterale stretta (icone SVG proprie + etichetta in sentence case), sotto i 720px diventa barra in basso. Contenuto allineato a sinistra; la vista del cielo è centrata nella sua colonna.
- **Live:** vista del cielo + dati chiave a destra + nastro della notte sotto. **Notti:** griglia di notti, ognuna con miniatura keogram; apertura a pannello con tre schede (Timelapse, Keogram, Startrails) e pulsante "Crea timelapse". **Colore:** anteprima prima/dopo con cursori (bilanciamento, gradiente, stretch, saturazione) e spiegazione in una riga per ogni cursore. **Camera** e **Sito:** form semplici. **Procedura guidata** al primo avvio: 3 passi (Camera → Posizione → Cartella e archivio) a tutta pagina, con anteprima live già attiva dal passo 1.
- Evita: carte tutte uguali con la stessa ombra, etichette tutto maiuscolo sopra ogni titolo, frecce "→" nei pulsanti, numerazioni decorative, font monospace per le etichette.

## Testi
Italiano e inglese (file JSON separati), frasi in sentence case, verbi chiari ("Crea timelapse", "Connetti camera", "Salva"). Gli errori dicono cosa è successo e cosa fare ("Non trovo nessuna camera. Collega la camera USB o avvia il simulatore ASCOM, poi riprova."). Gli stati vuoti invitano a un'azione.

## Qualità minima
Responsive fino a 360px; focus da tastiera visibile; contrasto AA in tutti e tre i temi; nessuna richiesta di rete esterna; nessun framework JS: HTML + CSS (variabili) + moduli ES; icone SVG inline.
