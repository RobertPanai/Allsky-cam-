using AllSky.Astro;
using AllSky.Sky;

namespace AllSky.Nina;

/// <summary>
/// Posizione in pixel di un punto sull'immagine all-sky (origine in alto a sinistra, come <see cref="FisheyeModel"/>).
/// </summary>
public readonly record struct PixelPosition(double X, double Y);

/// <summary>
/// Motivo dell'esito del mirino. Visible e' l'unico caso con posizione in pixel.
/// </summary>
public enum TargetMarkerStatus
{
    /// <summary>Il punto e' sopra l'orizzonte e cade nell'immagine: posizione disponibile.</summary>
    Visible,

    /// <summary>Altezza negativa: il soggetto e' sotto l'orizzonte geometrico.</summary>
    BelowHorizon,

    /// <summary>
    /// Il punto cade fuori dalla zona utile dell'immagine. Con altezza &gt;= 0 il modello lo mette sempre
    /// dentro il cerchio dell'orizzonte, quindi in pratica scatta quando il cerchio esce dal rettangolo del frame.
    /// </summary>
    OutsideCircle,

    /// <summary>Nessuna calibrazione ottica disponibile (o non accettata): invitare a calibrare.</summary>
    NoCalibration,

    /// <summary>Nessuna coordinata di soggetto (RA o Dec assenti o non finite).</summary>
    NoTarget,

    /// <summary>Le coordinate sono state lette troppo tempo fa (o hanno un istante futuro rispetto a "adesso").</summary>
    StaleCoordinates,
}

/// <summary>
/// Esito del calcolo del mirino. <see cref="Pixel"/> e' valorizzato solo con <see cref="Status"/> = Visible.
/// <see cref="Horizontal"/> e' valorizzato quando le coordinate sono valide (anche se il punto e' sotto l'orizzonte).
/// </summary>
public sealed record TargetMarkerResult(
    TargetMarkerStatus Status,
    PixelPosition? Pixel,
    HorizontalPosition? Horizontal);

/// <summary>
/// Calcolo puro della posizione del soggetto sul frame all-sky. Nessuna dipendenza da WPF, da rete o dall'orologio di sistema:
/// l'istante "adesso" e' un parametro.
/// </summary>
/// <remarks>
/// <b>Unita' e epoca delle coordinate.</b> RaDeg e DecDeg sono in gradi (RA in [0, 360), Dec in [-90, 90]).
/// L'epoca non e' verificata: la documentazione di NINA/ninaAPI non e' stata letta sul PC di Robert [NV].
/// Il codice assume che le coordinate siano gia' in J2000 e NON applica la precessione: quando l'epoca e' nota
/// (da docs/NINA-API.md, campo verificato su NINA reale), la precessione va fatta a monte, prima di questa funzione,
/// con una formula da fonte citata (per esempio Meeus, cap. 21). Non farla a memoria.
///
/// <b>Parallasse.</b> La all-sky sta vicino al telescopio: per oggetti celesti la direzione osservata coincide con quella
/// puntata dal mount. Nessuna correzione di parallasse viene applicata.
///
/// <b>Rifrazione.</b> L'altezza e' geometrica (come in <see cref="AllSky.Astro.SolarPosition"/>): nessuna rifrazione.
/// </remarks>
public static class TargetMarker
{
    /// <summary>
    /// Calcola la posizione del soggetto sul frame.
    /// </summary>
    /// <param name="model">Modello fisheye della calibrazione accettata. Null = calibrazione assente.</param>
    /// <param name="frameWidthPx">Larghezza del frame in pixel (maggiore di zero).</param>
    /// <param name="frameHeightPx">Altezza del frame in pixel (maggiore di zero).</param>
    /// <param name="raDeg">Ascensione retta del soggetto in gradi, o null se assente.</param>
    /// <param name="decDeg">Declinazione del soggetto in gradi, o null se assente.</param>
    /// <param name="coordinatesReadAtUtc">Istante UTC in cui le coordinate sono state lette da NINA.</param>
    /// <param name="nowUtc">Istante di calcolo (UTC).</param>
    /// <param name="maxCoordinateAge">Eta' massima accettata delle coordinate. Scelta dell'utente: nessun valore imposto qui.</param>
    /// <param name="siteLatDeg">Latitudine dell'osservatorio in gradi (nord positivo).</param>
    /// <param name="siteLonDeg">Longitudine dell'osservatorio in gradi (est positivo).</param>
    public static TargetMarkerResult Compute(
        FisheyeModel? model,
        int frameWidthPx,
        int frameHeightPx,
        double? raDeg,
        double? decDeg,
        DateTimeOffset coordinatesReadAtUtc,
        DateTimeOffset nowUtc,
        TimeSpan maxCoordinateAge,
        double siteLatDeg,
        double siteLonDeg)
    {
        if (frameWidthPx <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidthPx), frameWidthPx, "La larghezza del frame deve essere maggiore di zero.");
        if (frameHeightPx <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeightPx), frameHeightPx, "L'altezza del frame deve essere maggiore di zero.");
        if (maxCoordinateAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxCoordinateAge), maxCoordinateAge, "L'eta' massima non puo' essere negativa.");
        if (double.IsNaN(siteLatDeg) || siteLatDeg < -90.0 || siteLatDeg > 90.0)
            throw new ArgumentOutOfRangeException(nameof(siteLatDeg), siteLatDeg, "La latitudine deve essere tra -90 e 90 gradi.");
        if (double.IsNaN(siteLonDeg) || siteLonDeg < -180.0 || siteLonDeg > 180.0)
            throw new ArgumentOutOfRangeException(nameof(siteLonDeg), siteLonDeg, "La longitudine deve essere tra -180 e 180 gradi.");

        // Calibrazione assente: non e' un'eccezione, e' un esito.
        if (model is null)
        {
            return Absent(TargetMarkerStatus.NoCalibration);
        }

        if (raDeg is not double ra || decDeg is not double dec || !double.IsFinite(ra) || !double.IsFinite(dec))
        {
            return Absent(TargetMarkerStatus.NoTarget);
        }

        // Eta' delle coordinate: un istante futuro (orologio diverso tra PC e NINA) viene trattato come stale.
        TimeSpan age = nowUtc - coordinatesReadAtUtc;
        if (age < TimeSpan.Zero || age > maxCoordinateAge)
        {
            return Absent(TargetMarkerStatus.StaleCoordinates);
        }

        HorizontalPosition hz = EquatorialToHorizontal.Convert(ra, dec, nowUtc, siteLatDeg, siteLonDeg);
        if (hz.AltitudeDeg < 0.0)
        {
            return new TargetMarkerResult(TargetMarkerStatus.BelowHorizon, null, hz);
        }

        if (!model.TryAltAzToPixel(hz.AltitudeDeg, hz.AzimuthDeg, out double x, out double y))
        {
            return new TargetMarkerResult(TargetMarkerStatus.OutsideCircle, null, hz);
        }

        if (x < 0.0 || y < 0.0 || x > frameWidthPx || y > frameHeightPx)
        {
            return new TargetMarkerResult(TargetMarkerStatus.OutsideCircle, null, hz);
        }

        return new TargetMarkerResult(TargetMarkerStatus.Visible, new PixelPosition(x, y), hz);
    }

    private static TargetMarkerResult Absent(TargetMarkerStatus status) => new(status, null, null);
}

