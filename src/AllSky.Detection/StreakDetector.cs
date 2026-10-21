namespace AllSky.Detection;

/// <summary>
/// Rilevatore di scie luminose (meteore, satelliti in transito) in sequenze di frame notturni.
///
/// <para>
/// <b>Algoritmo:</b>
/// </para>
/// <list type="number">
///   <item><description>Fondo adattivo: media su al piu' N frame, poi media esponenziale con alpha = 1/N.
///   Il primo frame dopo <see cref="Reset"/> riempie il fondo e non produce rilevazioni.</description></item>
///   <item><description>Differenza frame - fondo (prima dell'aggiornamento del fondo). Le stelle fisse spariscono.</description></item>
///   <item><description>Soglia = max(mediana + K * sigma, minimo assoluto), con sigma = 1.4826 * MAD della differenza.</description></item>
///   <item><description>Se la frazione di pixel sopra soglia (fuori dalla maschera) supera il limite, il frame e' scartato.</description></item>
///   <item><description>Componenti connesse a 8 vicini; per ciascuna, PCA dei pixel: lunghezza, spessore, elongazione e scarto dall'asse.</description></item>
/// </list>
/// <para>
/// Le rilevazioni sono in pixel del frame ricevuto. La classe non e' thread-safe: un'istanza per flusso di frame.
/// Il ciclo caldo non alloca per pixel: i buffer sono riallocati solo se cambiano le dimensioni del frame.
/// </para>
/// </summary>
public sealed class StreakDetector
{
    // Istogramma della differenza: valori arrotondati e limitati a [-255, 255].
    private const int DiffOffset = 255;
    private const int DiffBins = 2 * DiffOffset + 1;

    // Istogramma di |differenza - mediana|: valori limitati a [0, 255].
    private const int DevBins = 256;

    // Pavimento della sigma stimata: evita una soglia nulla su immagini perfettamente piatte.
    private const double MinNoiseSigma = 0.5;

    // Per rumore gaussiano, sigma = 1.4826 * MAD.
    private const double MadToSigma = 1.4826;

    // Varianza minima dell'asse trasversale: un pixel di larghezza unitaria ha varianza 1/12.
    private const double MinCrossVariance = 1.0 / 12.0;

    private const byte StateNone = 0;
    private const byte StateCandidate = 1;
    private const byte StateVisited = 2;

    private readonly StreakDetectorOptions _options;
    private readonly int[] _diffHist = new int[DiffBins];
    private readonly int[] _devHist = new int[DevBins];

    private int _width;
    private int _height;

    /// <summary>True se l'ultimo frame elaborato e' stato scartato perche' globalmente variato (vedi <see cref="StreakDetectorOptions.MaxChangedFraction"/>).</summary>
    public bool LastFrameDiscarded { get; private set; }

    /// <summary>Frazione di pixel sopra soglia (fuori dalle maschere) nell'ultimo frame elaborato. 0 se non c'e' ancora un fondo.</summary>
    public double LastChangedFraction { get; private set; }

    // Numero di frame integrati nel fondo dall'ultimo Reset (0 = fondo vuoto).
    private int _framesIntegrated;

    private float[] _background = Array.Empty<float>();
    private float[] _diff = Array.Empty<float>();
    private byte[] _state = Array.Empty<byte>();
    private int[] _stack = Array.Empty<int>();

