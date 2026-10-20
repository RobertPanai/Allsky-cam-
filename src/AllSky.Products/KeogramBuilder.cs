namespace AllSky.Products;

/// <summary>
/// Keogram incrementale. Per ogni frame estrae una colonna di <c>3 * altezza</c> byte lungo un diametro
/// del frame e la accoda; <see cref="Build"/> restituisce l'immagine RGB 8 bit (row-major, 3 byte per pixel).
/// Memoria: si conservano solo le colonne (3 * H byte per frame) e i timestamp; i frame non vengono tenuti.
/// </summary>
/// <remarks>
/// Scelte semplici adottate dove la specifica lasciava spazio:
/// il campionamento è al pixel più vicino (nessuna interpolazione); fuori dal frame si usa il bordo più vicino;
/// la colonna N-S usa la colonna <c>floor((W-1)/2 + 0.5)</c>, cioè <c>W/2</c> per larghezza pari;
/// il diametro ha sempre lunghezza H, così l'altezza dell'output è sempre quella del frame.
/// </remarks>
public sealed class KeogramBuilder
{
    /// <summary>Numero massimo di colonne dell'immagine finale; oltre questo si fa media a blocchi.</summary>
    public const int MaxColumns = 4096;

    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private readonly int[] _srcX;
    private readonly int[] _srcY;
    private readonly List<DateTimeOffset> _timestamps;

    // Buffer delle colonne: frame f, riga y, canale c -> indice (f * H + y) * 3 + c.
    private byte[] _columns;
    private int _frameCount;

    /// <summary>
    /// Crea un keogram che usa la colonna centrale verticale (N-S) di ogni frame.
    /// </summary>
    /// <param name="frameWidth">Larghezza dei frame in ingresso, in pixel (almeno 1).</param>
    /// <param name="frameHeight">Altezza dei frame in ingresso, in pixel (almeno 1); è anche l'altezza del keogram.</param>
    /// <param name="expectedFrames">Numero di frame attesi, usato solo per pre-allocare il buffer (0 = nessuna pre-allocazione).</param>
    /// <exception cref="ArgumentOutOfRangeException">Dimensioni o <paramref name="expectedFrames"/> non validi.</exception>
    public KeogramBuilder(int frameWidth, int frameHeight, int expectedFrames = 0)
        : this(frameWidth, frameHeight, expectedFrames, 0.0)
    {
    }

