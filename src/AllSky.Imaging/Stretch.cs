namespace AllSky.Imaging;

/// <summary>Funzioni di stretch e conversione in sRGB a 8 bit.</summary>
public static class Stretch
{
    private const float FullScale = 65535f;

    /// <summary>
    /// Midtone transfer function: (m-1)x / ((2m-1)x - m). x in 0..1, midtone in (0,1).
    /// Mtf(0,m) = 0, Mtf(1,m) = 1.
    /// </summary>
    public static float Mtf(float x, float midtone)
    {
        if (!(midtone > 0f && midtone < 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(midtone), "midtone deve essere in (0,1).");
        }

        if (x <= 0f)
        {
            return 0f;
        }

        if (x >= 1f)
        {
            return 1f;
        }

        return (float)MtfD(x, midtone);
    }

    /// <summary>Stretch asinh: asinh(beta * x) / asinh(beta). x in 0..1, beta maggiore di zero.</summary>
    public static float Asinh(float x, float beta)
    {
        if (!(beta > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(beta), "beta deve essere maggiore di zero.");
        }

        if (x <= 0f)
        {
            return 0f;
        }

        if (x >= 1f)
        {
            return 1f;
        }

        return (float)(Math.Asinh(beta * x) / Math.Asinh(beta));
    }

    /// <summary>
    /// Midtone tale che la mediana del campione, dopo Mtf, valga targetBackground (bisezione).
    /// Se la mediana non e' in (0,1) restituisce 0.5 come fallback neutro.
    /// </summary>
    public static float AutoMidtone(float[] channelSample, float targetBackground)
    {
        ArgumentNullException.ThrowIfNull(channelSample);
        if (channelSample.Length == 0)
        {
            throw new ArgumentException("Campione vuoto.", nameof(channelSample));
        }

        if (!(targetBackground > 0f && targetBackground < 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(targetBackground), "targetBackground deve essere in (0,1).");
        }

        float[] sorted = (float[])channelSample.Clone();
        Array.Sort(sorted);
        double median = PercentileSorted(sorted, 0.5);
        if (median <= 0.0 || median >= 1.0)
        {
            return 0.5f;
        }

        // MTF e' decrescente in m: se il valore e' troppo alto serve un midtone piu' alto.
        double lo = 1e-6;
        double hi = 1.0 - 1e-6;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (MtfD(median, mid) - targetBackground > 0.0)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (float)(0.5 * (lo + hi));
    }

    /// <summary>
    /// Stima dei parametri di stretch: punto nero per canale (percentile BlackPercentile), punto bianco
    /// comune (massimo dei percentili WhitePercentile dei canali non piatti) e midtone (automatico dal verde,
    /// o opts.Midtone se fisso). Le statistiche usano un campione a passo fisso (al piu' 200000 pixel).
    /// Richiede opts.LinkedWhitePoint = true, perche' <see cref="StretchParams"/> ha un solo punto bianco.
    /// </summary>
    public static StretchParams Estimate(RgbImage img, StretchOptions opts)
    {
        ArgumentNullException.ThrowIfNull(img);
        ArgumentNullException.ThrowIfNull(opts);
        ValidateInputs(img, opts);
        if (!opts.LinkedWhitePoint)
        {
            throw new InvalidOperationException(
                "Estimate richiede LinkedWhitePoint = true: StretchParams contiene un solo punto bianco.");
        }

        LinkedLevels(img, opts, out float blackR, out float blackG, out float blackB, out float white);
        float mid = ResolveMidtone(img.G, blackG, white, opts);
        return new StretchParams(blackR, blackG, blackB, white, mid);
    }

    /// <summary>
    /// Converte in RGB sRGB a 8 bit, interleaved (3 byte per pixel). Con LinkedWhitePoint (default)
    /// equivale a ToSrgb8(img, opts, Estimate(img, opts)). Senza, ogni canale usa il proprio punto bianco.
    /// Poi saturazione sulla luminanza e gamma sRGB.
    /// </summary>
    public static byte[] ToSrgb8(RgbImage img, StretchOptions opts)
    {
        ArgumentNullException.ThrowIfNull(img);
        ArgumentNullException.ThrowIfNull(opts);

        if (opts.LinkedWhitePoint)
        {
            return ToSrgb8(img, opts, Estimate(img, opts));
        }

        ValidateInputs(img, opts);

        // Modalita' per canale: ogni canale ha il proprio punto nero e bianco.
        float[] black = new float[3];
        float[] white = new float[3];
        ChannelLevels(img.R, opts, out black[0], out white[0]);
        ChannelLevels(img.G, opts, out black[1], out white[1]);
        ChannelLevels(img.B, opts, out black[2], out white[2]);
        float mid = ResolveMidtone(img.G, black[1], white[1], opts);
        return Render(img, opts, black, white, mid);
    }

