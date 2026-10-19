using Xunit;

namespace AllSky.Astro.Tests;

// Soglie: giorno se h >= 0; crepuscolo civile se -6 <= h < 0; nautico se -12 <= h < -6;
// astronomico se -18 <= h < -12; notte se h < -18. Gli estremi inferiori sono inclusi.
public class SkyPhasesTests
{
    [Theory]
    [InlineData(90.0, SkyPhase.Day)]
    [InlineData(0.0001, SkyPhase.Day)]
    [InlineData(0.0, SkyPhase.Day)]
    [InlineData(-0.0001, SkyPhase.CivilTwilight)]
    [InlineData(-6.0, SkyPhase.CivilTwilight)]
    [InlineData(-6.0001, SkyPhase.NauticalTwilight)]
    [InlineData(-12.0, SkyPhase.NauticalTwilight)]
    [InlineData(-12.0001, SkyPhase.AstronomicalTwilight)]
    [InlineData(-18.0, SkyPhase.AstronomicalTwilight)]
    [InlineData(-18.0001, SkyPhase.Night)]
    [InlineData(-90.0, SkyPhase.Night)]
    public void Classify_UsesExactBoundaries(double altitudeDeg, SkyPhase expected)
    {
        Assert.Equal(expected, SkyPhases.Classify(altitudeDeg));
    }

    [Fact]
    public void Classify_NaN_Throws()
    {
        Assert.Throws<ArgumentException>(() => SkyPhases.Classify(double.NaN));
    }

    [Theory]
    [InlineData(SkyPhase.Day, "day")]
    [InlineData(SkyPhase.CivilTwilight, "twilight")]
    [InlineData(SkyPhase.NauticalTwilight, "twilight")]
    [InlineData(SkyPhase.AstronomicalTwilight, "twilight")]
    [InlineData(SkyPhase.Night, "night")]
    public void Simplify_MapsToApiValues(SkyPhase phase, string expected)
    {
        Assert.Equal(expected, SkyPhases.Simplify(phase));
    }
}
