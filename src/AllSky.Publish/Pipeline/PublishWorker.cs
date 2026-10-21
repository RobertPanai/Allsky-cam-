using AllSky.Core;

namespace AllSky.Publish;

/// <summary>
/// Publisher del modulo. Tiene solo l'ultimo frame offerto; a intervalli prepara l'immagine, la accoda su disco e la invia al server.
/// <para>
/// <see cref="Offer"/> non blocca e non lancia mai: memorizza il frame sostituendo il precedente. Tutto il lavoro
/// (ridimensionamento, JPEG, disco, rete) avviene nel ciclo di sfondo avviato da <see cref="StartAsync"/>.
/// </para>
/// <para>
/// Regole: con impostazioni disattivate o non valide non si fa nulla e si ricontrolla ogni secondo. Ogni intervallo
/// (minimo <see cref="MinIntervalSeconds"/> secondi) si prende l'ultimo frame nuovo; un'immagine identica alla ultima accodata
/// non viene reinviata. Lo svuotamento della coda invia solo i <see cref="MaxBacklogToSend"/> elementi piu' recenti, dal piu' vecchio.
/// Errori temporanei: backoff esponenziale, con il Retry-After del server come minimo. Credenziali rifiutate (401/403): invio
/// sospeso finche' le credenziali non cambiano. Errori permanenti (400, 413, 415): l'elemento viene scartato e non ritentato.
/// </para>
/// <para>
/// Scelte di interpretazione: il backoff e' un cancello sull'invio, l'acquisizione continua durante l'attesa e la coda
/// si limita da sola (limite di <see cref="DiskQueue"/>). Gli elementi espulsi dalla coda piena, quelli tagliati a
/// <see cref="MaxBacklogToSend"/> e quelli svuotati da un cambio di server o dispositivo sono contati in
/// <see cref="PublishStatus.DiscardedTotal"/>. Un'immagine mancante su disco viene rimossa senza contarla come scartata.
/// <see cref="PublishStatus.LastError"/> resta valorizzato anche dopo un invio riuscito: la data dell'ultimo successo e' in
/// <see cref="PublishStatus.LastSuccessUtc"/>.
/// </para>
/// </summary>
public sealed class PublishWorker : IPublisher
{
    /// <summary>Numero massimo di elementi inviati a ogni svuotamento della coda: i piu' vecchi vengono scartati.</summary>
    public const int MaxBacklogToSend = 3;

    /// <summary>Intervallo minimo fra due acquisizioni, in secondi.</summary>
    public const int MinIntervalSeconds = 15;

    private const int QuietTicksLogEvery = 10;
    private const int MaxTextLength = 200;
    private static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DisabledPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InternalErrorPause = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly IClock _clock;
    private readonly IPublishDelay _delay;
    private readonly IUplinkSender _sender;
    private readonly WebImagePreparer _preparer;
    private readonly DiskQueue _queue;
    private readonly RetryPolicy _retry;
    private readonly Action<string>? _log;

    // Lock dedicato al frame: Offer non deve mai aspettare disco o rete.
    private readonly object _frameGate = new();
    private PublishFrame? _pendingFrame;   // protetto da _frameGate
    private bool _acceptFrames;            // protetto da _frameGate

    // Lock di stato: tenuto solo per leggere o scrivere campi, mai durante I/O, log o eventi.
    private readonly object _gate = new();
    private PublishSettings _settings;     // protetto da _gate
    private bool _active;
    private PublishState _state = PublishState.Idle;
    private DateTimeOffset? _lastSuccessUtc;
    private string? _lastErrorCode;
    private string? _lastError;
    private DateTimeOffset? _lastErrorUtc;
    private int _sentTotal;
    private int _discardedTotal;
    private DateTimeOffset? _nextAttemptUtc;
    private int _failures;
    private DateTimeOffset _nextAcquireUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastProcessedCapturedUtc;
    private ulong? _lastQueuedHash;
    private int _quietTicks;
    private CancellationTokenSource _wakeCts = new();
    private PublishStatus? _lastNotified;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    /// <summary>Emesso quando cambia un campo dello stato visibile. Gli handler che lanciano vengono ignorati.</summary>
    public event Action<PublishStatus>? StatusChanged;