    /// <summary>
    /// Converte in sRGB a 8 bit usando parametri dati (per esempio stimati e levigati dall'Host).
    /// White - Black deve essere almeno 1 su ogni canale.
    /// </summary>
    public static byte[] ToSrgb8(RgbImage img, StretchOptions opts, StretchParams p)
    {
        ArgumentNullException.ThrowIfNull(img);
        ArgumentNullException.ThrowIfNull(opts);
        ValidateInputs(img, opts);
        ValidateParams(p, opts);

        float[] black = { p.BlackR, p.BlackG, p.BlackB };
        float[] white = { p.White, p.White, p.White };
        return Render(img, opts, black, white, p.Midtone);
    }

    private static void ValidateInputs(RgbImage img, StretchOptions opts)
    {
        int n = img.Width * img.Height;
        if (n <= 0)
        {
            throw new ArgumentException("Immagine vuota.", nameof(img));
        }

        if (img.R.Length != n || img.G.Length != n || img.B.Length != n)
        {
            throw new ArgumentException("Dimensioni dei canali incoerenti con Width * Height.", nameof(img));
        }

        if (!(opts.BlackPercentile >= 0.0 && opts.WhitePercentile <= 100.0 && opts.BlackPercentile < opts.WhitePercentile))
        {
            throw new ArgumentException("Percentili non validi: 0 <= nero < bianco <= 100.", nameof(opts));
        }

        if (!(opts.Saturation >= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(opts), "Saturation deve essere maggiore o uguale a 0.");
        }

        if (opts.Midtone.HasValue && !(opts.Midtone.Value > 0f && opts.Midtone.Value < 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(opts), "Midtone deve essere in (0,1).");
        }
    }

