using AllSky.Satellites;
using Xunit;

namespace AllSky.Satellites.Tests;

/// <summary>Test del modello di ombra cilindrica terrestre.</summary>
public sealed class EarthShadowTests
{
    // Sole lontano lungo +X (direzione unitaria moltiplicata per 1 UA).
    private static readonly EciKm Sun = new EciKm(1.5e8, 0.0, 0.0);

    [Fact]
    public void Satellite_dietro_la_Terra_non_e_illuminato()
    {
        // Lato notte, centrato sull'asse Terra-Sole: dentro il cilindro d'ombra.
        Assert.False(EarthShadow.IsSunlit(new EciKm(-7000.0, 0.0, 0.0), Sun));

        // Lato notte, a 6000 km dall'asse (sotto il raggio terrestre): ancora in ombra.
        Assert.False(EarthShadow.IsSunlit(new EciKm(-7000.0, 6000.0, 0.0), Sun));
    }

    [Fact]
    public void Satellite_dal_lato_del_Sole_e_illuminato()
    {
        Assert.True(EarthShadow.IsSunlit(new EciKm(7000.0, 0.0, 0.0), Sun));
        Assert.True(EarthShadow.IsSunlit(new EciKm(0.0, 7000.0, 0.0), Sun));
    }

    [Fact]
    public void Satellite_dal_lato_notte_fuori_dal_cilindro_e_illuminato()
    {
        // Lato notte ma a 7000 km dall'asse, più del raggio terrestre 6378.137 km.
        Assert.True(EarthShadow.IsSunlit(new EciKm(-7000.0, 7000.0, 0.0), Sun));
    }

    [Fact]
    public void Sole_nullo_non_e_ammesso()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            EarthShadow.IsSunlit(new EciKm(7000.0, 0.0, 0.0), new EciKm(0.0, 0.0, 0.0));
        });
    }
}
