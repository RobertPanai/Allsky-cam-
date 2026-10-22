using AllSky.Archive;
using AllSky.Astro;
using AllSky.Capture;
using AllSky.Core;
using AllSky.Publish;
using AllSky.Timelapse;
using System.Globalization;

namespace AllSky.Engine;

/// <summary>
/// Servizio applicativo dell'all-sky: cattura, elaborazione, archivio, prodotti e configurazione.
/// L'Host HTTP lo espone senza logica propria. La classe e' divisa in quattro file parziali:
/// ciclo di vita e configurazione (questo), frame e prodotti notturni, notti e archivio, timelapse.
/// <para>
/// Thread-safety: i membri pubblici possono essere chiamati da thread diversi. Lo stato e' protetto da
/// <c>_gate</c>; gli eventi vengono emessi sempre fuori dai lock. Le operazioni lente (connessione, riavvio
/// del ciclo di cattura, arresto) sono serializzate da <c>_control</c>.
/// </para>
/// </summary>
public sealed partial class AllSkyEngine : IAllSkyService, AllSky.Core.Calibration.ICalibrationService, IAsyncDisposable
{
    /// <summary>Versione dell'applicazione, riportata in <see cref="StatusDto.Version"/>.</summary>
    public const string Version = "0.1.0";

    private const long BytesPerMb = 1024L * 1024L;
    private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;
    private static readonly TimeSpan DiskUsageCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConfigStore Store = new();

    private readonly IClock _clock;
    private readonly ICameraProvider _cameras;
    private readonly Func<AppConfig, IFrameProcessor> _processorFactory;
    private readonly ITimelapseBuilder _timelapse;
    private readonly ICaptureDelay _delay;
    private readonly IJpegEncoder _encoder;
    private readonly string _configPath;
    private readonly Action<string, Exception?> _log;
    private readonly Func<string?> _locateFfmpeg;
    private readonly IPublisher? _publisher;
    private readonly Func<AppConfig, IFrameOverlay?>? _frameOverlayFactory;

    // Serializza connessione, riavvio del ciclo e arresto. Mai tenuto durante l'emissione di eventi.
    private readonly SemaphoreSlim _control = new(1, 1);

    // Protegge lo stato condiviso. Non si attende mai dentro questo lock.
    private readonly object _gate = new();

    // Serializza il salvataggio su disco della configurazione.
    private readonly object _saveGate = new();

    // IFrameProcessor non e' thread-safe: ogni accesso passa da questo lock.
    private readonly object _processorGate = new();

    private readonly Dictionary<string, string> _kindsById = new(StringComparer.Ordinal); // _gate
    private readonly List<Task> _background = new();                                      // _gate

    private AppConfig _config;                  // _gate
    private TimeZoneInfo _tz;                   // _gate
    private NightArchive _archive;              // _gate
    private string? _configError;               // _gate: errore di configurazione rilevato all'avvio
    private string? _error;                     // _gate: errore di cattura o di connessione
    private string? _driverWarning;             // _gate: limiti di esposizione del driver sanificati, null se validi
    private IFrameProcessor? _processor;        // _processorGate
    private ICameraSource? _camera;             // _gate
    private string? _cameraId;                  // _gate
    private string _cameraKind = string.Empty;  // _gate
    private bool _cameraConnected;              // _gate
    private AutoExposure? _autoExposure;        // _gate
    private CaptureLoop? _loop;                 // _gate
    private CancellationTokenSource? _loopCts;  // _gate
    private Task? _loopTask;                    // _gate
    private LastFrameDto? _lastFrame;           // _gate
    private byte[]? _lastJpeg;                  // _gate
    private RawFrame? _lastRaw;                 // _gate
    private DiskDto? _diskCache;                // _gate
    private DateTimeOffset _diskCacheAt;        // _gate
    private bool _started;                      // _gate
    private DateTimeOffset _startedAt;          // _gate

    /// <summary>Emesso a ogni cambio di stato, errore, frame, lavoro o configurazione applicata.</summary>
    public event Action<StatusDto>? StatusChanged;

