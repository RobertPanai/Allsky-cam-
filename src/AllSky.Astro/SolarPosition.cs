namespace AllSky.Astro;

/// <summary>
/// Posizione del Sole.
/// Algoritmo: versione semplificata della "solar position" del NOAA Global Monitoring Laboratory
/// (fonte: NOAA Solar Calculator, https://gml.noaa.gov/grad/solcalc/), che segue Meeus,
/// "Astronomical Algorithms" (2a ed.): equazione del tempo e declinazione dal giorno giuliano
/// e dai secoli giuliani.
/// Precisione attesa: circa 0.01°–0.1°.
/// L'altezza è geometrica: non è applicata la rifrazione atmosferica.
/// </summary>
public static class SolarPosition
{
    /// <summary>
    /// Altezza del Sole in gradi sull'orizzonte geometrico.
    /// </summary>
    /// <param name="utc">Istante di calcolo (UTC, o con qualsiasi offset: viene convertito).</param>
    /// <param name="latDeg">Latitudine dell'osservatore in gradi (nord positivo).</param>
    /// <param name="lonDeg">Longitudine dell'osservatore in gradi (est positivo).</param>
    /// <returns>Altezza in gradi, negativa se il Sole è sotto l'orizzonte.</returns>
    public static double Altitude(DateTimeOffset utc, double latDeg, double lonDeg) =>
        Horizon(utc, latDeg, lonDeg).AltitudeDeg;

    /// <summary>
    /// Altezza e azimut del Sole (azimut da nord verso est). Stessi calcoli di <see cref="Altitude"/>.
    /// </summary>
    public static HorizontalPosition Horizon(DateTimeOffset utc, double latDeg, double lonDeg)
    {
        AstroTime.CheckLatLon(latDeg, lonDeg);

        double jd = AstroTime.JulianDay(utc);
        double t = AstroTime.Centuries(jd);
        SunGeometry geo = ComputeGeometry(t);

        // Tempo solare vero in minuti dalla mezzanotte UTC.
        // NOAA: timeOffset = eqTime + 4 * longitudine - 60 * fuso; qui il fuso e' UTC (0).
        DateTime utcTime = utc.UtcDateTime;
        double utcMinutes = utcTime.TimeOfDay.TotalMinutes;
        double trueSolarMinutes = AstroTime.NormalizeRange(
            utcMinutes + geo.EquationOfTimeMin + 4.0 * lonDeg, 1440.0);

        // Angolo orario: 0 al mezzogiorno solare vero, negativo prima.
        double hourAngleDeg = trueSolarMinutes / 4.0 - 180.0;

        double latRad = AstroTime.ToRad(latDeg);
        double decRad = AstroTime.ToRad(geo.DeclinationDeg);
        double haRad = AstroTime.ToRad(hourAngleDeg);

        double sinAlt = Math.Sin(latRad) * Math.Sin(decRad)
                      + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(haRad);
        sinAlt = Math.Clamp(sinAlt, -1.0, 1.0);

        double altDeg = AstroTime.ToDeg(Math.Asin(sinAlt));
        double azDeg = AstroTime.AzimuthFromNorthDeg(haRad, decRad, latRad);
        return new HorizontalPosition { AltitudeDeg = altDeg, AzimuthDeg = azDeg };
    }

    /// <summary>
    /// Grandezze solari necessarie ad altri calcoli (usate anche dalla Luna per la fase).
    /// </summary>
    /// <param name="t">Secoli giuliani da J2000.0.</param>
    internal static SunGeometry ComputeGeometry(double t)
    {
        // Longitudine media e anomalia media del Sole (gradi).
        double l0 = AstroTime.NormalizeDeg(280.46646 + t * (36000.76983 + t * 0.0003032));
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        // Equazione del centro.
        double mRad = AstroTime.ToRad(m);
        double c = Math.Sin(mRad) * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(2.0 * mRad) * (0.019993 - 0.000101 * t)
                 + Math.Sin(3.0 * mRad) * 0.000289;

        double trueLong = l0 + c;
        double omega = 125.04 - 1934.136 * t;
        double apparentLong = trueLong - 0.00569 - 0.00478 * AstroTime.SinDeg(omega);

        // Obliquita' dell'eclittica corretta per la nutazione.
        double meanObliq = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
        double obliq = meanObliq + 0.00256 * AstroTime.CosDeg(omega);
        double obliqRad = AstroTime.ToRad(obliq);

        // Declinazione del Sole.
        double decDeg = AstroTime.ToDeg(
            Math.Asin(Math.Sin(obliqRad) * Math.Sin(AstroTime.ToRad(apparentLong))));

        // Equazione del tempo in minuti (formula NOAA / Meeus).
        double l0Rad = AstroTime.ToRad(l0);
        double y = Math.Tan(obliqRad / 2.0);
        y *= y;
        double eqRad = y * Math.Sin(2.0 * l0Rad)
                     - 2.0 * e * Math.Sin(mRad)
                     + 4.0 * e * y * Math.Sin(mRad) * Math.Cos(2.0 * l0Rad)
                     - 0.5 * y * y * Math.Sin(4.0 * l0Rad)
                     - 1.25 * e * e * Math.Sin(2.0 * mRad);
        double eqTimeMin = 4.0 * AstroTime.ToDeg(eqRad);

        return new SunGeometry(apparentLong, decDeg, eqTimeMin);
    }
}

/// <summary>
/// Grandezze geometriche del Sole in un dato istante.
/// </summary>
/// <param name="ApparentLongitudeDeg">Longitudine eclittica apparente del Sole (gradi).</param>
/// <param name="DeclinationDeg">Declinazione del Sole (gradi).</param>
/// <param name="EquationOfTimeMin">Equazione del tempo (minuti).</param>
internal readonly record struct SunGeometry(double ApparentLongitudeDeg, double DeclinationDeg, double EquationOfTimeMin);
