using Xunit;

namespace AllSky.Imaging.Tests;

public class DebayerHamiltonAdamsTests
{
    // Colori di (0,0), (1,0), (0,1), (1,1) in ordine di riga, come nei test di Debayer.
    private static string TileFor(BayerPattern pattern) => pattern switch
    {
        BayerPattern.RGGB => "RGGB",
        BayerPattern.BGGR => "BGGR",
        BayerPattern.GRBG => "GRBG",
        _ => "GBRG",
    };

    private static char ColorAt(string tile, int x, int y) => tile[((y & 1) << 1) | (x & 1)];

    // Mosaico da un'immagine piena: ogni pixel conserva solo il canale del proprio sito.
    private static ushort[] Mosaic(string tile, int w, int h, Func<int, int, char, double> sample)
    {
        var raw = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                raw[y * w + x] = (ushort)Math.Round(sample(x, y, ColorAt(tile, x, y)));
            }
        }

        return raw;
    }

    private static double Psnr(float[] expected, float[] actual)
    {
        double sse = 0.0;
        for (int i = 0; i < expected.Length; i++)
        {
            double d = expected[i] - actual[i];
            sse += d * d;
        }

        double mse = sse / expected.Length;
        return mse == 0.0 ? double.PositiveInfinity : 10.0 * Math.Log10(65535.0 * 65535.0 / mse);
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    public void UniformField_IsIdentical_ForEveryPattern(BayerPattern pattern)
    {
        const int w = 12;
        const int h = 10;
        var raw = new ushort[w * h];
        Array.Fill(raw, (ushort)1234);

        RgbImage img = DebayerHamiltonAdams.Demosaic(raw, w, h, pattern);

        for (int i = 0; i < w * h; i++)
        {
            TestMath.AssertNear(1234.0, img.R[i], 1e-3);
            TestMath.AssertNear(1234.0, img.G[i], 1e-3);
            TestMath.AssertNear(1234.0, img.B[i], 1e-3);
        }
    }

    [Theory]
    [InlineData(BayerPattern.RGGB, 0)]
    [InlineData(BayerPattern.RGGB, 1)]
    [InlineData(BayerPattern.RGGB, 2)]
    [InlineData(BayerPattern.BGGR, 0)]
    [InlineData(BayerPattern.BGGR, 1)]
    [InlineData(BayerPattern.BGGR, 2)]
    [InlineData(BayerPattern.GRBG, 0)]
    [InlineData(BayerPattern.GRBG, 1)]
    [InlineData(BayerPattern.GRBG, 2)]
    [InlineData(BayerPattern.GBRG, 0)]
    [InlineData(BayerPattern.GBRG, 1)]
    [InlineData(BayerPattern.GBRG, 2)]
    public void SingleChannelRamp_IsRecovered_AndOtherChannelsStayZero(BayerPattern pattern, int channel)
    {
        // Scena monocromatica a rampa lineare: il canale 'channel' vale 100 + 10x + 5y, gli altri 0.
        // Una rampa lineare e' riprodotta esattamente dal passo di differenze colore, quindi nell'interno
        // (margine 3, lontano dai bordi riflessi) il risultato e' esatto. Un errore nella mappatura
        // dei colori del pattern lo fa fallire.
        const int w = 16;
        const int h = 14;
        string tile = TileFor(pattern);
        char target = "RGB"[channel];
        ushort[] raw = Mosaic(tile, w, h, (x, y, color) => color == target ? 100.0 + 10.0 * x + 5.0 * y : 0.0);

        RgbImage img = DebayerHamiltonAdams.Demosaic(raw, w, h, pattern);
        float[][] planes = { img.R, img.G, img.B };

        for (int y = 3; y < h - 3; y++)
        {
            for (int x = 3; x < w - 3; x++)
            {
                int i = y * w + x;
                for (int c = 0; c < 3; c++)
                {
                    double expected = c == channel ? 100.0 + 10.0 * x + 5.0 * y : 0.0;
                    TestMath.AssertNear(expected, planes[c][i], 0.5);
                }
            }
        }
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    public void VerticalEdge_HasLessError_ThanBilinear(BayerPattern pattern)
    {
        // Immagine grigia con bordo verticale netto a meta' larghezza: 1000 a sinistra, 3000 a destra.
        // Il bilineare media attraverso il bordo; Hamilton-Adams sceglie la direzione verticale
        // (il gradiente orizzontale e' grande) e non mescola i due lati.
        const int w = 32;
        const int h = 32;
        string tile = TileFor(pattern);
        ushort[] raw = Mosaic(tile, w, h, (x, _, _) => x < w / 2 ? 1000.0 : 3000.0);

        var truthR = new float[w * h];
        for (int i = 0; i < truthR.Length; i++)
        {
            truthR[i] = (i % w) < w / 2 ? 1000f : 3000f;
        }

        RgbImage ha = DebayerHamiltonAdams.Demosaic(raw, w, h, pattern);
        RgbImage bil = Debayer.Bilinear(raw, w, h, pattern);

        double psnrHa = Psnr(truthR, ha.G);
        double psnrBil = Psnr(truthR, bil.G);
        Assert.True(psnrHa > psnrBil, $"PSNR HA {psnrHa:F2} dB, bilineare {psnrBil:F2} dB");
        Assert.True(Psnr(truthR, ha.R) > Psnr(truthR, bil.R));
        Assert.True(Psnr(truthR, ha.B) > Psnr(truthR, bil.B));
    }

    [Fact]
    public void Demosaic_RejectsLengthMismatch()
    {
        Assert.Throws<ArgumentException>(() => DebayerHamiltonAdams.Demosaic(new ushort[5], 2, 2, BayerPattern.RGGB));
    }
}