    /// <summary>Task del ciclo di sfondo (per i test): null prima del primo StartAsync.</summary>
    internal Task? LoopTask
    {
        get
        {
            lock (_gate)
            {
                return _loopTask;
            }
        }
    }

    /// <summary>Crea il worker. Non avvia nulla: il ciclo parte con <see cref="StartAsync"/>.</summary>
    /// <param name="initialSettings">Impostazioni iniziali.</param>
    /// <param name="clock">Orologio (UTC) per ogni decisione temporale.</param>
    /// <param name="delay">Attesa interrompibile del ciclo.</param>
    /// <param name="sender">Invio verso il server.</param>
    /// <param name="preparer">Ridimensionamento e codifica JPEG.</param>
    /// <param name="queue">Coda su disco.</param>
    /// <param name="retry">Politica di backoff.</param>
    /// <param name="log">Righe di log in italiano, senza segreti. Null: nessun log.</param>
    public PublishWorker(PublishSettings initialSettings, IClock clock, IPublishDelay delay, IUplinkSender sender,
        WebImagePreparer preparer, DiskQueue queue, RetryPolicy retry, Action<string>? log = null)
    {
        _settings = initialSettings ?? throw new ArgumentNullException(nameof(initialSettings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _preparer = preparer ?? throw new ArgumentNullException(nameof(preparer));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _log = log;

        _active = IsActive(initialSettings);
        _acceptFrames = _active;
        _state = _active ? PublishState.Idle : PublishState.Disabled;
    }

    /// <inheritdoc/>
    /// <remarks>Operazione O(1): memorizza solo il frame. Ignorata se la pubblicazione e' disattiva. Non lancia mai.</remarks>
    public void Offer(PublishFrame frame)
    {
        try
        {
            if (frame is null)
            {
                return;
            }

            lock (_frameGate)
            {
                if (_acceptFrames)
                {
                    _pendingFrame = frame;
                }
            }
        }
        catch (Exception)
        {
            // Offer non lancia mai: un frame che non si riesce a memorizzare viene semplicemente ignorato.
        }
    }

    /// <inheritdoc/>
    public PublishStatus GetStatus()
    {
        lock (_gate)
        {
            return BuildStatusLocked();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Cambia le impostazioni a caldo. Riprende da Suspended solo se cambiano credenziali. Se cambiano server o dispositivo, la coda viene svuotata.</remarks>
    public void ApplySettings(PublishSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool active = IsActive(settings);
        bool wasActive;
        bool credentialsChanged;
        bool endpointChanged;
        bool resumed = false;

        lock (_gate)
        {
            PublishSettings old = _settings;
            _settings = settings;
            wasActive = _active;
            _active = active;
            credentialsChanged = HasCredentialChange(old, settings);
            endpointChanged = !string.Equals(old.ServerUrl, settings.ServerUrl, StringComparison.Ordinal)
                || !string.Equals(old.DeviceId, settings.DeviceId, StringComparison.Ordinal);

            if (endpointChanged)
            {
                _lastQueuedHash = null;
            }

            if (!active)
            {
                _state = PublishState.Disabled;
                _nextAttemptUtc = null;
            }
            else if (!wasActive)
            {
                // Riattivazione: si riparte da zero, senza backoff ereditato.
                _state = PublishState.Idle;
                _nextAttemptUtc = null;
                _failures = 0;
            }
            else if (_state == PublishState.Suspended && credentialsChanged)
            {
                _state = PublishState.Idle;
                _nextAttemptUtc = null;
                _failures = 0;
                resumed = true;
            }
        }

        lock (_frameGate)
        {
            _acceptFrames = active;
            if (!active)
            {
                _pendingFrame = null;
            }
        }

        // Sveglia il ciclo: il prossimo giro usa le impostazioni nuove.
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _wakeCts;
            _wakeCts = new CancellationTokenSource();
        }

        previous.Cancel();

        if (endpointChanged)
        {
            int removed = _queue.TrimToNewest(0);
            if (removed > 0)
            {
                lock (_gate)
                {
                    _discardedTotal += removed;
                }
            }

            Log("Server o dispositivo cambiati: coda svuotata.");
        }

        if (resumed)
        {
            Log("Credenziali cambiate: invio ripreso.");
        }

        if (active && !wasActive)
        {
            Log("Pubblicazione attiva.");
        }
        else if (!active && wasActive)
        {
            Log("Pubblicazione disattivata.");
        }
        else if (!active && settings.Enabled)
        {
            IReadOnlyList<SettingsError> errors = SettingsValidator.Validate(settings);
            Log("Impostazioni non valide, pubblicazione sospesa: " + string.Join(", ", errors.Select(e => e.Path)) + ".");
        }

        NotifyIfChanged();
    }

    /// <inheritdoc/>
    /// <remarks>Usa le impostazioni correnti. Non invia frame. Se il test riesce mentre il publisher e' Suspended, lo riprende (Idle, senza backoff). Se fallisce, non cambia lo stato.</remarks>
    public async Task<TestResult> TestConnectionAsync(PublishSettings? overrideSettings, CancellationToken ct)
    {
        PublishSettings target = overrideSettings ?? CurrentSettings();
        try
        {
            TestResult result = await _sender.TestConnectionAsync(target, ct).ConfigureAwait(false);
            var outcome = new TestResult(result.Ok, Clean(result.Code, target, "errore"), Clean(result.Message, target, "Test non riuscito."), result.StatusCode);

            if (outcome.Ok && overrideSettings is null)
            {
                bool resumed;
                lock (_gate)
                {
                    resumed = _state == PublishState.Suspended;
                    if (resumed)
                    {
                        _state = PublishState.Idle;
                        _nextAttemptUtc = null;
                        _failures = 0;
                    }
                }

                if (resumed)
                {
                    Log("Connessione verificata: invio ripreso.");
                    NotifyIfChanged();
                }
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new TestResult(false, "network", "Connessione non riuscita: errore di rete o di configurazione.", null);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Avvia il ciclo di sfondo. Se il publisher era sospeso, lo riprende. Chiamarlo piu' volte e' tollerato.</remarks>
    public Task StartAsync(CancellationToken ct)
    {
        bool resumed;
        lock (_gate)
        {
            resumed = _state == PublishState.Suspended;
            if (resumed)
            {
                _state = PublishState.Idle;
                _nextAttemptUtc = null;
                _failures = 0;
            }

            if (_loopTask is not { IsCompleted: false })
            {
                if (_loopTask is not null)
                {
                    _loopCts?.Dispose();
                }

                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                CancellationToken token = _loopCts.Token;
                _loopTask = Task.Run(() => RunLoopSafeAsync(token));
            }
        }

        if (resumed)
        {
            Log("Invio ripreso su richiesta.");
        }

        NotifyIfChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Annulla il ciclo e attende al massimo 5 secondi. La coda resta su disco per il prossimo avvio.</remarks>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            cts = _loopCts;
            task = _loopTask;
        }

        if (cts is null || task is null)
        {
            return;
        }

        cts.Cancel();

        // Unico Task.Delay reale del modulo: solo come rete di sicurezza, il ciclo deve uscire prima.
        await Task.WhenAny(task, Task.Delay(StopTimeout)).ConfigureAwait(false);
        NotifyIfChanged();
    }

    /// <summary>Equivalente a <see cref="StopAsync"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Esegue UN giro del ciclo: acquisizione se e' il momento, poi svuotamento della coda. Usato dal ciclo e dai test.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        PublishSettings settings;
        bool active;
        lock (_gate)
        {
            settings = _settings;
            active = _active;
        }

        if (!active)
        {
            NotifyIfChanged();
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        bool due;
        lock (_gate)
        {
            due = now >= _nextAcquireUtc;
            if (due)
            {
                _nextAcquireUtc = now + IntervalOf(settings);
            }
        }

        if (due)
        {
            Acquire(settings);
        }

        await DrainAsync(settings, ct).ConfigureAwait(false);
        NotifyIfChanged();
    }

    /// <summary>Ciclo di sfondo con gestione di ogni eccezione: un errore imprevisto viene registrato e il ciclo riprende dopo 5 s.</summary>
    private async Task RunLoopSafeAsync(CancellationToken ct)
    {
        try
        {
            await LoopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arresto normale.
        }
        catch (Exception)
        {
            Log("Ciclo di pubblicazione terminato in modo imprevisto.");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CancellationToken wake;
                lock (_gate)
                {
                    wake = _wakeCts.Token;
                }

                await RunOnceAsync(ct).ConfigureAwait(false);
                TimeSpan wait = ComputeWait();

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, wake);
                try
                {
                    await _delay.DelayAsync(wait, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Svegliato da ApplySettings: il giro successivo usa le impostazioni nuove.
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                RecordError("interno", "Errore interno del pubblicatore.");
                Log("Errore interno del pubblicatore: nuovo tentativo tra 5 secondi.");
                NotifyIfChanged();
                try
                {
                    await _delay.DelayAsync(InternalErrorPause, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Tempo da dormire: il minore fra intervallo, prossimo tick e prossimo tentativo (se in backoff), con minimo 1 s.</summary>
    private TimeSpan ComputeWait()
    {
        DateTimeOffset now = _clock.UtcNow;
        lock (_gate)
        {
            if (!_active)
            {
                return DisabledPoll;
            }

            TimeSpan wait = IntervalOf(_settings);
            TimeSpan toTick = _nextAcquireUtc - now;
            if (toTick < wait)
            {
                wait = toTick;
            }

            if (_state == PublishState.Backoff && _nextAttemptUtc is { } next)
            {
                TimeSpan toAttempt = next - now;
                if (toAttempt < wait)
                {
                    wait = toAttempt;
                }
            }

            return wait < MinSleep ? MinSleep : wait;
        }
    }

    /// <summary>Acquisizione: prende l'ultimo frame nuovo, lo prepara e lo accoda. Nessun invio qui.</summary>
    private void Acquire(PublishSettings settings)
    {
        PublishFrame? frame;
        lock (_frameGate)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }

        if (frame is null || IsAlreadyProcessed(frame))
        {
            int quiet;
            lock (_gate)
            {
                quiet = _quietTicks++;
            }

            if (quiet % QuietTicksLogEvery == 0)
            {
                Log("Nessuna nuova immagine dalla camera.");
            }

            return;
        }

        lock (_gate)
        {
            _lastProcessedCapturedUtc = frame.CapturedAtUtc;
            _quietTicks = 0;
        }

        PreparedImage prepared;
        try
        {
            prepared = _preparer.Prepare(frame, settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordError("image", "Preparazione dell'immagine non riuscita.");
            Log("Preparazione dell'immagine non riuscita (" + ex.GetType().Name + ").");
            return;
        }

        ulong? lastHash;
        lock (_gate)
        {
            lastHash = _lastQueuedHash;
        }

        if (lastHash == prepared.ContentHash)
        {
            return;
        }

        try
        {
            string captureId = CaptureIdFactory.Create(settings.DeviceId, frame.CapturedAtUtc);
            var item = new QueuedItem(captureId, frame.CapturedAtUtc.ToUniversalTime(), frame.ExposureMs, frame.Gain,
                frame.Mode, frame.TemperatureC, string.Empty);

            bool existed = _queue.List().Any(i => i.CaptureId == captureId);
            int before = _queue.Count;
            _queue.Enqueue(item, prepared.Jpeg);
            int evicted = Math.Max(0, before + (existed ? 0 : 1) - _queue.Count);

            lock (_gate)
            {
                _lastQueuedHash = prepared.ContentHash;
                _discardedTotal += evicted;
            }

            Log("Immagine accodata.");
            if (evicted > 0)
            {
                Log($"Coda piena: scartate {evicted} immagini vecchie.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordError("image", "Salvataggio dell'immagine in coda non riuscito.");
            Log("Salvataggio dell'immagine in coda non riuscito (" + ex.GetType().Name + ").");
        }
    }

    private bool IsAlreadyProcessed(PublishFrame frame)
    {
        lock (_gate)
        {
            return _lastProcessedCapturedUtc is { } last && frame.CapturedAtUtc <= last;
        }
    }

    /// <summary>Svuotamento: invia gli elementi piu' recenti, dal piu' vecchio al piu' recente, rispettando lo stato e il backoff.</summary>
    private async Task DrainAsync(PublishSettings settings, CancellationToken ct)
    {
        if (IsGated(_clock.UtcNow))
        {
            return;
        }

        if (_queue.Count == 0)
        {
            return;
        }

        int trimmed = _queue.TrimToNewest(MaxBacklogToSend);
        if (trimmed > 0)
        {
            lock (_gate)
            {
                _discardedTotal += trimmed;
            }

            Log($"Coda: scartate {trimmed} immagini vecchie.");
        }

        IReadOnlyList<QueuedItem> items = _queue.List();
        foreach (QueuedItem item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (IsGated(_clock.UtcNow))
            {
                break;
            }

            byte[]? jpeg = _queue.ReadImage(item);
            if (jpeg is null)
            {
                _queue.Remove(item.CaptureId);
                Log("Immagine mancante su disco: rimossa dalla coda.");
                continue;
            }

            lock (_gate)
            {
                _state = PublishState.Sending;
            }

            NotifyIfChanged();
            SendResult result = await SendOneAsync(item, jpeg, settings, ct).ConfigureAwait(false);
            bool keepGoing = ApplyResult(item, result, settings);
            NotifyIfChanged();
            if (!keepGoing)
            {
                break;
            }
        }

        lock (_gate)
        {
            if (_state == PublishState.Sending)
            {
                _state = PublishState.Idle;
            }
        }
    }

    /// <summary>True se l'invio non puo' partire adesso: sospeso, o prima del prossimo tentativo di backoff.</summary>
    private bool IsGated(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _state == PublishState.Suspended || (_nextAttemptUtc is { } next && now < next);
        }
    }

    private async Task<SendResult> SendOneAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct)
    {
        try
        {
            return await _sender.SendAsync(item, jpeg, settings, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_state == PublishState.Sending)
                {
                    _state = PublishState.Idle;
                }
            }

            throw;
        }
        catch (Exception)
        {
            Log("Errore interno durante l'invio: nuovo tentativo piu' tardi.");
            return new SendResult(SendOutcome.Transient, null, "interno", "Errore interno durante l'invio.", null);
        }
    }

    /// <summary>Applica l'esito di un invio. Restituisce false se lo svuotamento va interrotto.</summary>
    private bool ApplyResult(QueuedItem item, SendResult result, PublishSettings settings)
    {
        string code = Clean(result.Code, settings, "errore");
        string reason = Clean(result.Reason, settings, "Invio non riuscito.");
        DateTimeOffset now = _clock.UtcNow;

        switch (result.Outcome)
        {
            case SendOutcome.Acknowledged:
                _queue.Remove(item.CaptureId);
                lock (_gate)
                {
                    _sentTotal++;
                    _lastSuccessUtc = now;
                    _failures = 0;
                    _state = PublishState.Idle;
                    _nextAttemptUtc = null;
                }

                Log("Immagine inviata.");
                return true;

            case SendOutcome.AuthError:
                lock (_gate)
                {
                    _state = PublishState.Suspended;
                    _nextAttemptUtc = null;
                    SetErrorLocked(code, reason, now);
                }

                Log("Credenziali rifiutate dal server: invio sospeso. Salva impostazioni con credenziali nuove per riprendere.");
                return false;

            case SendOutcome.Permanent:
                _queue.Remove(item.CaptureId);
                lock (_gate)
                {
                    _discardedTotal++;
                    SetErrorLocked(code, reason, now);
                }

                Log($"Immagine scartata dal server ({code}): non verra' ritentata.");
                return true;

            default:
                // Transient e RateLimited: l'elemento resta in coda e si riprova dopo il backoff.
                ScheduleRetry(result.Outcome == SendOutcome.RateLimited ? result.RetryAfter : null, code, reason, now);
                return false;
        }
    }

    private void ScheduleRetry(TimeSpan? retryAfter, string code, string reason, DateTimeOffset now)
    {
        int attempt;
        lock (_gate)
        {
            _failures++;
            attempt = _failures;
        }

        TimeSpan delay = _retry.GetDelay(attempt, retryAfter);
        lock (_gate)
        {
            _state = PublishState.Backoff;
            _nextAttemptUtc = now + delay;
            SetErrorLocked(code, reason, now);
        }

        Log($"Invio non riuscito ({code}): nuovo tentativo tra {(int)Math.Ceiling(delay.TotalSeconds)} secondi.");
    }

    /// <summary>Costruisce lo stato visibile. Chiamare con _gate preso.</summary>
    private PublishStatus BuildStatusLocked() => new(
        _state,
        _lastSuccessUtc,
        _lastErrorCode,
        _lastError,
        _lastErrorUtc,
        _queue.Count,
        _sentTotal,
        _discardedTotal,
        _state == PublishState.Backoff ? _nextAttemptUtc : null);

    private void SetErrorLocked(string code, string text, DateTimeOffset now)
    {
        _lastErrorCode = code;
        _lastError = text;
        _lastErrorUtc = now;
    }

    private void RecordError(string code, string text)
    {
        DateTimeOffset now = _clock.UtcNow;
        lock (_gate)
        {
            SetErrorLocked(code, text, now);
        }
    }

    /// <summary>Emette StatusChanged se lo stato visibile e' cambiato dall'ultima emissione. Gli eventi partono fuori dal lock.</summary>
    private void NotifyIfChanged()
    {
        PublishStatus current;
        lock (_gate)
        {
            current = BuildStatusLocked();
            if (current == _lastNotified)
            {
                return;
            }

            _lastNotified = current;
        }

        Action<PublishStatus>? handler = StatusChanged;
        if (handler is null)
        {
            return;
        }

        foreach (Delegate single in handler.GetInvocationList())
        {
            try
            {
                ((Action<PublishStatus>)single)(current);
            }
            catch (Exception)
            {
                // Un handler difettoso non deve fermare il publisher ne' gli altri handler.
            }
        }
    }

    private void Log(string message)
    {
        Action<string>? log = _log;
        if (log is null)
        {
            return;
        }

        try
        {
            log(message);
        }
        catch (Exception)
        {
            // Il log non deve mai interrompere il ciclo.
        }
    }

    private PublishSettings CurrentSettings()
    {
        lock (_gate)
        {
            return _settings;
        }
    }

    /// <summary>Ripulisce un testo prima di salvarlo o registrarlo: rimuove segreti e URL, toglie i caratteri di controllo, limita la lunghezza.</summary>
    private static string Clean(string? text, PublishSettings settings, string fallback)
    {
        string value = text ?? string.Empty;
        foreach (string secret in new[] { settings.Token.Reveal(), settings.ExtraHeaderValue.Reveal(), settings.ServerUrl })
        {
            if (secret.Length > 0)
            {
                value = value.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        value = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (value.Length > MaxTextLength)
        {
            value = value[..MaxTextLength];
        }

        return value.Length == 0 ? fallback : value;
    }

    private static bool IsActive(PublishSettings settings) => settings.Enabled && SettingsValidator.Validate(settings).Count == 0;

    private static bool HasCredentialChange(PublishSettings old, PublishSettings updated) =>
        !string.Equals(old.ServerUrl, updated.ServerUrl, StringComparison.Ordinal)
        || !string.Equals(old.DeviceId, updated.DeviceId, StringComparison.Ordinal)
        || !old.Token.Equals(updated.Token)
        || !string.Equals(old.ExtraHeaderName, updated.ExtraHeaderName, StringComparison.Ordinal)
        || !old.ExtraHeaderValue.Equals(updated.ExtraHeaderValue);

    private static TimeSpan IntervalOf(PublishSettings settings) =>
        TimeSpan.FromSeconds(Math.Max(MinIntervalSeconds, settings.IntervalSeconds));
}
