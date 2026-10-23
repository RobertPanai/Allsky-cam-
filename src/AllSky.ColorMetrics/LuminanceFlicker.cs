namespace AllSky.ColorMetrics;

/// <summary>
/// Flicker di luminanza. Il trend lento è la mediana mobile centrata della luminanza media del frame
/// (finestra in frame, <see cref="DefaultWindow"/> = 15). Residuo relativo: e_i = (L_i - M_i) / M_i.
/// <list type="bullet">
/// <item><see cref="FlickerResult.StdPercent"/>: deviazione standard campionaria di e_i, in %.</item>
/// <item><see cref="FlickerResult.P95AbsPercent"/>: 95° percentile di |e_i|, in %.</item>
/// </list>
/// Limite del metodo: la mediana mobile segue le oscillazioni periodiche più corte della finestra. Per un flicker
/// che alterna due valori ±a (periodo 2) la mediana prende il valore opposto e il residuo vale circa 2a
/// (escursione picco-picco), non a. Vedi i test.
/// </summary>
public static class LuminanceFlicker
{
    /// <summary>Ampiezza della finestra della mediana mobile, in frame.</summary>
    public const int DefaultWindow = 15;

    /// <summary>Calcola il flicker. I frame sono ordinati per istante.</summary>
    public static FlickerResult Measure(IReadOnlyList<FrameSample> frames, int window = DefaultWindow)
    {
        ArgumentNullException.ThrowIfNull(frames);
        FrameSample[] ordered = frames.OrderBy(f => f.TimeUtc).ToArray();
        double[] lum = ordered.Select(f => f.MeanLuminance).ToArray();
        if (lum.Length < 3)
        {
            return new FlickerResult(lum.Length, window, null, null);
        }

        double[] trend = Robust.CenteredMedian(lum, window);
        var rel = new List<double>(lum.Length);
        for (int i = 0; i < lum.Length; i++)
        {
            if (trend[i] > 0)
            {
                rel.Add((lum[i] - trend[i]) / trend[i]);
            }
        }

        if (rel.Count < 2)
        {
            return new FlickerResult(lum.Length, window, null, null);
        }

        double[] arr = rel.ToArray();
        double? std = Robust.SampleStd(arr);
        double p95 = Robust.Percentile(arr.Select(Math.Abs).ToArray(), 0.95);
        return new FlickerResult(lum.Length, window, std * 100.0, p95 * 100.0);
    }
}

/// <summary>Risultato del flicker. Valori in percento della luminanza locale.</summary>
public sealed record FlickerResult(int Frames, int Window, double? StdPercent, double? P95AbsPercent);
