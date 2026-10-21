# Diario del ralph loop — All Sky Brain desktop v1

Una voce per iterazione, la più recente in fondo. Formato:

```
## AAAA-MM-GG HH:MM — <ID task> <titolo>
- Fatto:
- Test: <eseguiti> / <passati> / <falliti (nomi)>
- Fatti verificati: <fatto> — <fonte> (<data>)
- Decisioni:
- Dubbi / da confermare con Robert:
```

## 2026-10-21 23:20 — Preparazione del piano (orchestratore, sessione cloud)
- Piano scritto da `main` @ 885535f (0.2.1 rilasciata). Branch `ralph/desktop-v1`.
- Fatti verificati e fonti: vedi IMPLEMENTATION_PLAN.md §1.
- Problemi aperti noti prima del loop:
  - ZWO `ASCOM.ASICamera2.Camera` rifiutata perché "Monochrome": modello e tipo sensore da confermare.
  - Limiti di esposizione ZWO: correzione 0.2.1 non ancora provata sulla camera reale.
