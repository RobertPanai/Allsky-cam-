using AllSky.Sky.Calibration;
using Xunit;

namespace AllSky.Sky.Tests;

// Fit del modello fisheye dalle posizioni del Sole. I punti sono sintetici: generati dal modello vero, con rumore gaussiano
// deterministico (seme fisso) e, dove richiesto, outlier. Le tolleranze sono quelle del task P3.2.
public class SunCalibrationCollectorTests
{
    private const double CenterTolerancePx = 1.0;
    private const double RadiusTolerance = 0.005;
    private const double RotationToleranceDeg = 0.5;
    private const double NoisePx = 0.5;
    private const int Seed = 20261022;

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    // Punti sintetici con azimut distribuiti uniformemente (piu' jitter) e altezze variabili tra 12 e 80 gradi.
    // I primi `outliers` punti hanno il Sole spostato di 40 px: rappresentano rilevamenti sbagliati.
    private static SunCalibrationCollector BuildCollector(
        FisheyeModel truth, int count, double noisePx, int outliers = 0, int seed = Seed, double altMin = 12.0, double altMax = 80.0)
    {
        var rng = new Random(seed);
        var collector = new SunCalibrationCollector();
        for (int i = 0; i < count; i++)
        {
            double az = ((i + 0.5 + (rng.NextDouble() - 0.5) * 0.6) * 360.0 / count + 360.0) % 360.0;
            double alt = altMin + rng.NextDouble() * (altMax - altMin);
            Assert.True(truth.TryAltAzToPixel(alt, az, out double px, out double py));

            px += noisePx * Gaussian(rng);
            py += noisePx * Gaussian(rng);
            if (i < outliers)
            {
                px += 40.0;
                py -= 40.0;
            }

            Assert.True(collector.Add(Start.AddMinutes(10.0 * i), px, py, alt, az),
                $"Punto {i} rifiutato dal collector.");
        }
        return collector;
    }

    // Box-Muller su un generatore con seme: deterministico.
    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double AngleDiff(double a, double b)
    {
        double d = Math.Abs(a - b) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }

    [Theory]
    [InlineData(602.3, 498.1, 470.0, 12.5, false, 0.0)]
    [InlineData(597.8, 503.4, 482.0, -35.0, true, 0.0)]
    [InlineData(600.4, 499.6, 465.0, 120.0, true, 0.01)]
    [InlineData(605.0, 495.0, 478.0, 200.0, false, -0.01)]
    public void Fit_RecoversModelWithNoise_AndPicksMirror(
        double cx, double cy, double scale, double rotation, bool mirrored, double k2)
    {
        var truth = new FisheyeModel(cx, cy, scale, rotation, mirrored, k2);
        SunCalibrationCollector collector = BuildCollector(truth, count: 30, noisePx: NoisePx);

        Assert.True(collector.TryFit(out SkyCalibrationResult? result), collector.LastFailureReason);
        Assert.NotNull(result);
        FisheyeModel fit = result!.Model;

        Assert.Equal(mirrored, fit.Mirrored);
        Assert.True(Math.Abs(fit.CenterX - cx) < CenterTolerancePx, $"CenterX {fit.CenterX} vs {cx}.");
        Assert.True(Math.Abs(fit.CenterY - cy) < CenterTolerancePx, $"CenterY {fit.CenterY} vs {cy}.");
        Assert.True(Math.Abs(fit.ScaleRadiusPx / scale - 1.0) < RadiusTolerance,
            $"ScaleRadiusPx {fit.ScaleRadiusPx} vs {scale}.");
        Assert.True(Math.Abs(fit.HorizonRadius / truth.HorizonRadius - 1.0) < RadiusTolerance,
            $"HorizonRadius {fit.HorizonRadius} vs {truth.HorizonRadius}.");
        Assert.True(AngleDiff(fit.NorthRotationDeg, rotation) < RotationToleranceDeg,
            $"Rotazione {fit.NorthRotationDeg} vs {rotation}.");
        Assert.True(result.Accepted, $"RMS frazionario {result.RmsFractionOfRadius} non accettato.");
        Assert.Equal(30, result.PointsUsed + result.OutliersRejected);
    }

    [Fact]
    public void Fit_WithThreeOutliers_StillWithinTolerance()
    {
        var truth = new FisheyeModel(601.0, 499.0, 472.0, 45.0, Mirrored: false, K2: 0.0);
        SunCalibrationCollector collector = BuildCollector(truth, count: 30, noisePx: NoisePx, outliers: 3);

        Assert.True(collector.TryFit(out SkyCalibrationResult? result), collector.LastFailureReason);
        FisheyeModel fit = result!.Model;

        Assert.True(result.OutliersRejected >= 3, $"Outlier rigettati: {result.OutliersRejected}.");
        Assert.True(Math.Abs(fit.CenterX - 601.0) < CenterTolerancePx);
        Assert.True(Math.Abs(fit.CenterY - 499.0) < CenterTolerancePx);
        Assert.True(Math.Abs(fit.ScaleRadiusPx / 472.0 - 1.0) < RadiusTolerance);
        Assert.True(AngleDiff(fit.NorthRotationDeg, 45.0) < RotationToleranceDeg);
        Assert.True(result.Accepted);
    }

