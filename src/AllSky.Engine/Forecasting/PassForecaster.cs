using System.Globalization;
using AllSky.Core.Passes;
using AllSky.Satellites;
using AllSky.Sky.Calibration;
using AllSky.Transits;

namespace AllSky.Engine.Forecasting;

/// <summary>
/// Impostazioni della previsione, già tradotte dalla configurazione. Senza dipendenze da <c>AppConfig</c>, così la logica si testa da sola.
/// </summary>
/// <param name="Enabled">Interruttore: falso significa nessuna richiesta e nessun calcolo.</param>
/// <param name="Targets">Bersagli da seguire.</param>
/// <param name="Site">Osservatore.</param>
/// <param name="Window">Finestra della previsione, positiva.</param>
/// <param name="Options">Soglie di elevazione e di buio.</param>
public sealed record PassForecastSettings(
    bool Enabled,
    IReadOnlyList<SatelliteTarget> Targets,
    ObserverSite Site,
    TimeSpan Window,
    PassOptions Options)
{
    /// <summary>Chiave di tutta la configurazione: se cambia, la previsione va ricalcolata.</summary>
    public string ComputeKey => FormattableString.Invariant(
        $"{Enabled}|{TargetKey}|{Site.LatDeg:R}|{Site.LonDeg:R}|{Site.AltM:R}|{Window:c}|{Options.MinElevationDeg:R}|{Options.SunMaxAltDeg:R}");

    /// <summary>Chiave dei soli bersagli: se cambia, serve un TLE anche prima dello scadere dei 2 ore.</summary>
    public string TargetKey => string.Join(",", Targets.Select(t => t.NoradId.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// Previsione dei passaggi: download dei TLE con la sorgente (che applica la policy CelesTrak), calcolo dei passaggi,
/// elenco per la UI, eventi di inizio e fine, tracce in pixel.
/// </summary>
/// <remarks>
/// <para>
/// Regole: un nuovo TLE non viene chiesto prima di <see cref="MinTleInterval"/> (2 ore), anche se il download fallisce:
/// l'istante della richiesta conta comunque. Un cambio dei bersagli fa richiedere subito i TLE mancanti (la sorgente
/// scarica solo quelli non in cache). La previsione viene ricalcolata ogni <see cref="RecomputeInterval"/> o quando
/// cambia la configurazione.
/// </para>
/// <para>
/// Con l'interruttore spento non si chiede nulla alla sorgente e non si calcola nulla. Un errore della sorgente o di un
/// propagatore diventa uno stato e un messaggio, mai un'eccezione verso l'Engine.
/// </para>
/// <para>
/// Thread: <see cref="RefreshAsync"/> è serializzato da un semaforo. I propagatori SGP4 non sono thread-safe, quindi
/// il calcolo e le proiezioni passano dallo stesso lock. Le letture della UI (<see cref="Snapshot"/>) non aspettano il calcolo.
/// </para>
/// </remarks>
public sealed class PassForecaster
{
    /// <summary>Intervallo minimo tra due richieste di TLE. Coincide con la frequenza di aggiornamento dei GP di CelesTrak.</summary>
    public static readonly TimeSpan MinTleInterval = TimeSpan.FromHours(2);

    /// <summary>Intervallo di ricalcolo della previsione con dati invariati.</summary>
    public static readonly TimeSpan RecomputeInterval = TimeSpan.FromMinutes(30);

    /// <summary>Anticipo con cui una traccia compare nell'immagine Live prima dell'inizio del passaggio.</summary>
    public static readonly TimeSpan TrackLead = TimeSpan.FromMinutes(15);

    private readonly ITleFetcher _fetcher;
    private readonly IOrbitPropagatorFactory _factory;

    // Serializza RefreshAsync: un solo download o ricalcolo alla volta. Lo stato TLE è di questo ruolo.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Serializza il lavoro sui propagatori (calcolo e proiezione delle tracce).
    private readonly object _computeGate = new();

    // Stato pubblicato alla UI. Brevissimo: non si attende mai il calcolo dentro questo lock.
    private readonly object _gate = new();
    private PassForecastStatus _status = PassForecastStatus.Disabled; // _gate
    private string? _message;                                          // _gate
    private DateTimeOffset? _computedUtc;                              // _gate
    private IReadOnlyList<Entry> _entries = Array.Empty<Entry>();      // _gate
    private HashSet<PassKey> _inProgress = new();                       // _gate
    private bool _updatePending;                                       // _gate

    // Stato di refresh, protetto da _refreshGate.
    private DateTimeOffset? _lastTleRequestUtc;
    private string? _fetchedTargetKey;
    private string? _computedKey;
    private Dictionary<int, TleSet> _tles = new();
    private TleFetchStatus _tleStatus = TleFetchStatus.FromCache;
    private string? _tleMessage;

    /// <summary>Crea il servizio con la sorgente dei TLE e la fabbrica dei propagatori.</summary>
    /// <param name="fetcher">Sorgente dei TLE (in produzione CelesTrak, con cache e blocco).</param>
    /// <param name="factory">Crea i propagatori dai TLE.</param>
    public PassForecaster(ITleFetcher fetcher, IOrbitPropagatorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(factory);
        _fetcher = fetcher;
        _factory = factory;
    }

    /// <summary>Un passaggio in una forma comoda per i confronti: numero NORAD e istante di inizio.</summary>
    private readonly record struct PassKey(int NoradId, DateTimeOffset StartUtc);

    /// <summary>Un passaggio calcolato con il suo propagatore, per le tracce.</summary>
    private sealed record Entry(PassForecastItem Item, SatellitePass Pass, IOrbitPropagator Propagator);

    /// <summary>
    /// Scarica i TLE se dovuto e ricalcola la previsione se dovuto. Non lancia per errori della sorgente.
    /// </summary>
    /// <returns>Vero se la previsione è stata ricalcolata in questa chiamata.</returns>
    public async Task<bool> RefreshAsync(PassForecastSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(settings, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> RefreshCoreAsync(PassForecastSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            // Spento: nessuna chiamata alla sorgente. Si svuota la previsione pubblicata.
            lock (_gate)
            {
                _status = PassForecastStatus.Disabled;
                _message = null;
                _computedUtc = null;
                _entries = Array.Empty<Entry>();
            }

            _computedKey = null;
            return false;
        }

        string targetKey = settings.TargetKey;
        bool targetsChanged = !string.Equals(targetKey, _fetchedTargetKey, StringComparison.Ordinal);
        bool tleDue = targetsChanged || _lastTleRequestUtc is not { } lastRequest || now - lastRequest >= MinTleInterval;

        bool recompute = tleDue
            || _computedKey != settings.ComputeKey
            || _computedUtc is not { } computedAt
            || now - computedAt >= RecomputeInterval;

        if (tleDue)
        {
            // L'istante della richiesta si registra prima di chiederla: anche un errore vale come richiesta.
            _lastTleRequestUtc = now;
            _fetchedTargetKey = targetKey;

            TleFetchResult result;
            try
            {
                result = await _fetcher.FetchAsync(settings.Targets, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // La sorgente non deve bloccare il servizio: si conservano i TLE noti e si segnala il problema.
                // L'errore entra nello stato TLE, così i ricalcoli successivi lo mantengono.
                _tleStatus = TleFetchStatus.Failed;
                _tleMessage = $"errore nel recupero dei dati orbitali: {ex.Message}";
                Compute(settings, now);
                return true;
            }

            _tles = result.Sets
                .GroupBy(s => s.NoradId)
                .ToDictionary(g => g.Key, g => g.First());
            _tleStatus = result.Status;
            _tleMessage = result.Message;
            recompute = true;
        }

        if (!recompute)
        {
            return false;
        }

        Compute(settings, now);
        return true;
    }

    /// <summary>Calcola i passaggi per tutti i bersagli con un TLE. Non lancia: gli errori diventano messaggi.</summary>
    private void Compute(PassForecastSettings settings, DateTimeOffset now)
    {
        var entries = new List<Entry>();
        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(_tleMessage))
        {
            messages.Add(_tleMessage!);
        }

        int withTle = 0;
        bool calculationFailed = false;

        lock (_computeGate)
        {
            foreach (SatelliteTarget target in settings.Targets)
            {
                if (!_tles.TryGetValue(target.NoradId, out TleSet? tle))
                {
                    messages.Add($"nessun TLE per il NORAD {target.NoradId}.");
                    continue;
                }

                withTle++;
                try
                {
                    IOrbitPropagator propagator = _factory.Create(tle);
                    IReadOnlyList<SatellitePass> passes = PassFinder.Find(
                        propagator,
                        settings.Site.LatDeg,
                        settings.Site.LonDeg,
                        settings.Site.AltM,
                        now,
                        settings.Window,
                        settings.Options);

                    foreach (SatellitePass pass in passes)
                    {
                        entries.Add(new Entry(ToItem(target, pass), pass, propagator));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    calculationFailed = true;
                    messages.Add($"calcolo non riuscito per il NORAD {target.NoradId}: {ex.Message}");
                }
            }
        }

        entries.Sort((a, b) => a.Item.StartUtc.CompareTo(b.Item.StartUtc));

        PassForecastStatus status;
        if (_tleStatus == TleFetchStatus.Blocked)
        {
            status = PassForecastStatus.Blocked;
        }
        else if (_tleStatus == TleFetchStatus.Failed || calculationFailed)
        {
            status = PassForecastStatus.Failed;
        }
        else if (settings.Targets.Count == 0)
        {
            status = PassForecastStatus.Waiting;
            messages.Add("nessun satellite configurato.");
        }
        else if (withTle == 0)
        {
            status = PassForecastStatus.Waiting;
        }
        else
        {
            status = PassForecastStatus.Ready;
        }

        string? message = messages.Count == 0 ? null : string.Join(" ", messages);
        lock (_gate)
        {
            _status = status;
            _message = message;
            _computedUtc = now;
            _entries = entries;
            _updatePending = true;
        }

        _computedKey = settings.ComputeKey;
    }

    /// <summary>
    /// Previsione riferita a <paramref name="now"/>: passaggi ancora da finire, quello in corso e il prossimo.
    /// </summary>
    public PassForecastSnapshot Snapshot(DateTimeOffset now)
    {
        PassForecastStatus status;
        string? message;
        DateTimeOffset? computedUtc;
        IReadOnlyList<Entry> entries;
        lock (_gate)
        {
            status = _status;
            message = _message;
            computedUtc = _computedUtc;
            entries = _entries;
        }

        List<PassForecastItem> upcoming = entries
            .Select(e => e.Item)
            .Where(p => p.EndUtc > now)
            .ToList();
        PassForecastItem? current = upcoming.FirstOrDefault(p => p.StartUtc <= now);
        PassForecastItem? next = upcoming.FirstOrDefault(p => p.StartUtc > now);
        return new PassForecastSnapshot(status, message, computedUtc, upcoming, current, next);
    }

    /// <summary>
    /// Cambiamenti dall'ultima chiamata: passaggi iniziati, finiti e ricalcolo della previsione. Chiamare a intervalli
    /// regolari (ogni pochi secondi), con l'istante corrente.
    /// </summary>
    public IReadOnlyList<PassChange> Evaluate(DateTimeOffset now)
    {
        var changes = new List<PassChange>();
        lock (_gate)
        {
            if (_updatePending)
            {
                _updatePending = false;
                changes.Add(new PassChange(PassChangeKind.ForecastUpdated, null, now));
            }

            var nowKeys = new HashSet<PassKey>();
            foreach (Entry e in _entries)
            {
                if (e.Item.StartUtc <= now && now < e.Item.EndUtc)
                {
                    nowKeys.Add(new PassKey(e.Item.NoradId, e.Item.StartUtc));
                }
            }

            foreach (Entry e in _entries)
            {
                var key = new PassKey(e.Item.NoradId, e.Item.StartUtc);
                if (nowKeys.Contains(key) && !_inProgress.Contains(key))
                {
                    changes.Add(new PassChange(PassChangeKind.Started, e.Item, now));
                }
            }

            foreach (PassKey key in _inProgress)
            {
                if (!nowKeys.Contains(key))
                {
                    PassForecastItem? item = _entries
                        .Select(e => e.Item)
                        .FirstOrDefault(p => p.NoradId == key.NoradId && p.StartUtc == key.StartUtc);
                    changes.Add(new PassChange(PassChangeKind.Ended, item, now));
                }
            }

            _inProgress = nowKeys;
        }

        return changes;
    }

    /// <summary>
    /// Tracce in pixel dei passaggi in corso o che iniziano entro <paramref name="lead"/>, proiettate con la calibrazione.
    /// </summary>
    public IReadOnlyList<PassTrackPath> Tracks(
        PassForecastSettings settings,
        SkyCalibrationResult calibration,
        DateTimeOffset now,
        TimeSpan lead)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(calibration);

        IReadOnlyList<Entry> entries;
        lock (_gate)
        {
            entries = _entries;
        }

        var paths = new List<PassTrackPath>();
        lock (_computeGate)
        {
            foreach (Entry e in entries)
            {
                if (now < e.Item.StartUtc - lead || now >= e.Item.EndUtc)
                {
                    continue;
                }

                ProjectedPassTrack projected = PassTrackProjector.Project(e.Pass, e.Propagator, settings.Site, calibration);
                if (projected.Runs.Count == 0)
                {
                    continue;
                }

                List<IReadOnlyList<PassTrackPoint>> runs = projected.Runs
                    .Select(r => (IReadOnlyList<PassTrackPoint>)r.Points
                        .Select(p => new PassTrackPoint(p.Utc, p.X, p.Y))
                        .ToList())
                    .ToList();
                paths.Add(new PassTrackPath(e.Item.Target, e.Item.StartUtc, e.Item.EndUtc, runs));
            }
        }

        return paths;
    }

    /// <summary>Passaggio calcolato con il suo propagatore, per il rilevamento dei transiti (P5.5). Usare solo tramite <see cref="ProjectWatched"/>.</summary>
    internal sealed record WatchedPass(PassForecastItem Item, SatellitePass Pass, IOrbitPropagator Propagator);

    /// <summary>Passaggi calcolati che finiscono dopo <paramref name="from"/> e iniziano prima di <paramref name="until"/>.</summary>
    internal IReadOnlyList<WatchedPass> WatchedPasses(DateTimeOffset from, DateTimeOffset until)
    {
        IReadOnlyList<Entry> entries;
        lock (_gate)
        {
            entries = _entries;
        }

        return entries
            .Where(e => e.Item.EndUtc > from && e.Item.StartUtc < until)
            .Select(e => new WatchedPass(e.Item, e.Pass, e.Propagator))
            .ToList();
    }

    /// <summary>Proietta un passaggio nei pixel con la calibrazione. Il propagatore è condiviso: il calcolo passa da <c>_computeGate</c>.</summary>
    internal ProjectedPassTrack ProjectWatched(WatchedPass watched, SkyCalibrationResult calibration, ObserverSite site)
    {
        lock (_computeGate)
        {
            return PassTrackProjector.Project(watched.Pass, watched.Propagator, site, calibration);
        }
    }

    /// <summary>
    /// Propagatori nuovi, uno per bersaglio con TLE, per il classificatore di scie. Sono istanze separate da quelle della
    /// previsione: il classificatore lavora su un altro thread.
    /// </summary>
    internal IReadOnlyList<TrackedSatellite> TrackedSatellites(PassForecastSettings settings)
    {
        var satellites = new List<TrackedSatellite>();
        lock (_computeGate)
        {
            foreach (SatelliteTarget target in settings.Targets)
            {
                if (!_tles.TryGetValue(target.NoradId, out TleSet? tle))
                {
                    continue;
                }

                try
                {
                    satellites.Add(new TrackedSatellite(target.Name, _factory.Create(tle)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un TLE illeggibile non deve fermare il rilevamento degli altri bersagli.
                }
            }
        }

        return satellites;
    }

    private static PassForecastItem ToItem(SatelliteTarget target, SatellitePass pass) => new(
        target.Name,
        target.NoradId,
        pass.Start.Utc,
        pass.Culmination.Utc,
        pass.End.Utc,
        pass.MaxElevationDeg,
        pass.Start.AzimuthDeg,
        pass.End.AzimuthDeg,
        pass.IlluminatedFraction,
        pass.IsVisibleToEye,
        pass.StartTruncated,
        pass.EndTruncated);
}
