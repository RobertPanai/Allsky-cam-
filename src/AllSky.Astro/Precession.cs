namespace AllSky.Astro;

/// <summary>
/// Precessione generale da J2000 a una data (P9.1). Formula di Meeus, <i>Astronomical Algorithms</i>, 2ª ed., cap. 21:
/// angoli ζ, z e θ come polinomi in secoli giuliani dal J2000 (eq. 21.2–21.4). Valori in arcsecondi nei polinomi.
/// Limite dichiarato [NV]: la formula è valida per epoche vicine al J2000; non include nutazione né aberrazione.
/// </summary>
public static class Precession
{
    /// <summary>Posizione equatoriale in gradi.</summary>
    /// <param name="RaDeg">Ascensione retta, gradi.</param>
    /// <param name="DecDeg">Declinazione, gradi.</param>
    public readonly record struct Equatorial(double RaDeg, double DecDeg);

    /// <summary>
    /// Converte una posizione media J2000 nella posizione media alla data giuliana indicata.
    /// </summary>
    /// <param name="raDeg">Ascensione retta J2000, gradi.</param>
    /// <param name="decDeg">Declinazione J2000, gradi, tra -90 e 90.</param>
    /// <param name="julianDay">Data giuliana di destinazione (TT o UT: differenza trascurabile per la precessione).</param>
    public static Equatorial FromJ2000(double raDeg, double decDeg, double julianDay)
    {
        if (!double.IsFinite(raDeg) || !double.IsFinite(decDeg) || decDeg < -90.0 || decDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(decDeg), decDeg, "La declinazione deve essere finita e tra -90 e 90 gradi.");
        }

        if (!double.IsFinite(julianDay))
        {
            throw new ArgumentOutOfRangeException(nameof(julianDay), julianDay, "La data giuliana deve essere finita.");
        }

        double t = (julianDay - 2451545.0) / 36525.0;
        double zeta = ArcsecToRad(2306.2181 * t + 0.30188 * t * t + 0.017998 * t * t * t);
        double z = ArcsecToRad(2306.2181 * t + 1.09468 * t * t + 0.018203 * t * t * t);
        double theta = ArcsecToRad(2004.3109 * t - 0.42665 * t * t - 0.041833 * t * t * t);

        double ra = AstroTime.ToRad(raDeg);
        double dec = AstroTime.ToRad(decDeg);

        double a = Math.Cos(dec) * Math.Sin(ra + zeta);
        double b = Math.Cos(theta) * Math.Cos(dec) * Math.Cos(ra + zeta) - Math.Sin(theta) * Math.Sin(dec);
        double c = Math.Sin(theta) * Math.Cos(dec) * Math.Cos(ra + zeta) + Math.Cos(theta) * Math.Sin(dec);

        double newRa = AstroTime.NormalizeDeg(AstroTime.ToDeg(Math.Atan2(a, b) + z));
        double newDec = AstroTime.ToDeg(Math.Asin(Math.Clamp(c, -1.0, 1.0)));
        return new Equatorial(newRa, newDec);
    }

    private static double ArcsecToRad(double arcsec) => arcsec / 3600.0 * Math.PI / 180.0;
}
