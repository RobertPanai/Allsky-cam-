using AllSky.Satellites;
using AllSky.Satellites.Sgp4;
using Xunit;

namespace AllSky.Satellites.Sgp4.Tests;

/// <summary>
/// Test dell'adapter SGP.NET con il TLE di esempio del README della libreria.
/// </summary>
/// <remarks>
/// Il TLE è un dato di esempio (epoca 2019-02-03), non un elemento orbitale attuale. I test verificano formato,
/// unità e plausibilità geometrica, non valori di riferimento. Il conteggio dei passaggi (3-8 in 24 h sopra 10 gradi)
/// è un ordine di grandezza plausibile per un'orbita bassa a 51.6 gradi, NON un risultato di riferimento.
/// Questi test richiedono il pacchetto NuGet SGP.NET: girano solo in CI Windows.
/// </remarks>
public sealed class SgpNetPropagatorTests
{
    private const string Name = "ISS (ZARYA)";
    private const string Line1 = "1 25544U 98067A   19034.73310439  .00001974  00000-0  38215-4 0  9991";
    private const string Line2 = "2 25544  51.6436 304.9146 0005074 348.4622  36.8575 15.53228055154526";

    // Torino, osservatorio di riferimento.
    private const double TorinoLat = 45.07;
    private const double TorinoLon = 7.69;
    private const double TorinoAltM = 240.0;

    // Epoca del TLE: giorno 19034.73310439 dell'anno 2019 (3 febbraio 2019, UTC).
    private static readonly DateTimeOffset Epoch =
        new DateTimeOffset(2019, 2, 3, 0, 0, 0, TimeSpan.Zero).AddSeconds(0.73310439 * 86400.0);

    private static IOrbitPropagator Propagatore()
    {
        return new SgpNetPropagatorFactory().Create(new TleSet(25544, Name, Line1, Line2, Epoch));
    }

    [Fact]
    public void Osservazione_ISS_da_Torino_ha_valori_plausibili()
    {
        IOrbitPropagator prop = Propagatore();
        SatObservation obs = prop.Observe(Epoch.AddHours(1), TorinoLat, TorinoLon, TorinoAltM);

        Assert.InRange(obs.ElevationDeg, -90.0, 90.0);
        Assert.True(obs.AzimuthDeg >= 0.0 && obs.AzimuthDeg < 360.0, $"Azimut {obs.AzimuthDeg} fuori da [0, 360).");
        Assert.InRange(obs.RangeKm, 350.0, 14000.0);

        // Posizione ECI in km: il raggio dell'ISS è circa 6700-6800 km.
        double r = obs.Eci.Norm;
        Assert.True(r > 6600.0 && r < 6900.0, $"Raggio ECI {r:F1} km, atteso circa 6700-6800 km.");
    }

    [Fact]
    public void Passaggi_in_24_ore_da_Torino_sono_nell_intervallo_plausibile()
    {
        IOrbitPropagator prop = Propagatore();
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(prop, TorinoLat, TorinoLon, TorinoAltM, Epoch, TimeSpan.FromHours(24));

        Assert.InRange(passes.Count, 3, 8);
        foreach (SatellitePass p in passes)
        {
            Assert.True(p.MaxElevationDeg >= 10.0 - 1e-9, "Un passaggio ha elevazione massima sotto la soglia.");
            Assert.True(p.Start.Utc <= p.Culmination.Utc && p.Culmination.Utc <= p.End.Utc, "Culminazione fuori dal passaggio.");
        }
    }

    [Fact]
    public void TLE_non_leggibile_produce_errore_chiaro()
    {
        var bad = new TleSet(25544, Name, "1 corrotta", Line2, Epoch);

        InvalidOperationException ex = Assert.ThrowsAny<InvalidOperationException>(() =>
        {
            new SgpNetPropagatorFactory().Create(bad);
        });

        Assert.Contains("25544", ex.Message);
    }
}
