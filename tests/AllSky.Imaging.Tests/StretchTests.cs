using Xunit;

namespace AllSky.Imaging.Tests;

public class StretchTests
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

    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.25f, 0.25f, 0.5f)]
    [InlineData(0.5f, 0.25f, 0.75f)]
    [InlineData(0.5f, 0.75f, 0.25f)]
    public void Mtf_MatchesHandComputedValues(float x, float midtone, float expected)
    {
        // (m-1)x / ((2m-1)x - m):
        // x=0.25, m=0.25: (-0.75*0.25) / (-0.5*0.25 - 0.25) = -0.1875 / -0.375 = 0.5
        // x=0.5,  m=0.25: (-0.75*0.5)  / (-0.5*0.5  - 0.25) = -0.375  / -0.5   = 0.75
        // x=0.5,  m=0.75: (-0.25*0.5)  / ( 0.5*0.5  - 0.75) = -0.125  / -0.5   = 0.25
        TestMath.AssertNear(expected, Stretch.Mtf(x, midtone), 1e-6);
    }

    [Fact]
    public void Mtf_Endpoints_AreZeroAndOne()
    {
        TestMath.AssertNear(0.0, Stretch.Mtf(0f, 0.3f), 1e-9);
        TestMath.AssertNear(1.0, Stretch.Mtf(1f, 0.3f), 1e-9);
    }

    [Fact]
    public void Asinh_EndpointsAndHalfValue()
    {
        // asinh(5) = 2.312439, asinh(10) = 2.998223, ratio = 0.771262
        TestMath.AssertNear(0.0, Stretch.Asinh(0f, 10f), 1e-9);
        TestMath.AssertNear(1.0, Stretch.Asinh(1f, 10f), 1e-6);
        TestMath.AssertNear(0.7713, Stretch.Asinh(0.5f, 10f), 1e-3);
    }

    [Fact]
    public void AutoMidtone_TargetEqualToMedian_GivesHalf()
    {
        // Mtf(x, 0.5) = x: con mediana 0.2 e target 0.2 il midtone e' 0.5.
        float[] sample = { 0.2f, 0.2f, 0.2f, 0.2f, 0.2f };

        float m = Stretch.AutoMidtone(sample, 0.2f);

        TestMath.AssertNear(0.5, m, 1e-3);
    }

    [Fact]
    public void AutoMidtone_MapsMedianToTarget()
    {
        // Mediana 0.05. Il midtone trovato deve portare Mtf(0.05, m) a 0.2.
        float[] sample = { 0.04f, 0.05f, 0.06f };

        float m = Stretch.AutoMidtone(sample, 0.2f);

        TestMath.AssertNear(0.2, Stretch.Mtf(0.05f, m), 1e-4);
    }

    [Fact]
    public void ToSrgb8_UniformGrey_ProducesEqualChannels()
    {
        // Canale piatto: punto nero 0, bianco 65535. Valore normalizzato 1000/65535 = 0.01526.
        // Il midtone automatico porta il valore stretchato a 0.2.
        // sRGB(0.2) = 1.055 * 0.2^(1/2.4) - 0.055 = 0.48465, x255 = 123.6, arrotondato 124.
        RgbImage img = Fill(4, 4, 1000f, 1000f, 1000f);

        byte[] rgb = Stretch.ToSrgb8(img, new StretchOptions());

        Assert.Equal(48, rgb.Length);
        Assert.True(rgb[0] > 0);
        Assert.InRange((int)rgb[0], 123, 125);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(rgb[3 * i], rgb[3 * i + 1]);
            Assert.Equal(rgb[3 * i], rgb[3 * i + 2]);
        }
    }

    [Fact]
    public void ToSrgb8_ConstantDifferentChannels_KeepsChannelOrder()
    {
        // Canali piatti 20000, 25000, 30000: tutti degenerati, quindi nero 0 e bianco comune 65535.
        // Normalizzati: R = 20000/65535 = 0.305176, G = 0.381470, B = 0.457764.
        // Midtone automatico dal verde, target 0.2: m = x(1-t)/(x(1-2t)+t) = 0.381470*0.8/(0.381470*0.6+0.2) = 0.711562.
        // Mtf(x, m) = (m-1)x / ((2m-1)x - m) con m = 0.711562:
        //   R: 0.151125 -> sRGB 0.425 -> 108
        //   G: 0.200000 -> sRGB 0.484647 -> 124
        //   B: 0.254936 -> sRGB 0.541992 -> 138
        // Tolleranza +-2 per gli arrotondamenti a mano.
        RgbImage img = Fill(4, 4, 20000f, 25000f, 30000f);

        byte[] rgb = Stretch.ToSrgb8(img, new StretchOptions());

        Assert.InRange((int)rgb[0], 106, 110);
        Assert.InRange((int)rgb[1], 122, 126);
        Assert.InRange((int)rgb[2], 136, 140);
        Assert.True(rgb[0] < rgb[1] && rgb[1] < rgb[2]);
    }

    [Fact]
    public void ToSrgb8_LinkedWhitePoint_UsesCommonWhite()
    {
        // Due pixel. Pixel 0: nero. Pixel 1: R = 30000, G = 30000, B = 60000.
        // Percentili 0 e 100 danno nero 0 per canale. Bianchi per canale: R 30000, G 30000, B 60000.
        // Bianco comune (linked) = 60000: R = G = 0.5, B = 1.0.
        // Midtone fisso 0.5 rende Mtf(x) = x. sRGB(0.5) = 0.735358, x255 = 187.5, arrotondato 188.
        // Non linked: ogni canale va a 1.0, quindi 255.
        var img = new RgbImage(
            2,
            1,
            new float[] { 0f, 30000f },
            new float[] { 0f, 30000f },
            new float[] { 0f, 60000f });
        var linked = new StretchOptions { BlackPercentile = 0.0, WhitePercentile = 100.0, Midtone = 0.5f };

        byte[] a = Stretch.ToSrgb8(img, linked);
        Assert.Equal(0, (int)a[0]);
        Assert.InRange((int)a[3], 187, 189);
        Assert.InRange((int)a[4], 187, 189);
        Assert.Equal(255, (int)a[5]);

        byte[] b = Stretch.ToSrgb8(img, linked with { LinkedWhitePoint = false });
        Assert.Equal(255, (int)b[3]);
        Assert.Equal(255, (int)b[4]);
        Assert.Equal(255, (int)b[5]);
    }

    [Fact]
    public void Estimate_ThenToSrgb8_EqualsSimpleToSrgb8()
    {
        // Canali piatti: nero 0 su ogni canale, bianco 65535, midtone 0.711562 (vedi test sopra).
        RgbImage img = Fill(4, 4, 20000f, 25000f, 30000f);
        var opts = new StretchOptions();

        StretchParams p = Stretch.Estimate(img, opts);

        Assert.Equal(0f, p.BlackR);
        Assert.Equal(0f, p.BlackG);
        Assert.Equal(0f, p.BlackB);
        Assert.Equal(65535f, p.White);
        TestMath.AssertNear(0.711562, p.Midtone, 1e-3);

        Assert.Equal(Stretch.ToSrgb8(img, opts), Stretch.ToSrgb8(img, opts, p));
    }

    [Fact]
    public void Estimate_RequiresLinkedWhitePoint()
    {
        RgbImage img = Fill(4, 4, 1000f, 1000f, 1000f);

        Assert.Throws<InvalidOperationException>(
            () => Stretch.Estimate(img, new StretchOptions { LinkedWhitePoint = false }));
    }

    [Fact]
    public void Estimate_LargeConstantImage_MatchesSmallImage()
    {
        // 500 x 500 = 250000 pixel, sopra 200000: il campione salta pixel, ma il valore e' costante.
        // Stesso risultato del caso 4 x 4: nero 0, bianco 65535 e midtone che porta Mtf(1000/65535) a 0.2.
        // Midtone = x(1-t)/(x(1-2t)+t) con x = 0.015259, t = 0.2: 0.012207 / 0.209155 = 0.058369.
        RgbImage small = Fill(4, 4, 1000f, 1000f, 1000f);
        RgbImage big = Fill(500, 500, 1000f, 1000f, 1000f);
        var opts = new StretchOptions();

        StretchParams ps = Stretch.Estimate(small, opts);
        StretchParams pb = Stretch.Estimate(big, opts);

        Assert.Equal(ps, pb);
        TestMath.AssertNear(0.058369, pb.Midtone, 1e-3);
    }

    [Fact]
    public void StretchParamsSmoother_AlphaHalf_FollowsExponentialAverage()
    {
        // alpha = 0.5 e' il peso del nuovo campione.
        var s = new StretchParamsSmoother(0.5);
        var first = new StretchParams(0f, 0f, 0f, 1000f, 0.2f);

        // Primo update: invariato.
        StretchParams o1 = s.Update(first);
        Assert.Equal(first, o1);

        // Nuovo campione: BlackR 1000, BlackG 2000, BlackB 3000, White 3000, Midtone 0.6.
        // BlackR   = 0.5 * 1000 + 0.5 * 0    = 500
        // BlackG   = 0.5 * 2000 + 0.5 * 0    = 1000
        // BlackB   = 0.5 * 3000 + 0.5 * 0    = 1500
        // White    = 0.5 * 3000 + 0.5 * 1000 = 2000
        // Midtone  = 0.5 * 0.6  + 0.5 * 0.2  = 0.4
        StretchParams o2 = s.Update(new StretchParams(1000f, 2000f, 3000f, 3000f, 0.6f));
        TestMath.AssertNear(500.0, o2.BlackR, 1e-3);
        TestMath.AssertNear(1000.0, o2.BlackG, 1e-3);
        TestMath.AssertNear(1500.0, o2.BlackB, 1e-3);
        TestMath.AssertNear(2000.0, o2.White, 1e-3);
        TestMath.AssertNear(0.4, o2.Midtone, 1e-6);
    }
}