    /// <summary>Crea il rilevatore. Lancia <see cref="ArgumentOutOfRangeException"/> se le opzioni non sono coerenti.</summary>
    public StreakDetector(StreakDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    /// <summary>Azzera il fondo adattivo. Il frame successivo riempie il fondo e non produce rilevazioni.</summary>
    public void Reset()
    {
        _framesIntegrated = 0;
        LastFrameDiscarded = false;
        LastChangedFraction = 0.0;
    }

    /// <summary>
    /// Elabora un frame e restituisce le scie trovate, ordinate per punteggio decrescente.
    /// </summary>
    /// <param name="frame">Frame di luminanza ridotto.</param>
    /// <param name="skyMask">Maschera cielo: true dove il pixel (x, y) e' utilizzabile. Null = tutto il frame.</param>
    /// <param name="exclusionCircles">Cerchi (centro x, centro y, raggio) da escludere, ad esempio la Luna.</param>
    /// <returns>Le scie rilevate, oppure lista vuota se il frame e' scartato o non c'e' ancora un fondo.</returns>
    public IReadOnlyList<Streak> Detect(
        LumaFrame frame,
        Func<int, int, bool>? skyMask = null,
        ReadOnlySpan<(int X, int Y, int R)> exclusionCircles = default)
    {
        ValidateFrame(frame);
        LastFrameDiscarded = false;
        LastChangedFraction = 0.0;
        for (int c = 0; c < exclusionCircles.Length; c++)
        {
            if (exclusionCircles[c].R < 0)
                throw new ArgumentOutOfRangeException(nameof(exclusionCircles), exclusionCircles[c].R, "Il raggio di esclusione non puo' essere negativo.");
        }

        int width = frame.Width;
        int height = frame.Height;
        int n = width * height;
        byte[] pixels = frame.Pixels;
        EnsureBuffers(width, height);

        // Primo frame dopo Reset: riempie il fondo, nessuna rilevazione.
        if (_framesIntegrated == 0)
        {
            for (int i = 0; i < n; i++)
            {
                _background[i] = pixels[i];
            }
            _framesIntegrated = 1;
            return Array.Empty<Streak>();
        }

        // Passo 1: differenza rispetto al fondo corrente, poi aggiornamento del fondo e istogramma della differenza.
        float alpha = 1.0f / Math.Min(_framesIntegrated + 1, _options.BackgroundFrames);
        Array.Clear(_diffHist);
        for (int i = 0; i < n; i++)
        {
            float d = pixels[i] - _background[i];
            _diff[i] = d;
            _background[i] += alpha * d;
            _diffHist[DiffBin(d)]++;
        }
        _framesIntegrated++;

        // Passo 2: mediana e MAD della differenza, dagli istogrammi (nessuna allocazione per pixel).
        float median = HistogramMedianBin(_diffHist, n) - DiffOffset;
        Array.Clear(_devHist);
        for (int i = 0; i < n; i++)
        {
            float dev = MathF.Abs(_diff[i] - median);
            _devHist[Math.Min((int)MathF.Round(dev), DevBins - 1)]++;
        }
        float mad = HistogramMedianBin(_devHist, n);
        double sigma = Math.Max(MadToSigma * mad, MinNoiseSigma);
        float threshold = (float)Math.Max(median + _options.ThresholdSigma * sigma, _options.MinDifferenceLuma);

        // Passo 3: pixel candidati, esclusi quelli fuori cielo o dentro un cerchio di esclusione.
        long changed = 0;
        for (int i = 0; i < n; i++)
        {
            if (_diff[i] < threshold)
            {
                _state[i] = StateNone;
                continue;
            }

            int x = i % width;
            int y = i / width;
            if (IsInsideAnyCircle(x, y, exclusionCircles) || (skyMask is not null && !skyMask(x, y)))
            {
                _state[i] = StateNone;
                continue;
            }

            _state[i] = StateCandidate;
            changed++;
        }

        // Frame globalmente variato (nuvole, fulmini, fari): scartato.
        LastChangedFraction = (double)changed / n;
        if (LastChangedFraction > _options.MaxChangedFraction)
        {
            LastFrameDiscarded = true;
            return Array.Empty<Streak>();
        }

        // Passo 4: componenti connesse a 8 vicini e filtro geometrico.
        var result = new List<Streak>();
        for (int i = 0; i < n; i++)
        {
            if (_state[i] != StateCandidate)
            {
                continue;
            }

            ComponentAccumulator acc = FloodFill(i, width, height);
            Streak? streak = BuildStreak(acc, sigma);
            if (streak is not null)
            {
                result.Add(streak);
            }
        }

        result.Sort((p, q) => q.Score.CompareTo(p.Score));
        return result;
    }

    // Converte una differenza in indice di istogramma, arrotondando e limitando a [-255, 255].
    private static int DiffBin(float d)
        => Math.Clamp((int)MathF.Round(d), -DiffOffset, DiffOffset) + DiffOffset;

    // Primo indice dell'istogramma in cui la frequenza cumulata raggiunge meta' del totale.
    private static int HistogramMedianBin(int[] hist, int total)
    {
        long half = (total + 1) / 2;
        long cumulative = 0;
        for (int b = 0; b < hist.Length; b++)
        {
            cumulative += hist[b];
            if (cumulative >= half)
            {
                return b;
            }
        }
        return hist.Length - 1;
    }

    private static bool IsInsideAnyCircle(int x, int y, ReadOnlySpan<(int X, int Y, int R)> circles)
    {
        foreach (var c in circles)
        {
            long dx = x - c.X;
            long dy = y - c.Y;
            if (dx * dx + dy * dy <= (long)c.R * c.R)
            {
                return true;
            }
        }
        return false;
    }

    // Riempimento a 8 vicini di una componente candidata. Ogni pixel entra nello stack una sola volta.
    private ComponentAccumulator FloodFill(int start, int width, int height)
    {
        var acc = new ComponentAccumulator();
        int top = 0;
        _stack[top++] = start;
        _state[start] = StateVisited;

        while (top > 0)
        {
            int p = _stack[--top];
            int x = p % width;
            int y = p / width;
            acc.Add(x, y, _diff[p]);

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if ((uint)ny >= (uint)height)
                {
                    continue;
                }

                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    if ((dx == 0 && dy == 0) || (uint)nx >= (uint)width)
                    {
                        continue;
                    }

                    int q = ny * width + nx;
                    if (_state[q] == StateCandidate)
                    {
                        _state[q] = StateVisited;
                        _stack[top++] = q;
                    }
                }
            }
        }

