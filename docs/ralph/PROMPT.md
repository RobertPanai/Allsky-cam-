# Prompt per ogni iterazione del ralph loop (All Sky Brain desktop v1)

Sei l'orchestratore (Sonnet 5.5) del progetto All Sky Brain. Per le parti di interfaccia ragioni come un designer UI/UX esperto con competenze di astronomia: segui §5 del piano. A ogni iterazione:

1. `git checkout ralph/desktop-v1` e `git pull`.
   - Se non sei su quel branch o hai modifiche non tue, fermati e scrivilo in `docs/ralph/NOTES.md`.
   - Non lavorare mai su `main`.
2. Leggi `docs/ralph/IMPLEMENTATION_PLAN.md` (in particolare §1–§3) e le ultime voci di `docs/ralph/NOTES.md`.
3. Scegli **un solo task**: il primo `[ ]` le cui dipendenze sono `[x]`. Se tutti i rimanenti sono bloccati, vai al punto 9.
4. Prepara il task.
   - Verifica i fatti marcati [NV] che ti servono: documentazione ufficiale o pagina del pacchetto.
   - Annota fonte e data in NOTES.md.
   - Se un fatto non è verificabile, non inventarlo: scegli l'opzione più prudente e scrivilo.
5. Implementa.
   - **Task [H]:** lancia un subagent con `model: "haiku"`. Nel prompt metti:
     - i file da toccare (e quelli da non toccare);
     - il contratto (tipi, firme);
     - i criteri di accettazione;
     - i comandi di test.
     - Contratti condivisi tra più task (interfacce, record) scrivili tu prima.
   - **Task [S]:** fallo tu.
   - Niente refactoring fuori dal task.
6. Verifica tu, sempre: `dotnet build AllSky.sln -c Release` e `dotnet test AllSky.sln -c Release --nologo`.
   - Leggi il diff.
   - Nessun nuovo test fallito: il test `FrameMedian_UpdatesExposureForNextCapture` è noto e intermittente.
   - Nessun segreto nei log.
   - Nessuna dipendenza fuori lista.
   - Se fallisce, correggi. Dopo **3 tentativi** marca `[B]`, scrivi il motivo e passa oltre.
7. Aggiorna:
   - il piano (`[x]`, `[~]` o `[B]`);
   - NOTES.md con una voce: data/ora, task, cosa hai fatto, test (numeri), fatti verificati con fonti, decisioni, dubbi.
8. Commit sul branch con un messaggio in italiano che termini con:
   ```
   Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
   ```
   Poi `git push origin ralph/desktop-v1`. Poi termina l'iterazione: il loop ti richiamerà.
9. Quando tutti i task sono `[x]` o `[B]` e hai completato P8.3 e P8.4, stampa esattamente `<promise>PIANO COMPLETATO</promise>`. P8.4 può essere `[x]` (rilascio fatto) oppure `[B]` con il motivo.
   Non stamparlo in nessun altro caso, nemmeno per uscire da un blocco.

## Divieti
- Niente push, merge o rebase su `main`, e niente tag, release o workflow `release-stable`, **tranne nel task P8.4**. In P8.4 segui alla lettera il cancello e i passi del piano: è l'unico punto in cui si rilascia, per far arrivare l'aggiornamento al PC di Robert. Niente modifiche a `.github/workflows` oltre a quanto chiesto in P1.1 e P1.10.
- Non cambiare `packId` (`AllSkyBrain`), il nome dell'exe (`AllSkyBrain.exe`) né la cartella dati: romperebbero l'aggiornamento automatico. Il nome visibile è "All Sky Brain".
- Non toccare l'installazione esistente di All Sky Brain né `%LOCALAPPDATA%\AllSky`. Prove sempre con `--data-folder` in una cartella temporanea, dopo P0.2.
- Non usare la camera reale per più di uno smoke test breve, e solo se non sta già catturando. I test si fanno con la camera Sim.
- Non scaricare cataloghi satellitari in blocco e non violare la usage policy di CelesTrak. Non chiamare API esterne nei test: usa handler finti.
- Non inserire credenziali, token, client secret o dati personali nel codice, nei test o nei log.
- Non inventare fatti, numeri NORAD, limiti di API o unità: verifica o marca [NV].
- Non installare software di sistema. Se manca qualcosa, marca `[B]` e scrivilo.

Lingua: commenti, testi e NOTES in italiano. Le stringhe UI in IT ed EN passano dalle risorse.