/// <summary>
/// Tempo sidereo e conversione equatoriale → orizzontale. Vivono qui e non in AllSky.Astro perche' servono solo al mirino
/// (AllSky.Astro oggi calcola solo Sole e Luna).
/// </summary>
internal static class EquatorialToHorizontal
{
    /// <summary>
    /// Conversione RA/Dec (gradi, epoca gia' allineata a J2000 dal chiamante) → altezza e azimut (gradi, nord = 0, est = 90).
    /// Formule: Meeus, "Astronomical Algorithms" (2a ed.), cap. 13, eq. 13.5 e 13.6:
    ///   sin(h) = sin(phi) sin(delta) + cos(phi) cos(delta) cos(H)
    ///   tan(A) = sin(H) / (cos(H) sin(phi) - tan(delta) cos(phi)), A misurato da sud verso ovest, +180 per il nord.
    /// La forma usata moltiplica numeratore e denominatore per cos(delta) > 0 per evitare tan(delta) ai poli.
    /// H = tempo sidereo locale - RA (angolo orario, positivo a ovest del meridiano).
    /// </summary>
    public static HorizontalPosition Convert(double raDeg, double decDeg, DateTimeOffset utc, double latDeg, double lonDeg)
    {
        double lst = LocalMeanSiderealDeg(utc, lonDeg);
        double hDeg = NormalizeDeg(lst - raDeg);

        double h = ToRad(hDeg);
        double d = ToRad(decDeg);
        double p = ToRad(latDeg);

        double sinAlt = Math.Sin(p) * Math.Sin(d) + Math.Cos(p) * Math.Cos(d) * Math.Cos(h);
        sinAlt = Math.Clamp(sinAlt, -1.0, 1.0);
        double altDeg = ToDeg(Math.Asin(sinAlt));

        double fromSouthWestDeg = ToDeg(Math.Atan2(
            Math.Sin(h) * Math.Cos(d),
            Math.Cos(h) * Math.Sin(p) * Math.Cos(d) - Math.Sin(d) * Math.Cos(p)));
        double azDeg = NormalizeDeg(fromSouthWestDeg + 180.0);

        return new HorizontalPosition { AltitudeDeg = altDeg, AzimuthDeg = azDeg };
    }

    /// <summary>
    /// Tempo sidereo medio locale in gradi: GMST + longitudine (est positiva).
    /// GMST da Meeus, eq. 12.4: theta0 = 280.46061837 + 360.98564736629 (JD - 2451545.0) + 0.000387933 T^2 - T^3 / 38710000,
    /// con T = (JD - 2451545.0) / 36525. Il JD e' calcolato da UTC invece che da UT1: lo scarto (|DUT1| &lt; 0.9 s)
    /// vale al massimo circa 0.004 gradi in tempo sidereo, trascurabile per un mirino.
    /// </summary>
    public static double LocalMeanSiderealDeg(DateTimeOffset utc, double lonDeg)
    {
        double jd = utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double d = jd - 2451545.0;
        double t = d / 36525.0;
        double gmst = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - t * t * t / 38710000.0;
        return NormalizeDeg(gmst + lonDeg);
    }

    private static double NormalizeDeg(double deg)
    {
        double r = deg % 360.0;
        return r < 0.0 ? r + 360.0 : r;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}
