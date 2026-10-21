namespace AllSky.Engine;

/// <summary>
/// Sanifica i limiti di esposizione letti dal driver prima di usarli nel controller di esposizione.
/// Un driver può riportare 0, NaN, infiniti o un massimo sotto il minimo: qui si ripiega su valori sicuri.
/// Funzione pura: non lancia mai e non restituisce NaN o infiniti.
/// </summary>
public static class ExposureLimits
{
    /// <summary>Minimo di ripiego quando il driver non fornisce un valore valido: 1 µs.</summary>
    public const double FallbackMinMs = 0.001;

    /// <summary>Massimo di ripiego quando né il driver né la configurazione forniscono un valore valido.</summary>
    public const double FallbackMaxMs = 30000.0;

    /// <summary>Vero se <paramref name="valueMs"/> è finito e strettamente positivo.</summary>
    public static bool IsUsable(double valueMs) => double.IsFinite(valueMs) && valueMs > 0.0;

    /// <summary>
    /// Restituisce (min, max) in millisecondi.
    /// <list type="bullet">
    /// <item>min: il valore del driver se è valido, altrimenti <see cref="FallbackMinMs"/>.</item>
    /// <item>max: il minimo tra la configurazione (o <see cref="FallbackMaxMs"/> se non valida) e il massimo del driver
    /// (ignorato se non valido).</item>
    /// <item>Se max risulta sotto min, max diventa min.</item>
    /// </list>
    /// </summary>
    public static (double Min, double Max) Sanitize(double infoMinMs, double infoMaxMs, double configMaxMs)
    {
        double min = IsUsable(infoMinMs) ? infoMinMs : FallbackMinMs;

        double max = IsUsable(configMaxMs) ? configMaxMs : FallbackMaxMs;
        if (IsUsable(infoMaxMs))
        {
            max = Math.Min(max, infoMaxMs);
        }

        if (max < min)
        {
            max = min;
        }

        return (min, max);
    }
}
