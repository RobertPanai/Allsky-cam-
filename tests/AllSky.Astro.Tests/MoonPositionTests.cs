using Xunit;

namespace AllSky.Astro.Tests;

// Riferimenti usati (tutti con data di consultazione 2026-10-19):
//
// [USNO] Celestial navigation, oggetto Moon: campi hc (altezza calcolata) e moon_illum (frazione illuminata, in percentuale intera).
//   Endpoint: https://aa.usno.navy.mil/api/celnav?date=YYYY-MM-DD&time=HH:MM:SS&coords=45.07,7.69
//   Documentazione: https://aa.usno.navy.mil/data/api
//   Nota: i valori sono stati letti tramite estrazione automatica del contenuto della risposta,
//   non dal JSON grezzo. Dal confronto numerico risulta che hc di USNO corrisponde all'altezza
//   geocentrica (senza parallasse diurna) entro 0.01 gradi; il codice restituisce l'altezza
//   topocentrica, che differisce di circa 0.6-0.9 gradi. Entrambe ricadono nella tolleranza di 1.5 gradi.
//   moon_illum e' arrotondato al punto percentuale intero.
//
// Tolleranza per la Luna: 1.5 gradi in altezza (come richiesto), 0.02 sulla frazione illuminata.
public class MoonPositionTests
{
    private const double TurinLatDeg = 45.07;
    private const double TurinLonDeg = 7.69;
    private const double MoonToleranceDeg = 1.5;
    private const double IlluminationTolerance = 0.02;

    [Fact]
    public void Moon2026October19_18UtcInTurin_MatchesUsno()
    {
        // [USNO] 2026-10-19 18:00:00 UT, Torino: hc = 24.859259, moon_illum = 60 (Waxing Gibbous)
        var utc = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);

        double alt = MoonPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);
        double illum = MoonPosition.IlluminatedFraction(utc);

        AssertNear(24.859259, alt, MoonToleranceDeg);
        AssertNear(0.60, illum, IlluminationTolerance);
    }

    [Fact]
    public void Moon2026June21_21UtcInTurin_MatchesUsno()
    {
        // [USNO] 2026-06-21 21:00:00 UT, Torino: hc = 22.990057, moon_illum = 50 (First Quarter)
        var utc = new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero);

        double alt = MoonPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);
        double illum = MoonPosition.IlluminatedFraction(utc);

        AssertNear(22.990057, alt, MoonToleranceDeg);
        AssertNear(0.50, illum, IlluminationTolerance);
    }

    [Fact]
    public void Moon2026March20_12UtcInTurin_MatchesUsno()
    {
        // [USNO] 2026-03-20 12:00:00 UT, Torino: hc = 54.302903, moon_illum = 3 (Waxing Crescent)
        var utc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

        double alt = MoonPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);
        double illum = MoonPosition.IlluminatedFraction(utc);

        AssertNear(54.302903, alt, MoonToleranceDeg);
        AssertNear(0.03, illum, IlluminationTolerance);
    }

    [Fact]
    public void IlluminatedFraction_IsWithinZeroAndOne()
    {
        var utc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int day = 0; day < 60; day++)
        {
            double k = MoonPosition.IlluminatedFraction(utc.AddDays(day));
            Assert.InRange(k, 0.0, 1.0);
        }
    }

    [Fact]
    public void InvalidLongitude_Throws()
    {
        var utc = new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => MoonPosition.Altitude(utc, TurinLatDeg, 200.0));
    }

    private static void AssertNear(double expected, double actual, double tolerance)
    {
        Assert.True(
            Math.Abs(actual - expected) <= tolerance,
            $"Atteso {expected} +/- {tolerance}, ottenuto {actual}.");
    }
}
