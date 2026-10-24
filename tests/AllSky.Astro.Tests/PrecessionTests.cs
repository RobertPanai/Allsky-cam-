using AllSky.Astro;
using Xunit;

namespace AllSky.Astro.Tests;

// Precessione da J2000 a una data (P9.1). Tassi di riferimento: precessione in ascensione retta circa 46,1"/anno
// e in declinazione circa 20,04"/anno, a α = 0 e δ = 0 (valori standard, Meeus cap. 21 [V]).
public class PrecessionTests
{
    private const double J2000 = 2451545.0;
    private const double DaysPerYear = 365.25;

    [Fact]
    public void AtJ2000_PositionIsUnchanged()
    {
        Precession.Equatorial p = Precession.FromJ2000(10.0, 20.0, J2000);

        Assert.InRange(p.RaDeg, 10.0 - 1e-9, 10.0 + 1e-9);
        Assert.InRange(p.DecDeg, 20.0 - 1e-9, 20.0 + 1e-9);
    }

    [Fact]
    public void TenYears_ShiftsRightAscensionAboutFortySixArcsecPerYear()
    {
        double jd = J2000 + 10.0 * DaysPerYear;

        Precession.Equatorial p = Precession.FromJ2000(0.0, 0.0, jd);

        double expectedDeg = 46.1 * 10.0 / 3600.0;
        Assert.InRange(p.RaDeg, expectedDeg * 0.99, expectedDeg * 1.01);
    }

    [Fact]
    public void TenYears_ShiftsDeclinationAboutTwentyArcsecPerYearAtRaZero()
    {
        double jd = J2000 + 10.0 * DaysPerYear;

        Precession.Equatorial p = Precession.FromJ2000(0.0, 0.0, jd);

        double expectedDeg = 20.04 * 10.0 / 3600.0;
        Assert.InRange(p.DecDeg, expectedDeg * 0.99, expectedDeg * 1.01);
    }

    [Fact]
    public void Result_StaysInValidRanges()
    {
        Precession.Equatorial p = Precession.FromJ2000(359.5, 89.5, J2000 + 100.0 * DaysPerYear);

        Assert.InRange(p.RaDeg, 0.0, 360.0);
        Assert.InRange(p.DecDeg, -90.0, 90.0);
    }

    [Fact]
    public void InvalidDeclination_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Precession.FromJ2000(0.0, 95.0, J2000));
    }
}
