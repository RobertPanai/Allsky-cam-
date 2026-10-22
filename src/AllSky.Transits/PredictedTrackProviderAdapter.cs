using AllSky.Detection;
using AllSky.Satellites;
using AllSky.Sky.Calibration;

namespace AllSky.Transits;

/// <summary>Satellite da includere nelle tracce previste, con il suo propagatore.</summary>
/// <param name="Name">Nome leggibile, usato solo per la visualizzazione.</param>
/// <param name="Propagator">Propagatore degli elementi orbitali del satellite.</param>
public sealed record TrackedSatellite(string Name, IOrbitPropagator Propagator);

/// <summary>Opzioni dell'adattatore delle tracce previste.</summary>
public sealed record PredictedTrackOptions
{
    /// <summary>Opzioni della ricerca dei passaggi. Null per i valori predefiniti di <see cref="PassOptions"/>.</summary>
    public PassOptions? Pass { get; init; }

    /// <summary>Opzioni della proiezione nei pixel.</summary>
    public TrackProjectionOptions Projection { get; init; } = new();

    /// <summary>Ampiezza della finestra in cui cercare i passaggi. Default 3 h. Valore iniziale.</summary>
    public TimeSpan LookupWindow { get; init; } = TimeSpan.FromHours(3);

    /// <summary>
    /// Margine ai bordi della finestra: i passaggi si cercano solo per frame lontani almeno di questo margine dai bordi,
    /// così che un passaggio che attraversa il bordo sia già completo. Default 30 min, più della durata di un passaggio LEO.
    /// </summary>
    public TimeSpan Margin { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Fornisce al <see cref="AllSky.Detection.StreakClassifier"/> le tracce previste in pixel, a partire dai passaggi.
/// </summary>
/// <remarks>
/// Ogni passaggio viene proiettato una volta sola e il risultato resta in cache finché il frame non esce dalla finestra.
/// Senza calibrazione valida non esistono tracce in pixel e <see cref="GetTracks"/> restituisce una lista vuota.
/// Ogni tratto visibile di un passaggio diventa un segmento dal primo all'ultimo punto (corda).
/// </remarks>
public sealed class PredictedTrackProviderAdapter : IPredictedTrackProvider
{
    private readonly IReadOnlyList<TrackedSatellite> _satellites;
    private readonly ObserverSite _site;
    private readonly SkyCalibrationResult? _calibration;
    private readonly PredictedTrackOptions _options;
    private CachedPasses? _cache;

    /// <summary>Crea l'adattatore.</summary>
    /// <param name="satellites">Satelliti da seguire.</param>
    /// <param name="site">Osservatore.</param>
    /// <param name="calibration">Calibrazione del cielo; senza calibrazione valida non vengono prodotte tracce.</param>
    /// <param name="options">Opzioni; se null, valori predefiniti.</param>
    public PredictedTrackProviderAdapter(
        IReadOnlyList<TrackedSatellite> satellites,
        ObserverSite site,
        SkyCalibrationResult? calibration,
        PredictedTrackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(satellites);
        ArgumentNullException.ThrowIfNull(site);
        _satellites = satellites;
        _site = site;
        _calibration = calibration;
        _options = options ?? new PredictedTrackOptions();
        if (_options.LookupWindow <= _options.Margin * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.LookupWindow, "La finestra di ricerca deve superare il doppio del margine.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc)
    {
        if (_calibration is null || !_calibration.Accepted || _satellites.Count == 0)
        {
            return Array.Empty<PredictedTrack>();
        }

        CachedPasses cache = EnsureCache(utc);
        var tracks = new List<PredictedTrack>();
        foreach ((string name, ProjectedPassTrack projected) in cache.Passes)
        {
            if (utc < projected.Pass.Start.Utc || utc > projected.Pass.End.Utc)
            {
                continue;
            }

            foreach (ProjectedTrackRun run in projected.Runs)
            {
                if (run.Points.Count == 0)
                {
                    continue;
                }

                TrackPixel first = run.Points[0];
                TrackPixel last = run.Points[^1];
                tracks.Add(new PredictedTrack(name, first.X, first.Y, last.X, last.Y));
            }
        }

        return tracks;
    }

    private CachedPasses EnsureCache(DateTimeOffset utc)
    {
        TimeSpan margin = _options.Margin;
        if (_cache is { } c && utc >= c.From + margin && utc + margin < c.To)
        {
            return c;
        }

        DateTimeOffset from = utc - margin;
        var passes = new List<(string Name, ProjectedPassTrack Track)>();
        foreach (TrackedSatellite satellite in _satellites)
        {
            IReadOnlyList<SatellitePass> found = PassFinder.Find(
                satellite.Propagator,
                _site.LatDeg,
                _site.LonDeg,
                _site.AltM,
                from,
                _options.LookupWindow,
                _options.Pass);

            foreach (SatellitePass pass in found)
            {
                ProjectedPassTrack projected = PassTrackProjector.Project(pass, satellite.Propagator, _site, _calibration, _options.Projection);
                passes.Add((satellite.Name, projected));
            }
        }

        _cache = new CachedPasses(from, from + _options.LookupWindow, passes);
        return _cache;
    }

    /// <summary>Passaggi proiettati nell'intervallo [From, To), con il nome del satellite di ciascuno.</summary>
    private sealed class CachedPasses
    {
        public CachedPasses(DateTimeOffset from, DateTimeOffset to, List<(string Name, ProjectedPassTrack Track)> passes)
        {
            From = from;
            To = to;
            Passes = passes;
        }

        public DateTimeOffset From { get; }

        public DateTimeOffset To { get; }

        public IReadOnlyList<(string Name, ProjectedPassTrack Track)> Passes { get; }
    }
}
