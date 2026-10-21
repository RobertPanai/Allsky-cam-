namespace AllSky.Astro;

/// <summary>
/// Funzioni di supporto interne: giorno giuliano, secoli giuliani, conversioni di gradi e controlli di input.
/// Tutte le funzioni trigonometriche accettano e restituiscono gradi, per ridurre gli errori di conversione.
/// </summary>
internal static class AstroTime
{
    /// <summary>Giorno giuliano (UT) dell'istante indicato. Epoca Unix = JD 2440587.5.</summary>
    public static double JulianDay(DateTimeOffset utc)
    {
        return utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
    }

    /// <summary>Secoli giuliani dall'epoca J2000.0 (JD 2451545.0).</summary>
    public static double Centuries(double julianDay)
    {
        return (julianDay - 2451545.0) / 36525.0;
    }

    /// <summary>Riporta un angolo in gradi nell'intervallo [0, 360).</summary>
    public static double NormalizeDeg(double deg)
    {
        return NormalizeRange(deg, 360.0);
    }

    /// <summary>Riporta un valore nell'intervallo [0, period).</summary>
    public static double NormalizeRange(double value, double period)
    {
        double r = value % period;
        if (r < 0.0)
        {
            r += period;
        }
        return r;
    }

    public static double ToRad(double deg) => deg * Math.PI / 180.0;

    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;

    public static double SinDeg(double deg) => Math.Sin(ToRad(deg));

    public static double CosDeg(double deg) => Math.Cos(ToRad(deg));

    /// <summary>
    /// Azimut da nord verso est (gradi, [0, 360)) da angolo orario e declinazione (radianti) e latitudine (radianti).
    /// Forma di Meeus, cap. 13: l'angolo viene misurato da sud verso ovest e poi convertito a partire da nord.
    /// </summary>
    public static double AzimuthFromNorthDeg(double hourAngleRad, double decRad, double latRad)
    {
        double fromSouthWest = Math.Atan2(
            Math.Sin(hourAngleRad),
            Math.Cos(hourAngleRad) * Math.Sin(latRad) - Math.Tan(decRad) * Math.Cos(latRad));
        return NormalizeDeg(ToDeg(fromSouthWest) + 180.0);
    }

    public static void CheckLatLon(double latDeg, double lonDeg)
    {
        if (double.IsNaN(latDeg) || latDeg < -90.0 || latDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(latDeg), latDeg, "La latitudine deve essere tra -90 e 90 gradi.");
        }
        if (double.IsNaN(lonDeg) || lonDeg < -180.0 || lonDeg > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lonDeg), lonDeg, "La longitudine deve essere tra -180 e 180 gradi (est positivo).");
        }
    }
}