    private static void ValidateParams(StretchParams p, StretchOptions opts)
    {
        float[] blacks = { p.BlackR, p.BlackG, p.BlackB };
        foreach (float b in blacks)
        {
            if (!(p.White - b >= 1f))
            {
                throw new ArgumentException("White - Black deve essere almeno 1 su ogni canale.", nameof(p));
            }
        }

        if (opts.Kind == StretchKind.Mtf && !(p.Midtone > 0f && p.Midtone < 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Midtone deve essere in (0,1).");
        }
    }

    /// <summary>
    /// Punti neri per canale e bianco comune. Il bianco e' il massimo dei percentili di bianco dei canali
    /// non piatti. Un canale piatto (range &lt; 1) ha nero 0 e il bianco comune. Se tutti sono piatti: 0..65535.
    /// </summary>
    private static void LinkedLevels(
        RgbImage img,
        StretchOptions opts,
        out float blackR,
        out float blackG,
        out float blackB,
        out float white)
    {
        ChannelStats(img.R, opts, out float bR, out float wR, out bool flatR);
        ChannelStats(img.G, opts, out float bG, out float wG, out bool flatG);
        ChannelStats(img.B, opts, out float bB, out float wB, out bool flatB);

        if (flatR && flatG && flatB)
        {
            blackR = 0f;
            blackG = 0f;
            blackB = 0f;
            white = FullScale;
            return;
        }

        white = 0f;
        if (!flatR)
        {
            white = Math.Max(white, wR);
        }

        if (!flatG)
        {
            white = Math.Max(white, wG);
        }

        if (!flatB)
        {
            white = Math.Max(white, wB);
        }

        blackR = flatR ? 0f : bR;
        blackG = flatG ? 0f : bG;
        blackB = flatB ? 0f : bB;
    }

    /// <summary>Punti neri e bianchi di un singolo canale. Canale piatto: nero 0, bianco 65535.</summary>
    private static void ChannelLevels(float[] channel, StretchOptions opts, out float black, out float white)
    {
        ChannelStats(channel, opts, out black, out white, out bool flat);
        if (flat)
        {
            black = 0f;
            white = FullScale;
        }
    }

    /// <summary>Percentili di nero e di bianco su un campione, e flag di canale piatto (range &lt; 1).</summary>
    private static void ChannelStats(
        float[] channel,
        StretchOptions opts,
        out float black,
        out float white,
        out bool flat)
    {
        float[] sorted = SortedSample(channel);
        black = (float)PercentileSorted(sorted, opts.BlackPercentile / 100.0);
        white = (float)PercentileSorted(sorted, opts.WhitePercentile / 100.0);
        flat = white - black < 1f;
    }

    /// <summary>Midtone da usare: fisso se presente, altrimenti automatico sul verde normalizzato a campione.</summary>
    private static float ResolveMidtone(float[] green, float black, float white, StretchOptions opts)
    {
        if (opts.Kind != StretchKind.Mtf)
        {
            return 0.5f;
        }

        if (opts.Midtone.HasValue)
        {
            return opts.Midtone.Value;
        }

        return AutoMidtone(NormalizedSample(green, black, white), opts.TargetBackground);
    }

    /// <summary>Render sRGB a 8 bit: normalizzazione per canale, stretch, saturazione, gamma.</summary>
    private static byte[] Render(RgbImage img, StretchOptions opts, float[] black, float[] white, float midtone)
    {
        int n = img.Width * img.Height;
        float scaleR = 1f / (white[0] - black[0]);
        float scaleG = 1f / (white[1] - black[1]);
        float scaleB = 1f / (white[2] - black[2]);
        float sat = opts.Saturation;

        byte[] rgb = new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            float r = Shape(Math.Clamp((img.R[i] - black[0]) * scaleR, 0f, 1f), opts, midtone);
            float g = Shape(Math.Clamp((img.G[i] - black[1]) * scaleG, 0f, 1f), opts, midtone);
            float b = Shape(Math.Clamp((img.B[i] - black[2]) * scaleB, 0f, 1f), opts, midtone);

            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            r = Math.Clamp(lum + sat * (r - lum), 0f, 1f);
            g = Math.Clamp(lum + sat * (g - lum), 0f, 1f);
            b = Math.Clamp(lum + sat * (b - lum), 0f, 1f);

            rgb[3 * i] = ToByte(SrgbEncode(r));
            rgb[3 * i + 1] = ToByte(SrgbEncode(g));
            rgb[3 * i + 2] = ToByte(SrgbEncode(b));
        }

        return rgb;
    }

    private static double MtfD(double x, double m)
    {
        return (m - 1.0) * x / ((2.0 * m - 1.0) * x - m);
    }

    private static float Shape(float x, StretchOptions opts, float midtone)
    {
        return opts.Kind == StretchKind.Asinh ? Asinh(x, opts.Beta) : Mtf(x, midtone);
    }

    /// <summary>
    /// Campione ordinato a passo fisso di un canale (step = PixelSampling.Step). Con n &lt;= 200000 e' l'intero canale.
    /// </summary>
    private static float[] SortedSample(float[] channel)
    {
        int step = PixelSampling.Step(channel.Length);
        float[] sample = new float[(channel.Length + step - 1) / step];
        for (int i = 0, k = 0; i < channel.Length; i += step, k++)
        {
            sample[k] = channel[i];
        }

        Array.Sort(sample);
        return sample;
    }

    /// <summary>Campione normalizzato in 0..1 (stessa scala di Normalize), a passo fisso, per il midtone automatico.</summary>
    private static float[] NormalizedSample(float[] channel, float black, float white)
    {
        int step = PixelSampling.Step(channel.Length);
        float[] sample = new float[(channel.Length + step - 1) / step];
        float scale = 1f / (white - black);
        for (int i = 0, k = 0; i < channel.Length; i += step, k++)
        {
            sample[k] = Math.Clamp((channel[i] - black) * scale, 0f, 1f);
        }

        return sample;
    }

    /// <summary>Percentile interpolato su un array gia' ordinato; p in 0..1.</summary>
    private static double PercentileSorted(float[] sorted, double p)
    {
        double pos = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double frac = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    /// <summary>Gamma sRGB (OETF a tratti) su un valore in 0..1.</summary>
    private static double SrgbEncode(double v)
    {
        double c = Math.Clamp(v, 0.0, 1.0);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
    }

    private static byte ToByte(double v01)
    {
        return (byte)Math.Round(Math.Clamp(v01, 0.0, 1.0) * 255.0);
    }
}
