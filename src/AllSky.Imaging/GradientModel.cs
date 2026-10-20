namespace AllSky.Imaging;

/// <summary>
/// Modello del gradiente di fondo, per canale (0 = R, 1 = G, 2 = B).
/// Il fondo e' un polinomio di grado 2 con 6 coefficienti nelle coordinate normalizzate
/// u, v in [-1, 1] (u lungo la larghezza, v lungo l'altezza): base (1, u, v, u^2, uv, v^2).
/// Contiene anche la media del modello sui pixel di cielo, per sottrarre solo la parte variabile.
/// </summary>
public sealed class GradientModel
{
    private readonly double[][] _coefficients;
    private readonly double[] _skyMean;

    internal GradientModel(int width, int height, double[][] coefficients, double[] skyMean)
    {
        Width = width;
        Height = height;
        _coefficients = coefficients;
        _skyMean = skyMean;
    }

    /// <summary>Larghezza dell'immagine usata per il fit.</summary>
    public int Width { get; }

    /// <summary>Altezza dell'immagine usata per il fit.</summary>
    public int Height { get; }

    /// <summary>Copia dei 6 coefficienti del canale, in ordine (1, u, v, u^2, uv, v^2).</summary>
    public double[] GetCoefficients(int channel)
    {
        CheckChannel(channel);
        return (double[])_coefficients[channel].Clone();
    }

    /// <summary>Media del modello sui pixel di cielo del canale (usata da <see cref="GradientRemoval.Apply"/>).</summary>
    public double GetSkyMean(int channel)
    {
        CheckChannel(channel);
        return _skyMean[channel];
    }

    /// <summary>Valore del fondo stimato nel pixel (x, y) del canale, senza centratura sulla media del cielo.</summary>
    public double EvaluateAt(int channel, int x, int y)
    {
        CheckChannel(channel);
        return Evaluate(_coefficients[channel], NormU(x, Width), NormV(y, Height));
    }

    internal double[] CoefficientsRef(int channel) => _coefficients[channel];

    internal double SkyMeanRef(int channel) => _skyMean[channel];

    // Coordinata normalizzata in [-1, 1]; un'immagine di larghezza 1 ha u = 0.
    internal static double NormU(double x, int width) => width > 1 ? 2.0 * x / (width - 1) - 1.0 : 0.0;

    internal static double NormV(double y, int height) => height > 1 ? 2.0 * y / (height - 1) - 1.0 : 0.0;

    internal static double Evaluate(double[] c, double u, double v)
        => c[0] + c[1] * u + c[2] * v + c[3] * u * u + c[4] * u * v + c[5] * v * v;

    private static void CheckChannel(int channel)
    {
        if (channel < 0 || channel > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "Il canale deve essere 0 (R), 1 (G) o 2 (B).");
        }
    }
}