    /// <summary>
    /// Crea un keogram che usa un diametro del frame passante per il centro, a un angolo scelto.
    /// 0 gradi = colonna N-S (verticale), 90 gradi = riga E-O (orizzontale).
    /// </summary>
    /// <param name="frameWidth">Larghezza dei frame in ingresso, in pixel (almeno 1).</param>
    /// <param name="frameHeight">Altezza dei frame in ingresso, in pixel (almeno 1); è anche l'altezza del keogram.</param>
    /// <param name="expectedFrames">Numero di frame attesi, usato solo per pre-allocare il buffer.</param>
    /// <param name="angleDegrees">Angolo del diametro in gradi, finito. Valori fuori da 0..360 sono ammessi e periodici.</param>
    /// <exception cref="ArgumentOutOfRangeException">Dimensioni, <paramref name="expectedFrames"/> o angolo non validi.</exception>
    public KeogramBuilder(int frameWidth, int frameHeight, int expectedFrames, double angleDegrees)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameHeight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFrames);
        if (!double.IsFinite(angleDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(angleDegrees), "L'angolo deve essere un numero finito.");
        }

        long columnBytes = 3L * frameHeight;
        long initial = (long)expectedFrames * columnBytes;
        if (initial > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedFrames), "Troppi frame attesi per il buffer.");
        }

        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _columns = new byte[initial];
        _timestamps = new List<DateTimeOffset>(expectedFrames);
        (_srcX, _srcY) = BuildSamplingTable(frameWidth, frameHeight, angleDegrees);
    }

    /// <summary>Numero di frame aggiunti finora.</summary>
    public int FrameCount => _frameCount;

    /// <summary>Larghezza dell'immagine che restituirebbe <see cref="Build"/>: il numero di frame, al massimo <see cref="MaxColumns"/>.</summary>
    public int OutputWidth => Math.Min(_frameCount, MaxColumns);

    /// <summary>Timestamp dei frame aggiunti, nello stesso ordine delle colonne (non conservano i pixel).</summary>
    public IReadOnlyList<DateTimeOffset> Timestamps => _timestamps;

    /// <summary>
    /// Aggiunge la colonna del frame come colonna successiva del keogram.
    /// </summary>
    /// <param name="rgb8">Frame RGB 8 bit, row-major, lunghezza esattamente <c>W * H * 3</c>.</param>
    /// <param name="timestamp">Istante di cattura del frame.</param>
    /// <exception cref="ArgumentException">Lunghezza di <paramref name="rgb8"/> diversa da <c>W * H * 3</c>.</exception>
    public void AddFrame(ReadOnlySpan<byte> rgb8, DateTimeOffset timestamp)
    {
        long expected = 3L * _frameWidth * _frameHeight;
        if (rgb8.Length != expected)
        {
            throw new ArgumentException($"Il frame deve avere {expected} byte (RGB 8 bit), ricevuti {rgb8.Length}.", nameof(rgb8));
        }

        int rowBytes = 3 * _frameHeight;
        EnsureCapacity((long)(_frameCount + 1) * rowBytes);

        Span<byte> column = _columns.AsSpan(_frameCount * rowBytes, rowBytes);
        for (int y = 0; y < _frameHeight; y++)
        {
            int src = (_srcY[y] * _frameWidth + _srcX[y]) * 3;
            int dst = y * 3;
            column[dst] = rgb8[src];
            column[dst + 1] = rgb8[src + 1];
            column[dst + 2] = rgb8[src + 2];
        }

        _timestamps.Add(timestamp);
        _frameCount++;
    }

    /// <summary>
    /// Restituisce il keogram come RGB 8 bit row-major (3 byte per pixel), di dimensione
    /// <see cref="OutputWidth"/> per <c>frameHeight</c>. Senza frame restituisce un array vuoto.
    /// Se i frame sono più di <see cref="MaxColumns"/>, ogni colonna di output è la media (arrotondata) di un blocco di colonne.
    /// </summary>
    public byte[] Build()
    {
        int n = _frameCount;
        if (n == 0)
        {
            return Array.Empty<byte>();
        }

        int height = _frameHeight;
        int rowBytes = 3 * height;
        int outW = Math.Min(n, MaxColumns);
        byte[] result = new byte[checked(outW * height * 3)];

        if (n <= MaxColumns)
        {
            for (int k = 0; k < n; k++)
            {
                int colStart = k * rowBytes;
                for (int y = 0; y < height; y++)
                {
                    int dst = (y * outW + k) * 3;
                    int src = colStart + y * 3;
                    result[dst] = _columns[src];
                    result[dst + 1] = _columns[src + 1];
                    result[dst + 2] = _columns[src + 2];
                }
            }

            return result;
        }

        for (int k = 0; k < outW; k++)
        {
            long start = (long)k * n / outW;
            long end = (long)(k + 1) * n / outW;
            int count = (int)(end - start);
            for (int y = 0; y < height; y++)
            {
                for (int c = 0; c < 3; c++)
                {
                    long sum = 0;
                    int offset = y * 3 + c;
                    for (long f = start; f < end; f++)
                    {
                        sum += _columns[(int)(f * rowBytes) + offset];
                    }

                    // Media arrotondata: il risultato sta sempre in 0..255.
                    result[(y * outW + k) * 3 + c] = (byte)((sum + count / 2) / count);
                }
            }
        }

        return result;
    }

    private void EnsureCapacity(long needed)
    {
        if (needed <= _columns.Length)
        {
            return;
        }

        if (needed > Array.MaxLength)
        {
            throw new InvalidOperationException("Il keogram supera la dimensione massima di buffer supportata.");
        }

        long grown = Math.Max(needed, (long)_columns.Length * 2);
        Array.Resize(ref _columns, (int)Math.Min(grown, Array.MaxLength));
    }

    /// <summary>
    /// Per ogni riga di output calcola il pixel sorgente sul diametro: punto <c>centro + s * (sin a, cos a)</c>,
    /// con <c>s = j - (H-1)/2</c>, arrotondato al pixel più vicino e limitato al frame.
    /// </summary>
    private static (int[] X, int[] Y) BuildSamplingTable(int width, int height, double angleDegrees)
    {
        double cx = (width - 1) / 2.0;
        double cy = (height - 1) / 2.0;
        (double sin, double cos) = Math.SinCos(angleDegrees * Math.PI / 180.0);

        var xs = new int[height];
        var ys = new int[height];
        for (int j = 0; j < height; j++)
        {
            double s = j - cy;
            xs[j] = Nearest(cx + s * sin, width - 1);
            ys[j] = Nearest(cy + s * cos, height - 1);
        }

        return (xs, ys);
    }

    private static int Nearest(double value, int max)
    {
        return (int)Math.Clamp(Math.Floor(value + 0.5), 0, max);
    }
}
