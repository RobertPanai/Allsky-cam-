namespace AllSky.Satellites;

/// <summary>
/// Posizione approssimata del Sole in ECI, in km.
/// </summary>
/// <remarks>
/// Fonte: Jean Meeus, "Astronomical Algorithms" (2a ed., 1998), capitolo 25 "Solar Coordinates",
/// formule a bassa precisione (errore dell'ordine di 0.01°). Le formule della longitudine apparente,
/// dell'obliquità sono quelle del capitolo. La distanza usa l'approssimazione classica
/// R = 1.00014 - 0.01671 cos M - 0.00014 cos 2M (UA): NON è stata riletta riga per riga sul testo di Meeus,
/// ed è verificata solo dal test di distanza (0.983-1.017 UA) e dal confronto con SolarPosition.
/// Il sistema è equatoriale con equinozio della data. Per l'ombra terrestre l'errore è trascurabile.
/// </remarks>
public static class SunEphemeris
{
    /// <summary>Unità astronomica in km (IAU 2012).</summary>
    public const double AstronomicalUnitKm = 149_597_870.7;

    /// <summary>Posizione del Sole in ECI nell'istante indicato, in km.</summary>
    /// <param name="utc">Istante (qualsiasi offset: viene convertito in UTC).</param>
    public static EciKm SunEci(DateTimeOffset utc)
    {
        // Giorno giuliano e secoli giuliani da J2000.0.
        double jd = utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double t = (jd - 2451545.0) / 36525.0;

        // Longitudine media e anomalia media del Sole (gradi).
        double l0 = 280.46646 + 36000.76983 * t + 0.0003032 * t * t;
        double m = 357.52911 + 35999.05029 * t - 0.0001537 * t * t;
        double mRad = ToRad(m);

        // Equazione del centro.
        double c = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(mRad)
                 + (0.019993 - 0.000101 * t) * Math.Sin(2.0 * mRad)
                 + 0.000289 * Math.Sin(3.0 * mRad);
        double trueLong = l0 + c;

        // Longitudine apparente: correzione per la nutazione e l'aberrazione (Meeus 25.6 e 25.8).
        double omega = 125.04 - 1934.136 * t;
        double lambdaRad = ToRad(trueLong - 0.00569 - 0.00478 * Math.Sin(ToRad(omega)));

        // Obliquità media dell'eclittica più la correzione di nutazione (Meeus 22.2 e 22.3).
        double epsilon0 = 23.4392911 - 0.0130042 * t - 0.00000016 * t * t + 0.0000005 * t * t * t;
        double epsilonRad = ToRad(epsilon0 + 0.00256 * Math.Cos(ToRad(omega)));

        // Distanza Terra-Sole in UA (formula approssimata di Meeus, cap. 25).
        double rAu = 1.00014 - 0.01671 * Math.Cos(mRad) - 0.00014 * Math.Cos(2.0 * mRad);
        double rKm = rAu * AstronomicalUnitKm;

        // Dall'eclittica all'equatore: rotazione di epsilon attorno all'asse X.
        double x = rKm * Math.Cos(lambdaRad);
        double y = rKm * Math.Sin(lambdaRad) * Math.Cos(epsilonRad);
        double z = rKm * Math.Sin(lambdaRad) * Math.Sin(epsilonRad);

        return new EciKm(x, y, z);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
