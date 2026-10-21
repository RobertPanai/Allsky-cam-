using Xunit;

namespace AllSky.Sky.Tests;

// Convenzione del modello (vedi FisheyeModel): zenit al centro, y verso il basso,
// Nord in alto e Est a sinistra con Mirrored = false e NorthRotationDeg = 0.
// NorthRotationDeg ruota in senso orario. Tutti i test rispettano questa convenzione.
public class FisheyeModelTests
{
    private const double CenterX = 600.0;
    private const double CenterY = 500.0;
    private const double ScaleRadiusPx = 480.0;

    private const double RoundTripToleranceDeg = 0.01;
    private const double RoundTripTolerancePx = 0.01;
    private const double ExactTolerance = 1e-9;

    private static FisheyeModel Model(double k2, bool mirrored, double rotationDeg)
        => new(CenterX, CenterY, ScaleRadiusPx, rotationDeg, mirrored, k2);

    [Theory]
    [InlineData(0.0, false, 0.0)]
    [InlineData(0.0, true, 0.0)]
    [InlineData(0.0, false, 37.0)]
    [InlineData(0.0, true, 37.0)]
    [InlineData(0.0, false, 180.0)]
    [InlineData(0.0, true, 180.0)]
    [InlineData(0.1, false, 0.0)]
    [InlineData(0.1, true, 0.0)]
    [InlineData(0.1, false, 37.0)]
    [InlineData(0.1, true, 37.0)]
    [InlineData(0.1, false, 180.0)]
    [InlineData(0.1, true, 180.0)]
    [InlineData(-0.1, false, 0.0)]
    [InlineData(-0.1, true, 0.0)]
    [InlineData(-0.1, false, 37.0)]
    [InlineData(-0.1, true, 37.0)]
    [InlineData(-0.1, false, 180.0)]
    [InlineData(-0.1, true, 180.0)]
    public void AltAzRoundTrip_OnGrid_ReturnsSameValues(double k2, bool mirrored, double rotationDeg)
    {
        var model = Model(k2, mirrored, rotationDeg);

        for (double alt = 5.0; alt <= 89.0; alt += 2.0)
        {
            for (double az = 0.0; az < 360.0; az += 3.0)
            {
                Assert.True(model.TryAltAzToPixel(alt, az, out double x, out double y),
                    $"Proiezione rifiutata per alt={alt}, az={az}.");
                Assert.True(model.TryPixelToAltAz(x, y, out double altBack, out double azBack),
                    $"Inversione rifiutata per alt={alt}, az={az}.");

                Assert.True(Math.Abs(alt - altBack) < RoundTripToleranceDeg,
                    $"Errore in altezza {alt - altBack} gradi per alt={alt}, az={az}.");
                Assert.True(AngleDiff(az, azBack) < RoundTripToleranceDeg,
                    $"Errore in azimut {AngleDiff(az, azBack)} gradi per alt={alt}, az={az}.");
            }
        }
    }

