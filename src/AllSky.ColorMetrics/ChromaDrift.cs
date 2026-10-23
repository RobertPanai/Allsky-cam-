namespace AllSky.ColorMetrics;

/// <summary>
/// Deriva cromatica del fondo cielo. Per ogni frame: R/G e B/G dal fondo (mediana). Per ciascun rapporto:
/// escursione totale (max - min), pendenza per ora (regressione lineare sul tempo reale) e deviazione standard
/// campionaria dei residui dopo la mediana mobile centrata (finestra <see cref="LuminanceFlicker.DefaultWindow"/>).
/// </summary>
public static class ChromaDrift
{
    /// <summary>Statistiche di un rapporto nel tempo.</summary>
    /// <param name="Excursion">max - min del rapporto nella notte.</param>
    /// <param name="SlopePerHour">Pendenza della regressione lineare, unità del rapporto per ora.</param>
    /// <param name="ResidualStd">Deviazione standard dei residui dopo il trend lento, unità del rapporto.</param>
    /// <param name="ResidualStdPercent">Stessa deviazione in % del rapporto medio.</param>
    public sealed record RatioDrift(double? Excursion, double? SlopePerHour, double? ResidualStd, double? ResidualStdPercent);

    /// <summary>Risultato: R/G e B/G. <see cref="UsedFrames"/> conta i frame con G > 0.</summary>
    public sealed record ChromaDriftResult(int UsedFrames, double? SpanHours, RatioDrift RG, RatioDrift BG);

    /// <summary>Calcola la deriva dei rapporti R/G e B/G. I frame con G nullo non entrano.</summary>
    public static ChromaDriftResult Measure(IReadOnlyList<FrameSample> frames, int window = LuminanceFlicker.DefaultWindow)
    {
        ArgumentNullException.ThrowIfNull(frames);
        FrameSample[] ordered = frames.Where(f => f.SkyG > 0).OrderBy(f => f.TimeUtc).ToArray();
        int n = ordered.Length;
        if (n < 2)
        {
            var empty = new RatioDrift(null, null, null, null);
            return new ChromaDriftResult(n, null, empty, empty);
        }

        double[] hours = ordered.Select(f => (f.TimeUtc - ordered[0].TimeUtc).TotalHours).ToArray();
        double[] rg = ordered.Select(f => f.SkyR / f.SkyG).ToArray();
        double[] bg = ordered.Select(f => f.SkyB / f.SkyG).ToArray();
        return new ChromaDriftResult(n, hours[^1], Describe(hours, rg, window), Describe(hours, bg, window));
    }

    private static RatioDrift Describe(double[] hours, double[] ratio, int window)
    {
        double excursion = ratio.Max() - ratio.Min();
        double? slope = Robust.Slope(hours, ratio);
        double[] trend = Robust.CenteredMedian(ratio, window);
        double[] residuals = new double[ratio.Length];
        for (int i = 0; i < ratio.Length; i++)
        {
            residuals[i] = ratio[i] - trend[i];
        }

        double? std = Robust.SampleStd(residuals);
        double mean = ratio.Average();
        double? stdPct = std is { } s && mean != 0 ? s / Math.Abs(mean) * 100.0 : null;
        return new RatioDrift(excursion, slope, std, stdPct);
    }
}
