namespace AllSky.Astro;

/// <summary>
/// Posizione della Luna a bassa precisione.
/// Algoritmo: serie di Meeus, "Astronomical Algorithms" (2a ed.), capitolo 47, troncata ai termini principali
/// (32 termini di longitudine/distanza e 15 di latitudine di maggiore ampiezza, più i termini additivi di Meeus).
/// Precisione attesa: circa 0.5°–1° in altezza. Questo valore e' una stima onesta del troncamento:
/// non e' stata verificata su un intervallo ampio di date e non sostituisce un ephemeride completa.
/// L'altezza tiene conto della parallasse diurna (correzione topocentrica approssimata, h - pi*cos h),
/// ma non della rifrazione atmosferica.
/// </summary>
public static class MoonPosition
{
    private const double EarthRadiusKm = 6378.14;

    // Termini di longitudine (L, in 1e-6 gradi) e distanza (R, in 1e-3 km).
    // Argomenti: D, M (anomalia solare), M' (anomalia lunare), F (argomento di latitudine). Seno per L, coseno per R.
    private static readonly (int D, int M, int Mp, int F, double L, double R)[] LongDistTerms =
    {
        (0, 0, 1, 0, 6288774, -20905355),
        (2, 0, -1, 0, 1274027, -3699111),
        (2, 0, 0, 0, 658314, -2955968),
        (0, 0, 2, 0, 213618, -569925),
        (0, 1, 0, 0, -185116, 48888),
        (0, 0, 0, 2, -114332, -3149),
        (2, 0, -2, 0, 58793, 246158),
        (2, -1, -1, 0, 57066, -152138),
        (2, 0, 1, 0, 53322, -170733),
        (2, -1, 0, 0, 45758, -204586),
        (0, 1, -1, 0, -40923, -129620),
        (1, 0, 0, 0, -34720, 108743),
        (0, 1, 1, 0, -30383, 104755),
        (2, 0, 0, -2, 15327, 10321),
        (0, 0, 1, 2, 12528, 0),
        (0, 0, 1, -2, 10980, 79661),
        (4, 0, -1, 0, 10675, -34782),
        (0, 0, 3, 0, 10034, -23210),
        (4, 0, -2, 0, 8548, -21636),
        (2, 1, -1, 0, -7888, 24208),
        (2, 1, 0, 0, -6766, 30824),
        (1, 0, -1, 0, -5163, -8379),
        (1, 1, 0, 0, 4987, -16675),
        (2, -1, 1, 0, 4036, -12831),
        (2, 0, 2, 0, 3994, -10445),
        (4, 0, 0, 0, 3861, -11650),
        (2, 0, -3, 0, 3665, 14403),
        (0, 1, -2, 0, -2689, -7003),
        (2, 0, -1, 2, -2602, 0),
        (2, -1, -2, 0, 2390, 10056),
        (1, 0, 1, 0, -2348, 6322),
        (2, -2, 0, 0, 2236, -9884),
    };

    // Termini di latitudine (B, in 1e-6 gradi), seno dell'argomento.
    private static readonly (int D, int M, int Mp, int F, double B)[] LatTerms =
    {
        (0, 0, 0, 1, 5128122),
        (0, 0, 1, 1, 280602),
        (0, 0, 1, -1, 277693),
        (2, 0, 0, -1, 173237),
        (2, 0, -1, 1, 55413),
        (2, 0, -1, -1, 46271),
        (2, 0, 0, 1, 32573),
        (0, 0, 2, 1, 17198),
        (2, 0, 1, -1, 9266),
        (0, 0, 2, -1, 8822),
        (2, -1, 0, -1, 8216),
        (2, 0, -2, -1, 4324),
        (2, 0, 1, 1, 4200),
        (2, 1, 0, -1, -3359),
        (2, -1, -1, 1, 2463),
    };

    /// <summary>
    /// Altezza della Luna in gradi sull'orizzonte geometrico (con parallasse, senza rifrazione).
    /// Precisione attesa: circa 0.5°–1°.
    /// </summary>
    /// <param name="utc">Istante di calcolo (con qualsiasi offset: viene convertito in UTC).</param>
    /// <param name="latDeg">Latitudine dell'osservatore in gradi (nord positivo).</param>
    /// <param name="lonDeg">Longitudine dell'osservatore in gradi (est positivo).</param>
    public static double Altitude(DateTimeOffset utc, double latDeg, double lonDeg)
    {
        AstroTime.CheckLatLon(latDeg, lonDeg);

        double jd = AstroTime.JulianDay(utc);
        double t = AstroTime.Centuries(jd);
        var ecl = GeocentricEcliptic(t);

        // Eclittica -> equatoriale (ascensione retta e declinazione).
        double eps = AstroTime.ToRad(MeanObliquityDeg(t));
        double lam = AstroTime.ToRad(ecl.LonDeg);
        double bet = AstroTime.ToRad(ecl.LatDeg);
        double ra = Math.Atan2(
            Math.Sin(lam) * Math.Cos(eps) - Math.Tan(bet) * Math.Sin(eps),
            Math.Cos(lam));
        double dec = Math.Asin(Math.Clamp(
            Math.Sin(bet) * Math.Cos(eps) + Math.Cos(bet) * Math.Sin(eps) * Math.Sin(lam),
            -1.0, 1.0));

        // Angolo orario locale.
        double lstDeg = AstroTime.NormalizeDeg(GreenwichMeanSiderealDeg(jd, t) + lonDeg);
        double hourAngle = AstroTime.ToRad(lstDeg) - ra;

        // Altezza geometrica (geocentrica) e correzione di parallasse diurna.
        double phi = AstroTime.ToRad(latDeg);
        double sinH = Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(hourAngle);
        double hGeo = Math.Asin(Math.Clamp(sinH, -1.0, 1.0));
        double parallax = Math.Asin(EarthRadiusKm / ecl.DistanceKm);
        double hTopo = hGeo - parallax * Math.Cos(hGeo);

        return AstroTime.ToDeg(hTopo);
    }