    [Theory]
    [InlineData(0.0, false, 0.0)]
    [InlineData(0.0, true, 37.0)]
    [InlineData(0.1, false, 180.0)]
    [InlineData(-0.1, true, 0.0)]
    public void PixelRoundTrip_InsideCircle_ReturnsSamePixel(double k2, bool mirrored, double rotationDeg)
    {
        var model = Model(k2, mirrored, rotationDeg);

        for (double x = CenterX - 470.0; x <= CenterX + 470.0; x += 47.0)
        {
            for (double y = CenterY - 470.0; y <= CenterY + 470.0; y += 47.0)
            {
                if (!model.IsInsideSky(x, y))
                {
                    continue;
                }

                Assert.True(model.TryPixelToAltAz(x, y, out double alt, out double az),
                    $"Inversione rifiutata per il pixel ({x}, {y}).");
                Assert.True(model.TryAltAzToPixel(alt, az, out double xBack, out double yBack),
                    $"Proiezione rifiutata per alt={alt}, az={az}.");

                double err = Math.Sqrt((x - xBack) * (x - xBack) + (y - yBack) * (y - yBack));
                Assert.True(err < RoundTripTolerancePx,
                    $"Errore di {err} px per il pixel ({x}, {y}).");
            }
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(-0.1)]
    public void Zenith_IsAtCenter(double k2)
    {
        var model = Model(k2, mirrored: false, rotationDeg: 37.0);

        Assert.True(model.TryAltAzToPixel(90.0, 123.0, out double x, out double y));

        Assert.Equal(CenterX, x, ExactTolerance);
        Assert.Equal(CenterY, y, ExactTolerance);
    }

    [Fact]
    public void CenterPixel_IsZenith()
    {
        var model = Model(0.1, mirrored: false, rotationDeg: 0.0);

        Assert.True(model.TryPixelToAltAz(CenterX, CenterY, out double alt, out _));

        Assert.Equal(90.0, alt, ExactTolerance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(-0.1)]
    public void Horizon_IsOnBorder(double k2)
    {
        var model = Model(k2, mirrored: false, rotationDeg: 0.0);
        double horizon = model.HorizonRadius;

        Assert.True(model.TryAltAzToPixel(0.0, 0.0, out double x, out double y));

        double dist = Math.Sqrt((x - CenterX) * (x - CenterX) + (y - CenterY) * (y - CenterY));
        Assert.Equal(horizon, dist, 1e-6);
        Assert.True(model.IsInsideSky(x, y), "Il punto sull'orizzonte deve essere sul bordo (dentro).");
        Assert.False(model.IsInsideSky(CenterX, CenterY - horizon * 1.001),
            "Un punto appena oltre l'orizzonte non deve essere dentro il cielo.");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(-0.1)]
    public void ScaleRadius_IsRadiusAtZenithDistance90_OnlyWithK2Zero(double k2)
    {
        // Con K2 = 0 lo scale radius coincide con il raggio a z = 90 gradi; con K2 != 0 no.
        var model = Model(k2, mirrored: false, rotationDeg: 0.0);

        Assert.True(model.TryAltAzToPixel(0.0, 0.0, out _, out double y));
        double distance = Math.Abs(y - CenterY);
        Assert.Equal(model.HorizonRadius, distance, 1e-6);
        if (k2 == 0.0)
        {
            Assert.Equal(ScaleRadiusPx, distance, 1e-6);
        }
        else
        {
            Assert.NotEqual(ScaleRadiusPx, distance);
        }
    }

    [Fact]
    public void HorizonRadius_IncludesK2()
    {
        var model = Model(0.1, mirrored: false, rotationDeg: 0.0);

        Assert.Equal(ScaleRadiusPx * 1.1, model.HorizonRadius, ExactTolerance);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.0)]
    [InlineData(0.1)]
    public void NegativeAltitude_ReturnsFalse(double k2)
    {
        var model = Model(k2, mirrored: false, rotationDeg: 0.0);

        Assert.False(model.TryAltAzToPixel(-0.5, 0.0, out _, out _));
        Assert.False(model.TryAltAzToPixel(-90.0, 200.0, out _, out _));
    }

    [Fact]
    public void PointsOutsideCircle_ReturnFalse()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 0.0);

