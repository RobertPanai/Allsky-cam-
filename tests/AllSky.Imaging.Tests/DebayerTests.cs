using Xunit;

namespace AllSky.Imaging.Tests;

public class DebayerTests
{
    private static ushort ValueFor(char color)
    {
        if (color == 'R')
        {
            return 1000;
        }

        if (color == 'G')
        {
            return 2000;
        }

        if (color == 'B')
        {
            return 3000;
        }

        throw new ArgumentException("Colore non valido nel test.");
    }

    [Theory]
    [InlineData(BayerPattern.RGGB, "RGGB")]
    [InlineData(BayerPattern.BGGR, "BGGR")]
    [InlineData(BayerPattern.GRBG, "GRBG")]
    [InlineData(BayerPattern.GBRG, "GBRG")]
    public void ConstantMosaic_GivesConstantChannels_IncludingBorders(BayerPattern pattern, string tile)
    {
        // tile = colori di (0,0), (1,0), (0,1), (1,1) in ordine di riga.
        // Mosaico con R=1000, G=2000, B=3000 nei rispettivi siti: ogni canale vale costante
        // ovunque, bordi compresi (la riflessione conserva colore e valore). Un errore di
        // mappatura del pattern produce valori sbagliati e fa fallire il test.
        const int w = 8;
        const int h = 8;
        var raw = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                raw[y * w + x] = ValueFor(tile[((y & 1) << 1) | (x & 1)]);
            }
        }

        RgbImage img = Debayer.Bilinear(raw, w, h, pattern);

        for (int i = 0; i < w * h; i++)
        {
            TestMath.AssertNear(1000.0, img.R[i], 1e-3);
            TestMath.AssertNear(2000.0, img.G[i], 1e-3);
            TestMath.AssertNear(3000.0, img.B[i], 1e-3);
        }
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    public void LinearRamp_IsPreserved_InInterior(BayerPattern pattern)
    {
        // f(x,y) = 10x + y. Per una funzione lineare, la media su campioni simmetrici rispetto
        // al centro vale f(centro). Esempio RGGB, pixel (1,1) = B:
        //   R dai 4 diagonali: (0 + 20 + 2 + 22) / 4 = 11 = f(1,1)
        //   G dai 4 ortogonali: (10 + 1 + 21 + 12) / 4 = 11 = f(1,1)
        // Vale per tutti i pattern. I pixel interni (1..4 in 6x6) non toccano i bordi.
        const int w = 6;
        const int h = 6;
        var raw = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                raw[y * w + x] = (ushort)(10 * x + y);
            }
        }

        RgbImage img = Debayer.Bilinear(raw, w, h, pattern);

        for (int y = 1; y <= h - 2; y++)
        {
            for (int x = 1; x <= w - 2; x++)
            {
                int i = y * w + x;
                double expected = 10 * x + y;
                TestMath.AssertNear(expected, img.R[i], 1e-3);
                TestMath.AssertNear(expected, img.G[i], 1e-3);
                TestMath.AssertNear(expected, img.B[i], 1e-3);
            }
        }
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        // width = 1 -> ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() => Debayer.Bilinear(new ushort[2], 1, 2, BayerPattern.RGGB));
        // 3 elementi invece di 2 * 2 = 4 -> ArgumentException
        Assert.Throws<ArgumentException>(() => Debayer.Bilinear(new ushort[3], 2, 2, BayerPattern.RGGB));
    }
}
