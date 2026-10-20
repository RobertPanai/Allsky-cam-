using System.Buffers;

namespace AllSky.Imaging;

/// <summary>
/// Rilevamento e correzione dei pixel caldi su un mosaico Bayer.
/// <para>
/// <b>Criterio di rilevamento</b> (per pixel p nel margine interno di 2 pixel):
/// </para>
/// <list type="number">
/// <item>Riferimento locale: mediana degli 8 vicini dello stesso colore Bayer, cioe' a passo 2 in x e y
/// (offset in {-2, 0, 2}^2 meno il centro). Il passo 2 conserva la parita' di riga e colonna, quindi il colore.</item>
/// <item>Residuo r = v(p) - mediana vicini. Su tutto il frame (campionato, vedi <see cref="PixelSampling"/>) si calcolano
/// centro c = mediana(r) e scala robusta s = 1.4826 * MAD(r), con s almeno 1 ADU.</item>
/// <item>Soglia: limite(p) = mediana vicini + c + k * s, dove k e' il parametro <c>sigma</c>.</item>
/// <item><b>Pixel caldo</b> se v(p) &gt; limite(p) E tutti gli 8 vicini a distanza 1 (qualsiasi colore) hanno valore &lt;= limite(p).</item>
/// </list>
/// <para>
/// La seconda condizione e' il criterio per non perdere le stelle. Una stella reale colpisce un intorno
/// di pixel contigui: il picco ha vicini a distanza 1 molto luminosi e viene escluso. Il solo test sui vicini
/// a passo 2 non basta: il centro di una stella 3x3 ha i vicini a passo 2 tutti scuri e verrebbe scambiato
/// per un pixel caldo. Un pixel caldo isolato ha invece i vicini a distanza 1 al livello del fondo.
/// </para>
/// <para>
/// Limiti dichiarati: non si correggono i pixel nel margine di 2 pixel del bordo; due pixel caldi adiacenti
/// non vengono corretti (i loro vicini non sono "bassi"). La mappa si costruisce da un frame, oppure dalla
/// mediana per pixel di piu' frame, che elimina gli eventi transitori e lascia i pixel caldi persistenti.
/// </para>
/// </summary>
public static class HotPixelFilter
{
    private const double MadToSigma = 1.4826;
    private const double MinSigma = 1.0;
    private const int Margin = 2;

    /// <summary>Costruisce la mappa dei pixel caldi da un frame.</summary>
    /// <param name="frame">Frame grezzo a 16 bit, Width * Height elementi. Non viene modificato.</param>
    /// <param name="width">Larghezza in pixel.</param>
    /// <param name="height">Altezza in pixel.</param>
    /// <param name="sigma">Parametro k della soglia (finito e &gt; 0), in multipli della scala robusta.</param>
    /// <returns>La mappa dei pixel caldi, eventualmente vuota.</returns>
    public static HotPixelMap Build(ushort[] frame, int width, int height, double sigma)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateSize(frame, width, height);
        ValidateSigma(sigma);

        var hot = new List<int>();
        int interiorW = width - 2 * Margin;
        int interiorH = height - 2 * Margin;
        if (interiorW <= 0 || interiorH <= 0)
        {
            return new HotPixelMap(width, height, Array.Empty<int>());
        }

        // Primo passo: campione del residuo su tutto il frame (limite di memoria: PixelSampling.MaxSampleSize).
        long interior = (long)interiorW * interiorH;
        int step = PixelSampling.Step(interior);
        int sampleLength = (int)((interior + step - 1) / step);

