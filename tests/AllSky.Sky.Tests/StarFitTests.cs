using Xunit;

namespace AllSky.Sky.Tests;

// Raffinamento della calibrazione con stelle sintetiche (P9.1). Le stelle sono generate dal modello vero su una griglia
// deterministica; il rumore è una sinusoide di ampiezza nota, senza generatori casuali. Nessun catalogo reale è usato qui.
public class StarFitTests
{
    private static readonly FisheyeModel Truth = new(960.0, 540.0, 500.0, 0.0, false, 0.02);

    /// <summary>Stelle sintetiche sopra l'orizzonte, con altezza e azimut su una griglia deterministica.</summary>
    private static IReadOnlyList<(double AltDeg, double AzDeg)> Directions(int count)
    {
        var list = new List<(double, double)>();
        for (int i = 0; i < count; i++)
        {
            double alt = 15.0 + (i * 37 % 70);
            double az = i * 97 % 360;
            list.Add((alt, az));
        }

        return list;
    }

    private static List<StarObservation> Observe(FisheyeModel model, IReadOnlyList<(double AltDeg, double AzDeg)> dirs, double noisePx)
    {
        var stars = new List<StarObservation>();
        for (int i = 0; i < dirs.Count; i++)
        {
            Assert.True(model.TryAltAzToPixel(dirs[i].AltDeg, dirs[i].AzDeg, out double x, out double y));
            double nx = noisePx * Math.Sin(i * 1.7);
            double ny = noisePx * Math.Cos(i * 2.3);
            stars.Add(new StarObservation(x + nx, y + ny, dirs[i].AltDeg, dirs[i].AzDeg));
        }

        return stars;
    }

    [Fact]
    public void Fit_NoiseFree_RecoversTheTruthExactly()
    {
        var stars = Observe(Truth, Directions(60), noisePx: 0.0);
        var start = Truth with { CenterX = 975.0, CenterY = 528.0, ScaleRadiusPx = 470.0, K2 = 0.0 };

        StarFitResult result = StarFit.Fit(stars, start);

        Assert.InRange(result.Model.CenterX, Truth.CenterX - 1e-4, Truth.CenterX + 1e-4);
        Assert.InRange(result.Model.CenterY, Truth.CenterY - 1e-4, Truth.CenterY + 1e-4);
        Assert.InRange(result.Model.ScaleRadiusPx, Truth.ScaleRadiusPx - 1e-3, Truth.ScaleRadiusPx + 1e-3);
        Assert.InRange(result.Model.K2, Truth.K2 - 1e-6, Truth.K2 + 1e-6);
        Assert.True(result.RmsPx < 1e-6, $"RMS {result.RmsPx}");
    }

    [Fact]
    public void Fit_WithSubPixelNoise_StaysWithinTolerance()
    {
        var stars = Observe(Truth, Directions(60), noisePx: 0.2);
        var start = Truth with { CenterX = 975.0, CenterY = 528.0, ScaleRadiusPx = 470.0, K2 = 0.0 };

        StarFitResult result = StarFit.Fit(stars, start);

        Assert.InRange(result.Model.CenterX, Truth.CenterX - 0.5, Truth.CenterX + 0.5);
        Assert.InRange(result.Model.CenterY, Truth.CenterY - 0.5, Truth.CenterY + 0.5);
        Assert.InRange(result.Model.ScaleRadiusPx, Truth.ScaleRadiusPx - 2.0, Truth.ScaleRadiusPx + 2.0);
        Assert.InRange(result.Model.K2, Truth.K2 - 0.005, Truth.K2 + 0.005);
        Assert.True(result.RmsPx < 0.3, $"RMS {result.RmsPx}");
    }

    [Fact]
    public void Fit_TooFewStars_Throws()
    {
        var stars = Observe(Truth, Directions(3), noisePx: 0.0);

        Assert.Throws<ArgumentException>(() => StarFit.Fit(stars, Truth));
    }

    [Fact]
    public void Fit_StarsBelowHorizon_AreIgnored()
    {
        var stars = Observe(Truth, Directions(60), noisePx: 0.0).ToList();
        // Una stella sotto l'orizzonte non ha pixel: non deve far fallire il fit né entrare nel calcolo.
        stars.Add(new StarObservation(0.0, 0.0, -5.0, 10.0));

        StarFitResult result = StarFit.Fit(stars, Truth with { CenterX = 975.0 });

        Assert.InRange(result.Model.CenterX, Truth.CenterX - 1e-3, Truth.CenterX + 1e-3);
    }

    [Fact]
    public void Identify_MatchesEachCataloguedStarToTheNearestDetection()
    {
        var catalogue = Directions(40);
        var detections = new List<(double X, double Y)>();
        foreach (var (alt, az) in catalogue)
        {
            Assert.True(Truth.TryAltAzToPixel(alt, az, out double x, out double y));
            detections.Add((x + 0.3, y - 0.2));
        }

        // Rumore di fondo lontano dalle stelle: non deve essere abbinato.
        detections.Add((5.0, 5.0));
        detections.Add((1900.0, 1070.0));

        IReadOnlyList<StarObservation> matches = StarFit.Identify(Truth, catalogue, detections, maxDistancePx: 2.0);

        Assert.Equal(catalogue.Count, matches.Count);
        foreach (StarObservation m in matches)
        {
            Assert.True(Truth.TryAltAzToPixel(m.AltDeg, m.AzDeg, out double px, out double py));
            Assert.True(Math.Abs(m.X - px) < 0.5 && Math.Abs(m.Y - py) < 0.5);
        }
    }

    [Fact]
    public void Identify_RejectsNonPositiveDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StarFit.Identify(Truth, Directions(4), Array.Empty<(double, double)>(), maxDistancePx: 0.0));
    }
}