    /// <summary>Emesso per ogni frame elaborato e archiviato.</summary>
    public event Action<FrameEvent>? FrameProduced;

    /// <summary>
    /// Crea il servizio e carica la configurazione. Non avvia nulla: per la cattura chiamare <see cref="StartAsync"/>.
    /// Una configurazione non valida o illeggibile non blocca la creazione: lo stato diventa "error" con il motivo.
    /// </summary>
    /// <param name="dependencies">Dipendenze iniettate.</param>
    /// <exception cref="ArgumentNullException">Se una dipendenza e' null.</exception>
    /// <exception cref="ArgumentException">Se il percorso della configurazione e' vuoto.</exception>
    public AllSkyEngine(EngineDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.Clock);
        ArgumentNullException.ThrowIfNull(dependencies.CameraProvider);
        ArgumentNullException.ThrowIfNull(dependencies.ProcessorFactory);
        ArgumentNullException.ThrowIfNull(dependencies.TimelapseBuilder);
        ArgumentNullException.ThrowIfNull(dependencies.CaptureDelay);
        ArgumentNullException.ThrowIfNull(dependencies.JpegEncoder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencies.ConfigPath);

        _clock = dependencies.Clock;
        _cameras = dependencies.CameraProvider;
        _processorFactory = dependencies.ProcessorFactory;
        _timelapse = dependencies.TimelapseBuilder;
        _delay = dependencies.CaptureDelay;
        _encoder = dependencies.JpegEncoder;
        _configPath = Path.GetFullPath(dependencies.ConfigPath);
        _log = dependencies.Log ?? ((_, _) => { });
        _locateFfmpeg = dependencies.LocateFfmpeg ?? (() => FfmpegLocator.Locate());
        _publisher = dependencies.Publisher;
        _frameOverlayFactory = dependencies.FrameOverlayFactory;
        _observingConditions = dependencies.ObservingConditions;
        _weatherClock = new EngineWeatherClock(dependencies.Clock);
        InitPassForecasting(dependencies.OrbitPropagators, dependencies.TleFetcher);

        AppConfig config;
        try
        {
            config = Store.Load(_configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Lettura della configurazione non riuscita: uso i valori predefiniti", ex);
            config = new AppConfig();
            _configError = $"Impossibile leggere la configurazione '{_configPath}': {ex.Message}. Controllare i permessi del file.";
        }

        _config = config;
        _tz = ResolveTimeZone(config.Site.Timezone) ?? TimeZoneInfo.Utc;
        _archive = CreateArchive(config, _tz);
        ApplyWeatherConfig(config);

        IReadOnlyList<ValidationError> errors = ValidateAll(config);
        if (errors.Count > 0)
        {
            _configError ??= ConfigErrorText(errors);
        }
    }

    // ---- Ciclo di vita ----