    /// <summary>
    /// Frazione illuminata del disco lunare, da 0 (luna nuova) a 1 (luna piena).
    /// Calcolata dall'elongazione Sole-Luna: k = (1 - cos psi) / 2.
    /// </summary>
    public static double IlluminatedFraction(DateTimeOffset utc)
    {
        double t = AstroTime.Centuries(AstroTime.JulianDay(utc));
        var ecl = GeocentricEcliptic(t);
        double sunLon = SolarPosition.ComputeGeometry(t).ApparentLongitudeDeg;

        // Sole sull'eclittica (latitudine 0): cos psi = cos(beta) * cos(lambda_Luna - lambda_Sole).
        double cosElong = AstroTime.CosDeg(ecl.LatDeg) * AstroTime.CosDeg(ecl.LonDeg - sunLon);
        double k = (1.0 - cosElong) / 2.0;
        return Math.Clamp(k, 0.0, 1.0);
    }

    /// <summary>
    /// Longitudine e latitudine eclittiche geocentriche (gradi) e distanza (km), Meeus cap. 47 troncato.
    /// </summary>
    private static (double LonDeg, double LatDeg, double DistanceKm) GeocentricEcliptic(double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        double t4 = t3 * t;

        // Argomenti fondamentali (gradi).
        double lp = 218.3164477 + 481267.88123421 * t - 0.0015786 * t2 + t3 / 538841.0 - t4 / 65194000.0;
        double d = 297.8501921 + 445267.1114034 * t - 0.0018819 * t2 + t3 / 545868.0 - t4 / 113065000.0;
        double m = 357.5291092 + 35999.0502909 * t - 0.0001536 * t2 + t3 / 24490000.0;
        double mp = 134.9633964 + 477198.8675055 * t + 0.0087414 * t2 + t3 / 69699.0 - t4 / 14712000.0;
        double f = 93.2720950 + 483202.0175233 * t - 0.0036539 * t2 - t3 / 3526000.0 + t4 / 863310000.0;
        double a1 = 119.75 + 131.849 * t;
        double a2 = 53.09 + 479264.290 * t;
        double a3 = 313.45 + 481266.484 * t;
        double e = 1.0 - 0.002516 * t - 0.0000074 * t2;

        double sumL = 0.0;
        double sumR = 0.0;
        double sumB = 0.0;

        foreach (var term in LongDistTerms)
        {
            double arg = term.D * d + term.M * m + term.Mp * mp + term.F * f;
            double fac = EFactor(term.M, e);
            sumL += fac * term.L * AstroTime.SinDeg(arg);
            sumR += fac * term.R * AstroTime.CosDeg(arg);
        }

        foreach (var term in LatTerms)
        {
            double arg = term.D * d + term.M * m + term.Mp * mp + term.F * f;
            double fac = EFactor(term.M, e);
            sumB += fac * term.B * AstroTime.SinDeg(arg);
        }

        // Termini additivi di Meeus cap. 47.
        sumL += 3958.0 * AstroTime.SinDeg(a1) + 1962.0 * AstroTime.SinDeg(lp - f) + 318.0 * AstroTime.SinDeg(a2);
        sumB += -2235.0 * AstroTime.SinDeg(lp)
              + 382.0 * AstroTime.SinDeg(a3)
              + 175.0 * AstroTime.SinDeg(a1 - f)
              + 175.0 * AstroTime.SinDeg(a1 + f)
              + 127.0 * AstroTime.SinDeg(lp - mp)
              - 115.0 * AstroTime.SinDeg(lp + mp);

        double lonDeg = AstroTime.NormalizeDeg(lp + sumL / 1000000.0);
        double latDeg = sumB / 1000000.0;
        double distKm = 385000.56 + sumR / 1000.0;
        return (lonDeg, latDeg, distKm);
    }

    /// <summary>
    /// Fattore di eccentricità terrestre E^|M| per i termini che contengono l'anomalia solare M.
    /// </summary>
    private static double EFactor(int mCoef, double e)
    {
        int a = Math.Abs(mCoef);
        if (a == 2)
        {
            return e * e;
        }
        if (a == 1)
        {
            return e;
        }
        return 1.0;
    }

    /// <summary>Obliquita' media dell'eclittica in gradi (Meeus cap. 22).</summary>
    private static double MeanObliquityDeg(double t)
    {
        return 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
    }

    /// <summary>Tempo sidereo medio di Greenwich in gradi (Meeus eq. 12.4).</summary>
    private static double GreenwichMeanSiderealDeg(double jd, double t)
    {
        double deg = 280.46061837
                   + 360.98564736629 * (jd - 2451545.0)
                   + 0.000387933 * t * t
                   - t * t * t / 38710000.0;
        return AstroTime.NormalizeDeg(deg);
    }
}
