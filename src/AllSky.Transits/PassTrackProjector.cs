using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Transits;

/// <summary>Posizione dell'osservatore, necessaria per calcolare alt/az del satellite.</summary>
/// <param name="LatDeg">Latitudine in gradi, nord positivo.</param>
/// <param name="LonDeg">Longitudine in gradi, est positivo.</param>
/// <param name="AltM">Altitudine sul livello del mare, in metri.</param>
public sealed record ObserverSite(double LatDeg, double LonDeg, double AltM);

/// <summary>Opzioni della proiezione di un passaggio.</summary>
public sealed record TrackProjectionOptions
{
    /// <summary>
    /// Passo di campionamento del passaggio nel tempo. Default 2 s: la traccia in pixel è una
    /// poligonale tra questi campioni, quindi un passo più fine cambia poco. Valore iniziale.
    /// </summary>
    public TimeSpan SampleStep { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Punto della traccia prevista nel frame, con l'istante a cui corrisponde.</summary>
/// <param name="Utc">Istante in UTC.</param>
/// <param name="X">Coordinata x in pixel, nelle stesse unità del modello di calibrazione.</param>
/// <param name="Y">Coordinata y in pixel.</param>
public sealed record TrackPixel(DateTimeOffset Utc, double X, double Y);

/// <summary>Tratto continuo della traccia visibile: sopra l'orizzonte e dentro il cerchio del cielo.</summary>
/// <param name="Points">Punti in ordine temporale.</param>
public sealed record ProjectedTrackRun(IReadOnlyList<TrackPixel> Points);

/// <summary>Traccia di un passaggio proiettata nei pixel, già tagliata sull'orizzonte.</summary>
/// <param name="Pass">Passaggio di origine.</param>
/// <param name="Calibrated">True se la calibrazione era valida. Se false, <see cref="Runs"/> è vuoto.</param>
/// <param name="Runs">Tratti visibili, in ordine temporale. Possono essere più di uno se il satellite esce e rientra dal cielo.</param>
public sealed record ProjectedPassTrack(SatellitePass Pass, bool Calibrated, IReadOnlyList<ProjectedTrackRun> Runs)
{
    /// <summary>Lunghezza complessiva dei tratti visibili, in pixel (somma delle poligonali).</summary>
    public double LengthPx
    {
        get
        {
            double total = 0.0;
            foreach (ProjectedTrackRun run in Runs)
            {
                for (int i = 1; i < run.Points.Count; i++)
                {
                    total += Distance(run.Points[i - 1], run.Points[i]);
                }
            }

            return total;
        }
    }

    private static double Distance(TrackPixel a, TrackPixel b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Proietta un passaggio di un satellite nei pixel dell'immagine, con il modello fisheye calibrato.
/// </summary>
/// <remarks>
/// <see cref="SatellitePass"/> contiene solo inizio, culminazione e fine: per la traccia servono campioni
/// intermedi, quindi il propagatore viene interrogato di nuovo sull'intervallo del passaggio. I campioni sotto
/// l'orizzonte (elevazione &lt; 0) o fuori dal cerchio del cielo vengono scartati e dividono la traccia in tratti.
/// Con calibrazione assente o non accettata il risultato è vuoto e marcato come non calibrato.
/// </remarks>
public static class PassTrackProjector
{
    /// <summary>Proietta il passaggio nei pixel.</summary>
    /// <param name="pass">Passaggio da proiettare.</param>
    /// <param name="propagator">Propagatore del satellite, interrogato per i campioni intermedi.</param>
    /// <param name="site">Osservatore.</param>
    /// <param name="calibration">Calibrazione del cielo. Valida solo se <see cref="SkyCalibrationResult.Accepted"/> è true.</param>
    /// <param name="options">Opzioni; se null, valori predefiniti.</param>
    public static ProjectedPassTrack Project(
        SatellitePass pass,
        IOrbitPropagator propagator,
        ObserverSite site,
        SkyCalibrationResult? calibration,
        TrackProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(propagator);
        ArgumentNullException.ThrowIfNull(site);
        options ??= new TrackProjectionOptions();
        if (options.SampleStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.SampleStep, "Il passo di campionamento deve essere positivo.");
        }

        if (calibration is null || !calibration.Accepted)
        {
            return new ProjectedPassTrack(pass, false, Array.Empty<ProjectedTrackRun>());
        }

        FisheyeModel model = calibration.Model;
        var runs = new List<ProjectedTrackRun>();
        var current = new List<TrackPixel>();

        foreach (DateTimeOffset utc in SampleTimes(pass.Start.Utc, pass.End.Utc, options.SampleStep))
        {
            SatObservation obs = propagator.Observe(utc, site.LatDeg, site.LonDeg, site.AltM);
            if (obs.ElevationDeg >= 0.0
                && model.TryAltAzToPixel(obs.ElevationDeg, obs.AzimuthDeg, out double x, out double y)
                && model.IsInsideSky(x, y))
            {
                current.Add(new TrackPixel(utc, x, y));
            }
            else if (current.Count > 0)
            {
                runs.Add(new ProjectedTrackRun(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            runs.Add(new ProjectedTrackRun(current.ToArray()));
        }

        return new ProjectedPassTrack(pass, true, runs);
    }

    /// <summary>Istanti da <paramref name="from"/> a <paramref name="to"/> con passo fisso, estremo finale incluso.</summary>
    private static IEnumerable<DateTimeOffset> SampleTimes(DateTimeOffset from, DateTimeOffset to, TimeSpan step)
    {
        if (to <= from)
        {
            yield return from;
            yield break;
        }

        DateTimeOffset t = from;
        while (t < to)
        {
            yield return t;
            t += step;
        }

        yield return to;
    }
}
