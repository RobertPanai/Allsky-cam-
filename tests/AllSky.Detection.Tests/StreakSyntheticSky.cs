using AllSky.Detection;

namespace AllSky.Detection.Tests;

/// <summary>
/// Scia sintetica iniettata in un frame: segmento da (X0, Y0) a (X1, Y1), spessore circa 2 px,
/// eccesso di luminanza di picco <see cref="Excess"/> sopra il fondo.
/// </summary>
public readonly record struct SyntheticTrail(double X0, double Y0, double X1, double Y1, double Excess)
{
    // Offset di mezzo pixel applicato sia al disegno sia al confronto: con 0.5 la banda di 2 px e' centrata sulle righe.
    public const double Offset = 0.5;

    public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));

    /// <summary>True se la rilevazione corrisponde a questa scia: punto medio entro 4 px e angolo entro 5 gradi.</summary>
    public bool Matches(Streak s)
    {
        double ax = X0 + Offset;
        double ay = Y0 + Offset;
        double bx = X1 + Offset;
        double by = Y1 + Offset;

        double mx = (s.X1 + s.X2) / 2.0;
        double my = (s.Y1 + s.Y2) / 2.0;
        double distance = DistanceToSegment(mx, my, ax, ay, bx, by);

        double trailAngle = Math.Atan2(by - ay, bx - ax);
        double streakAngle = Math.Atan2(s.Y2 - s.Y1, s.X2 - s.X1);
        double angleDiff = Math.Abs(Math.IEEERemainder(trailAngle - streakAngle, Math.PI));

        return distance <= 4.0 && angleDiff <= 5.0 * Math.PI / 180.0;
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax;
        double vy = by - ay;
        double len2 = vx * vx + vy * vy;
        double t = len2 > 0 ? Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len2, 0.0, 1.0) : 0.0;
        double dx = px - (ax + t * vx);
        double dy = py - (ay + t * vy);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Generatore deterministico di frame notturni 960x720: fondo gaussiano (media 20, sigma 3),
/// 150 stelle fisse (puntini di circa 2-3 px, picco 80-200) e scie iniettate.
/// Stesso seed, stessi frame.
/// </summary>
internal sealed class StreakSyntheticSky
{
    public const int Width = 960;
    public const int Height = 720;
    public const int StarCount = 150;
    public const double BackgroundMean = 20.0;
    public const double NoiseSigma = 3.0;

    // Margine dal bordo per i punti di partenza e di arrivo delle scie.
    private const double Margin = 10.0;

    private readonly Random _rng;
    private readonly double[] _starX = new double[StarCount];
    private readonly double[] _starY = new double[StarCount];
    private readonly double[] _starPeak = new double[StarCount];
    private bool _hasSpare;
    private double _spare;

    public StreakSyntheticSky(int seed)
    {
        _rng = new Random(seed);
        for (int s = 0; s < StarCount; s++)
        {
            _starX[s] = Math.Floor(5 + _rng.NextDouble() * (Width - 10));
            _starY[s] = Math.Floor(5 + _rng.NextDouble() * (Height - 10));
            _starPeak[s] = 80 + _rng.NextDouble() * 120;
        }
    }

    /// <summary>Fattori moltiplicativi per le stelle, uniformi in [1 - maxRelative, 1 + maxRelative].</summary>
    public double[] RandomStarScale(double maxRelative)
    {
        var scale = new double[StarCount];
        for (int s = 0; s < StarCount; s++)
        {
            scale[s] = 1.0 + (2.0 * _rng.NextDouble() - 1.0) * maxRelative;
        }
        return scale;
    }

