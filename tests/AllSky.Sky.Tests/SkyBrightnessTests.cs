using Xunit;

namespace AllSky.Sky.Tests;

// Brillanza del cielo stimata (P9.3): fondo sintetico con brillanza nota.
public class SkyBrightnessTests
{
    private const double ZeroPoint = 22.0;

    [Fact]
    public void OneAduPerSecondPerArcsec2_GivesTheZeroPoint()
    {
        // Flusso 1 ADU/s/arcsec²: μ = Z.
        double mu = SkyBrightness.MagPerArcsec2(backgroundAdu: 4.0, exposureSeconds: 2.0, pixelAreaArcsec2: 2.0, zeroPoint: ZeroPoint);

        Assert.InRange(mu, ZeroPoint - 1e-9, ZeroPoint + 1e-9);
    }

    [Fact]
    public void HundredTimesTheFlux_IsFiveMagnitudesBrighter()
    {
        double faint = SkyBrightness.MagPerArcsec2(backgroundAdu: 10.0, exposureSeconds: 1.0, pixelAreaArcsec2: 1.0, zeroPoint: ZeroPoint);
        double bright = SkyBrightness.MagPerArcsec2(backgroundAdu: 1000.0, exposureSeconds: 1.0, pixelAreaArcsec2: 1.0, zeroPoint: ZeroPoint);

        Assert.InRange(faint - bright, 5.0 - 1e-9, 5.0 + 1e-9);
    }

    [Fact]
    public void OffsetIsAddedAsCalibration()
    {
        double plain = SkyBrightness.MagPerArcsec2(20.0, 1.0, 1.0, ZeroPoint);
        double shifted = SkyBrightness.MagPerArcsec2(20.0, 1.0, 1.0, ZeroPoint, offsetMag: 0.3);

        Assert.InRange(shifted - plain, 0.3 - 1e-9, 0.3 + 1e-9);
    }

    [Fact]
    public void InvalidInputs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyBrightness.MagPerArcsec2(0.0, 1.0, 1.0, ZeroPoint));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyBrightness.MagPerArcsec2(10.0, 0.0, 1.0, ZeroPoint));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyBrightness.MagPerArcsec2(10.0, 1.0, double.NaN, ZeroPoint));
    }
}
