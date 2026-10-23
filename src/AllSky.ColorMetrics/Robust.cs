namespace AllSky.ColorMetrics;

/// <summary>
/// Statistiche usate dalle metriche. Funzioni pure: non modificano gli argomenti.
/// Le serie vuote lanciano <see cref="ArgumentException"/> dove il valore non è definito; le funzioni che possono
/// non avere risposta (deviazione standard con meno di 2 valori, pendenza senza varianza) restituiscono null.
/// </summary>
public static class Robust
{
    /// <summary>Mediana. Per un numero pari di valori, media dei due centrali.</summary>
    public static double Median(double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Serie vuota.", nameof(values));
        }

        double[] copy = (double[])values.Clone();
        Array.Sort(copy);
        int n = copy.Length;
        return n % 2 == 1 ? copy[n / 2] : 0.5 * (copy[n / 2 - 1] + copy[n / 2]);
    }

    /// <summary>Percentile p (0..1) con interpolazione lineare fra i valori ordinati.</summary>
    public static double Percentile(double[] values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Serie vuota.", nameof(values));
        }

        if (!(p >= 0 && p <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Il percentile deve essere fra 0 e 1.");
        }

        double[] copy = (double[])values.Clone();
        Array.Sort(copy);
        double pos = p * (copy.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        return copy[lo] + (copy[hi] - copy[lo]) * (pos - lo);
    }

    /// <summary>Deviazione standard campionaria (denominatore n-1). Null con meno di 2 valori.</summary>
    public static double? SampleStd(double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Length;
        if (n < 2)
        {
            return null;
        }

        double mean = values.Average();
        double ss = 0;
        foreach (double v in values)
        {
            ss += (v - mean) * (v - mean);
        }

        return Math.Sqrt(ss / (n - 1));
    }

    /// <summary>Scarto mediano assoluto: mediana di |x - mediana(x)|.</summary>
    public static double Mad(double[] values)
    {
        double med = Median(values);
        double[] dev = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            dev[i] = Math.Abs(values[i] - med);
        }

        return Median(dev);
    }

    /// <summary>
    /// Mediana mobile centrata su una finestra di <paramref name="window"/> valori (forzata a dispari, minimo 1).
    /// Ai bordi la finestra si restringe in modo simmetrico (stessa mezza ampiezza sui due lati), così una rampa
    /// lineare resta esatta anche lì.
    /// </summary>
    public static double[] CenteredMedian(double[] values, int window)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Serie vuota.", nameof(values));
        }

        int w = Math.Max(1, window);
        if (w % 2 == 0)
        {
            w++;
        }

        int half = w / 2;
        int n = values.Length;
        var result = new double[n];
        var buffer = new double[w];
        for (int i = 0; i < n; i++)
        {
            int h = Math.Min(half, Math.Min(i, n - 1 - i));
            int len = 2 * h + 1;
            Array.Copy(values, i - h, buffer, 0, len);
            Array.Sort(buffer, 0, len);
            result[i] = buffer[h];
        }

        return result;
    }

    /// <summary>Pendenza della regressione lineare ai minimi quadrati di y su x. Null se x non varia.</summary>
    public static double? Slope(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("x e y devono avere la stessa lunghezza.");
        }

        int n = x.Length;
        if (n < 2)
        {
            return null;
        }

        double mx = x.Average();
        double my = y.Average();
        double sxy = 0;
        double sxx = 0;
        for (int i = 0; i < n; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
        }

        return sxx > 0 ? sxy / sxx : null;
    }
}