    /// <summary>
    /// Avvia il servizio: coda dei timelapse, pulizia dei marker residui e connessione alla camera configurata.
    /// Un errore di connessione non lancia eccezioni: diventa lo stato "error".
    /// </summary>
    /// <param name="cancellationToken">Annullamento della connessione iniziale.</param>
    /// <exception cref="InvalidOperationException">Se il servizio e' gia' avviato.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        AppConfig config;
        string? configError;
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("Il servizio è già avviato.");
            }

            _started = true;
            _startedAt = now;
            config = _config;
            configError = _configError;
        }

        lock (_productsGate)
        {
            _accNight ??= ObservingNight.GetNightId(now, TimeZoneOf());
            _lastPartialSave = now;
        }

        StartTimelapseWorker();
        StartPassForecasting();
        ClearStaleTimelapseMarkers();

        if (configError is null && !string.IsNullOrWhiteSpace(config.Camera.Id))
        {
            await _control.WaitAsync(cancellationToken);
            try
            {
                await ConnectInternalAsync(config.Camera.Id, cancellationToken);
            }
            finally
            {
                _control.Release();
            }
        }

        RaiseStatus();
    }

    /// <summary>
    /// Arresta il servizio in modo pulito: ferma il ciclo di cattura, rilascia la camera, salva i prodotti parziali
    /// della notte in corso, attende i lavori in background e ferma la coda dei timelapse. Idempotente.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
        }

        await StopPassForecastingAsync();
        await _control.WaitAsync();
        try
        {
            await StopCaptureAsync();
            SetError(null);
        }
        finally
        {
            _control.Release();
        }

        await StopDetectionAsync();
        FlushPartialProducts();
        await WaitForBackgroundAsync();
        await StopTimelapseWorkerAsync();
        RaiseStatus();
    }

    /// <summary>Arresta il servizio e chiude la sorgente meteo. Equivale a <see cref="StopAsync"/> piu' la chiusura delle risorse meteo.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await ReleaseWeatherAsync();
    }

    // ---- Stato ----

    /// <inheritdoc />
    public StatusDto GetStatus()
    {
        DateTimeOffset now = _clock.UtcNow;
        AppConfig config;
        ICameraSource? camera;
        CaptureLoop? loop;
        string cameraId;
        string kind;
        bool connected;
        string? error;
        string? warning;
        LastFrameDto? lastFrame;
        bool started;
        DateTimeOffset startedAt;
        NightArchive archive;
        IReadOnlyList<JobDto> jobs;
        lock (_gate)
        {
            config = _config;
            camera = _camera;
            loop = _loop;
            cameraId = _cameraId ?? config.Camera.Id;
            kind = _cameraKind;
            connected = _cameraConnected;
            error = _error ?? _configError;
            warning = _driverWarning;
            lastFrame = _lastFrame;
            started = _started;
            startedAt = _startedAt;
            archive = _archive;
            jobs = SnapshotJobsLocked();
        }

        (double? sun, double? moon, string? phase) = Celestial(config, now);
        string state = error is not null
            ? "error"
            : loop?.State == CaptureState.Capturing ? "capturing" : "idle";
        long uptime = started ? (long)(now - startedAt).TotalSeconds : 0;

        return new StatusDto(
            Version,
            state,
            phase,
            sun,
            moon,
            new CameraStatusDto(cameraId, camera?.Name ?? string.Empty, kind, connected),
            lastFrame,
            Disk(archive, now),
            uptime,
            error,
            jobs,
            warning);
    }

    /// <inheritdoc />
    public byte[]? GetLatestJpeg(bool raw)
    {
        if (!raw)
        {
            lock (_gate)
            {
                return _lastJpeg;
            }
        }

        RawFrame? frame;
        lock (_gate)
        {
            frame = _lastRaw;
        }

        if (frame is null)
        {
            return null;
        }

        try
        {
            lock (_processorGate)
            {
                return _processor?.ProcessRawPreview(frame);
            }
        }
        catch (Exception ex)
        {
            _log("Anteprima grezza non disponibile", ex);
            return null;
        }
    }

    // ---- Camere ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<CameraCandidate>> DiscoverCamerasAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CameraCandidate> candidates = await _cameras.DiscoverAsync(cancellationToken);
            RememberKinds(candidates);
            return candidates;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log("Ricerca delle camere non riuscita", ex);
            throw new EngineException(
                $"Ricerca delle camere non riuscita: {ex.Message}. Controllare che il driver della camera sia installato e che il servizio sia in esecuzione.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<ConnectResult> ConnectCameraAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new ConnectResult(false, "Indicare la camera da collegare.");
        }

        lock (_gate)
        {
            if (!_started)
            {
                return new ConnectResult(false, "Il servizio non è avviato: avviarlo prima di collegare la camera.");
            }
        }

        await _control.WaitAsync(cancellationToken);
        try
        {
            return await ConnectInternalAsync(id, cancellationToken);
        }
        finally
        {
            _control.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectCameraAsync(CancellationToken cancellationToken)
    {
        await _control.WaitAsync(cancellationToken);
        try
        {
            await StopCaptureAsync();
            SetError(null);
        }
        finally
        {
            _control.Release();
        }

        RaiseStatus();
    }

    // ---- Configurazione ----

    /// <inheritdoc />
    public AppConfig GetConfig()
    {
        return ConfigSnapshot();
    }

    /// <inheritdoc />
    public ConfigUpdateResult UpdateConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        IReadOnlyList<ValidationError> errors = ValidateAll(config);
        if (errors.Count > 0)
        {
            return new ConfigUpdateResult(false, errors);
        }

        AppConfig previous;
        lock (_saveGate)
        {
            try
            {
                Store.Save(_configPath, config);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log("Salvataggio della configurazione non riuscito", ex);
                return new ConfigUpdateResult(
                    false,
                    [new ValidationError("file", $"Impossibile salvare la configurazione in '{_configPath}': {ex.Message}. Controllare i permessi della cartella e riprovare.")]);
            }

            lock (_gate)
            {
                previous = _config;
                _config = config;
                _configError = null;
                _tz = ResolveTimeZone(config.Site.Timezone) ?? TimeZoneInfo.Utc;
                _archive = CreateArchive(config, _tz);
            }

            // La sorgente meteo si ricrea solo se la sua sezione cambia: niente riavvio e niente driver riconnessi inutilmente.
            if (!Equals(previous.Weather, config.Weather))
            {
                ApplyWeatherConfig(config);
            }
        }

        lock (_processorGate)
        {
            _processor?.UpdateConfig(config);
        }

        TrackBackground(Task.Run(() => ApplyChangesAsync(previous, config)));
        RaiseStatus();
        return new ConfigUpdateResult(true, Array.Empty<ValidationError>());
    }

    // ---- Connessione e ciclo di cattura (sotto _control) ----

    private async Task<ConnectResult> ConnectInternalAsync(string id, CancellationToken ct)
    {
        await StopCaptureAsync();
        string kind = await ResolveKindAsync(id, ct);

        ICameraSource camera;
        try
        {
            camera = _cameras.Create(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"Creazione della camera '{id}' non riuscita", ex);
            return ConnectFailed($"La camera '{id}' non è disponibile: {ex.Message}. Controllare l'identificativo scelto e che il driver sia installato.");
        }

        try
        {
            await camera.ConnectAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(camera);
            throw;
        }
        catch (Exception ex)
        {
            await DisposeQuietlyAsync(camera);
            _log($"Connessione alla camera '{id}' non riuscita", ex);
            return ConnectFailed($"Connessione alla camera '{id}' non riuscita: {ex.Message}. Controllare che la camera sia accesa e collegata, poi riprovare.");
        }

        try
        {
            IFrameProcessor processor = _processorFactory(ConfigSnapshot());
            lock (_processorGate)
            {
                _processor = processor;
            }

            lock (_gate)
            {
                _camera = camera;
                _cameraId = id;
                _cameraKind = kind;
                _cameraConnected = true;
            }

            StartLoop(camera, keepExposure: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _camera = null;
                _cameraConnected = false;
            }

            await DisposeQuietlyAsync(camera);
            _log($"Avvio della cattura con la camera '{id}' non riuscito", ex);
            return ConnectFailed($"Avvio della cattura non riuscito: {ex.Message}. Controllare la configurazione della camera (guadagno, esposizione massima).");
        }

        SetError(null);
        PersistCameraId(id);
        string? warning;
        lock (_gate)
        {
            warning = _driverWarning;
        }

        RaiseStatus();
        return new ConnectResult(true, null, warning);
    }

    private ConnectResult ConnectFailed(string message)
    {
        SetError(message);
        RaiseStatus();
        return new ConnectResult(false, message);
    }

    /// <summary>Avvia un nuovo ciclo di cattura. Con <paramref name="keepExposure"/> riparte dall'esposizione corrente.</summary>
    private void StartLoop(ICameraSource camera, bool keepExposure)
    {
        AppConfig config = ConfigSnapshot();
        CameraInfo info = camera.Info;

        AutoExposure? previous;
        lock (_gate)
        {
            previous = _autoExposure;
        }

        ExposureSetting initial = keepExposure && previous is not null
            ? previous.Current
            : new ExposureSetting(config.Camera.DayExposureMs, config.Camera.Gain);

        // Limiti del driver sanificati: il log segnala solo i casi in cui il valore usato differisce da quello del driver.
        (double minExposureMs, double maxExposureMs) = ExposureLimits.Sanitize(
            info.MinExposureMs,
            info.MaxExposureMs,
            config.Camera.MaxExposureMs);
        bool driverLimitsCorrected =
            minExposureMs != info.MinExposureMs
            || !ExposureLimits.IsUsable(info.MaxExposureMs)
            || maxExposureMs != Math.Min(config.Camera.MaxExposureMs, info.MaxExposureMs);
        // Il testo resta nel log e arriva a ConnectResult.Warning / StatusDto.Warning (avviso, non errore): la vista Camera ne ricava i valori.
        string? driverWarning = driverLimitsCorrected
            ? $"Limiti di esposizione del driver non validi (min={Fmt(info.MinExposureMs)}, max={Fmt(info.MaxExposureMs)}): " +
              $"uso min={Fmt(minExposureMs)}, max={Fmt(maxExposureMs)}"
            : null;
        if (driverWarning is not null)
        {
            _log(driverWarning, null);
        }

        lock (_gate)
        {
            _driverWarning = driverWarning;
        }

        // Gain fisso da configurazione: il controller regola solo l'esposizione.
        var exposureOptions = new AutoExposureOptions(
            minExposureMs,
            maxExposureMs,
            config.Camera.Gain,
            config.Camera.Gain);
        var autoExposure = new AutoExposure(exposureOptions, info.MaxAdu, initial);

        var loop = new CaptureLoop(
            camera,
            _clock,
            OnFrameAsync,
            new TickingDelay(_delay, OnTick),
            autoExposure,
            CaptureLoopOptions.FromConfig(config),
            TransitPolicyFor(config));
        loop.OnError += OnLoopError;

        var cts = new CancellationTokenSource();
        Task task = loop.RunAsync(cts.Token);
        lock (_gate)
        {
            _autoExposure = autoExposure;
            _loop = loop;
            _loopCts = cts;
            _loopTask = task;
        }

        _ = task.ContinueWith(
            t => _log("Il ciclo di cattura è terminato in modo anomalo", t.Exception),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnLoopError(Exception ex)
    {
        _log("Errore di cattura", ex);
        SetError($"Errore di lettura dalla camera: {ex.Message}. Controllare il collegamento e l'alimentazione della camera, poi riconnetterla.");
        RaiseStatus();
    }

    private async Task StopCaptureAsync()
    {
        await StopLoopAsync();

        ICameraSource? camera;
        lock (_gate)
        {
            camera = _camera;
            _camera = null;
            _cameraConnected = false;
            _driverWarning = null;
        }

        if (camera is not null)
        {
            await DisposeQuietlyAsync(camera);
        }
    }

    private async Task StopLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            cts = _loopCts;
            task = _loopTask;
            _loopCts = null;
            _loopTask = null;
            _loop = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (task is not null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
            // Arresto richiesto: normale.
        }
        catch (Exception ex)
        {
            _log("Ciclo di cattura terminato con errore durante l'arresto", ex);
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>Riavvia il ciclo mantenendo la camera collegata e l'esposizione corrente.</summary>
    private async Task RestartLoopAsync()
    {
        await StopLoopAsync();

        ICameraSource? camera;
        lock (_gate)
        {
            camera = _camera;
        }

        if (camera is not null)
        {
            StartLoop(camera, keepExposure: true);
        }
    }

    /// <summary>
    /// Applica in background i cambi di configurazione che richiedono di toccare la camera o il ciclo.
    /// Gli errori non escono: diventano lo stato "error".
    /// </summary>
    private async Task ApplyChangesAsync(AppConfig previous, AppConfig config)
    {
        await _control.WaitAsync();
        try
        {
            bool started;
            lock (_gate)
            {
                started = _started;
            }

            if (!started)
            {
                return;
            }

            if (!string.Equals(previous.Camera.Id, config.Camera.Id, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(config.Camera.Id))
                {
                    await StopCaptureAsync();
                }
                else
                {
                    await ConnectInternalAsync(config.Camera.Id, CancellationToken.None);
                }

                return;
            }

            bool captureChanged = previous.Capture != config.Capture
                || previous.Site.Latitude != config.Site.Latitude
                || previous.Site.Longitude != config.Site.Longitude;
            bool cameraChanged = previous.Camera != config.Camera;
            if (captureChanged || cameraChanged)
            {
                await RestartLoopAsync();
            }
        }
        catch (Exception ex)
        {
            _log("Applicazione della nuova configurazione non riuscita", ex);
            SetError($"Applicazione della configurazione non riuscita: {ex.Message}. Riavviare il servizio o riconnettere la camera.");
            RaiseStatus();
        }
        finally
        {
            _control.Release();
        }
    }

    private async Task DisposeQuietlyAsync(ICameraSource camera)
    {
        try
        {
            await camera.DisposeAsync();
        }
        catch (Exception ex)
        {
            _log("Rilascio della camera non riuscito", ex);
        }
    }

    private async Task<string> ResolveKindAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_kindsById.TryGetValue(id, out string? known))
            {
                return known;
            }
        }

        try
        {
            IReadOnlyList<CameraCandidate> candidates = await _cameras.DiscoverAsync(ct);
            RememberKinds(candidates);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log("Ricerca del tipo di camera non riuscita", ex);
        }

        lock (_gate)
        {
            return _kindsById.TryGetValue(id, out string? kind) ? kind : "sconosciuto";
        }
    }

    private void RememberKinds(IReadOnlyList<CameraCandidate> candidates)
    {
        lock (_gate)
        {
            foreach (CameraCandidate candidate in candidates)
            {
                _kindsById[candidate.Id] = candidate.Kind;
            }
        }
    }

    /// <summary>Salva su disco l'identificativo della camera collegata, se diverso da quello in configurazione.</summary>
    private void PersistCameraId(string id)
    {
        lock (_saveGate)
        {
            AppConfig snapshot;
            lock (_gate)
            {
                if (string.Equals(_config.Camera.Id, id, StringComparison.Ordinal))
                {
                    return;
                }

                _config = _config with { Camera = _config.Camera with { Id = id } };
                snapshot = _config;
            }

            try
            {
                Store.Save(_configPath, snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log("Salvataggio della camera scelta non riuscito", ex);
            }
        }
    }

    // ---- Notti e archivio (lettura) ----

    /// <summary>Chiusura del servizio: attesa dei lavori in background e rilascio delle risorse.</summary>
    private async Task WaitForBackgroundAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            pending = _background.ToArray();
            _background.Clear();
        }

        try
        {
            await Task.WhenAll(pending);
        }
        catch (Exception ex)
        {
            _log("Un lavoro in background ha generato un errore", ex);
        }
    }

    private void TrackBackground(Task task)
    {
        lock (_gate)
        {
            _background.RemoveAll(t => t.IsCompleted);
            _background.Add(task);
        }
    }

    // ---- Utilita' di stato ----

    private static (double? Sun, double? Moon, string? Phase) Celestial(AppConfig config, DateTimeOffset now)
    {
        try
        {
            double sun = SolarPosition.Altitude(now, config.Site.Latitude, config.Site.Longitude);
            double moon = MoonPosition.Altitude(now, config.Site.Latitude, config.Site.Longitude);
            return (sun, moon, SkyPhases.Simplify(SkyPhases.Classify(sun)));
        }
        catch (ArgumentException)
        {
            return (null, null, null);
        }
    }

    private DiskDto? Disk(NightArchive archive, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_diskCache is not null && now - _diskCacheAt < DiskUsageCacheTtl)
            {
                return _diskCache;
            }
        }

        try
        {
            DiskUsage usage = archive.GetDiskUsage();
            var dto = new DiskDto(
                usage.FreeBytes is { } free ? (double?)(free / BytesPerGb) : null,
                usage.UsedBytes / BytesPerGb);
            lock (_gate)
            {
                _diskCache = dto;
                _diskCacheAt = now;
            }

            return dto;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Lettura dell'uso del disco non riuscita", ex);
            lock (_gate)
            {
                return _diskCache;
            }
        }
    }

    private void SetError(string? message)
    {
        lock (_gate)
        {
            _error = message;
        }
    }

    private AppConfig ConfigSnapshot()
    {
        lock (_gate)
        {
            return _config;
        }
    }

    private TimeZoneInfo TimeZoneOf()
    {
        lock (_gate)
        {
            return _tz;
        }
    }

    private NightArchive ArchiveOf()
    {
        lock (_gate)
        {
            return _archive;
        }
    }

    private IReadOnlyList<JobDto> SnapshotJobsLocked()
    {
        return _jobs.Select(j => new JobDto("timelapse", j.NightId, j.Progress)).ToList();
    }

    /// <summary>Emette lo stato corrente. Chiamare sempre fuori da <c>_gate</c>.</summary>
    private void RaiseStatus()
    {
        if (StatusChanged is null)
        {
            return;
        }

        Notify(StatusChanged, GetStatus());
    }

    private void Notify<T>(Action<T>? handler, T value)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<T>)subscriber)(value);
            }
            catch (Exception ex)
            {
                _log("Un ascoltatore ha generato un errore", ex);
            }
        }
    }

    /// <summary>Formato invariante di un valore in millisecondi, per i messaggi di log.</summary>
    private static string Fmt(double valueMs) => valueMs.ToString("R", CultureInfo.InvariantCulture);

    private static IReadOnlyList<ValidationError> ValidateAll(AppConfig config)
    {
        var errors = new List<ValidationError>(Store.Validate(config));

        if (ResolveTimeZone(config.Site.Timezone) is null)
        {
            errors.Add(new ValidationError(
                "site.timezone",
                $"Fuso orario non riconosciuto: '{config.Site.Timezone}'. Usare un nome IANA, per esempio Europe/Rome."));
        }

        if (string.IsNullOrWhiteSpace(config.Storage.Folder))
        {
            errors.Add(new ValidationError("storage.folder", "La cartella dell'archivio non può essere vuota."));
        }

        if (config.Products.TimelapseFps < 1 || config.Products.TimelapseFps > 120)
        {
            errors.Add(new ValidationError("products.timelapseFps", "Il timelapse deve avere da 1 a 120 fotogrammi al secondo."));
        }

        if (config.Color.ManualGains.Length != 3 || config.Color.ManualGains.Any(g => !(g > 0.0 && double.IsFinite(g))))
        {
            errors.Add(new ValidationError("color.manualGains", "Servono tre guadagni positivi (R, G, B)."));
        }

        if (config.Camera.Gain < 0)
        {
            errors.Add(new ValidationError("camera.gain", "Il guadagno non può essere negativo."));
        }

        if (!(config.Camera.MaxExposureMs > 0.0 && double.IsFinite(config.Camera.MaxExposureMs)))
        {
            errors.Add(new ValidationError("camera.maxExposureMs", "L'esposizione massima deve essere maggiore di zero."));
        }

        if (!(config.Camera.DayExposureMs > 0.0 && double.IsFinite(config.Camera.DayExposureMs)))
        {
            errors.Add(new ValidationError("camera.dayExposureMs", "L'esposizione iniziale deve essere maggiore di zero."));
        }

        return errors;
    }

    private static string ConfigErrorText(IEnumerable<ValidationError> errors)
    {
        string details = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
        return $"Configurazione non valida ({details}). Correggere le impostazioni nella pagina Impostazioni e salvarle.";
    }

    private static TimeZoneInfo? ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private NightArchive CreateArchive(AppConfig config, TimeZoneInfo tz)
    {
        string folder = string.IsNullOrWhiteSpace(config.Storage.Folder)
            ? new AppConfig.StorageConfig().Folder
            : config.Storage.Folder;
        return new NightArchive(folder, _clock, tz);
    }
}