    /// <summary>Scia casuale con lunghezza in [minLength, maxLength] e SNR in [minSnr, maxSnr] (SNR = eccesso di picco / sigma del fondo).</summary>
    public SyntheticTrail RandomTrail(double minSnr, double maxSnr, double minLength, double maxLength)
    {
        while (true)
        {
            double length = minLength + _rng.NextDouble() * (maxLength - minLength);
            double angle = _rng.NextDouble() * 2.0 * Math.PI;
            double x0 = Margin + _rng.NextDouble() * (Width - 2 * Margin);
            double y0 = Margin + _rng.NextDouble() * (Height - 2 * Margin);
            double x1 = x0 + length * Math.Cos(angle);
            double y1 = y0 + length * Math.Sin(angle);
            if (x1 < Margin || x1 > Width - Margin || y1 < Margin || y1 > Height - Margin)
            {
                continue;
            }

            double snr = minSnr + _rng.NextDouble() * (maxSnr - minSnr);
            return new SyntheticTrail(x0, y0, x1, y1, snr * NoiseSigma);
        }
    }

    /// <summary>
    /// Genera un frame. <paramref name="starScale"/> modula la luminosita' delle stelle (null = invariate).
    /// <paramref name="illuminatedFraction"/> e <paramref name="illuminatedBoost"/> aggiungono un'illuminazione
    /// globale a una frazione casuale di pixel.
    /// </summary>
    public byte[] Render(
        IReadOnlyList<SyntheticTrail>? trails = null,
        double[]? starScale = null,
        double illuminatedFraction = 0.0,
        double illuminatedBoost = 0.0)
    {
        var excess = new float[Width * Height];
        for (int s = 0; s < StarCount; s++)
        {
            double k = starScale is null ? 1.0 : starScale[s];
            StampStar(excess, _starX[s], _starY[s], (_starPeak[s] - BackgroundMean) * k);
        }

        if (trails is not null)
        {
            foreach (var trail in trails)
            {
                StampTrail(excess, trail);
            }
        }

        var pixels = new byte[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            double v = BackgroundMean + NextGaussian() * NoiseSigma + excess[i];
            if (illuminatedFraction > 0 && _rng.NextDouble() < illuminatedFraction)
            {
                v += illuminatedBoost;
            }
            pixels[i] = (byte)Math.Clamp(Math.Round(v), 0.0, 255.0);
        }
        return pixels;
    }

    // Puntino: centro al picco, quattro vicini al 50 per cento (circa 2-3 px visibili).
    private static void StampStar(float[] excess, double x, double y, double amplitude)
    {
        int cx = (int)x;
        int cy = (int)y;
        Put(excess, cx, cy, (float)amplitude);
        Put(excess, cx + 1, cy, (float)(0.5 * amplitude));
        Put(excess, cx - 1, cy, (float)(0.5 * amplitude));
        Put(excess, cx, cy + 1, (float)(0.5 * amplitude));
        Put(excess, cx, cy - 1, (float)(0.5 * amplitude));
    }

    // Banda di spessore circa 2 px: pixel con distanza dal segmento minore di 0.9.
    private static void StampTrail(float[] excess, SyntheticTrail t)
    {
        double ax = t.X0 + SyntheticTrail.Offset;
        double ay = t.Y0 + SyntheticTrail.Offset;
        double bx = t.X1 + SyntheticTrail.Offset;
        double by = t.Y1 + SyntheticTrail.Offset;
        double vx = bx - ax;
        double vy = by - ay;
        double len2 = vx * vx + vy * vy;

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx)) - 3);
        int maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, bx)) + 3);
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, by)) - 3);
        int maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, by)) + 3);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x - ax;
                double py = y - ay;
                double tt = Math.Clamp((px * vx + py * vy) / len2, 0.0, 1.0);
                double dx = px - tt * vx;
                double dy = py - tt * vy;
                if (dx * dx + dy * dy <= 0.9 * 0.9)
                {
                    int i = y * Width + x;
                    excess[i] = Math.Max(excess[i], (float)t.Excess);
                }
            }
        }
    }

    private static void Put(float[] excess, int x, int y, float value)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            return;
        }
        int i = y * Width + x;
        excess[i] = Math.Max(excess[i], value);
    }

    // Box-Muller con seconda deviata in cache: deterministico rispetto al seed.
    private double NextGaussian()
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return _spare;
        }

        double u1 = 1.0 - _rng.NextDouble();
        double u2 = _rng.NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        _spare = r * Math.Sin(theta);
        _hasSpare = true;
        return r * Math.Cos(theta);
    }
}
