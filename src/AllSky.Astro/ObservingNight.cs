using System.Globalization;

namespace AllSky.Astro;

/// <summary>
/// Intervallo continuo di una stessa fase del cielo. Gli intervalli di una notte sono contigui:
/// il To di un intervallo coincide con il From del successivo.
/// </summary>
public readonly record struct PhaseBand(SkyPhase Phase, DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Notte osservativa: va da mezzogiorno locale a mezzogiorno locale del giorno successivo.
/// L'identificatore e' la data di inizio nel formato "YYYY-MM-DD".
/// </summary>
public static class ObservingNight
{
    private const string IdFormat = "yyyy-MM-dd";
    private static readonly TimeSpan DefaultStep = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Identificatore della notte a cui appartiene un istante.
    /// Esempi con fuso Europe/Rome: 19/10 alle 03:00 locali -&gt; "2026-10-18"; 19/10 alle 13:00 locali -&gt; "2026-10-19".
    /// </summary>
    public static string GetNightId(DateTimeOffset utc, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);

        DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, tz);
        DateOnly day = DateOnly.FromDateTime(local.DateTime);
        if (local.Hour < 12)
        {
            day = day.AddDays(-1);
        }
        return day.ToString(IdFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Fasi del cielo della notte, con passo di campionamento di 1 minuto.
    /// </summary>
    public static IReadOnlyList<PhaseBand> GetBands(string nightId, double latDeg, double lonDeg, TimeZoneInfo tz)
    {
        return GetBands(nightId, latDeg, lonDeg, tz, DefaultStep);
    }

    /// <summary>
    /// Fasi del cielo della notte campionando l'altezza del Sole con il passo indicato.
    /// Campioni consecutivi con la stessa fase vengono uniti in un solo intervallo.
    /// Il confine fra due fasi e' quindi noto con un'approssimazione pari al passo.
    /// </summary>
    /// <param name="nightId">Identificatore "YYYY-MM-DD" (data di inizio della notte).</param>
    /// <param name="latDeg">Latitudine dell'osservatore in gradi.</param>
    /// <param name="lonDeg">Longitudine dell'osservatore in gradi (est positivo).</param>
    /// <param name="tz">Fuso orario locale dell'osservatorio.</param>
    /// <param name="step">Passo di campionamento, maggiore di zero.</param>
    public static IReadOnlyList<PhaseBand> GetBands(
        string nightId, double latDeg, double lonDeg, TimeZoneInfo tz, TimeSpan step)
    {
        ArgumentNullException.ThrowIfNull(nightId);
        ArgumentNullException.ThrowIfNull(tz);
        AstroTime.CheckLatLon(latDeg, lonDeg);
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "Il passo di campionamento deve essere maggiore di zero.");
        }

        DateOnly day = DateOnly.ParseExact(nightId, IdFormat, CultureInfo.InvariantCulture);
        DateTimeOffset start = LocalNoonUtc(day, tz);
        DateTimeOffset end = LocalNoonUtc(day.AddDays(1), tz);

        var bands = new List<PhaseBand>();
        bool hasCurrent = false;
        SkyPhase currentPhase = SkyPhase.Day;
        DateTimeOffset bandFrom = start;

        for (DateTimeOffset t = start; t < end; t += step)
        {
            SkyPhase phase = SkyPhases.Classify(SolarPosition.Altitude(t, latDeg, lonDeg));

            if (!hasCurrent)
            {
                hasCurrent = true;
                currentPhase = phase;
                bandFrom = t;
            }
            else if (phase != currentPhase)
            {
                bands.Add(new PhaseBand(currentPhase, bandFrom, t));
                currentPhase = phase;
                bandFrom = t;
            }
        }

        if (hasCurrent)
        {
            bands.Add(new PhaseBand(currentPhase, bandFrom, end));
        }

        return bands;
    }

    /// <summary>
    /// Mezzogiorno locale del giorno indicato, espresso in UTC.
    /// </summary>
    private static DateTimeOffset LocalNoonUtc(DateOnly day, TimeZoneInfo tz)
    {
        var noonLocal = new DateTime(day.Year, day.Month, day.Day, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime noonUtc = TimeZoneInfo.ConvertTimeToUtc(noonLocal, tz);
        return new DateTimeOffset(noonUtc, TimeSpan.Zero);
    }
}
