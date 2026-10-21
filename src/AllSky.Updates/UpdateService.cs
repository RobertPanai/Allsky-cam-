using System.Net.Sockets;

namespace AllSky.Updates;

/// <summary>
/// Controllo, download e installazione degli aggiornamenti di AllSky.
/// </summary>
/// <remarks>
/// Regole:
/// <list type="bullet">
/// <item>Installazione non aggiornabile: stato <see cref="UpdateState.Disabled"/> e nessuna chiamata al backend.</item>
/// <item>Controllo automatico (solo con AutoCheck attivo): il primo controllo 60 s dopo la creazione, poi ogni 24 h.
/// Se trova una versione la scarica da sola, ma non la installa.</item>
/// <item>Installazione solo su richiesta dell'utente: <see cref="ApplyLater"/> non viene mai chiamato dal servizio.</item>
/// <item>Errori: diventano stato <see cref="UpdateState.Error"/> con codice e messaggio fissi. Nessuna eccezione esce dal servizio.</item>
/// </list>
/// Tutti i metodi pubblici sono thread-safe. Controllo e download sono serializzati tra loro.
/// </remarks>
public sealed class UpdateService : IDisposable
{
    /// <summary>Ritardo del primo controllo automatico dopo la creazione del servizio.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    /// <summary>Intervallo minimo tra due controlli automatici.</summary>
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    /// <summary>Durata massima di un controllo della versione.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Ogni quanto il timer verifica se il controllo automatico è dovuto.</summary>
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);

    private const string NetworkMessage =
        "Impossibile contattare il server degli aggiornamenti. Controllare la connessione e riprovare.";
    private const string GenericMessage = "Aggiornamento non riuscito. Riprovare più tardi.";
    private const string ApplyMessage = "Impossibile avviare l'installazione dell'aggiornamento. Riprovare più tardi.";

    private readonly IUpdateBackend _backend;
    private readonly IUpdateClock _clock;
    private readonly bool _installed;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _gate = new();
    private UpdateStatus _status;              // _gate
    private bool _autoCheck;                   // _gate
    private DateTimeOffset _nextAutoCheckUtc;  // _gate
    private Timer? _timer;                     // _gate
    private bool _disposed;                    // _gate

    /// <summary>Crea il servizio. Non avvia alcun timer: vedi <see cref="Start"/>.</summary>
    /// <param name="backend">Sorgente degli aggiornamenti.</param>
    /// <param name="clock">Orologio, per il calcolo dei 24 h.</param>
    /// <param name="autoCheck">Impostazione del controllo automatico salvata.</param>
    public UpdateService(IUpdateBackend backend, IUpdateClock clock, bool autoCheck)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);

        _backend = backend;
        _clock = clock;
        _autoCheck = autoCheck;
        _installed = ReadSafely(() => backend.IsInstalled, false);
        string currentVersion = ReadSafely(() => backend.CurrentVersion, null) ?? string.Empty;
        _nextAutoCheckUtc = clock.UtcNow + StartupDelay;
        _status = new UpdateStatus(
            _installed ? UpdateState.Idle : UpdateState.Disabled,
            currentVersion,
            AvailableVersion: null,
            ProgressPercent: null,
            ErrorCode: null,
            ErrorMessage: null,
            LastCheckUtc: null);
    }

    /// <summary>Vero se l'installazione può aggiornarsi.</summary>
    public bool IsInstalled => _installed;

    /// <summary>Impostazione corrente del controllo automatico.</summary>
    public bool AutoCheckEnabled
    {
        get
        {
            lock (_gate)
            {
                return _autoCheck;
            }
        }
    }

    /// <summary>Istantanea dello stato.</summary>
    public UpdateStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    /// <summary>Cambia il controllo automatico. Ha effetto dal prossimo controllo programmato.</summary>
    /// <param name="enabled">True per controllare e scaricare da soli.</param>
    public void SetAutoCheck(bool enabled)
    {
        lock (_gate)
        {
            _autoCheck = enabled;
        }
    }

    /// <summary>Avvia il timer del controllo automatico. Senza installazione aggiornabile non fa nulla.</summary>
    public void Start()
    {
        if (!_installed)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _timer = new Timer(OnTimer, null, StartupDelay, TickPeriod);
        }
    }

    /// <summary>Ferma il timer. Le operazioni in corso terminano normalmente.</summary>
    public void Dispose()
    {
        Timer? timer;
        lock (_gate)
        {
            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    /// <summary>
    /// Controllo richiesto dall'utente (o dal controllo automatico). Il risultato è nello stato restituito: non lancia eccezioni.
    /// Con un aggiornamento già scaricato non interroga il server.
    /// </summary>
    /// <param name="cancellationToken">Annullamento della richiesta.</param>
    public async Task<UpdateStatus> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_installed)
        {
            return GetStatus();
        }

        try
        {
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return GetStatus();
        }

        try
        {
            UpdateStatus before = GetStatus();
            if (before.State is UpdateState.ReadyToInstall or UpdateState.Downloading)
            {
                return before;
            }

            Update(s => s with { State = UpdateState.Checking, ProgressPercent = null, ErrorCode = null, ErrorMessage = null });
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(CheckTimeout);

                // WaitAsync: anche un backend che ignora il token non blocca il servizio oltre il timeout.
                string? available = await _backend.CheckAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                DateTimeOffset now = _clock.UtcNow;
                Update(s => available is null
                    ? s with { State = UpdateState.UpToDate, AvailableVersion = null, LastCheckUtc = now }
                    : s with { State = UpdateState.Available, AvailableVersion = available, LastCheckUtc = now });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Update(s => s with { State = before.State });
            }
            catch (Exception ex)
            {
                Fail(ex, clearAvailableVersion: true);
            }
        }
        finally
        {
            _operations.Release();
        }

        return GetStatus();
    }

    /// <summary>
    /// Scarica la versione trovata dall'ultimo controllo. Funziona solo dallo stato <see cref="UpdateState.Available"/>.
    /// Non lancia eccezioni.
    /// </summary>
    /// <param name="cancellationToken">Annullamento del download. Il Tray e il controllo automatico non lo annullano.</param>
    public async Task<UpdateStatus> DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_installed)
        {
            return GetStatus();
        }

        try
        {
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return GetStatus();
        }

        try
        {
            if (GetStatus().State == UpdateState.Available)
            {
                await DownloadCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _operations.Release();
        }

        return GetStatus();
    }

    /// <summary>
    /// Programma l'installazione dell'aggiornamento scaricato: avviene quando AllSky esce, poi l'app si riavvia.
    /// Va chiamato solo su richiesta dell'utente. Senza un aggiornamento pronto non fa nulla.
    /// </summary>
    public UpdateStatus ApplyLater()
    {
        if (!_installed || GetStatus().State != UpdateState.ReadyToInstall)
        {
            return GetStatus();
        }

        try
        {
            _backend.ApplyAndRestartLater();
        }
        catch (Exception)
        {
            Update(s => s with
            {
                State = UpdateState.Error,
                ErrorCode = UpdateErrorCodes.Apply,
                ErrorMessage = ApplyMessage,
                ProgressPercent = null,
            });
        }

        return GetStatus();
    }

    /// <summary>
    /// Controllo programmato: se è il momento e il controllo automatico è attivo, controlla e, se trova una versione, la scarica.
    /// Chiamato dal timer; i test lo chiamano direttamente con un orologio finto. Non lancia eccezioni.
    /// </summary>
    internal async Task TickAsync()
    {
        bool due;
        lock (_gate)
        {
            if (!_installed || _disposed || !_autoCheck)
            {
                return;
            }

            DateTimeOffset now = _clock.UtcNow;
            due = now >= _nextAutoCheckUtc;
            if (due)
            {
                _nextAutoCheckUtc = now + AutoCheckInterval;
            }
        }

        if (!due)
        {
            return;
        }

        UpdateStatus status = await CheckNowAsync().ConfigureAwait(false);
        if (status.State == UpdateState.Available && AutoCheckEnabled)
        {
            await DownloadAsync().ConfigureAwait(false);
        }
    }

    private void OnTimer(object? state)
    {
        _ = TickAsync();
    }

    /// <summary>Corpo del download. Chiamato con il semaforo già acquisito.</summary>
    private async Task DownloadCoreAsync(CancellationToken cancellationToken)
    {
        Update(s => s with { State = UpdateState.Downloading, ProgressPercent = 0, ErrorCode = null, ErrorMessage = null });
        try
        {
            var progress = new CallbackProgress(p => Update(s => s with { ProgressPercent = Math.Clamp(p, 0, 100) }));
            await _backend.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
            Update(s => s with { State = UpdateState.ReadyToInstall, ProgressPercent = 100 });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(s => s with { State = UpdateState.Available, ProgressPercent = null });
        }
        catch (Exception ex)
        {
            Fail(ex, clearAvailableVersion: false);
        }
    }

    /// <summary>Classifica l'eccezione e la registra nello stato con un messaggio fisso.</summary>
    private void Fail(Exception ex, bool clearAvailableVersion)
    {
        (string code, string message) = Describe(ex);
        Update(s => s with
        {
            State = UpdateState.Error,
            ErrorCode = code,
            ErrorMessage = message,
            ProgressPercent = null,
            AvailableVersion = clearAvailableVersion ? null : s.AvailableVersion,
        });
    }

    /// <summary>
    /// Codice e messaggio per un'eccezione del backend. Le eccezioni di rete e i timeout diventano "network";
    /// le letture e le scritture su disco (IOException) sono trattate come rete perché Velopack le segnala così.
    /// </summary>
    private static (string Code, string Message) Describe(Exception ex) => ex switch
    {
        HttpRequestException or SocketException or IOException or TimeoutException or OperationCanceledException
            => (UpdateErrorCodes.Network, NetworkMessage),
        _ => (UpdateErrorCodes.Unknown, GenericMessage),
    };

    private void Update(Func<UpdateStatus, UpdateStatus> change)
    {
        lock (_gate)
        {
            _status = change(_status);
        }
    }

    private static T ReadSafely<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            // Il backend non deve far cadere il servizio: si tratta come non disponibile.
            return fallback;
        }
    }

    /// <summary>Riporta l'avanzamento in modo sincrono, senza SynchronizationContext.</summary>
    private sealed class CallbackProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
