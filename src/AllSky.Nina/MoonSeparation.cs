using AllSky.Astro;

namespace AllSky.Nina;

/// <summary>Esito del calcolo della separazione soggetto-Luna.</summary>
public enum MoonSeparationStatus
{
    /// <summary>Separazione calcolata, Luna sopra l'orizzonte.</summary>
    Ok,

    /// <summary>Nessuna coordinata valida del soggetto: nessuna separazione.</summary>
    NoTarget,

    /// <summary>
    /// Luna sotto l'orizzonte geometrico. La separazione viene calcolata lo stesso (e' un dato geometrico),
    /// ma lo stato lo segnala perche' la Luna non e' visibile.
    /// </summary>
    MoonBelowHorizon,

    /// <summary>Epoca delle coordinate diversa da J2000: la precessione non e' implementata, nessuna separazione.</summary>
    Unsupported,
}

/// <summary>
/// Separazione angolare tra il soggetto e la Luna. <see cref="MoonAltitudeDeg"/> e <see cref="MoonIlluminationFraction"/>
/// sono sempre valorizzati: non dipendono dal soggetto.
/// </summary>
public sealed record MoonSeparation(
    MoonSeparationStatus Status,
    double? SeparationDeg,
    double MoonAltitudeDeg,
    double? MoonIlluminationFraction);

/// <summary>
/// Separazione angolare soggetto-Luna e avviso configurabile. Calcolo puro: nessuna rete, nessun orologio di sistema,
/// nessuna dipendenza WPF. L'istante e' un parametro.
/// </summary>
/// <remarks>
/// <b>Epoca.</b> RA e Dec del soggetto sono in gradi e l'epoca va dichiarata dal chiamante. Solo <see cref="CoordinateEpoch.J2000"/>
/// e' accettata: con altre epoche lo stato e' <see cref="MoonSeparationStatus.Unsupported"/>. Come in
/// <see cref="TargetMarker"/> e <see cref="TargetCurve"/>, la precessione non e' applicata: l'epoca di NINA resta [NV].
///
/// <b>Posizione della Luna: accuratezza.</b> <c>AllSky.Astro.MoonPosition</c> espone solo altezza e azimut (non RA/Dec).
/// La separazione e' quindi calcolata tra le direzioni in alt/az dei due astri (ripiego dichiarato): l'angolo tra due
/// direzioni non dipende dal sistema di riferimento, quindi il risultato e' corretto a meno dell'errore della posizione lunare.
/// Errore atteso della posizione lunare (Meeus cap. 47 troncato, come dichiarato in <c>AllSky.Astro</c>): circa 0.5–1 grado [NV].
/// Il valore usato qui e' <see cref="PositionErrorDeg"/> = 1.0 grado, il limite superiore. Le separazioni hanno quindi un'incertezza
/// di circa un grado e non vanno lette con precisione maggiore; i test usano la stessa tolleranza.
///
/// <b>Altezza.</b> Quella del soggetto e' geometrica (senza parallasse, oggetto a distanza infinita); quella della Luna include
/// la parallasse diurna approssimata di <c>MoonPosition</c>. Nessuna rifrazione.
/// </remarks>
public static class MoonSeparationCalculator
{
    /// <summary>
    /// Errore atteso della posizione lunare, in gradi: limite superiore della stima 0.5–1 grado di
    /// <c>AllSky.Astro.MoonPosition</c> (Meeus cap. 47 troncato). Stima dichiarata, non misurata su un riferimento esterno [NV].
    /// </summary>
    public const double PositionErrorDeg = 1.0;

    /// <summary>Calcola la separazione tra il soggetto e la Luna nell'istante indicato.</summary>
    /// <param name="raDeg">Ascensione retta del soggetto in gradi, o null se assente.</param>
    /// <param name="decDeg">Declinazione del soggetto in gradi, o null se assente.</param>
    /// <param name="epoch">Epoca dichiarata delle coordinate: solo J2000 e' calcolabile.</param>
    /// <param name="utc">Istante di calcolo (UTC, qualsiasi offset).</param>
    /// <param name="siteLatDeg">Latitudine del sito in gradi (nord positivo).</param>
    /// <param name="siteLonDeg">Longitudine del sito in gradi (est positivo).</param>
    public static MoonSeparation Compute(
        double? raDeg,
        double? decDeg,
        CoordinateEpoch epoch,
        DateTimeOffset utc,
        double siteLatDeg,
        double siteLonDeg)
    {
        HorizontalPosition moon = MoonPosition.Horizon(utc, siteLatDeg, siteLonDeg);
        double? illumination = MoonPosition.IlluminatedFraction(utc);

        if (raDeg is not double ra || decDeg is not double dec
            || !double.IsFinite(ra) || !double.IsFinite(dec) || dec < -90.0 || dec > 90.0)
        {
            return new MoonSeparation(MoonSeparationStatus.NoTarget, null, moon.AltitudeDeg, illumination);
        }

        if (epoch != CoordinateEpoch.J2000)
        {
            return new MoonSeparation(MoonSeparationStatus.Unsupported, null, moon.AltitudeDeg, illumination);
        }

        HorizontalPosition target = EquatorialToHorizontal.Convert(ra, dec, utc, siteLatDeg, siteLonDeg);
        double separation = AngularSeparationDeg(target.AltitudeDeg, target.AzimuthDeg, moon.AltitudeDeg, moon.AzimuthDeg);

        MoonSeparationStatus status = moon.AltitudeDeg < 0.0
            ? MoonSeparationStatus.MoonBelowHorizon
            : MoonSeparationStatus.Ok;
        return new MoonSeparation(status, separation, moon.AltitudeDeg, illumination);
    }

