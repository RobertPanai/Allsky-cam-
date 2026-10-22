using AllSky.Astro;

namespace AllSky.Core.Tonight;

/// <summary>Campione dell'altezza del Sole e della Luna su un intervallo della notte.</summary>
/// <param name="Utc">Istante del campione.</param>
/// <param name="SunAltitudeDeg">Altezza geometrica del Sole in gradi (fonte: <see cref="SolarPosition"/>).</param>
/// <param name="MoonAltitudeDeg">Altezza della Luna in gradi, con parallasse (fonte: <see cref="MoonPosition"/>).</param>
public sealed record TonightSample(DateTimeOffset Utc, double SunAltitudeDeg, double MoonAltitudeDeg);

/// <summary>Fascia continua della stessa fase dentro la finestra tramonto-alba.</summary>
/// <param name="Phase">Fase del cielo.</param>
/// <param name="From">Inizio, UTC.</param>
/// <param name="To">Fine, UTC.</param>
public sealed record TonightSegment(SkyPhase Phase, DateTimeOffset From, DateTimeOffset To);

/// <summary>Cambio di fase del cielo: da questo istante vale <paramref name="To"/>.</summary>
/// <param name="To">Nuova fase.</param>
/// <param name="AtUtc">Istante del cambio, UTC.</param>
public sealed record TonightPhaseChange(SkyPhase To, DateTimeOffset AtUtc);

/// <summary>
/// Linea temporale dalla fine del giorno (tramonto) all'inizio del giorno successivo (alba) della notte osservativa.
/// Logica pura: riceve le fasi già calcolate da <see cref="ObservingNight.GetBands(string, double, double, TimeZoneInfo)"/>
/// e i campioni di altezza, e ricava finestra, fasce, cambi di fase e posizione dell'istante corrente.
/// </summary>
/// <remarks>
/// Il tramonto è il primo passaggio da Giorno a una fase non diurna; l'alba è l'ultimo passaggio da una fase non diurna a
/// Giorno. Se la notte non ha né l'uno né l'altro (Sole sempre sopra o sempre sotto l'orizzonte), la linea non è disponibile.
/// Gli orari sono geometrici: il tramonto e l'alba sono i passaggi del centro del Sole per 0°, senza rifrazione.
/// </remarks>
public sealed record TonightTimeline(
    bool Available,
    DateTimeOffset? Sunset,
    DateTimeOffset? Sunrise,
    IReadOnlyList<TonightSegment> Segments,
    IReadOnlyList<TonightPhaseChange> Changes,
    IReadOnlyList<TonightSample> Samples)
{
    /// <summary>Linea non disponibile: nessun tramonto o nessuna alba in questa notte.</summary>
    public static TonightTimeline Unavailable { get; } = new(
        false, null, null, Array.Empty<TonightSegment>(), Array.Empty<TonightPhaseChange>(), Array.Empty<TonightSample>());

    /// <summary>
    /// Costruisce la linea dalle fasi della notte. I campioni non influiscono sulla finestra e possono essere aggiunti
    /// dopo con <see cref="WithSamples"/>.
    /// </summary>
    /// <param name="bands">Fasi della notte, in ordine, senza buchi, come le restituisce <see cref="ObservingNight"/>.</param>
    public static TonightTimeline Build(IReadOnlyList<PhaseBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        int sunsetIndex = -1;
        for (int i = 0; i + 1 < bands.Count; i++)
        {
            if (bands[i].Phase == SkyPhase.Day && bands[i + 1].Phase != SkyPhase.Day)
            {
                sunsetIndex = i;
                break;
            }
        }

        int sunriseIndex = -1;
        for (int j = bands.Count - 1; j > 0; j--)
        {
            if (bands[j].Phase == SkyPhase.Day && bands[j - 1].Phase != SkyPhase.Day)
            {
                sunriseIndex = j;
                break;
            }
        }

        if (sunsetIndex < 0 || sunriseIndex <= sunsetIndex + 1)
        {
            return Unavailable;
        }

        DateTimeOffset sunset = bands[sunsetIndex].To;
        DateTimeOffset sunrise = bands[sunriseIndex].From;

        var segments = new List<TonightSegment>();
        for (int k = sunsetIndex + 1; k < sunriseIndex; k++)
        {
            segments.Add(new TonightSegment(bands[k].Phase, bands[k].From, bands[k].To));
        }

        var changes = new List<TonightPhaseChange> { new(segments[0].Phase, sunset) };
        for (int k = 1; k < segments.Count; k++)
        {
            changes.Add(new TonightPhaseChange(segments[k].Phase, segments[k].From));
        }
        changes.Add(new TonightPhaseChange(SkyPhase.Day, sunrise));

        return new TonightTimeline(
            true, sunset, sunrise, segments, changes, Array.Empty<TonightSample>());
    }

    /// <summary>Stessa linea con i campioni di altezza indicati.</summary>
    public TonightTimeline WithSamples(IReadOnlyList<TonightSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return this with { Samples = samples };
    }

    /// <summary>
    /// Posizione di un istante nella finestra tramonto-alba, da 0 (tramonto) a 1 (alba).
    /// Null se la linea non è disponibile o l'istante è fuori dalla finestra.
    /// </summary>
    public double? ProgressAt(DateTimeOffset utc)
    {
        if (!Available || Sunset is not { } sunset || Sunrise is not { } sunrise)
        {
            return null;
        }
        if (utc < sunset || utc > sunrise)
        {
            return null;
        }

        double total = (sunrise - sunset).TotalSeconds;
        return Math.Clamp((utc - sunset).TotalSeconds / total, 0.0, 1.0);
    }

    /// <summary>Primo cambio di fase strettamente dopo l'istante indicato, o null se non ce ne sono più.</summary>
    public TonightPhaseChange? NextChangeAfter(DateTimeOffset utc)
    {
        foreach (TonightPhaseChange change in Changes)
        {
            if (change.AtUtc > utc)
            {
                return change;
            }
        }
        return null;
    }
}