        return acc;
    }

    // Statistiche della componente: momenti del primo e secondo ordine dei pixel e somme di luminanza.
    private struct ComponentAccumulator
    {
        public int Area;
        public double Sx;
        public double Sy;
        public double Sxx;
        public double Syy;
        public double Sxy;
        public double Integrated;
        public double Peak;

        public void Add(int x, int y, double excess)
        {
            Area++;
            Sx += x;
            Sy += y;
            Sxx += (double)x * x;
            Syy += (double)y * y;
            Sxy += (double)x * y;
            Integrated += excess;
            if (excess > Peak)
            {
                Peak = excess;
            }
        }
    }

    // Applica i criteri geometrici a una componente. Restituisce null se non e' una scia.
    private Streak? BuildStreak(ComponentAccumulator acc, double noiseSigma)
    {
        if (acc.Area < _options.MinAreaPx)
        {
            return null;
        }

        double n = acc.Area;
        double mx = acc.Sx / n;
        double my = acc.Sy / n;

        // Matrice di covarianza 2x2 simmetrica [[a, b], [b, c]].
        double a = acc.Sxx / n - mx * mx;
        double c = acc.Syy / n - my * my;
        double b = acc.Sxy / n - mx * my;

        // Autovalori: lambda1 >= lambda2 (varianze lungo e attraverso l'asse principale).
        double half = (a + c) / 2.0;
        double disc = Math.Sqrt((a - c) * (a - c) / 4.0 + b * b);
        double lambda1 = Math.Max(half + disc, 0.0);
        double lambda2 = Math.Max(half - disc, 0.0);

        // Per k pixel equispaziati di passo 1, la varianza vale (k^2 - 1) / 12: quindi sqrt(12 * var + 1) ~ k.
        double length = Math.Sqrt(12.0 * lambda1 + 1.0);
        double width = n / length;
        double elongation = Math.Sqrt(lambda1 / Math.Max(lambda2, MinCrossVariance));

        if (length < _options.MinLengthPx || width > _options.MaxWidthPx || elongation < _options.MinElongation)
        {
            return null;
        }

        // Autovettore di lambda1: (b, lambda1 - a), oppure un asse coordinato se la matrice e' diagonale.
        double vx;
        double vy;
        if (Math.Abs(b) > 1e-12)
        {
            vx = b;
            vy = lambda1 - a;
        }
        else if (a >= c)
        {
            vx = 1.0;
            vy = 0.0;
        }
        else
        {
            vx = 0.0;
            vy = 1.0;
        }
        double norm = Math.Sqrt(vx * vx + vy * vy);
        vx /= norm;
        vy /= norm;

        double straightness = Math.Sqrt(lambda2);
        double snr = acc.Peak / noiseSigma;
        double score = snr * Math.Sqrt(length) / (1.0 + straightness);
        double halfLength = length / 2.0;

        return new Streak(
            X1: mx - vx * halfLength,
            Y1: my - vy * halfLength,
            X2: mx + vx * halfLength,
            Y2: my + vy * halfLength,
            LengthPx: length,
            PeakLuma: acc.Peak,
            IntegratedLuma: acc.Integrated,
            Straightness: straightness,
            Score: score);
    }

    // Alloca i buffer solo quando cambiano le dimensioni: un cambio di formato azzera anche il fondo.
    private void EnsureBuffers(int width, int height)
    {
        if (width == _width && height == _height && _background.Length == width * height)
        {
            return;
        }

        int n = width * height;
        _width = width;
        _height = height;
        _background = new float[n];
        _diff = new float[n];
        _state = new byte[n];
        _stack = new int[n];
        _framesIntegrated = 0;
    }

    private static void ValidateFrame(LumaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Pixels is null)
        {
            throw new ArgumentException("I pixel del frame non possono essere null.", nameof(frame));
        }
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException($"Dimensioni del frame non valide: {frame.Width}x{frame.Height}.", nameof(frame));
        }

        long expected = (long)frame.Width * frame.Height;
        if (expected > int.MaxValue)
        {
            throw new ArgumentException($"Frame troppo grande: {frame.Width}x{frame.Height}.", nameof(frame));
        }
        if (frame.Pixels.Length != expected)
        {
            throw new ArgumentException(
                $"Il buffer ha {frame.Pixels.Length} byte, attesi {expected} per il frame {frame.Width}x{frame.Height}.",
                nameof(frame));
        }
    }
}
