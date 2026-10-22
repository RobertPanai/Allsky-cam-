using AllSky.Core;
using AllSky.Core.YouTube;
using AllSky.Engine;
using AllSky.YouTube;

namespace AllSky.Host;

/// <summary>
/// Implementazione di <see cref="IYouTubeService"/> nell'Host: collega OAuth, scheduler e configurazione. Nei messaggi al log
/// finisce solo il codice d'errore o il tipo dell'eccezione, mai il testo dell'eccezione, il secret o i token.
/// </summary>
internal sealed class HostYouTubeService : IYouTubeService
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    private readonly IAllSkyService _engine;
    private readonly YouTubeOAuthService _oauth;
    private readonly YouTubeAuthStore _store;
    private readonly UploadJournal _journal;
    private readonly YouTubeUploadScheduler _scheduler;
    private readonly AllSky.YouTube.IClock _clock;
    private readonly Func<Uri, CancellationToken, Task> _openBrowser;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _configGate = new();
    private readonly object _gate = new();
    private YouTubeProblem? _problem;   // _gate
    private int _activeUploads;         // _gate

    public HostYouTubeService(
        IAllSkyService engine,
        YouTubeOAuthService oauth,
        YouTubeAuthStore store,
        UploadJournal journal,
        YouTubeUploadScheduler scheduler,
        AllSky.YouTube.IClock clock,
        Func<Uri, CancellationToken, Task> openBrowser,
        Action<string> log)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _openBrowser = openBrowser ?? throw new ArgumentNullException(nameof(openBrowser));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public event Action<YouTubeStatus>? StatusChanged;

    /// <inheritdoc />
    public YouTubeStatus GetStatus()
    {
        AppConfig.YouTubeConfig config = _engine.GetConfig().YouTube;
        bool connected = IsConnected();
        DateTimeOffset now = _clock.UtcNow;
        UploadJournalState state = _journal.State;

        YouTubeProblem? problem;
        bool uploading;
        lock (_gate)
        {
            problem = _problem;
            uploading = _activeUploads > 0;
        }

        problem ??= JournalProblem(state, now);

        DateTimeOffset? next = null;
        if (config.Enabled && connected && YouTubeConfigMapper.Errors(config).Count == 0)
        {
            YouTubeUploadSettings settings = YouTubeConfigMapper.ToUploadSettings(config);
            next = UploadSchedule.NextAttemptUtc(now, ZoneOf(), settings.DailyTime, settings.CatchUpWindow, state.LastSlotDate);
        }

        return new YouTubeStatus(connected, config.Enabled, config.ClientId, uploading, next, LastUpload(state), problem);
    }

    /// <inheritdoc />
    public YouTubeSettingsSnapshot GetSettings() => YouTubeSettingsRules.ToSnapshot(_engine.GetConfig().YouTube);

    /// <inheritdoc />
    public async Task<YouTubeLoginResult> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Fail(YouTubeProblem.CredentialsMissing);
        }

        OAuthClientCredentials credentials;
        try
        {
            credentials = new OAuthClientCredentials(clientId.Trim(), new OAuthSecret(clientSecret.Trim()));
        }
        catch (ArgumentException)
        {
            return Fail(YouTubeProblem.CredentialsMissing);
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        try
        {
            await _oauth.LoginAsync(credentials, _openBrowser, LoginTimeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Fail(YouTubeProblem.LoginTimedOut);
        }
        catch (OAuthException ex)
        {
            _log("YouTube: collegamento non riuscito (" + ex.ErrorCode + ").");
            return Fail(ex.ErrorCode == "access_denied" ? YouTubeProblem.LoginDenied : YouTubeProblem.LoginFailed);
        }
        catch (InvalidOperationException)
        {
            _log("YouTube: protezione dei segreti non disponibile, credenziali non salvate.");
            return Fail(YouTubeProblem.SecretProtectionUnavailable);
        }
        catch (Exception ex)
        {
            _log("YouTube: collegamento non riuscito (" + ex.GetType().Name + ").");
            return Fail(YouTubeProblem.LoginFailed);
        }

        // Le credenziali sono salvate: il caricamento automatico si accende con l'account collegato.
        bool saved = UpdateConfig(c => c with { Enabled = true, ClientId = credentials.ClientId });
        if (!saved)
        {
            return Fail(YouTubeProblem.SettingsNotSaved);
        }

        lock (_gate)
        {
            _problem = null;
        }

        Notify();
        return new YouTubeLoginResult(true, null);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        try
        {
            await _oauth.LogoutAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Il file locale e' gia' stato rimosso dal servizio OAuth: la revoca mancata si segnala solo nel log.
            _log("YouTube: revoca non riuscita (" + Code(ex) + "); credenziali locali rimosse.");
        }

        UpdateConfig(c => c with { Enabled = false });
        lock (_gate)
        {
            _problem = null;
        }

        Notify();
    }

    /// <inheritdoc />
    public YouTubeSaveResult SaveSettings(YouTubeSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        IReadOnlyList<YouTubeSettingsProblem> problems = YouTubeSettingsRules.Validate(change);
        if (problems.Count > 0)
        {
            return new YouTubeSaveResult(false, problems);
        }

        if (!UpdateConfig(c => YouTubeSettingsRules.Apply(c, change)))
        {
            return new YouTubeSaveResult(false, []);
        }

        Notify();
        return new YouTubeSaveResult(true, []);
    }

    /// <inheritdoc />
    public async Task<YouTubeUploadOutcome> UploadLastNightNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken)
    {
        if (!IsConnected())
        {
            return Record(new UploadOutcome(UploadOutcomeKind.AuthRequired, "Account YouTube non collegato."));
        }

        if (YouTubeConfigMapper.Errors(_engine.GetConfig().YouTube).Count > 0)
        {
            return Record(new UploadOutcome(UploadOutcomeKind.ConfigError, "Impostazioni YouTube non valide."));
        }

        UploadOutcome outcome = await RunAsync(
            ct => _scheduler.UploadLatestNowAsync(acceptUncertainRisk, ct),
            cancellationToken).ConfigureAwait(false);
        return Record(outcome);
    }

    /// <summary>Vero se il caricamento automatico e' acceso e l'account e' collegato. Usato dal ciclo in background.</summary>
    internal bool ShouldRun() => _engine.GetConfig().YouTube.Enabled && IsConnected();

    /// <summary>Un passo del caricamento automatico. Chiamato dal ciclo in background.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!ShouldRun())
        {
            return;
        }

        if (YouTubeConfigMapper.Errors(_engine.GetConfig().YouTube).Count > 0)
        {
            SetProblem(YouTubeProblem.SettingsInvalid);
            _log("YouTube: impostazioni non valide, caricamento automatico saltato.");
            Notify();
            return;
        }

        UploadOutcome outcome = await RunAsync(ct => _scheduler.TickAsync(ct), cancellationToken).ConfigureAwait(false);
        Record(outcome);
    }

    /// <summary>Arresto dell'Host: annulla i caricamenti in corso. Il registro resta riprendibile.</summary>
    internal void Stop() => _stopping.Cancel();

    private async Task<UploadOutcome> RunAsync(Func<CancellationToken, Task<UploadOutcome>> operation, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        lock (_gate)
        {
            _activeUploads++;
        }

        try
        {
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // Modelli o impostazioni rifiutati dal mapper, dopo il controllo di pre-volo: nessuna chiamata di rete.
            return new UploadOutcome(UploadOutcomeKind.ConfigError, "Impostazioni YouTube non valide.");
        }
        finally
        {
            lock (_gate)
            {
                _activeUploads--;
            }
        }
    }

    private YouTubeUploadOutcome Record(UploadOutcome outcome)
    {
        YouTubeProblem? problem = outcome.Kind switch
        {
            UploadOutcomeKind.Completed or UploadOutcomeKind.AlreadyUploaded => null,
            UploadOutcomeKind.QuotaBlocked => YouTubeProblem.QuotaExceeded,
            UploadOutcomeKind.AuthRequired => YouTubeProblem.AuthRequired,
            UploadOutcomeKind.NeedsReview => YouTubeProblem.NeedsReview,
            UploadOutcomeKind.Failed => YouTubeProblem.UploadFailed,
            UploadOutcomeKind.InProgress => YouTubeProblem.RetryScheduled,
            UploadOutcomeKind.ConfigError => YouTubeProblem.SettingsInvalid,
            _ => PreviousProblem(),
        };
        lock (_gate)
        {
            _problem = problem;
        }

        if (outcome.Kind != UploadOutcomeKind.NotDue)
        {
            _log("YouTube: " + outcome.Message);
        }

        Notify();
        return new YouTubeUploadOutcome(MapKind(outcome.Kind), outcome.VideoId);
    }

    private YouTubeProblem? PreviousProblem()
    {
        lock (_gate)
        {
            return _problem;
        }
    }

    private void SetProblem(YouTubeProblem problem)
    {
        lock (_gate)
        {
            _problem = problem;
        }
    }

    private YouTubeLoginResult Fail(YouTubeProblem problem)
    {
        SetProblem(problem);
        Notify();
        return new YouTubeLoginResult(false, problem);
    }

    private bool UpdateConfig(Func<AppConfig.YouTubeConfig, AppConfig.YouTubeConfig> change)
    {
        lock (_configGate)
        {
            AppConfig current = _engine.GetConfig();
            AppConfig next = current with { YouTube = change(current.YouTube) };
            ConfigUpdateResult result = _engine.UpdateConfig(next);
            if (!result.Ok)
            {
                _log("YouTube: salvataggio della configurazione non riuscito.");
            }

            return result.Ok;
        }
    }

    private bool IsConnected() => _store.Load() is not null;

    private TimeZoneInfo ZoneOf() => YouTubeConfigMapper.ResolveZone(_engine.GetConfig().Site.Timezone);

    private static YouTubeProblem? JournalProblem(UploadJournalState state, DateTimeOffset now)
    {
        if (state.QuotaBlockedUntilUtc is { } until && now < until)
        {
            return YouTubeProblem.QuotaExceeded;
        }

        if (state.Nights.Values.Any(r => r.State == NightUploadState.NeedsReview))
        {
            return YouTubeProblem.NeedsReview;
        }

        return state.Nights.Values.Any(r => r.State == NightUploadState.Failed) ? YouTubeProblem.UploadFailed : null;
    }

    private static YouTubeLastUpload? LastUpload(UploadJournalState state)
    {
        NightUploadRecord? record = state.Nights.Values
            .Where(r => r.State == NightUploadState.Completed && YouTubeLinks.IsValidVideoId(r.VideoId))
            .OrderByDescending(r => r.UpdatedUtc)
            .FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        return new YouTubeLastUpload(record.NightId, record.VideoId!, YouTubeLinks.WatchUrl(record.VideoId)!, record.UpdatedUtc);
    }

    private static YouTubeUploadOutcomeKind MapKind(UploadOutcomeKind kind) => kind switch
    {
        UploadOutcomeKind.Completed => YouTubeUploadOutcomeKind.Completed,
        UploadOutcomeKind.AlreadyUploaded => YouTubeUploadOutcomeKind.AlreadyUploaded,
        UploadOutcomeKind.InProgress => YouTubeUploadOutcomeKind.InProgress,
        UploadOutcomeKind.NeedsReview => YouTubeUploadOutcomeKind.NeedsReview,
        UploadOutcomeKind.QuotaBlocked => YouTubeUploadOutcomeKind.QuotaBlocked,
        UploadOutcomeKind.AuthRequired => YouTubeUploadOutcomeKind.AuthRequired,
        UploadOutcomeKind.Failed => YouTubeUploadOutcomeKind.Failed,
        UploadOutcomeKind.ConfigError => YouTubeUploadOutcomeKind.ConfigError,
        UploadOutcomeKind.NoNight => YouTubeUploadOutcomeKind.NoNight,
        UploadOutcomeKind.DailyLimitReached => YouTubeUploadOutcomeKind.DailyLimitReached,
        UploadOutcomeKind.NotDue => YouTubeUploadOutcomeKind.NotDue,
        UploadOutcomeKind.SlotMissed => YouTubeUploadOutcomeKind.SlotMissed,
        _ => YouTubeUploadOutcomeKind.Unavailable,
    };

    private static string Code(Exception ex) => ex is OAuthException oauth ? oauth.ErrorCode : ex.GetType().Name;

    private void Notify()
    {
        Action<YouTubeStatus>? handler = StatusChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(GetStatus());
        }
        catch (Exception ex)
        {
            _log("YouTube: aggiornamento dello stato non riuscito (" + ex.GetType().Name + ").");
        }
    }
}