    [Fact]
    public void Fit_WithFewPoints_KeepsK2Zero()
    {
        var truth = new FisheyeModel(600.0, 500.0, 480.0, 10.0, Mirrored: false, K2: 0.0);
        SunCalibrationCollector collector = BuildCollector(truth, count: 8, noisePx: NoisePx);

        Assert.True(collector.TryFit(out SkyCalibrationResult? result), collector.LastFailureReason);

        Assert.Equal(0.0, result!.Model.K2, 1e-12);
    }

    [Fact]
    public void Fit_WithLargeNoise_ReturnsModelButNotAccepted()
    {
        var truth = new FisheyeModel(600.0, 500.0, 480.0, 10.0, Mirrored: false, K2: 0.0);
        SunCalibrationCollector collector = BuildCollector(truth, count: 20, noisePx: 25.0);

        Assert.True(collector.TryFit(out SkyCalibrationResult? result), collector.LastFailureReason);

        Assert.False(result!.Accepted);
        Assert.True(result.RmsFractionOfRadius >= 0.01);
    }

    [Fact]
    public void Fit_FewerThanSixPoints_ReturnsFalseWithReason()
    {
        var truth = new FisheyeModel(600.0, 500.0, 480.0, 10.0, Mirrored: false, K2: 0.0);
        SunCalibrationCollector collector = BuildCollector(truth, count: 5, noisePx: NoisePx);

        Assert.False(collector.TryFit(out SkyCalibrationResult? result));
        Assert.Null(result);
        Assert.Contains("insufficienti", collector.LastFailureReason!);
    }

    [Fact]
    public void Fit_InsufficientAzimuthCoverage_ReturnsFalseWithReason()
    {
        // Azimut tra 0 e 27 gradi: spread 27, sotto il minimo di 60.
        var truth = new FisheyeModel(600.0, 500.0, 480.0, 10.0, Mirrored: false, K2: 0.0);
        var collector = new SunCalibrationCollector();
        for (int i = 0; i < 10; i++)
        {
            double alt = 20.0 + 5.0 * i;
            double az = 3.0 * i;
            Assert.True(truth.TryAltAzToPixel(alt, az, out double px, out double py));
            Assert.True(collector.Add(Start.AddMinutes(10.0 * i), px, py, alt, az));
        }

        Assert.False(collector.TryFit(out SkyCalibrationResult? result));
        Assert.Null(result);
        Assert.Contains("azimutale", collector.LastFailureReason!);
    }

    [Fact]
    public void Fit_InsufficientAltitudeCoverage_ReturnsFalseWithReason()
    {
        // Altezze tra 40 e 46 gradi: spread 6, sotto il minimo di 15. Azimut ben distribuiti.
        var truth = new FisheyeModel(600.0, 500.0, 480.0, 10.0, Mirrored: false, K2: 0.0);
        var collector = new SunCalibrationCollector();
        for (int i = 0; i < 10; i++)
        {
            double alt = 40.0 + 0.6 * i;
            double az = 36.0 * i;
            Assert.True(truth.TryAltAzToPixel(alt, az, out double px, out double py));
            Assert.True(collector.Add(Start.AddMinutes(10.0 * i), px, py, alt, az));
        }

        Assert.False(collector.TryFit(out SkyCalibrationResult? result));
        Assert.Null(result);
        Assert.Contains("altezza", collector.LastFailureReason!);
    }

    [Fact]
    public void Add_RejectsAltitudeBelowTenDegrees()
    {
        var collector = new SunCalibrationCollector();

        Assert.False(collector.Add(Start, 600, 500, 9.9, 0.0));
        Assert.True(collector.Add(Start, 600, 500, 10.0, 0.0));
        Assert.Equal(1, collector.Count);
    }

    [Fact]
    public void Add_RejectsNearDuplicates_AcceptsFiveMinutesApart()
    {
        var collector = new SunCalibrationCollector();

        Assert.True(collector.Add(Start, 600, 500, 30.0, 0.0));
        Assert.False(collector.Add(Start.AddSeconds(299), 601, 500, 31.0, 10.0), "Meno di 5 minuti: deve essere scartato.");
        Assert.True(collector.Add(Start.AddMinutes(5), 602, 500, 32.0, 20.0), "Esattamente 5 minuti: accettato.");
        Assert.Equal(2, collector.Count);
    }

    [Fact]
    public void Add_RejectsNonFiniteValues()
    {
        var collector = new SunCalibrationCollector();

        Assert.False(collector.Add(Start, double.NaN, 500, 30.0, 0.0));
        Assert.False(collector.Add(Start, 600, double.PositiveInfinity, 30.0, 0.0));
        Assert.False(collector.Add(Start, 600, 500, 30.0, double.NaN));
        Assert.Equal(0, collector.Count);
    }

    [Fact]
    public void TryFit_WithoutPoints_ReturnsFalse()
    {
        var collector = new SunCalibrationCollector();

        Assert.False(collector.TryFit(out _));
        Assert.NotNull(collector.LastFailureReason);
    }
}
