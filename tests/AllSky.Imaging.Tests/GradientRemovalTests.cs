using Xunit;

namespace AllSky.Imaging.Tests;

public class GradientRemovalTests
{
    private const int W = 200;
    private const int H = 160;

    // Gradiente lineare noto per canale (0 = R, 1 = G, 2 = B), senza rumore.
    private static double Ramp(int c, int x, int y) => c switch
    {
        0 => 1000.0 + 4.0 * x + 2.0 * y,
        1 => 2000.0 + 3.0 * x - 2.0 * y,
        _ => 500.0 + 6.0 * x + 1.0 * y,
    };

    // Rampa lineare piu' rumore deterministico (+-3) e alcune stelle (pixel molto luminosi).
    private static RgbImage MakeRampWithStars(out bool[] star, out (int X, int Y)[] stars)
    {
        int n = W * H;
        var r = new float[n];
        var g = new float[n];
        var b = new float[n];
        star = new bool[n];
        uint seed = 12345u;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                seed = seed * 1103515245u + 12345u;
                float noise = (float)(int)((seed >> 16) & 7u) - 3f;
                int i = y * W + x;
                r[i] = (float)Ramp(0, x, y) + noise;
                g[i] = (float)Ramp(1, x, y) + noise;
                b[i] = (float)Ramp(2, x, y) + noise;
            }
        }

        stars = new[] { (20, 30), (100, 80), (150, 120), (60, 140), (180, 20), (45, 90) };
        foreach (var (sx, sy) in stars)
        {
            int i = sy * W + sx;
            star[i] = true;
            r[i] = 50000f;
            g[i] = 50000f;
            b[i] = 50000f;
        }

        return new RgbImage(W, H, r, g, b);
    }

    private static RgbImage Copy(RgbImage img)
        => new(img.Width, img.Height, (float[])img.R.Clone(), (float[])img.G.Clone(), (float[])img.B.Clone());

    private static float[] PlaneOf(RgbImage img, int c) => c switch
    {
        0 => img.R,
        1 => img.G,
        _ => img.B,
    };

    // Media e deviazione standard dei pixel non esclusi.
    private static (double Mean, double Std) Stats(float[] plane, bool[] exclude)
    {
        double sum = 0.0;
        double sum2 = 0.0;
        int k = 0;
        for (int i = 0; i < plane.Length; i++)
        {
            if (exclude[i])
            {
                continue;
            }

            sum += plane[i];
            sum2 += (double)plane[i] * plane[i];
            k++;
        }

        double mean = sum / k;
        return (mean, Math.Sqrt(Math.Max(0.0, sum2 / k - mean * mean)));
    }

    [Fact]
    public void Apply_FlattensLinearRamp_KeepsMeanAndPreservesStars()
    {
        RgbImage img = MakeRampWithStars(out bool[] star, out var stars);
        SkyMask mask = SkyMask.Full(W, H);
        RgbImage before = Copy(img);

        GradientModel model = GradientRemoval.Fit(img, mask);
        GradientRemoval.Apply(img, model, 1f);

        for (int c = 0; c < 3; c++)
        {
            (double mean0, double std0) = Stats(PlaneOf(before, c), star);
            (double mean1, double std1) = Stats(PlaneOf(img, c), star);

            Assert.True(std1 * 10.0 < std0, $"canale {c}: std {std0} -> {std1}, attesa riduzione > 10x");
            TestMath.AssertNear(mean0, mean1, mean0 * 0.005);
        }

        // Le stelle non deformano il fit: il modello sulla stella coincide con la rampa.
        foreach (var (sx, sy) in stars)
        {
            for (int c = 0; c < 3; c++)
            {
                TestMath.AssertNear(Ramp(c, sx, sy), model.EvaluateAt(c, sx, sy), 5.0);
            }

            // La stella resta luminosa: cambia solo della parte di gradiente rimossa (pochi percento).
            int i = sy * W + sx;
            double original = before.R[i];
            Assert.True(Math.Abs(img.R[i] - original) < 0.02 * original);
            Assert.True(img.R[i] > 45000f);
        }
    }

    [Fact]
    public void Apply_WithStrengthZero_LeavesImageIdentical()
    {
        RgbImage img = MakeRampWithStars(out _, out _);
        RgbImage before = Copy(img);
        GradientModel model = GradientRemoval.Fit(img, SkyMask.Full(W, H));

        GradientRemoval.Apply(img, model, 0f);

        Assert.Equal(before.R, img.R);
        Assert.Equal(before.G, img.G);
        Assert.Equal(before.B, img.B);
    }

    [Fact]
    public void Apply_RejectsStrengthOutsideUnitInterval()
    {
        RgbImage img = MakeRampWithStars(out _, out _);
        GradientModel model = GradientRemoval.Fit(img, SkyMask.Full(W, H));

        Assert.Throws<ArgumentOutOfRangeException>(() => GradientRemoval.Apply(img, model, 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientRemoval.Apply(img, model, -0.1f));
    }

    [Fact]
    public void Fit_WithTooFewSkyPixels_Throws()
    {
        RgbImage img = MakeRampWithStars(out _, out _);
        // Raggio minimo: resta solo il pixel centrale, troppo poco per una finestra 8x8 con 8 pixel di cielo.
        SkyMask tiny = SkyMask.Create(W, H, new SkyMaskOptions(0.5, 0.5, 0.01));

        Assert.Throws<InvalidOperationException>(() => GradientRemoval.Fit(img, tiny));
    }
}
