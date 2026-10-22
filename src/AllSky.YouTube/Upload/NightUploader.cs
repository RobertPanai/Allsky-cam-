namespace AllSky.YouTube;

/// <summary>
/// Carica un timelapse notturno in modo idempotente. Protocollo di stato (ogni passo viene salvato prima del successivo):
/// <list type="number">
/// <item>registro: notte InProgress senza sessione, con il conteggio dei nuovi caricamenti del giorno;</item>
/// <item>sessione: la URI viene salvata (cifrata) prima di inviare il primo byte;</item>
/// <item>byte: ogni risposta 308 aggiorna l'offset; la ripresa usa sempre "bytes */total";</item>
/// <item>completato: il registro passa a Completed con l'id video.</item>
/// </list>
/// Un crash in qualunque punto lascia InProgress: al riavvio si riprende dalla sessione, mai si riparte da zero.
/// </summary>
public sealed class NightUploader
{
    /// <summary>Attesa prima di riprovare un caricamento con errore transitorio.</summary>
    public static readonly TimeSpan RetryLater = TimeSpan.FromMinutes(30);

    /// <summary>Quante risposte consecutive senza avanzamento tollerare prima di rimandare.</summary>
    private const int MaxStalledRounds = 10;

    private readonly ResumableUploadClient _client;
    private readonly UploadJournal _journal;
    private readonly IClock _clock;
    private readonly UploadCalendar _calendar;
    private readonly RetryPolicy _retry;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">Attesa tra i tentativi. Nei test e' un no-op che registra le attese.</param>
    public NightUploader(
        ResumableUploadClient client,
        UploadJournal journal,
        IClock clock,
        TimeZoneInfo zone,
        RetryPolicy? retry = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _calendar = new UploadCalendar(zone ?? throw new ArgumentNullException(nameof(zone)));
        _retry = retry ?? RetryPolicy.Default;
        _delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    /// <summary>Avvia il caricamento di una notte nuova. Conta come nuovo caricamento del giorno.</summary>
    public async Task<UploadOutcome> StartAsync(LatestTimelapse night, YouTubeUploadSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(night);
        ArgumentNullException.ThrowIfNull(settings);

        DateTimeOffset now = _clock.UtcNow;
        if (!TryBuildMetadata(night, settings, now, out VideoMetadata? metadata, out string? error))
            return new UploadOutcome(UploadOutcomeKind.ConfigError, error!);
        if (!TryFileLength(night.FilePath, out long length, out error))
            return new UploadOutcome(UploadOutcomeKind.ConfigError, error!);

        UploadJournalState state = _journal.State;
        if (state.Nights.TryGetValue(night.NightId, out NightUploadRecord? existing))
        {
            switch (existing.State)
            {
                case NightUploadState.Completed:
                    return new UploadOutcome(UploadOutcomeKind.AlreadyUploaded, $"La notte {night.NightId} risulta gia' caricata.", ExtractVideoId(existing));
                case NightUploadState.NeedsReview:
                    return new UploadOutcome(UploadOutcomeKind.NeedsReview, "La notte richiede una verifica su YouTube prima di ricaricarla.");
                case NightUploadState.Failed:
                    return new UploadOutcome(UploadOutcomeKind.Failed, $"Caricamento precedente non riuscito: {existing.LastError}");
                case NightUploadState.InProgress:
                    return await ContinueAsync(existing, metadata, cancellationToken).ConfigureAwait(false);
            }
        }

        if (state.Nights.Values.Any(r => r.State == NightUploadState.InProgress))
            return new UploadOutcome(UploadOutcomeKind.InProgress, "Un altro caricamento e' gia' in corso.");

        string today = UploadCalendar.Key(_calendar.LocalDate(now));
        int startsToday = state.StartsDate == today ? state.StartsCount : 0;
        if (startsToday >= settings.MaxUploadsPerDay)
            return new UploadOutcome(UploadOutcomeKind.DailyLimitReached, $"Limite giornaliero di {settings.MaxUploadsPerDay} caricamento raggiunto.");

        var record = new NightUploadRecord(
            night.NightId,
            NightUploadState.InProgress,
            night.FilePath,
            length,
            default,
            null,
            0,
            null,
            null,
            now);

        // Prima di qualunque chiamata di rete: stato "in corso" e conteggio del giorno.
        if (!TrySave(s => WithRecord(s, record) with { StartsDate = today, StartsCount = startsToday + 1 }))
            return new UploadOutcome(UploadOutcomeKind.ConfigError, "Impossibile salvare il registro dei caricamenti: nessun caricamento avviato.");

        return await ContinueAsync(record, metadata, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Riprende un caricamento InProgress. Se non e' ancora stata aperta una sessione, la apre solo se la notte e'
    /// ancora quella indicata da <paramref name="latest"/>; altrimenti lo marca Failed (nessun byte e' stato inviato).
    /// </summary>
    public async Task<UploadOutcome> ResumeAsync(
        NightUploadRecord record,
        LatestTimelapse? latest,
        YouTubeUploadSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(settings);

        VideoMetadata? metadata = null;
        if (record.SessionUri.IsEmpty)
        {
            if (latest is null || latest.NightId != record.NightId)
            {
                NightUploadRecord abandoned = record with
                {
                    State = NightUploadState.Failed,
                    LastError = "Caricamento non avviato: la notte non e' piu' la piu' recente.",
                    NextAttemptUtc = null,
                    UpdatedUtc = _clock.UtcNow,
                };
                TrySave(s => WithRecord(s, abandoned));
                return new UploadOutcome(UploadOutcomeKind.Failed, abandoned.LastError!);
            }

            if (!TryBuildMetadata(latest, settings, _clock.UtcNow, out metadata, out string? error))
                return new UploadOutcome(UploadOutcomeKind.ConfigError, error!);
            if (!TryFileLength(latest.FilePath, out long length, out error))
                return new UploadOutcome(UploadOutcomeKind.ConfigError, error!);

            // Nessun byte inviato: il file puo' essere stato rigenerato, quindi si usa quello attuale.
            record = record with { FilePath = latest.FilePath, TotalBytes = length };
        }

        return await ContinueAsync(record, metadata, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UploadOutcome> ContinueAsync(NightUploadRecord record, VideoMetadata? metadata, CancellationToken ct)
    {
        bool resumed = !record.SessionUri.IsEmpty;
        try
        {
            if (!resumed)
            {
                if (metadata is null)
                    return new UploadOutcome(UploadOutcomeKind.ConfigError, "Metadati del video non disponibili.");

                long total = record.TotalBytes;
                Uri started = await WithRetryAsync(c => _client.StartSessionAsync(metadata, total, c), ct).ConfigureAwait(false);
                record = record with { SessionUri = new OAuthSecret(started.AbsoluteUri), UpdatedUtc = _clock.UtcNow };
                // La sessione va salvata prima del primo byte. Se il salvataggio fallisce non si invia nulla.
                if (!TrySave(record))
                    return new UploadOutcome(UploadOutcomeKind.InProgress, "Sessione aperta ma registro non salvato: nuovo tentativo al prossimo ciclo.");
            }

            var session = new Uri(record.SessionUri.Reveal());
            UploadProgress progress = resumed
                ? await WithRetryAsync(c => _client.QueryAsync(session, record.TotalBytes, c), ct).ConfigureAwait(false)
                : new UploadProgress(0, null, null);

            return await UploadBytesAsync(record, session, progress, ct).ConfigureAwait(false);
        }
        catch (YouTubeApiException ex) when (ex.IsQuotaExceeded)
        {
            return HandleQuota(record);
        }
        catch (YouTubeApiException ex) when (ex.IsUnauthorized)
        {
            return HandleAuth(record);
        }
        catch (YouTubeApiException ex) when (ex.IsSessionGone)
        {
            // Sessione gia' esistente e scaduta: il caricamento potrebbe essere stato completato. Non si ricarica da soli.
            return resumed
                ? Review(record, "Sessione di caricamento scaduta: verificare su YouTube se il video e' gia' presente.")
                : Fail(record, "Sessione di caricamento non disponibile subito dopo l'apertura.");
        }
        catch (OAuthException ex) when (IsAuthPermanent(ex))
        {
            return HandleAuth(record);
        }
        catch (YouTubeApiException ex) when (!ex.IsTransientStatus)
        {
            return Fail(record, ex.Message);
        }
        catch (IOException)
        {
            return Fail(record, "Impossibile leggere il file del timelapse.");
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            return Transient(record, Describe(ex));
        }
    }

    /// <summary>Invia i chunk dall'offset indicato fino al completamento. Il file deve avere ancora la dimensione registrata.</summary>
    private async Task<UploadOutcome> UploadBytesAsync(NightUploadRecord record, Uri session, UploadProgress progress, CancellationToken ct)
    {
        long total = record.TotalBytes;
        if (progress.Completed)
            return Complete(record, progress.VideoId!);

        if (!TryFileLength(record.FilePath, out long currentLength, out _) || currentLength != total)
            return Review(record, "Il file del timelapse e' cambiato dopo l'avvio del caricamento.");

        long offset = progress.NextOffset;
        int stalled = 0;
        using var file = new FileStream(record.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        while (true)
        {
            if (offset >= total)
            {
                // Tutti i byte inviati ma nessuna conferma: si chiede lo stato al server.
                progress = await WithRetryAsync(c => _client.QueryAsync(session, total, c), ct).ConfigureAwait(false);
            }
            else
            {
                int length = (int)Math.Min(_client.ChunkBytes, total - offset);
                var chunk = new byte[length];
                file.Seek(offset, SeekOrigin.Begin);
                file.ReadExactly(chunk);
                long start = offset;
                progress = await WithRetryAsync(c => _client.SendChunkAsync(session, chunk, start, total, c), ct).ConfigureAwait(false);
            }

            if (progress.Completed)
                return Complete(record, progress.VideoId!);

            if (progress.RetryAfter is { } wait)
                await _delay(wait, ct).ConfigureAwait(false);

            if (progress.NextOffset == offset)
            {
                if (++stalled > MaxStalledRounds)
                    return Transient(record, "Il caricamento non avanza.");
            }
            else
            {
                stalled = 0;
            }

            offset = progress.NextOffset;
        }
    }

    private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _retry.MaxAttempts && IsTransient(ex, ct))
            {
                await _delay(_retry.DelayAfter(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return false;

        return ex switch
        {
            YouTubeApiException api => api.IsTransientStatus,
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            OAuthException oauth => !IsAuthPermanent(oauth),
            _ => false,
        };
    }

    private static bool IsAuthPermanent(OAuthException ex) =>
        ex.IsPermanent || ex.ErrorCode is "not_logged_in" or "no_refresh_token";

    private static string Describe(Exception ex) => ex switch
    {
        YouTubeApiException or OAuthException => ex.Message,
        HttpRequestException => "Errore di rete verso YouTube.",
        TaskCanceledException or TimeoutException => "Timeout di rete verso YouTube.",
        _ => "Errore imprevisto durante il caricamento (" + ex.GetType().Name + ").",
    };

    private UploadOutcome Complete(NightUploadRecord record, string videoId)
    {
        NightUploadRecord done = record with
        {
            State = NightUploadState.Completed,
            VideoId = videoId,
            SessionUri = default,
            LastError = null,
            NextAttemptUtc = null,
            UpdatedUtc = _clock.UtcNow,
        };
        if (TrySave(done))
            return new UploadOutcome(UploadOutcomeKind.Completed, $"Video caricato (id {videoId}).", videoId);

        // Il video esiste: al prossimo ciclo la sessione ancora salvata viene interrogata e il caricamento risulta completato.
        return new UploadOutcome(
            UploadOutcomeKind.Completed,
            $"Video caricato (id {videoId}), ma il registro non e' stato aggiornato: sara' riconciliato al prossimo ciclo.",
            videoId);
    }

    private UploadOutcome HandleQuota(NightUploadRecord record)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset until = _calendar.NextMidnightUtc(now);
        NightUploadRecord kept = record with
        {
            LastError = "Quota YouTube esaurita.",
            NextAttemptUtc = null,
            UpdatedUtc = now,
        };
        TrySave(s => WithRecord(s, kept) with { QuotaBlockedUntilUtc = until });
        return new UploadOutcome(UploadOutcomeKind.QuotaBlocked, $"Quota YouTube esaurita: nuovo tentativo dopo {_calendar.Format(until)}.");
    }

    private UploadOutcome HandleAuth(NightUploadRecord record)
    {
        DateTimeOffset now = _clock.UtcNow;
        NightUploadRecord kept = record with
        {
            LastError = "Accesso YouTube non valido: collegare di nuovo l'account.",
            NextAttemptUtc = now + RetryLater,
            UpdatedUtc = now,
        };
        TrySave(kept);
        return new UploadOutcome(UploadOutcomeKind.AuthRequired, kept.LastError!);
    }

    private UploadOutcome Transient(NightUploadRecord record, string reason)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset next = now + RetryLater;
        NightUploadRecord kept = record with
        {
            Attempts = record.Attempts + 1,
            LastError = reason,
            NextAttemptUtc = next,
            UpdatedUtc = now,
        };
        TrySave(kept);
        return new UploadOutcome(UploadOutcomeKind.InProgress, $"{reason} Nuovo tentativo alle {_calendar.Format(next)}.");
    }

    private UploadOutcome Fail(NightUploadRecord record, string reason)
    {
        NightUploadRecord failed = record with
        {
            State = NightUploadState.Failed,
            SessionUri = default,
            LastError = reason,
            NextAttemptUtc = null,
            UpdatedUtc = _clock.UtcNow,
        };
        TrySave(failed);
        return new UploadOutcome(UploadOutcomeKind.Failed, reason);
    }

    private UploadOutcome Review(NightUploadRecord record, string reason)
    {
        NightUploadRecord review = record with
        {
            State = NightUploadState.NeedsReview,
            SessionUri = default,
            LastError = reason,
            NextAttemptUtc = null,
            UpdatedUtc = _clock.UtcNow,
        };
        TrySave(review);
        return new UploadOutcome(UploadOutcomeKind.NeedsReview, reason);
    }

    private bool TrySave(NightUploadRecord record) => TrySave(s => WithRecord(s, record));

    /// <summary>Salvataggio nel registro. False se non riesce: il chiamante non deve inviare altri byte in quel caso.</summary>
    private bool TrySave(Func<UploadJournalState, UploadJournalState> change)
    {
        try
        {
            _journal.Update(change);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static UploadJournalState WithRecord(UploadJournalState state, NightUploadRecord record)
    {
        var nights = new Dictionary<string, NightUploadRecord>(state.Nights, StringComparer.Ordinal)
        {
            [record.NightId] = record,
        };
        return state with { Nights = nights };
    }

    private static string? ExtractVideoId(NightUploadRecord record) => record.VideoId;

    /// <summary>Titolo, descrizione e impostazioni validati. Nessuna chiamata di rete.</summary>
    private bool TryBuildMetadata(LatestTimelapse night, YouTubeUploadSettings settings, DateTimeOffset now, out VideoMetadata? metadata, out string? error)
    {
        metadata = null;
        error = null;

        if (string.IsNullOrEmpty(settings.CategoryId) || !settings.CategoryId.All(char.IsAsciiDigit))
        {
            error = "L'ID categoria YouTube deve contenere solo cifre.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(settings.Location))
        {
            error = "Il luogo di ripresa e' vuoto (serve per il modello del titolo).";
            return false;
        }
        if (settings.MaxUploadsPerDay < 1)
        {
            error = "Il limite giornaliero di caricamenti deve essere almeno 1.";
            return false;
        }
        if (settings.CatchUpWindow < TimeSpan.Zero)
        {
            error = "La finestra di recupero non puo' essere negativa.";
            return false;
        }
        if (settings.Tags.Any(string.IsNullOrWhiteSpace))
        {
            error = "Un tag e' vuoto.";
            return false;
        }

        var context = new TitleContext(
            _calendar.ToLocal(now),
            night.NightStartLocal,
            settings.Location,
            night.Frames,
            night.VideoDuration,
            night.Meteors,
            night.Transits);

        TemplateParseResult<TitleTemplate> title = TitleTemplate.Parse(settings.TitleTemplate);
        if (title.Template is null)
        {
            error = "Titolo: " + string.Join(" ", title.Errors.Select(e => e.Message));
            return false;
        }

        TemplateParseResult<DescriptionTemplate> description = DescriptionTemplate.Parse(settings.DescriptionTemplate);
        if (description.Template is null)
        {
            error = "Descrizione: " + string.Join(" ", description.Errors.Select(e => e.Message));
            return false;
        }

        try
        {
            string renderedTitle = title.Template.Render(context);
            string renderedDescription = description.Template.Render(context);
            if (renderedTitle.Length == 0)
            {
                error = "Il titolo risulta vuoto.";
                return false;
            }

            metadata = new VideoMetadata(
                renderedTitle,
                renderedDescription,
                settings.Tags.Select(t => t.Trim()).ToList(),
                settings.CategoryId,
                settings.Privacy);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = "Modello non valido: " + ex.Message;
            return false;
        }
    }

    private static bool TryFileLength(string path, out long length, out string? error)
    {
        length = 0;
        error = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            error = "File del timelapse non trovato.";
            return false;
        }
        if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            error = "Il timelapse da caricare deve essere un file MP4.";
            return false;
        }
        length = new FileInfo(path).Length;
        if (length <= 0)
        {
            error = "Il file del timelapse e' vuoto.";
            return false;
        }
        return true;
    }
}
