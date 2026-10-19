using Xunit;

namespace AllSky.Astro.Tests;

// Riferimenti usati (tutti con data di consultazione 2026-10-19):
//
// [USNO] Celestial navigation, campo hc (altezza calcolata del Sole, geometrica).
//   Endpoint: https://aa.usno.navy.mil/api/celnav?date=YYYY-MM-DD&time=HH:MM:SS&coords=45.07,7.69
//   Documentazione: https://aa.usno.navy.mil/data/api
//   Nota: i valori sono stati letti tramite estrazione automatica del contenuto della risposta,
//   non dal JSON grezzo. La correzione di rifrazione riportata da USNO e' circa -0.007 gradi a
//   queste altezze, quindi trascurabile rispetto alla tolleranza.
//
// [TAD] timeanddate.com, Turin: https://timeanddate.com/astronomy/italy/turin
//   Per 21 maggio 2025: mezzogiorno solare alle 13:25 locali (CEST, cioe' 11:25 UT),
//   altezza massima 65.2 gradi. Il valore e' arrotondato al decimo di grado.
//
// NOAA Solar Calculator (https://gml.noaa.gov/grad/solcalc/): NON consultato come riferimento
// numerico (lo strumento interattivo non era accessibile). Non esistono valori NOAA nei test.
//
// Tolleranza per il Sole: 0.3 gradi.
public class SolarPositionTests
{
    private const double TurinLatDeg = 45.07;
    private const double TurinLonDeg = 7.69;
    private const double SunToleranceDeg = 0.3;

    [Fact]
    public void JuneSolsticeNearSolarNoonInTurin_MatchesUsno()
    {
        // [USNO] 2026-06-21 11:31:00 UT, Torino: hc = 68.367868
        var utc = new DateTimeOffset(2026, 6, 21, 11, 31, 0, TimeSpan.Zero);

        double alt = SolarPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);

        AssertNear(68.367868, alt, SunToleranceDeg);
    }

    [Fact]
    public void DecemberSolsticeNearSolarNoonInTurin_MatchesUsno()
    {
        // [USNO] 2026-12-21 11:30:00 UT, Torino: hc = 21.490394
        var utc = new DateTimeOffset(2026, 12, 21, 11, 30, 0, TimeSpan.Zero);

        double alt = SolarPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);

        AssertNear(21.490394, alt, SunToleranceDeg);
    }

    [Fact]
    public void MarchEquinoxNoonUtc_InTurin_MatchesUsno()
    {
        // [USNO] 2026-03-20 12:00:00 UT, Torino: hc = 44.589835
        var utc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

        double alt = SolarPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);

        AssertNear(44.589835, alt, SunToleranceDeg);
    }

    [Fact]
    public void SolarNoonMay2025InTurin_MatchesTimeAndDate()
    {
        // [TAD] 2025-05-21, mezzogiorno solare 13:25 CEST = 11:25 UT, altezza massima 65.2 (arrotondata).
        var utc = new DateTimeOffset(2025, 5, 21, 11, 25, 0, TimeSpan.Zero);

        double alt = SolarPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);

        AssertNear(65.2, alt, SunToleranceDeg);
    }

    [Fact]
    public void SameInstantWithDifferentOffsets_GivesSameAltitude()
    {
        var utc = new DateTimeOffset(2026, 6, 21, 11, 31, 0, TimeSpan.Zero);
        var rome = new DateTimeOffset(2026, 6, 21, 13, 31, 0, TimeSpan.FromHours(2));

        double a = SolarPosition.Altitude(utc, TurinLatDeg, TurinLonDeg);
        double b = SolarPosition.Altitude(rome, TurinLatDeg, TurinLonDeg);

        Assert.Equal(a, b, 9);
    }

    [Fact]
    public void InvalidLatitude_Throws()
    {
        var utc = new DateTimeOffset(2026, 6, 21, 11, 31, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => SolarPosition.Altitude(utc, 91.0, 0.0));
    }

    private static void AssertNear(double expected, double actual, double tolerance)
    {
        Assert.True(
            Math.Abs(actual - expected) <= tolerance,
            $"Atteso {expected} +/- {tolerance}, ottenuto {actual}.");
    }
}
