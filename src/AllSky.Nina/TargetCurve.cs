using AllSky.Astro;

namespace AllSky.Nina;

/// <summary>
/// Epoca dichiarata delle coordinate del soggetto. Solo <see cref="J2000"/> è calcolabile: la precessione
/// non è implementata e nessuna epoca diversa viene convertita in silenzio.
/// </summary>
public enum CoordinateEpoch
{
    /// <summary>Equinozio J2000.0: coordinate usabili direttamente.</summary>
    J2000,

    /// <summary>Equinozio della data (JNow): richiede la precessione, non implementata.</summary>
    OfDate,

    /// <summary>Epoca non nota.</summary>
    Unknown,
}

/// <summary>Esito del calcolo della curva.</summary>
public enum TargetCurveStatus
{
    /// <summary>Curva calcolata.</summary>
    Ok,

    /// <summary>Nessun soggetto o coordinate mancanti/non valide: serie vuota.</summary>
    NoTarget,

    /// <summary>Epoca diversa da J2000 senza precessione: serie vuota.</summary>
    UnsupportedEpoch,

    /// <summary>Fine della notte non successiva all'inizio: nessuna serie né fasi.</summary>
    InvalidWindow,
}

/// <summary>
/// Campione della curva. <see cref="MoonAltitudeDeg"/> è l'altezza della Luna nello stesso istante.
/// </summary>
public readonly record struct TargetCurvePoint(
    DateTimeOffset UtcTime,
    double AltitudeDeg,
    double AzimuthDeg,
    double MoonAltitudeDeg);

/// <summary>
/// Risultato della curva. Le fasi crepuscolari (<see cref="PhaseBands"/>) sono calcolate ogni volta che la finestra è valida,
/// anche senza soggetto, così il grafico può mostrare lo sfondo senza NINA.
/// Campi metadati valorizzati solo con <see cref="Status"/> = Ok, salvo dove indicato.
/// </summary>
public sealed record TargetCurveResult(
    TargetCurveStatus Status,
    string? Reason,
    string? TargetName,
    IReadOnlyList<TargetCurvePoint> Points,
    IReadOnlyList<PhaseBand> PhaseBands,
    DateTimeOffset? TransitUtc,
    double? TransitAltitudeDeg,
    double? MoonAltitudeAtTransitDeg,
    double? MaxAltitudeDeg,
    double? ThresholdDeg,
    double? HoursAboveThreshold);

/// <summary>
/// Curva altezza/ora di un soggetto durante una notte osservativa. Calcolo puro: nessuna rete, nessun orologio di sistema,
/// nessuna dipendenza WPF. Il grafico (P11.4 UI) non è in questo file.
/// </summary>
/// <remarks>
/// <b>Epoca.</b> RA e Dec in gradi. Con epoca diversa da J2000 il risultato è <see cref="TargetCurveStatus.UnsupportedEpoch"/>:
/// niente precessione inventata. Le coordinate NINA restano [NV] finché non si verifica l'epoca su NINA reale (docs/NINA-API.md).
///
/// <b>Altezza.</b> Geometrica, senza rifrazione e senza parallasse (oggetto a distanza infinita), come il Sole e la Luna di AllSky.Astro.
///
/// <b>Transito.</b> Transito superiore (angolo orario = 0) cercato con il segno dell'angolo orario tra i campioni e
/// raffinato per bisezione: il tempo non dipende dal passo. Se non cade nella finestra, <c>TransitUtc</c> è null.
///
/// <b>Ore sopra soglia.</b> Calcolate solo con soglia fornita (nessun valore imposto). Integrazione sui campioni con
/// interpolazione lineare dell'altezza tra due campioni consecutivi.
/// </remarks>
public static class TargetCurve
{
    /// <summary>Passo predefinito dei campioni, in minuti.</summary>
    public const int DefaultStepMinutes = 10;

    /// <summary>Passo minimo ammesso, in minuti.</summary>
    public const int MinStepMinutes = 1;

    /// <summary>Passo massimo ammesso, in minuti.</summary>
    public const int MaxStepMinutes = 60;

    private const int BisectionIterations = 60;

    /// <summary>Calcola la curva a partire dal soggetto letto da NINA (può essere null).</summary>
    public static TargetCurveResult Compute(
        NinaTargetState? target,
        CoordinateEpoch epoch,
        DateTimeOffset nightStartUtc,
        DateTimeOffset nightEndUtc,
        double siteLatDeg,
        double siteLonDeg,
        int stepMinutes = DefaultStepMinutes,
        double? thresholdDeg = null)
    {
        return Compute(
            target?.Name, target?.RaDeg, target?.DecDeg, epoch,
            nightStartUtc, nightEndUtc, siteLatDeg, siteLonDeg, stepMinutes, thresholdDeg);
    }