        Assert.False(model.TryPixelToAltAz(CenterX + ScaleRadiusPx + 1.0, CenterY, out _, out _));
        Assert.False(model.TryPixelToAltAz(CenterX, CenterY - ScaleRadiusPx - 0.5, out _, out _));
        Assert.False(model.IsInsideSky(CenterX + ScaleRadiusPx + 1.0, CenterY));
        Assert.True(model.TryPixelToAltAz(CenterX + ScaleRadiusPx - 1.0, CenterY, out _, out _));
    }

    [Fact]
    public void East_IsOnLeft_WithBaseConvention()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 0.0);

        Assert.True(model.TryAltAzToPixel(45.0, 90.0, out double x, out double y));

        Assert.True(x < CenterX, $"Con la convenzione base l'Est deve stare a sinistra (x={x}).");
        Assert.Equal(CenterY, y, ExactTolerance);
    }

    [Fact]
    public void North_IsUp_WithBaseConvention()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 0.0);

        Assert.True(model.TryAltAzToPixel(45.0, 0.0, out double x, out double y));

        Assert.True(y < CenterY, $"Il Nord deve stare in alto (y={y}).");
        Assert.Equal(CenterX, x, ExactTolerance);
    }

    [Fact]
    public void Mirrored_PutsEastOnRight()
    {
        var model = Model(0.0, mirrored: true, rotationDeg: 0.0);

        Assert.True(model.TryAltAzToPixel(45.0, 90.0, out double x, out double y));

        Assert.True(x > CenterX, $"Con Mirrored l'Est deve stare a destra (x={x}).");
        Assert.Equal(CenterY, y, ExactTolerance);
    }

    [Fact]
    public void Rotation90_PutsNorthOnRight()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 90.0);

        Assert.True(model.TryAltAzToPixel(45.0, 0.0, out double x, out double y));

        Assert.True(x > CenterX, $"Con rotazione di 90 gradi il Nord deve stare a destra (x={x}).");
        Assert.Equal(CenterY, y, ExactTolerance);
    }

    [Fact]
    public void Rotation180_PutsNorthDown()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 180.0);

        Assert.True(model.TryAltAzToPixel(45.0, 0.0, out double x, out double y));

        Assert.True(y > CenterY, $"Con rotazione di 180 gradi il Nord deve stare in basso (y={y}).");
        Assert.Equal(CenterX, x, ExactTolerance);
    }

    [Fact]
    public void NaN_IsRejectedInConstructor()
    {
        var ex1 = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(double.NaN, CenterY, ScaleRadiusPx, 0.0, false));
        Assert.Contains("NaN", ex1.Message);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(CenterX, CenterY, ScaleRadiusPx, double.PositiveInfinity, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(CenterX, CenterY, ScaleRadiusPx, 0.0, false, double.NaN));
    }

    [Fact]
    public void NonPositiveRadius_IsRejectedInConstructor()
    {
        var zero = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(CenterX, CenterY, 0.0, 0.0, false));
        Assert.Contains("0", zero.Message);

        var negative = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(CenterX, CenterY, -12.5, 0.0, false));
        Assert.Contains("-12.5", negative.Message);
    }

    [Fact]
    public void K2AtOrBelowLimit_IsRejectedInConstructor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FisheyeModel(CenterX, CenterY, ScaleRadiusPx, 0.0, false, -1.0 / 3.0));
    }

    [Fact]
    public void NonFiniteInput_ReturnsFalse()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 0.0);

        Assert.False(model.TryAltAzToPixel(double.NaN, 0.0, out _, out _));
        Assert.False(model.TryAltAzToPixel(45.0, double.PositiveInfinity, out _, out _));
        Assert.False(model.TryPixelToAltAz(double.NaN, CenterY, out _, out _));
        Assert.False(model.IsInsideSky(CenterX, double.NaN));
    }

    [Fact]
    public void PixelScale_AtZenithAndHorizon_MatchesDerivative()
    {
        // Equidistante: 90 gradi su R = 480 px, quindi 0.1875 gradi per pixel = 675 arcsec/px, costante.
        var equidistant = Model(0.0, mirrored: false, rotationDeg: 0.0);
        Assert.Equal(675.0, equidistant.PixelScaleArcsecPerPx(90.0), 1e-9);
        Assert.Equal(675.0, equidistant.PixelScaleArcsecPerPx(0.0), 1e-9);

        // Con K2 = 0.1 all'orizzonte (t = 1) dr/dz = R * (1 + 0.3) / 90.
        var corrected = Model(0.1, mirrored: false, rotationDeg: 0.0);
        Assert.Equal(324000.0 / (ScaleRadiusPx * 1.3), corrected.PixelScaleArcsecPerPx(0.0), 1e-9);
    }

    [Fact]
    public void PixelScale_AgreesWithFiniteDifference()
    {
        var model = Model(0.1, mirrored: false, rotationDeg: 0.0);
        const double alt = 45.0;
        const double step = 0.001;

        Assert.True(model.TryAltAzToPixel(alt, 0.0, out double x1, out double y1));
        Assert.True(model.TryAltAzToPixel(alt + step, 0.0, out double x2, out double y2));

        double dr = Math.Sqrt((x2 - CenterX) * (x2 - CenterX) + (y2 - CenterY) * (y2 - CenterY))
                  - Math.Sqrt((x1 - CenterX) * (x1 - CenterX) + (y1 - CenterY) * (y1 - CenterY));
        double finiteDiff = step * 3600.0 / Math.Abs(dr);

        Assert.Equal(finiteDiff, model.PixelScaleArcsecPerPx(alt), finiteDiff * 1e-4);
    }

    [Fact]
    public void PixelScale_OutOfRangeAltitude_Throws()
    {
        var model = Model(0.0, mirrored: false, rotationDeg: 0.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => model.PixelScaleArcsecPerPx(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.PixelScaleArcsecPerPx(90.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.PixelScaleArcsecPerPx(double.NaN));
    }

    // Differenza minima tra due azimut in gradi, con gestione del passaggio 359 -> 0.
    private static double AngleDiff(double a, double b)
    {
        double d = Math.Abs(a - b) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }
}
