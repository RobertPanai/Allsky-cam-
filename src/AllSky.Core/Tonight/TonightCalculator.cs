using AllSky.Astro;

namespace AllSky.Core.Tonight;

/// <summary>Risultato del calcolo della vista "Stanotte" in un istante.</summary>
/// <param name="ComputedUtc">Istante del calcolo, UTC.</param>
/// <param name="NightId">Notte osservativa a cui appartiene l'istante, formato YYYY-MM-DD.</param>
/// <param name="Timeline">Linea tramonto-alba della notte, con i campioni di altezza.</param>
/// <param name="Moon">Glifo della fase lunare nell'istante di calcolo.</param>
/// <param name="SunAltitudeNowDeg">Altezza del Sole nell'istante di calcolo, in gradi.</param>
/// <param name="MoonAltitudeNowDeg">Altezza della Luna nell'istante di calcolo, in gradi.</param>
public sealed record TonightSnapshot(
    DateTimeOffset ComputedUtc,
    string NightId,
    TonightTimeline Timeline,
    MoonPhaseGlyph Moon,
    double SunAltitudeNowDeg,
    double MoonAltitudeNowDeg);

/// <summary>Esito di un evento previsto dalla vista: il prossimo cambio di fase e i riferimenti futuri.</summary>
/// <param name="NextChange">Prossimo cambio di fase del cielo, o null se la notte è finita o non disponibile.</param>
/// <param name="NextTransit">Prossimo transito di un satellite. Null finché il calcolo dei transiti (P4) non è disponibile.</param>
/// <param name="LastMeteorUtc">Istante dell'ultima meteora registrata. Null finché il rilevamento (P5) non è disponibile.</param>
public sealed record TonightUpcoming(
    TonightPhaseChange? NextChange,
    TonightTransit? NextTransit,
    DateTimeOffset? LastMeteorUtc);

/// <summary>Transito previsto di un satellite: nome e istante di inizio.</summary>
/// <param name="Target">Nome del satellite.</param>
/// <param name="StartUtc">Inizio del transito, UTC.</param>
public sealed record TonightTransit(string Target, DateTimeOffset StartUtc);

/// <summary>
/// Calcola la notte della vista "Stanotte": fasi e altezze con <c>AllSky.Astro</c>, glifo lunare. Logica pura: non legge
/// l'orologio, riceve l'istante. Il calcolo costa qualche millisecondo e va chiamato fuori dal thread dell'interfaccia.
/// </summary>
public static class TonightCalculator
{
    /// <summary>Passo di campionamento predefinito delle altezze: 5 minuti.</summary>
    public static readonly TimeSpan DefaultSampleStep = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Calcola la notte che contiene <paramref name="now"/>: da mezzogiorno a mezzogiorno locale, come definisce
    /// <see cref="ObservingNight"/>.
    /// </summary>
    /// <param name="now">Istante di calcolo.</param>
    /// <param name="latDeg">Latitudine del sito in gradi (nord positivo).</param>
    /// <param name="lonDeg">Longitudine del sito in gradi (est positivo).</param>
    /// <param name="tz">Fuso orario del sito.</param>
    /// <param name="sampleStep">Passo dei campioni di altezza, maggiore di zero; predefinito 5 minuti.</param>
    public static TonightSnapshot Compute(
        DateTimeOffset now, double latDeg, double lonDeg, TimeZoneInfo tz, TimeSpan? sampleStep = null)
    {
        ArgumentNullException.ThrowIfNull(tz);
        AstroTime.CheckLatLon(latDeg, lonDeg);

        TimeSpan step = sampleStep ?? DefaultSampleStep;
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleStep), step, "Il passo dei campioni deve essere maggiore di zero.");
        }

        string nightId = ObservingNight.GetNightId(now, tz);
        IReadOnlyList<PhaseBand> bands = ObservingNight.GetBands(nightId, latDeg, lonDeg, tz);
        TonightTimeline timeline = TonightTimeline.Build(bands);
        if (timeline.Available && timeline.Sunset is { } sunset && timeline.Sunrise is { } sunrise)
        {
            timeline = timeline.WithSamples(Sample(sunset, sunrise, step, latDeg, lonDeg));
        }

        return new TonightSnapshot(
            now,
            nightId,
            timeline,
            MoonPhaseGlyph.At(now, latDeg),
            SolarPosition.Altitude(now, latDeg, lonDeg),
            MoonPosition.Altitude(now, latDeg, lonDeg));
    }

    /// <summary>Altezze del Sole e della Luna da <paramref name="from"/> a <paramref name="to"/>, con l'ultimo campione sul bordo.</summary>
    public static IReadOnlyList<TonightSample> Sample(
        DateTimeOffset from, DateTimeOffset to, TimeSpan step, double latDeg, double lonDeg)
    {
        var samples = new List<TonightSample>();
        for (DateTimeOffset t = from; t < to; t += step)
        {
            samples.Add(AtInstant(t, latDeg, lonDeg));
        }
        samples.Add(AtInstant(to, latDeg, lonDeg));
        return samples;
    }

    private static TonightSample AtInstant(DateTimeOffset t, double latDeg, double lonDeg) =>
        new(t, SolarPosition.Altitude(t, latDeg, lonDeg), MoonPosition.Altitude(t, latDeg, lonDeg));
}

/// <summary>Risoluzione del fuso orario del sito, con lo stesso comportamento dell'Engine.</summary>
public static class TonightSite
{
    /// <summary>
    /// Fuso orario per identificativo IANA o Windows. Se manca o non si trova, restituisce UTC.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
