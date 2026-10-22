using System;
using System.Collections.Generic;

namespace AllSky.Core.Design;

/// <summary>Una stella dello sfondo, in pixel dell'area di disegno.</summary>
/// <param name="X">Ascissa in pixel, da 0 a larghezza.</param>
/// <param name="Y">Ordinata in pixel, da 0 a altezza (verso il basso).</param>
/// <param name="Magnitude">Magnitudine apparente (più basso = più luminosa).</param>
/// <param name="Radius">Raggio del disco in pixel.</param>
/// <param name="Alpha">Opacità da 0.2 a 1, legata alla luminosità.</param>
public sealed record Star(double X, double Y, double Magnitude, double Radius, double Alpha);

/// <summary>
/// Parametri dello sfondo stellare. Il campo visivo è in gradi, così la densità è quella del cielo reale.
/// </summary>
public sealed record StarfieldOptions
{
    /// <summary>Ampiezza in gradi della porzione di cielo mostrata in orizzontale. L'altezza segue il rapporto d'aspetto.</summary>
    public double FieldWidthDegrees { get; init; } = 60;

    /// <summary>Moltiplicatore della densità (1 = densità reale a occhio nudo). Serve solo nei test.</summary>
    public double DensityScale { get; init; } = 1.0;

    /// <summary>Magnitudine più luminosa generata (Sirio è circa -1.5).</summary>
    public double MinMagnitude { get; init; } = -1.5;

    /// <summary>Magnitudine limite, quella dell'occhio nudo in cielo buio.</summary>
    public double MaxMagnitude { get; init; } = 6.5;

    /// <summary>
    /// Pendenza logaritmica del conteggio: N(&lt;m) cresce come 10^(LogSlope·m). Il valore 0.5 è la regola empirica
    /// delle conte stellari (circa tre volte più stelle per magnitudine), non un catalogo.
    /// </summary>
    public double LogSlope { get; init; } = 0.5;

    /// <summary>Frazione delle stelle addensate sulla banda della Via Lattea, il resto è distribuito uniformemente.</summary>
    public double BandFraction { get; init; } = 0.4;

    /// <summary>Deviazione standard trasversale della banda, in gradi: la banda è sottile, come la Via Lattea in cielo buio.</summary>
    public double BandSigmaDegrees { get; init; } = 4;

    /// <summary>Angolo della banda in gradi, da 0 orizzontale, positivo in senso antiorario sullo schermo.</summary>
    public double BandAngleDegrees { get; init; } = -28;
}

/// <summary>
/// Genera le stelle di uno sfondo statico. Logica pura: nessun riferimento a WPF, quindi testabile ovunque.
/// </summary>
/// <remarks>
/// Stesso seme, stesse dimensioni e stesse opzioni danno sempre gli stessi punti. Il generatore è SplitMix64,
/// scritto qui e non il <see cref="Random"/> di .NET, così il risultato non cambia con la versione del runtime.
/// Densità: circa 9100 stelle entro la magnitudine 6.5 su tutto il cielo, cioè 9096 / 41253 deg² ≈ 0.22 per grado quadrato.
/// </remarks>
public static class StarfieldGenerator
{
    /// <summary>Stelle a occhio nudo (magnitudine ≤ 6.5) per grado quadrato, media su tutto il cielo.</summary>
    public const double NakedEyeStarsPerSquareDegree = 9096.0 / 41253.0;

    /// <summary>Genera le stelle per un'area di <paramref name="width"/>×<paramref name="height"/> pixel.</summary>
    /// <param name="seed">Seme del generatore. Lo sfondo della UI usa un seme fisso.</param>
    /// <param name="width">Larghezza dell'area in pixel, maggiore di zero.</param>
    /// <param name="height">Altezza dell'area in pixel, maggiore di zero.</param>
    /// <param name="options">Parametri; se null si usano i valori predefiniti.</param>
    /// <exception cref="ArgumentOutOfRangeException">Dimensioni non positive o opzioni non valide.</exception>
    public static IReadOnlyList<Star> Generate(int seed, int width, int height, StarfieldOptions? options = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "La larghezza deve essere maggiore di zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "L'altezza deve essere maggiore di zero.");
        }

