using AllSky.Astro;
using AllSky.Satellites;
using Xunit;

namespace AllSky.Satellites.Tests;

/// <summary>Test della posizione approssimata del Sole in ECI.</summary>
public sealed class SunEphemerisTests
{
    [Fact]
    public void Distanza_dal_Sole_e_circa_una_unita_astronomica()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero),  // perielio, circa
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),  // afelio, circa
            new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero),
        };

        foreach (DateTimeOffset d in dates)
        {
            double au = SunEphemeris.SunEci(d).Norm / SunEphemeris.AstronomicalUnitKm;
            Assert.True(au > 0.983 && au < 1.017, $"Distanza {au:F4} UA il {d:u}, attesa tra 0.983 e 1.017.");
        }
    }

    [Fact]
    public void Declinazione_nulla_all_equinozio_di_marzo()
    {
        // Equinozio di marzo 2026 circa il 20 marzo: la declinazione è prossima a zero (tolleranza 0.5 gradi).
        EciKm s = SunEphemeris.SunEci(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));
        double decDeg = Math.Asin(s.Z / s.Norm) * 180.0 / Math.PI;
        Assert.True(Math.Abs(decDeg) < 0.5, $"Declinazione {decDeg:F3} gradi all'equinozio, attesa vicina a 0.");
    }

    [Fact]
    public void Declinazione_circa_23_gradi_al_solstizio_di_giugno()
    {
        EciKm s = SunEphemeris.SunEci(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
        double decDeg = Math.Asin(s.Z / s.Norm) * 180.0 / Math.PI;
        Assert.True(Math.Abs(decDeg - 23.44) < 0.3, $"Declinazione {decDeg:F3} gradi al solstizio, attesa circa 23.44.");
    }

    [Fact]
    public void Altezza_del_Sole_coerente_con_SolarPosition()
    {
        // Confronto incrociato: altezza calcolata dal vettore ECI del Sole e dal GMST, contro SolarPosition (NOAA).
        const double lat = 45.07;
        const double lon = 7.69;
        double latRad = lat * Math.PI / 180.0;
        double lonRad = lon * Math.PI / 180.0;

        for (int h = 0; h < 24; h++)
        {
            DateTimeOffset t = new DateTimeOffset(2026, 10, 22, h, 0, 0, TimeSpan.Zero);
            EciKm sun = SunEphemeris.SunEci(t);
            double sunNorm = sun.Norm;

            // Zenit dell'osservatore in ECI: il sistema ruota di GMST attorno all'asse Z.
            double local = CircularOrbitPropagator.GmstDeg(t) * Math.PI / 180.0 + lonRad;
            double zx = Math.Cos(latRad) * Math.Cos(local);
            double zy = Math.Cos(latRad) * Math.Sin(local);
            double zz = Math.Sin(latRad);

            double altEci = Math.Asin(Math.Clamp((sun.X * zx + sun.Y * zy + sun.Z * zz) / sunNorm, -1.0, 1.0)) * 180.0 / Math.PI;
            double altRef = SolarPosition.Altitude(t, lat, lon);

            Assert.True(Math.Abs(altEci - altRef) < 0.3,
                $"Alle {h:D2}:00 UTC altezza ECI {altEci:F3} gradi, SolarPosition {altRef:F3} gradi.");
        }
    }
}
