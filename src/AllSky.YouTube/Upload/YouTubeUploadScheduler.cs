namespace AllSky.YouTube;

/// <summary>
/// Decide quando caricare il timelapse dell'ultima notte completata.
/// Ordine delle regole ad ogni tick:
/// <list type="number">
/// <item>quota esaurita fino a mezzanotte locale: nessuna chiamata;</item>
/// <item>un caricamento InProgress va ripreso (non conta come nuovo caricamento), rispettando l'attesa di ripresa;</item>
/// <item>una sola esecuzione per giorno locale all'ora scelta; se il PC era spento, si recupera entro la finestra;</item>
/// <item>oltre la finestra l'ora di oggi viene saltata e segnalata.</item>
/// </list>
/// Il caricamento manuale ("carica ora la notte scorsa") usa le stesse regole di idempotenza, ma ignora l'ora.
/// Tutte le operazioni sono serializzate: un solo caricamento alla volta.
/// </summary>
public sealed class YouTubeUploadScheduler
{
    private readonly IClock _clock;
    private readonly UploadCalendar _calendar;
    private readonly ILatestTimelapseProvider _provider;
    private readonly NightUploader _uploader;
    private readonly UploadJournal _journal;
    private readonly Func<YouTubeUploadSettings> _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public YouTubeUploadScheduler(
        IClock clock,
        TimeZoneInfo zone,
        ILatestTimelapseProvider provider,
        NightUploader uploader,
        UploadJournal journal,
        Func<YouTubeUploadSettings> settings)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _calendar = new UploadCalendar(zone ?? throw new ArgumentNullException(nameof(zone)));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Un passo del pianificatore. Da chiamare all'avvio e periodicamente (es. ogni 10 minuti).</summary>
    public async Task<UploadOutcome> TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TickCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Carica subito la notte piu' recente completata.</summary>
    /// <param name="acceptUncertainRisk">
    /// True solo dopo conferma dell'utente: rimuove un record NeedsReview o Failed della notte e ricarica. Con NeedsReview
    /// il video potrebbe gia' esistere su YouTube, quindi il rischio di duplicato resta a carico dell'utente.
    /// </param>
    public async Task<UploadOutcome> UploadLatestNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            YouTubeUploadSettings settings = _settings();
            UploadJournalState state = _journal.State;

            if (state.QuotaBlockedUntilUtc is { } blockedUntil && now < blockedUntil)
                return QuotaBlocked(blockedUntil);

            LatestTimelapse? latest = _provider.GetLatestCompleted();
            NightUploadRecord? active = state.Nights.Values.FirstOrDefault(r => r.State == NightUploadState.InProgress);
            if (active is not null)
                return await _uploader.ResumeAsync(active, latest, settings, cancellationToken).ConfigureAwait(false);

            if (latest is null)
                return new UploadOutcome(UploadOutcomeKind.NoNight, "Nessuna notte completata con timelapse pronto.");

            if (acceptUncertainRisk
                && state.Nights.TryGetValue(latest.NightId, out NightUploadRecord? previous)
                && previous.State is NightUploadState.NeedsReview or NightUploadState.Failed)
            {
                string id = latest.NightId;
                TryUpdate(s => s with { Nights = WithoutRecord(s, id) });
            }

            return await _uploader.StartAsync(latest, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UploadOutcome> TickCoreAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        YouTubeUploadSettings settings = _settings();
        UploadJournalState state = _journal.State;

        if (state.QuotaBlockedUntilUtc is { } blockedUntil && now < blockedUntil)
            return QuotaBlocked(blockedUntil);

        LatestTimelapse? latest = _provider.GetLatestCompleted();
        NightUploadRecord? active = state.Nights.Values.FirstOrDefault(r => r.State == NightUploadState.InProgress);
        if (active is not null)
        {
            if (active.NextAttemptUtc is { } next && now < next)
                return new UploadOutcome(UploadOutcomeKind.NotDue, $"Caricamento in attesa di ripresa: nuovo tentativo alle {_calendar.Format(next)}.");
            return await _uploader.ResumeAsync(active, latest, settings, cancellationToken).ConfigureAwait(false);
        }

        DateOnly today = _calendar.LocalDate(now);
        string todayKey = UploadCalendar.Key(today);
        if (state.LastSlotDate == todayKey)
            return new UploadOutcome(UploadOutcomeKind.NotDue, "Caricamento di oggi gia' elaborato.");

        DateTimeOffset slot = _calendar.SlotUtc(today, settings.DailyTime);
        if (now < slot)
            return new UploadOutcome(UploadOutcomeKind.NotDue, $"Prossimo caricamento alle {_calendar.Format(slot)}.");

        if (now - slot > settings.CatchUpWindow)
        {
            MarkSlot(todayKey);
            return new UploadOutcome(
                UploadOutcomeKind.SlotMissed,
                $"Caricamento delle {_calendar.Format(slot)} saltato: il PC non era acceso entro la finestra di recupero.");
        }

        if (latest is null)
        {
            MarkSlot(todayKey);
            return new UploadOutcome(UploadOutcomeKind.NoNight, "Nessuna notte completata con timelapse pronto.");
        }

        UploadOutcome outcome = await _uploader.StartAsync(latest, settings, cancellationToken).ConfigureAwait(false);
        MarkSlot(todayKey);
        return outcome;
    }

    private UploadOutcome QuotaBlocked(DateTimeOffset until) =>
        new(UploadOutcomeKind.QuotaBlocked, $"Quota YouTube esaurita: nuovo tentativo dopo {_calendar.Format(until)}.");

    private void MarkSlot(string dayKey)
    {
        // Se il salvataggio fallisce, al tick successivo si riprova: il limite giornaliero impedisce un secondo nuovo caricamento.
        TryUpdate(s => s with { LastSlotDate = dayKey });
    }

    private void TryUpdate(Func<UploadJournalState, UploadJournalState> change)
    {
        try
        {
            _journal.Update(change);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Stato non salvato: il chiamante lo ritenta al ciclo successivo.
        }
    }

    private static IReadOnlyDictionary<string, NightUploadRecord> WithoutRecord(UploadJournalState state, string nightId)
    {
        var nights = new Dictionary<string, NightUploadRecord>(state.Nights, StringComparer.Ordinal);
        nights.Remove(nightId);
        return nights;
    }
}
