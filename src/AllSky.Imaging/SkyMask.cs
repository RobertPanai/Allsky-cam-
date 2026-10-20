namespace AllSky.Imaging;

/// <summary>
/// Maschera booleana del cielo, un valore per pixel in ordine row-major (indice = y * Width + x).
/// True = pixel di cielo. La maschera e' immutabile: <see cref="WithExcludedDiscs"/> ne restituisce una nuova.
/// Coordinate dei pixel: x e y interi, con (0, 0) in alto a sinistra.
/// </summary>
public sealed class SkyMask
{
    private readonly bool[] _sky;

    private SkyMask(int width, int height, bool[] sky)
    {
        Width = width;
        Height = height;
        _sky = sky;

        int count = 0;
        for (int i = 0; i < sky.Length; i++)
        {
            if (sky[i])
            {
                count++;
            }
        }

        PixelCount = count;
    }

    /// <summary>Larghezza in pixel.</summary>
    public int Width { get; }

    /// <summary>Altezza in pixel.</summary>
    public int Height { get; }

    /// <summary>Numero di pixel di cielo (valori true).</summary>
    public int PixelCount { get; }

    /// <summary>Accesso interno ai valori, senza copia. Non va modificato.</summary>
    internal bool[] Values => _sky;

    /// <summary>
    /// Genera la maschera da un cerchio fisheye (equidistante) e da un profilo d'orizzonte opzionale.
    /// Un pixel e' cielo se cade dentro il cerchio e la sua elevazione e' maggiore o uguale
    /// all'orizzonte nel suo azimut. L'azimut e' misurato in senso orario dall'alto dell'immagine;
    /// il verso Est (sinistra o destra) segue <see cref="SkyMaskOptions.EastLeft"/>.
    /// </summary>
    public static SkyMask Create(int width, int height, SkyMaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateSize(width, height);
        if (!double.IsFinite(options.CenterX) || !double.IsFinite(options.CenterY))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Centro del cerchio non finito.");
        }

        if (!double.IsFinite(options.Radius) || options.Radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Il raggio deve essere finito e maggiore di zero.");
        }

        double[]? hAz = null;
        double[]? hEl = null;
        if (options.Horizon != null)
        {
            (hAz, hEl) = BuildHorizon(options.Horizon);
        }

        double cx = options.CenterX * width;
        double cy = options.CenterY * height;
        double rPx = options.Radius * Math.Min(width, height) / 2.0;
        var sky = new bool[checked(width * height)];

