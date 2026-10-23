using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class LuminanceFlickerTests
{
    private static List<FrameSample> Series(int n, Func<int, double> luma) =>
        Enumerable.Range(0, n).Select(i => ColorTestData.Sample(luma(i), 0.4, 0.5, 0.5, i)).ToList();

    [Fact]
    public void NessunFlicker_ResiduoZero()
    {
        FlickerResult r = LuminanceFlicker.Measure(Series(300, _ => 0.4));
        Assert.Equal(0.0, r.StdPercent!.Value, 1e-9);
        Assert.Equal(0.0, r.P95AbsPercent!.Value, 1e-9);
    }

    [Fact]
    public void TreValoriCiclici_0Piu5_0Zero_Meno5_ResiduoAtteso()
    {
        // Pattern (+5 %, 0, -5 %) ripetuto, finestra 15 = 5 periodi: la mediana cade sul valore 0, quindi
        // il residuo relativo è esattamente +5 %, 0, -5 % nei frame interni. Atteso: deviazione sqrt(2/3)*5 = 4.08 %.
        double[] p = [0.05, 0.0, -0.05];
        FlickerResult r = LuminanceFlicker.Measure(Series(1200, i => 0.4 * (1 + p[i % 3])));
        Assert.InRange(r.StdPercent!.Value, 4.0, 4.15);
        Assert.InRange(r.P95AbsPercent!.Value, 4.9, 5.1);
    }

    [Fact]
    public void PeriodoDue_PlusMinus5_LaMedianaRestituisceLEscursionePicoPicco()
    {
        // Limite del metodo, misurato: con ±5 % alternato la mediana di 15 frame prende il valore opposto,
        // quindi il residuo vale circa +10.5 % e -9.5 % (escursione picco-picco), non 5 %.
        FlickerResult r = LuminanceFlicker.Measure(Series(1200, i => 0.4 * (i % 2 == 0 ? 1.05 : 0.95)));
        Assert.InRange(r.StdPercent!.Value, 9.9, 10.1);
        Assert.InRange(r.P95AbsPercent!.Value, 10.3, 10.7);
    }

    [Fact]
    public void TrendLentoSinusoidale_NonContaComeFlicker()
    {
        // Sinusoide di periodo 200 frame ampiezza 5 %: la mediana mobile di 15 la segue quasi esattamente.
        FlickerResult r = LuminanceFlicker.Measure(Series(600, i => 0.4 * (1 + 0.05 * Math.Sin(2 * Math.PI * i / 200.0))));
        Assert.InRange(r.StdPercent!.Value, 0.0, 0.01);
    }

    [Fact]
    public void SerieCorta_DatiInsufficienti()
    {
        FlickerResult r = LuminanceFlicker.Measure(Series(2, _ => 0.4));
        Assert.Null(r.StdPercent);
    }
}
