namespace AllSky.Imaging;

/// <summary>Stima e applicazione dei guadagni di bilanciamento del bianco.</summary>
public static class WhiteBalance
{
    private const int ClipIterations = 3;
    private const double ClipSigma = 2.5;
    private const double MadToSigma = 1.4826;

    /// <summary>
    /// Guadagni notturni dal fondo cielo. Per ogni canale: mediana sigma-clippata
    /// (3 iterazioni, 2.5 sigma, sigma = 1.4826 * MAD) dei pixel nella maschera (o tutti).
    /// Sopra 200000 pixel selezionati la mediana usa un campione a passo fisso.
    /// I guadagni portano le tre mediane a quella del verde (G = 1.0).
    /// </summary>
    public static (double R, double G, double B) EstimateNightGains(RgbImage img, bool[]? skyMask)
    {
        ArgumentNullException.ThrowIfNull(img);
        int n = img.Width * img.Height;
        if (skyMask != null && skyMask.Length != n)
        {
            throw new ArgumentException("skyMask deve avere Width * Height elementi.", nameof(skyMask));
        }

        double medR = SigmaClippedMedian(img.R, skyMask);
        double medG = SigmaClippedMedian(img.G, skyMask);
        double medB = SigmaClippedMedian(img.B, skyMask);
        if (medR <= 0.0 || medG <= 0.0 || medB <= 0.0)
        {
            throw new InvalidOperationException("Mediana del fondo non positiva: impossibile stimare i guadagni.");
        }

        return (medG / medR, 1.0, medG / medB);
    }

    /// <summary>
    /// Guadagni diurni grey-world robusti. Esclude i pixel saturi (un canale &gt;= 65000),
    /// tiene i pixel con luminanza fra il 5 e il 95 percentile, e usa la media dei canali.
    /// Luminanza Rec.709. I percentili usano un campione a passo fisso (al piu' 200000 pixel non saturi).
    /// </summary>
    public static (double R, double G, double B) EstimateDayGains(RgbImage img)
    {
        ArgumentNullException.ThrowIfNull(img);
        int n = img.Width * img.Height;

        // Campione a passo fisso fra i pixel non saturi. Con n <= 200000 sono tutti.
        int step = PixelSampling.Step(n);
        var sampled = new List<double>();
        int seen = 0;
        for (int i = 0; i < n; i++)
        {
            if (IsSaturated(img, i))
            {
                continue;
            }

            if (seen % step == 0)
            {
                sampled.Add(Luma(img, i));
            }

            seen++;
        }

        if (sampled.Count == 0)
        {
            throw new InvalidOperationException("Tutti i pixel sono saturi.");
        }

        double[] sorted = sampled.ToArray();
        Array.Sort(sorted);
        double lo = Percentile(sorted, 0.05);
        double hi = Percentile(sorted, 0.95);

        double sr = 0.0;
        double sg = 0.0;
        double sb = 0.0;
        long k = 0;
        for (int i = 0; i < n; i++)
        {
            if (IsSaturated(img, i))
            {
                continue;
            }

            double l = Luma(img, i);
            if (l < lo || l > hi)
            {
                continue;
            }

            sr += img.R[i];
            sg += img.G[i];
            sb += img.B[i];
            k++;
        }

        if (k == 0)
        {
            throw new InvalidOperationException("Nessun pixel utile per il grey-world.");
        }

        double mR = sr / k;
        double mG = sg / k;
        double mB = sb / k;
        if (mR <= 0.0 || mG <= 0.0 || mB <= 0.0)
        {
            throw new InvalidOperationException("Media di canale non positiva: impossibile stimare i guadagni.");
        }

        return (mG / mR, 1.0, mG / mB);
    }

    /// <summary>Interpolazione lineare fra due set di guadagni; t viene limitato a [0, 1].</summary>
    public static (double R, double G, double B) Blend(
        (double R, double G, double B) g1,
        (double R, double G, double B) g2,
        double t)
    {
        double k = Math.Clamp(t, 0.0, 1.0);
        return (
            g1.R + (g2.R - g1.R) * k,
            g1.G + (g2.G - g1.G) * k,
            g1.B + (g2.B - g1.B) * k);
    }

    /// <summary>Applica i guadagni in place, con clamp a 0..65535.</summary>
    public static void Apply(RgbImage img, (double R, double G, double B) gains)
    {
        ArgumentNullException.ThrowIfNull(img);
        Scale(img.R, gains.R);
        Scale(img.G, gains.G);
        Scale(img.B, gains.B);
    }

    private static void Scale(float[] plane, double gain)
    {
        for (int i = 0; i < plane.Length; i++)
        {
            plane[i] = (float)Math.Clamp(plane[i] * gain, 0.0, 65535.0);
        }
    }

    /// <summary>
    /// Mediana sigma-clippata del canale sui pixel in maschera (o tutti). Con piu' di 200000 pixel
    /// selezionati usa un campione a passo fisso fra quelli selezionati.
    /// </summary>
    private static double SigmaClippedMedian(float[] channel, bool[]? mask)
    {
        int total = channel.Length;
        if (mask != null)
        {
            total = 0;
            for (int i = 0; i < channel.Length; i++)
            {
                if (mask[i])
                {
                    total++;
                }
            }
        }

        if (total == 0)
        {
            throw new InvalidOperationException("Nessun pixel nella maschera del cielo.");
        }

        int step = PixelSampling.Step(total);
        var list = new List<double>(total / step + 1);
        int seen = 0;
        for (int i = 0; i < channel.Length; i++)
        {
            if (mask == null || mask[i])
            {
                if (seen % step == 0)
                {
                    list.Add(channel[i]);
                }

                seen++;
            }
        }

        double[] cur = list.ToArray();
        for (int it = 0; it < ClipIterations; it++)
        {
            double med = Median(cur);

            double[] dev = new double[cur.Length];
            for (int i = 0; i < cur.Length; i++)
            {
                dev[i] = Math.Abs(cur[i] - med);
            }

            double limit = ClipSigma * MadToSigma * Median(dev);

            var kept = new List<double>(cur.Length);
            for (int i = 0; i < cur.Length; i++)
            {
                if (Math.Abs(cur[i] - med) <= limit)
                {
                    kept.Add(cur[i]);
                }
            }

            if (kept.Count == cur.Length || kept.Count == 0)
            {
                break;
            }

            cur = kept.ToArray();
        }

        return Median(cur);
    }

    /// <summary>Mediana; ordina l'array in place. Richiede un array non vuoto.</summary>
    private static double Median(double[] values)
    {
        Array.Sort(values);
        int n = values.Length;
        return n % 2 == 1 ? values[n / 2] : 0.5 * (values[n / 2 - 1] + values[n / 2]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double pos = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double frac = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    private static bool IsSaturated(RgbImage img, int i)
    {
        return img.R[i] >= 65000f || img.G[i] >= 65000f || img.B[i] >= 65000f;
    }

    private static double Luma(RgbImage img, int i)
    {
        return 0.2126 * img.R[i] + 0.7152 * img.G[i] + 0.0722 * img.B[i];
    }
}
