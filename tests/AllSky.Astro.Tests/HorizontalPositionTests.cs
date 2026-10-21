using AllSky.Astro;
using Xunit;

namespace AllSky.Astro.Tests;

public sealed class HorizontalPositionTests
{
    private const double Lat = 45.07;
    private const double Lon = 7.69;

    [Fact]
    public void SolarHorizon_Altitude_MatchesAltitudeMethod()
    {
        var utc = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);

        HorizontalPosition h = SolarPosition.Horizon(utc, Lat, Lon);

        Assert.Equal(SolarPosition.Altitude(utc, Lat, Lon), h.AltitudeDeg, precision: 9);
    }

    [Fact]
    public void SolarHorizon_NearSolarNoon_IsSouthAndHigh()
    {
        // Mezzogiorno solare a Torino il 19 ottobre: circa 10:50 UTC.
        HorizontalPosition h = SolarPosition.Horizon(new DateTimeOffset(2026, 10, 19, 10, 50, 0, TimeSpan.Zero), Lat, Lon);

        Assert.InRange(h.AzimuthDeg, 170, 190);
        Assert.InRange(h.AltitudeDeg, 33, 40);
    }

    [Fact]
    public void SolarHorizon_MorningSun_IsEast()
    {
        // Alba di metà mattina: Sole a est, azimut in (0, 180).
        HorizontalPosition h = SolarPosition.Horizon(new DateTimeOffset(2026, 10, 19, 7, 0, 0, TimeSpan.Zero), Lat, Lon);

        Assert.InRange(h.AzimuthDeg, 0, 180);
        Assert.True(h.AltitudeDeg > 0);
    }

    [Fact]
    public void MoonHorizon_Altitude_MatchesAltitudeMethod_AndAzimuthIsNormalized()
    {
        var utc = new DateTimeOffset(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);

        HorizontalPosition h = MoonPosition.Horizon(utc, Lat, Lon);

        Assert.Equal(MoonPosition.Altitude(utc, Lat, Lon), h.AltitudeDeg, precision: 9);
        Assert.InRange(h.AzimuthDeg, 0, 360);
        Assert.True(h.AzimuthDeg < 360);
    }
}
