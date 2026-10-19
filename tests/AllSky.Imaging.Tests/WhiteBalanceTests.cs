using Xunit;

namespace AllSky.Imaging.Tests;

public class WhiteBalanceTests
{
    private static RgbImage Fill(int w, int h, float r, float g, float b)
    {
        int n = w * h;
        var rr = new float[n];
        var gg = new float[n];
        var bb = new float[n];
        Array.Fill(rr, r);
        Array.Fill(gg, g);
        Array.Fill(bb, b);
        return new RgbImage(w, h, rr, gg, bb);
    }

    [Fact]
    public void NightGains_ConstantBackground_MatchesHandComputed()
    {
        // R = 800 = 0.8 G, G = 1000, B = 1300 = 1.3 G.
        // Guadagni: R = 1000/800 = 1.25, G = 1, B = 1000/1300 = 0.76923.
        RgbImage img = Fill(10, 10, 800f, 1000f, 1300f);

        var (gr, gg, gb) = WhiteBalance.EstimateNightGains(img, null);

        TestMath.AssertNear(1.25, gr, 1e-3);
        TestMath.AssertNear(1.0, gg, 1e-3);
        TestMath.AssertNear(0.76923, gb, 1e-3);
    }

    [Fact]
    public void NightGains_IgnoreOnePercentBrightStars()
    {
        // 100x100 = 10000 pixel. I primi 100 (1%) sono stelle: R = 48000, G = 60000, B = 78000.
        // Gli altri 9900 hanno fondo R = 800, G = 1000, B = 1300.
        // Mediana di R: posizioni 4999 e 5000 valgono 800, quindi medR = 800.
        // MAD = 0 (9900 deviazioni nulle), soglia 0: le stelle vengono scartate.
        // Attesi (1.25, 1, 0.76923), con e senza maschera che esclude le stelle.
        const int w = 100;
        const int h = 100;
        RgbImage img = Fill(w, h, 800f, 1000f, 1300f);
        for (int i = 0; i < 100; i++)
        {
            img.R[i] = 48000f;
            img.G[i] = 60000f;
            img.B[i] = 78000f;
        }

        var (gr, gg, gb) = WhiteBalance.EstimateNightGains(img, null);
        TestMath.AssertNear(1.25, gr, 1e-3);
        TestMath.AssertNear(1.0, gg, 1e-3);
        TestMath.AssertNear(0.76923, gb, 1e-3);

        bool[] mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = i >= 100;
        }

        var (mr, mg, mb) = WhiteBalance.EstimateNightGains(img, mask);
        TestMath.AssertNear(1.25, mr, 1e-3);
        TestMath.AssertNear(1.0, mg, 1e-3);
        TestMath.AssertNear(0.76923, mb, 1e-3);
    }

    [Fact]
    public void DayGains_ExcludeSaturatedPixels()
    {
        // 99 pixel con R = 1000, G = 2000, B = 500; il pixel 0 e' saturo (65535 ovunque).
        // Escluso il saturo: mR = 1000, mG = 2000, mB = 500, quindi gR = 2, gG = 1, gB = 4.
        // Se fosse incluso, mR = (99000 + 65535) / 100 = 1645.35 e il test fallirebbe.
        RgbImage img = Fill(10, 10, 1000f, 2000f, 500f);
        img.R[0] = 65535f;
        img.G[0] = 65535f;
        img.B[0] = 65535f;

        var (gr, gg, gb) = WhiteBalance.EstimateDayGains(img);

        TestMath.AssertNear(2.0, gr, 1e-6);
        TestMath.AssertNear(1.0, gg, 1e-6);
        TestMath.AssertNear(4.0, gb, 1e-6);
    }

    [Fact]
    public void Blend_InterpolatesLinearly()
    {
        var a = (1.0, 2.0, 3.0);
        var b = (3.0, 4.0, 5.0);

        var half = WhiteBalance.Blend(a, b, 0.5);
        TestMath.AssertNear(2.0, half.R, 1e-9);
        TestMath.AssertNear(3.0, half.G, 1e-9);
        TestMath.AssertNear(4.0, half.B, 1e-9);

        var zero = WhiteBalance.Blend(a, b, 0.0);
        TestMath.AssertNear(1.0, zero.R, 1e-9);

        var one = WhiteBalance.Blend(a, b, 1.0);
        TestMath.AssertNear(3.0, one.R, 1e-9);
        TestMath.AssertNear(5.0, one.B, 1e-9);
    }

    [Fact]
    public void Smoother_AlphaHalf_FollowsExponentialAverage()
    {
        // alpha = 0.5 e' il peso del nuovo campione.
        var s = new Smoother(0.5);

        var o1 = s.Update((1.0, 1.0, 1.0));
        TestMath.AssertNear(1.0, o1.R, 1e-9);

        // 0.5 * 2 + 0.5 * 1 = 1.5
        var o2 = s.Update((2.0, 2.0, 2.0));
        TestMath.AssertNear(1.5, o2.R, 1e-9);

        // 0.5 * 2 + 0.5 * 1.5 = 1.75
        var o3 = s.Update((2.0, 2.0, 2.0));
        TestMath.AssertNear(1.75, o3.G, 1e-9);
    }

    [Fact]
    public void Apply_ScalesAndClamps()
    {
        // R: 60000 * 2 = 120000 -> clamp 65535. G: 1000 * 1 = 1000. B: 100 * 0.5 = 50.
        RgbImage img = Fill(1, 1, 60000f, 1000f, 100f);

        WhiteBalance.Apply(img, (2.0, 1.0, 0.5));

        TestMath.AssertNear(65535.0, img.R[0], 1e-3);
        TestMath.AssertNear(1000.0, img.G[0], 1e-3);
        TestMath.AssertNear(50.0, img.B[0], 1e-3);
    }

    [Fact]
    public void NightGains_LargeConstantImage_MatchesSmallImage()
    {
        // 500 x 500 = 250000 pixel, sopra 200000: la mediana usa un campione a passo fisso.
        // Valore costante, quindi lo stesso risultato del caso 10 x 10: (1.25, 1, 0.76923).
        RgbImage img = Fill(500, 500, 800f, 1000f, 1300f);

        var (gr, gg, gb) = WhiteBalance.EstimateNightGains(img, null);

        TestMath.AssertNear(1.25, gr, 1e-3);
        TestMath.AssertNear(1.0, gg, 1e-3);
        TestMath.AssertNear(0.76923, gb, 1e-3);
    }

    [Fact]
    public void DayGains_LargeConstantImage_MatchesSmallImage()
    {
        // 500 x 500 = 250000 pixel, sopra 200000. Medie costanti: mR = 1000, mG = 2000, mB = 500.
        // gR = 2000/1000 = 2, gG = 1, gB = 2000/500 = 4.
        RgbImage img = Fill(500, 500, 1000f, 2000f, 500f);

        var (gr, gg, gb) = WhiteBalance.EstimateDayGains(img);

        TestMath.AssertNear(2.0, gr, 1e-6);
        TestMath.AssertNear(1.0, gg, 1e-6);
        TestMath.AssertNear(4.0, gb, 1e-6);
    }
}