    /// <summary>
    /// Avviso di separazione troppo piccola. <paramref name="minSeparationDeg"/> e' scelto dall'utente: null = nessun avviso
    /// (nessun valore imposto). Senza separazione calcolata (per esempio <see cref="MoonSeparationStatus.NoTarget"/>)
    /// non c'e' nulla da confrontare e l'avviso e' falso.
    /// </summary>
    /// <returns>True se la separazione e' calcolata e inferiore alla soglia.</returns>
    public static bool IsBelowMinimum(MoonSeparation result, double? minSeparationDeg)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (minSeparationDeg is double min)
        {
            if (!double.IsFinite(min) || min < 0.0 || min > 180.0)
            {
                throw new ArgumentOutOfRangeException(nameof(minSeparationDeg), minSeparationDeg,
                    "La soglia di separazione deve essere tra 0 e 180 gradi.");
            }
        }
        else
        {
            return false;
        }

        return result.SeparationDeg is double sep && sep < minSeparationDeg.Value;
    }

    /// <summary>
    /// Angolo tra due direzioni date in alt/az (gradi), con la formula dell'angolo tra vettori a stabilita' numerica
    /// (atan2 di |a x b| e a . b), valida anche per separazioni vicine a 0 o 180 gradi.
    /// </summary>
    internal static double AngularSeparationDeg(double altA, double azA, double altB, double azB)
    {
        (double ax, double ay, double az) = UnitVector(altA, azA);
        (double bx, double by, double bz) = UnitVector(altB, azB);

        double dot = ax * bx + ay * by + az * bz;
        double cx = ay * bz - az * by;
        double cy = az * bx - ax * bz;
        double cz = ax * by - ay * bx;
        double cross = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        return ToDeg(Math.Atan2(cross, dot));
    }

    /// <summary>Vettore unitario nel riferimento locale (nord, est, zenit) per altezza e azimut (da nord verso est).</summary>
    private static (double X, double Y, double Z) UnitVector(double altDeg, double azDeg)
    {
        double h = ToRad(altDeg);
        double a = ToRad(azDeg);
        return (Math.Cos(h) * Math.Cos(a), Math.Cos(h) * Math.Sin(a), Math.Sin(h));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}

/// <summary>
/// Inversa di <see cref="EquatorialToHorizontal"/>: da altezza e azimut (gradi, nord = 0) a RA/Dec (gradi, RA in [0, 360)),
/// per lo stesso istante e sito. Serve a riportare una direzione in alt/az (per esempio di test) nel sistema equatoriale
/// che usa il mirino. Formule: Meeus, cap. 13, in forma inversa.
/// </summary>
internal static class HorizontalToEquatorial
{
    public static (double RaDeg, double DecDeg) Convert(double altDeg, double azDeg, DateTimeOffset utc, double latDeg, double lonDeg)
    {
        double h = ToRad(altDeg);
        double a = ToRad(azDeg);
        double p = ToRad(latDeg);

        double sinDec = Math.Sin(p) * Math.Sin(h) + Math.Cos(p) * Math.Cos(h) * Math.Cos(a);
        double dec = Math.Asin(Math.Clamp(sinDec, -1.0, 1.0));

        // sin H * cos d = -cos h * sin A ; cos H * cos d = sin h * cos p - cos h * cos A * sin p.
        double hourAngle = Math.Atan2(
            -Math.Cos(h) * Math.Sin(a),
            Math.Sin(h) * Math.Cos(p) - Math.Cos(h) * Math.Cos(a) * Math.Sin(p));

        double lst = EquatorialToHorizontal.LocalMeanSiderealDeg(utc, lonDeg);
        double ra = lst - ToDeg(hourAngle);
        ra %= 360.0;
        if (ra < 0.0) ra += 360.0;
        return (ra, ToDeg(dec));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}