        for (int y = 0; y < height; y++)
        {
            double dy = y - cy;
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx;
                double r = Math.Sqrt(dx * dx + dy * dy);
                if (r > rPx)
                {
                    continue;
                }

                double elev = 90.0 - 90.0 * r / rPx;
                double az = options.EastLeft
                    ? Math.Atan2(-dx, -dy)
                    : Math.Atan2(dx, -dy);
                double azDeg = NormalizeDeg(az * 180.0 / Math.PI);
                double horizon = hAz == null || hEl == null ? 0.0 : HorizonAt(hAz, hEl, azDeg);
                sky[y * width + x] = elev >= horizon;
            }
        }

        return new SkyMask(width, height, sky);
    }

    /// <summary>Maschera con tutti i pixel di cielo. Utile come riferimento o per test.</summary>
    public static SkyMask Full(int width, int height)
    {
        ValidateSize(width, height);
        var sky = new bool[checked(width * height)];
        Array.Fill(sky, true);
        return new SkyMask(width, height, sky);
    }

    /// <summary>
    /// Restituisce una nuova maschera in cui i pixel dentro uno o piu' dischi sono esclusi (false).
    /// Usare per Sole e Luna. I dischi sono (x, y, raggio) in pixel; il bordo e' incluso nel disco.
    /// </summary>
    public SkyMask WithExcludedDiscs(IEnumerable<(double X, double Y, double Radius)> discs)
    {
        ArgumentNullException.ThrowIfNull(discs);
        bool[] sky = (bool[])_sky.Clone();

        foreach (var disc in discs)
        {
            if (!double.IsFinite(disc.X) || !double.IsFinite(disc.Y) || !double.IsFinite(disc.Radius) || disc.Radius < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(discs), "Disco non valido: coordinate finite e raggio >= 0.");
            }

            int x0 = (int)Math.Clamp(Math.Floor(disc.X - disc.Radius), 0, Width - 1);
            int x1 = (int)Math.Clamp(Math.Ceiling(disc.X + disc.Radius), -1, Width - 1);
            int y0 = (int)Math.Clamp(Math.Floor(disc.Y - disc.Radius), 0, Height - 1);
            int y1 = (int)Math.Clamp(Math.Ceiling(disc.Y + disc.Radius), -1, Height - 1);
            double r2 = disc.Radius * disc.Radius;

            for (int y = y0; y <= y1; y++)
            {
                double ey = y - disc.Y;
                for (int x = x0; x <= x1; x++)
                {
                    double ex = x - disc.X;
                    if (ex * ex + ey * ey <= r2)
                    {
                        sky[y * Width + x] = false;
                    }
                }
            }
        }

        return new SkyMask(Width, Height, sky);
    }

    /// <summary>Restituisce true se il pixel (x, y) e' cielo. Lancia eccezione se fuori dall'immagine.</summary>
    public bool IsSky(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return _sky[y * Width + x];
    }

    /// <summary>Copia della maschera come bool[] di Width * Height elementi (compatibile con WhiteBalance).</summary>
    public bool[] ToBoolArray() => (bool[])_sky.Clone();

    /// <summary>Maschera a byte di Width * Height elementi: 255 = cielo, 0 = escluso.</summary>
    public byte[] ToByteMask()
    {
        var result = new byte[_sky.Length];
        for (int i = 0; i < _sky.Length; i++)
        {
            result[i] = _sky[i] ? (byte)255 : (byte)0;
        }

        return result;
    }

    private static void ValidateSize(int width, int height)
    {
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "La larghezza deve essere almeno 1.");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "L'altezza deve essere almeno 1.");
        }

        if ((long)width * height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Immagine troppo grande per una maschera.");
        }
    }

    // Valida il profilo e lo restituisce ordinato per azimut in [0, 360).
    private static (double[] Az, double[] El) BuildHorizon(IReadOnlyList<(double AzimuthDeg, double ElevationDeg)> horizon)
    {
        if (horizon.Count == 0)
        {
            throw new ArgumentException("Il profilo d'orizzonte deve avere almeno un punto.", nameof(horizon));
        }

        var pts = new (double Az, double El)[horizon.Count];
        for (int i = 0; i < pts.Length; i++)
        {
            (double az, double el) = horizon[i];
            if (!double.IsFinite(az) || !double.IsFinite(el) || el < -90.0 || el > 90.0)
            {
                throw new ArgumentOutOfRangeException(nameof(horizon), "Punto d'orizzonte non valido: azimut finito, elevazione in [-90, 90].");
            }

            pts[i] = (NormalizeDeg(az), el);
        }

        Array.Sort(pts, (p, q) => p.Az.CompareTo(q.Az));
        var az2 = new double[pts.Length];
        var el2 = new double[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            az2[i] = pts[i].Az;
            el2[i] = pts[i].El;
        }

        return (az2, el2);
    }

    // Interpolazione lineare circolare: il segmento dall'ultimo punto al primo passa per 360 gradi.
    // az deve essere ordinato in [0, 360).
    private static double HorizonAt(double[] az, double[] el, double a)
    {
        int n = az.Length;
        if (n == 1)
        {
            return el[0];
        }

        for (int i = 0; i < n; i++)
        {
            double a0 = az[i];
            double a1 = i + 1 < n ? az[i + 1] : az[0] + 360.0;
            double aa = a < a0 ? a + 360.0 : a;
            if (aa >= a0 && aa < a1)
            {
                double t = (aa - a0) / (a1 - a0);
                int j = (i + 1) % n;
                return el[i] + (el[j] - el[i]) * t;
            }
        }

        return el[n - 1];
    }

    private static double NormalizeDeg(double deg)
    {
        double d = deg % 360.0;
        if (d < 0.0)
        {
            d += 360.0;
        }

        return d >= 360.0 ? 0.0 : d;
    }
}