        double[] residuals = ArrayPool<double>.Shared.Rent(sampleLength);
        double[] deviations = ArrayPool<double>.Shared.Rent(sampleLength);
        try
        {
            int taken = 0;
            long counter = 0;
            for (int y = Margin; y < height - Margin; y++)
            {
                for (int x = Margin; x < width - Margin; x++, counter++)
                {
                    if (counter % step != 0)
                    {
                        continue;
                    }

                    double reference = SameColourMedian(frame, width, height, x, y);
                    residuals[taken++] = frame[y * width + x] - reference;
                }
            }

            Span<double> residualSpan = residuals.AsSpan(0, taken);
            double center = Median(residualSpan);

            for (int i = 0; i < taken; i++)
            {
                deviations[i] = Math.Abs(residuals[i] - center);
            }

            double mad = Median(deviations.AsSpan(0, taken));
            double scale = Math.Max(MinSigma, MadToSigma * mad);
            double threshold = sigma * scale;

            // Secondo passo: rilevamento.
            for (int y = Margin; y < height - Margin; y++)
            {
                for (int x = Margin; x < width - Margin; x++)
                {
                    double reference = SameColourMedian(frame, width, height, x, y);
                    double limit = reference + center + threshold;
                    if (frame[y * width + x] <= limit)
                    {
                        continue;
                    }

                    if (AllImmediateNeighboursAtOrBelow(frame, width, x, y, limit))
                    {
                        hot.Add(y * width + x);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(residuals);
            ArrayPool<double>.Shared.Return(deviations);
        }

        return new HotPixelMap(width, height, hot.ToArray());
    }

    /// <summary>
    /// Costruisce la mappa dalla mediana per pixel di piu' frame (stessa dimensione), poi applica il criterio
    /// del singolo frame. Gli eventi presenti in pochi frame (raggi cosmici, satelliti) non entrano nella mappa.
    /// </summary>
    /// <param name="frames">Almeno un frame grezzo, tutti Width * Height elementi.</param>
    /// <param name="width">Larghezza in pixel.</param>
    /// <param name="height">Altezza in pixel.</param>
    /// <param name="sigma">Parametro k della soglia (finito e &gt; 0).</param>
    public static HotPixelMap Build(IReadOnlyList<ushort[]> frames, int width, int height, double sigma)
    {
        DarkBuilder.ValidateFrames(frames, width, height);
        ValidateSigma(sigma);
        ushort[] median = DarkBuilder.MedianFrame(frames, width, height);
        return Build(median, width, height, sigma);
    }

    /// <summary>
    /// Sostituisce i pixel della mappa con la mediana dei loro vicini dello stesso colore (passo 2).
    /// I valori di sostituzione sono calcolati tutti prima della scrittura, quindi non dipendono dall'ordine.
    /// Un pixel senza vicini validi resta invariato.
    /// </summary>
    /// <param name="frame">Frame grezzo modificato sul posto.</param>
    /// <param name="width">Larghezza in pixel.</param>
    /// <param name="height">Altezza in pixel.</param>
    /// <param name="map">Mappa costruita per le stesse dimensioni.</param>
    public static void Apply(ushort[] frame, int width, int height, HotPixelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ValidateSize(frame, width, height);
        if (map.Width != width || map.Height != height)
        {
            throw new ArgumentException("La mappa dei pixel caldi non corrisponde alle dimensioni del frame.", nameof(map));
        }

        int[] indices = map.Indices;
        if (indices.Length == 0)
        {
            return;
        }

        int n = frame.Length;
        ushort[] replacements = ArrayPool<ushort>.Shared.Rent(indices.Length);
        try
        {
            for (int j = 0; j < indices.Length; j++)
            {
                int p = indices[j];
                if (p < 0 || p >= n)
                {
                    throw new ArgumentException("Indice fuori dal frame nella mappa dei pixel caldi.", nameof(map));
                }

                int x = p % width;
                int y = p / width;
                double median = SameColourMedian(frame, width, height, x, y);
                replacements[j] = double.IsNaN(median)
                    ? frame[p]
                    : (ushort)Math.Clamp(Math.Round(median), 0.0, ushort.MaxValue);
            }

            for (int j = 0; j < indices.Length; j++)
            {
                frame[indices[j]] = replacements[j];
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(replacements);
        }
    }

    /// <summary>
    /// Mediana degli 8 vicini dello stesso colore (passo 2). Restituisce NaN se non ce ne sono nel frame.
    /// Usa stackalloc: nessuna allocazione heap.
    /// </summary>
    private static double SameColourMedian(ushort[] frame, int width, int height, int x, int y)
    {
        Span<double> buffer = stackalloc double[8];
        int count = 0;
        for (int dy = -2; dy <= 2; dy += 2)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= height)
            {
                continue;
            }

            for (int dx = -2; dx <= 2; dx += 2)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = x + dx;
                if (nx < 0 || nx >= width)
                {
                    continue;
                }

                buffer[count++] = frame[ny * width + nx];
            }
        }

        if (count == 0)
        {
            return double.NaN;
        }

        return Median(buffer.Slice(0, count));
    }

    /// <summary>True se i 8 vicini a distanza 1 (di qualsiasi colore) hanno valore &lt;= limit. Richiede (x, y) nel margine.</summary>
    private static bool AllImmediateNeighboursAtOrBelow(ushort[] frame, int width, int x, int y, double limit)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (frame[(y + dy) * width + (x + dx)] > limit)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Mediana di un campione non vuoto. Ordina il campione in place. Per lunghezza pari, media dei due centrali.</summary>
    private static double Median(Span<double> values)
    {
        values.Sort();
        int n = values.Length;
        if ((n & 1) == 1)
        {
            return values[n / 2];
        }

        return 0.5 * (values[n / 2 - 1] + values[n / 2]);
    }

    private static void ValidateSize(ushort[] frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Larghezza e altezza devono essere > 0.");
        }

        if ((long)width * height != frame.Length)
        {
            throw new ArgumentException("frame.Length diverso da width * height.", nameof(frame));
        }
    }

    private static void ValidateSigma(double sigma)
    {
        if (!(sigma > 0.0) || double.IsInfinity(sigma))
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), "sigma deve essere finito e > 0.");
        }
    }
}