        options ??= new StarfieldOptions();
        Validate(options);

        double pixelsPerDegree = width / options.FieldWidthDegrees;
        double fieldHeightDegrees = height / pixelsPerDegree;
        double skyDegrees2 = options.FieldWidthDegrees * fieldHeightDegrees;
        int total = (int)Math.Round(NakedEyeStarsPerSquareDegree * options.DensityScale * skyDegrees2);
        int inBand = (int)Math.Round(total * options.BandFraction);

        var rng = new SplitMix64((ulong)(uint)seed);
        var stars = new List<Star>(total);

        for (int i = 0; i < total - inBand; i++)
        {
            double x = rng.NextDouble() * width;
            double y = rng.NextDouble() * height;
            stars.Add(MakeStar(rng, x, y, options));
        }

        // Banda: lungo la direzione della banda, con scarto trasversale gaussiano. Si scartano i punti fuori dall'area.
        double theta = options.BandAngleDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(theta);
        double dirY = -Math.Sin(theta);   // schermo: y verso il basso
        double perpX = -dirY;
        double perpY = dirX;
        double centerX = width / 2.0;
        double centerY = height / 2.0;
        double halfDiagonal = Math.Sqrt((double)width * width + (double)height * height) / 2.0;
        double sigmaPixels = options.BandSigmaDegrees * pixelsPerDegree;

        for (int i = 0; i < inBand; i++)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                double along = (rng.NextDouble() * 2 - 1) * halfDiagonal;
                double across = rng.NextGaussian() * sigmaPixels;
                double x = centerX + along * dirX + across * perpX;
                double y = centerY + along * dirY + across * perpY;
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    stars.Add(MakeStar(rng, x, y, options));
                    break;
                }
            }
        }

        return stars;
    }

    /// <summary>Magnitudine, raggio e opacità di una stella alla posizione indicata.</summary>
    private static Star MakeStar(SplitMix64 rng, double x, double y, StarfieldOptions options)
    {
        double magnitude = SampleMagnitude(rng.NextDouble(), options);

        // Flusso relativo alla stella più brillante del campione: 2.512^-(m - m_min).
        double flux = Math.Pow(10, -0.4 * (magnitude - options.MinMagnitude));
        double radius = 0.35 + 1.25 * Math.Sqrt(flux);
        double alpha = Math.Clamp(0.2 + 0.8 * Math.Pow(flux, 0.4), 0.2, 1.0);
        return new Star(x, y, magnitude, radius, alpha);
    }

    /// <summary>
    /// Campiona la magnitudine con la distribuzione inversa di N(&lt;m) ∝ 10^(LogSlope·m) su [MinMagnitude, MaxMagnitude].
    /// </summary>
    internal static double SampleMagnitude(double u, StarfieldOptions options)
    {
        double b = options.LogSlope;
        double low = Math.Pow(10, b * options.MinMagnitude);
        double high = Math.Pow(10, b * options.MaxMagnitude);
        double m = Math.Log10(low + u * (high - low)) / b;
        return Math.Clamp(m, options.MinMagnitude, options.MaxMagnitude);
    }

    private static void Validate(StarfieldOptions options)
    {
        if (!(options.FieldWidthDegrees > 0) || !(options.FieldWidthDegrees <= 360))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Il campo visivo deve essere tra 0 e 360 gradi.");
        }

        if (!(options.DensityScale > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "La densità deve essere positiva.");
        }

        if (!(options.MaxMagnitude > options.MinMagnitude) || !(options.LogSlope > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Intervallo di magnitudine o pendenza non validi.");
        }

        if (options.BandFraction < 0 || options.BandFraction > 1 || !(options.BandSigmaDegrees > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Frazione o larghezza della banda non valide.");
        }
    }

    /// <summary>Generatore SplitMix64: piccolo, veloce e identico su ogni piattaforma.</summary>
    private sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed) => _state = seed;

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Numero in [0, 1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Gaussiana standard con Box-Muller.</summary>
        public double NextGaussian()
        {
            double u1 = 1.0 - NextDouble();   // (0, 1], evita il logaritmo di zero
            double u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
