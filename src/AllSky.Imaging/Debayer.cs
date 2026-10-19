namespace AllSky.Imaging;

/// <summary>Demosaicing Bayer con interpolazione bilineare.</summary>
public static class Debayer
{
    // Colore per posizione, indice ((y & 1) << 1) | (x & 1). 0 = R, 1 = G, 2 = B.
    private static readonly int[][] Layouts =
    {
        new[] { 0, 1, 1, 2 }, // RGGB
        new[] { 2, 1, 1, 0 }, // BGGR
        new[] { 1, 0, 2, 1 }, // GRBG
        new[] { 1, 2, 0, 1 }, // GBRG
    };

    /// <summary>
    /// Restituisce un'immagine RGB. Il canale nativo di ogni pixel e' il valore grezzo;
    /// gli altri canali sono la media dei pixel di quel colore nell'intorno 3x3.
    /// I bordi usano la riflessione, che conserva la parita' del pattern.
    /// </summary>
    public static RgbImage Bilinear(ushort[] raw, int width, int height, BayerPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Servono almeno 2 colonne.");
        }

        if (height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Servono almeno 2 righe.");
        }

        if (!Enum.IsDefined(pattern))
        {
            throw new ArgumentOutOfRangeException(nameof(pattern));
        }

        if ((long)width * height != raw.Length)
        {
            throw new ArgumentException("raw.Length diverso da width * height.", nameof(raw));
        }

        int n = width * height;
        float[][] planes = { new float[n], new float[n], new float[n] };
        int[] layout = Layouts[(int)pattern];
        double[] sum = new double[3];
        int[] cnt = new int[3];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int native = layout[((y & 1) << 1) | (x & 1)];

                sum[0] = 0.0;
                sum[1] = 0.0;
                sum[2] = 0.0;
                cnt[0] = 0;
                cnt[1] = 0;
                cnt[2] = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = Mirror(y + dy, height);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Mirror(x + dx, width);
                        int c = layout[((ny & 1) << 1) | (nx & 1)];
                        sum[c] += raw[ny * width + nx];
                        cnt[c]++;
                    }
                }

                for (int c = 0; c < 3; c++)
                {
                    if (c == native)
                    {
                        planes[c][idx] = raw[idx];
                    }
                    else
                    {
                        planes[c][idx] = cnt[c] > 0 ? (float)(sum[c] / cnt[c]) : 0f;
                    }
                }
            }
        }

        return new RgbImage(width, height, planes[0], planes[1], planes[2]);
    }

    private static int Mirror(int i, int n)
    {
        if (i < 0)
        {
            return -i;
        }

        if (i >= n)
        {
            return 2 * (n - 1) - i;
        }

        return i;
    }
}