    /// <summary>Calcolo puro a partire da coordinate esplicite.</summary>
    /// <param name="targetName">Nome del soggetto, solo per il risultato.</param>
    /// <param name="raDeg">Ascensione retta in gradi, o null se assente.</param>
    /// <param name="decDeg">Declinazione in gradi, o null se assente.</param>
    /// <param name="epoch">Epoca dichiarata delle coordinate.</param>
    /// <param name="nightStartUtc">Inizio della notte (UTC).</param>
    /// <param name="nightEndUtc">Fine della notte (UTC).</param>
    /// <param name="siteLatDeg">Latitudine del sito in gradi (nord positivo).</param>
    /// <param name="siteLonDeg">Longitudine del sito in gradi (est positivo).</param>
    /// <param name="stepMinutes">Passo dei campioni, da 1 a 60 minuti.</param>
    /// <param name="thresholdDeg">Soglia di altezza scelta dall'utente, o null per non calcolare le ore sopra soglia.</param>
    public static TargetCurveResult Compute(
        string? targetName,
        double? raDeg,
        double? decDeg,
        CoordinateEpoch epoch,
        DateTimeOffset nightStartUtc,
        DateTimeOffset nightEndUtc,
        double siteLatDeg,
        double siteLonDeg,
        int stepMinutes = DefaultStepMinutes,
        double? thresholdDeg = null)
    {
        if (stepMinutes < MinStepMinutes || stepMinutes > MaxStepMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(stepMinutes), stepMinutes,
                $"Il passo deve essere tra {MinStepMinutes} e {MaxStepMinutes} minuti.");
        }
        if (thresholdDeg is double thr && (!double.IsFinite(thr) || thr < -90.0 || thr > 90.0))
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdDeg), thresholdDeg,
                "La soglia di altezza deve essere tra -90 e 90 gradi.");
        }
        if (double.IsNaN(siteLatDeg) || siteLatDeg < -90.0 || siteLatDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteLatDeg), siteLatDeg, "La latitudine deve essere tra -90 e 90 gradi.");
        }
        if (double.IsNaN(siteLonDeg) || siteLonDeg < -180.0 || siteLonDeg > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteLonDeg), siteLonDeg, "La longitudine deve essere tra -180 e 180 gradi.");
        }

        DateTimeOffset start = nightStartUtc.ToUniversalTime();
        DateTimeOffset end = nightEndUtc.ToUniversalTime();
        if (end <= start)
        {
            return Empty(TargetCurveStatus.InvalidWindow,
                "La fine della notte deve essere successiva all'inizio.", targetName, Array.Empty<PhaseBand>(), thresholdDeg);
        }

        IReadOnlyList<DateTimeOffset> times = SampleTimes(start, end, TimeSpan.FromMinutes(stepMinutes));
        IReadOnlyList<PhaseBand> phases = PhaseBandsFor(times, siteLatDeg, siteLonDeg);

        if (raDeg is null || decDeg is null)
        {
            return Empty(TargetCurveStatus.NoTarget,
                "Nessun soggetto con coordinate: NINA non le espone o la sequenza non è in corso.", targetName, phases, thresholdDeg);
        }
        double ra = raDeg.Value;
        double dec = decDeg.Value;
        if (!double.IsFinite(ra) || !double.IsFinite(dec) || dec < -90.0 || dec > 90.0)
        {
            return Empty(TargetCurveStatus.NoTarget,
                "Coordinate non valide: valori non finiti o declinazione fuori da -90..90 gradi.", targetName, phases, thresholdDeg);
        }
        if (epoch != CoordinateEpoch.J2000)
        {
            return Empty(TargetCurveStatus.UnsupportedEpoch,
                $"Epoca {epoch} non supportata: la precessione a J2000.0 non è implementata.", targetName, phases, thresholdDeg);
        }

        var points = new List<TargetCurvePoint>(times.Count);
        double maxAlt = double.NegativeInfinity;
        foreach (DateTimeOffset t in times)
        {
            HorizontalPosition hz = EquatorialToHorizontal.Convert(ra, dec, t, siteLatDeg, siteLonDeg);
            double moonAlt = MoonPosition.Altitude(t, siteLatDeg, siteLonDeg);
            points.Add(new TargetCurvePoint(t, hz.AltitudeDeg, hz.AzimuthDeg, moonAlt));
            maxAlt = Math.Max(maxAlt, hz.AltitudeDeg);
        }

        DateTimeOffset? transit = FindUpperTransit(times, ra, dec, siteLatDeg, siteLonDeg);
        double? transitAlt = null;
        double? moonAtTransit = null;
        if (transit is DateTimeOffset tr)
        {
            transitAlt = EquatorialToHorizontal.Convert(ra, dec, tr, siteLatDeg, siteLonDeg).AltitudeDeg;
            moonAtTransit = MoonPosition.Altitude(tr, siteLatDeg, siteLonDeg);
            maxAlt = Math.Max(maxAlt, transitAlt.Value);
        }

        double? hoursAbove = thresholdDeg is double th ? HoursAbove(points, th) : null;

        return new TargetCurveResult(
            TargetCurveStatus.Ok,
            null,
            targetName,
            points,
            phases,
            transit,
            transitAlt,
            moonAtTransit,
            maxAlt,
            thresholdDeg,
            hoursAbove);
    }

    private static TargetCurveResult Empty(
        TargetCurveStatus status, string reason, string? targetName, IReadOnlyList<PhaseBand> phases, double? thresholdDeg) =>
        new(status, reason, targetName, Array.Empty<TargetCurvePoint>(), phases,
            null, null, null, null, thresholdDeg, null);

    /// <summary>Istanti di campionamento da inizio a fine (fine inclusa).</summary>
    private static IReadOnlyList<DateTimeOffset> SampleTimes(DateTimeOffset start, DateTimeOffset end, TimeSpan step)
    {
        var list = new List<DateTimeOffset>();
        for (DateTimeOffset t = start; t < end; t += step)
        {
            list.Add(t);
        }
        list.Add(end);
        return list;
    }

    /// <summary>
    /// Fasi del cielo nella finestra, stessa logica di <c>ObservingNight.GetBands</c> ma su una finestra arbitraria.
    /// Confini noti con approssimazione pari al passo.
    /// </summary>
    private static IReadOnlyList<PhaseBand> PhaseBandsFor(IReadOnlyList<DateTimeOffset> times, double latDeg, double lonDeg)
    {
        var bands = new List<PhaseBand>();
        SkyPhase? current = null;
        DateTimeOffset from = times[0];

        foreach (DateTimeOffset t in times)
        {
            SkyPhase phase = SkyPhases.Classify(SolarPosition.Altitude(t, latDeg, lonDeg));
            if (current is null)
            {
                current = phase;
                from = t;
            }
            else if (phase != current.Value)
            {
                bands.Add(new PhaseBand(current.Value, from, t));
                current = phase;
                from = t;
            }
        }

        if (current is SkyPhase last)
        {
            bands.Add(new PhaseBand(last, from, times[^1]));
        }
        return bands;
    }

    /// <summary>
    /// Transito superiore nella finestra: passaggio dell'angolo orario da negativo a non negativo tra due campioni,
    /// raffinato per bisezione. Se ce ne sono più d'uno (finestra più lunga del giorno siderale), vince quello più alto.
    /// </summary>
    private static DateTimeOffset? FindUpperTransit(
        IReadOnlyList<DateTimeOffset> times, double raDeg, double decDeg, double latDeg, double lonDeg)
    {
        DateTimeOffset? best = null;
        double bestAlt = double.NegativeInfinity;

        double prevHa = HourAngleDeg(times[0], raDeg, lonDeg);
        for (int i = 1; i < times.Count; i++)
        {
            double ha = HourAngleDeg(times[i], raDeg, lonDeg);
            if (prevHa < 0.0 && ha >= 0.0)
            {
                DateTimeOffset lo = times[i - 1];
                DateTimeOffset hi = times[i];
                for (int k = 0; k < BisectionIterations && hi - lo > TimeSpan.FromMilliseconds(1); k++)
                {
                    DateTimeOffset mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
                    if (HourAngleDeg(mid, raDeg, lonDeg) < 0.0)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double alt = EquatorialToHorizontal.Convert(raDeg, decDeg, hi, latDeg, lonDeg).AltitudeDeg;
                if (alt > bestAlt)
                {
                    bestAlt = alt;
                    best = hi;
                }
            }
            prevHa = ha;
        }
        return best;
    }

    /// <summary>Angolo orario in gradi, in [-180, 180): zero al transito, positivo dopo.</summary>
    private static double HourAngleDeg(DateTimeOffset utc, double raDeg, double lonDeg)
    {
        double lst = EquatorialToHorizontal.LocalMeanSiderealDeg(utc, lonDeg);
        double h = (lst - raDeg) % 360.0;
        if (h < -180.0) h += 360.0;
        if (h >= 180.0) h -= 360.0;
        return h;
    }

    /// <summary>
    /// Ore con altezza maggiore o uguale alla soglia, con interpolazione lineare tra campioni consecutivi.
    /// </summary>
    private static double HoursAbove(IReadOnlyList<TargetCurvePoint> points, double thresholdDeg)
    {
        double hours = 0.0;
        for (int i = 1; i < points.Count; i++)
        {
            double a = points[i - 1].AltitudeDeg;
            double b = points[i].AltitudeDeg;
            double dh = (points[i].UtcTime - points[i - 1].UtcTime).TotalHours;
            bool upA = a >= thresholdDeg;
            bool upB = b >= thresholdDeg;

            if (upA && upB)
            {
                hours += dh;
            }
            else if (upA != upB)
            {
                double frac = upA ? (a - thresholdDeg) / (a - b) : (b - thresholdDeg) / (b - a);
                hours += dh * frac;
            }
        }
        return hours;
    }
}
