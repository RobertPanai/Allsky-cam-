using AllSky.Astro;
using AllSky.Capture;
using Xunit;

namespace AllSky.Capture.Tests;

public class AutoExposureTests
{
    private const int MaxAdu = 65535;
    private const double Target = 0.25 * MaxAdu;

    private static readonly AutoExposureOptions Options = new(2, 30000, 0, 500);

    /// <summary>Modello lineare della camera: misura = k * esposizione * 10^(gain/200), saturata a [0, MaxAdu].</summary>
    private static int Measure(double k, ExposureSetting s)
    {
        return (int)Math.Clamp(Math.Round(k * s.ExposureMs * Math.Pow(10.0, s.Gain / 200.0)), 0, MaxAdu);
    }

    /// <summary>Valore di k per cui l'impostazione <paramref name="s"/> produce la misura <paramref name="measuredAdu"/>.</summary>
    private static double KFor(ExposureSetting s, double measuredAdu)
    {
        return measuredAdu / (s.ExposureMs * Math.Pow(10.0, s.Gain / 200.0));
    }

    /// <summary>Logaritmo del segnale effettivo: ln(esposizione) + gain * ln(10) / 200.</summary>
    private static double LogSignal(ExposureSetting s)
    {
        return Math.Log(s.ExposureMs) + s.Gain * Math.Log(10.0) / 200.0;
    }

    [Fact]
    public void TargetAdu_IsQuarterOfMaxByDefault()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        Assert.Equal(0.25 * MaxAdu, ae.TargetAdu, 9);
    }

    [Theory]
    [InlineData(0.25)]   // quattro volte piu' scuro del target
    [InlineData(0.125)]  // otto volte piu' scuro
    [InlineData(4.0)]    // quattro volte piu' chiaro (satura)
    [InlineData(10.0)]   // dieci volte piu' chiaro (satura)
    public void ConvergesWithinEightSteps_AndDoesNotOscillateAfterwards(double measuredOverTarget)
    {
        var start = new ExposureSetting(100, 250);
        double k = KFor(start, Target * measuredOverTarget);
        var ae = new AutoExposure(Options, MaxAdu, start);

        int convergedAt = -1;
        for (int step = 1; step <= 8; step++)
        {
            int measured = Measure(k, ae.Current);
            if (convergedAt < 0 && Math.Abs(measured - Target) <= 0.10 * Target)
            {
                convergedAt = step;
            }

            ae.Update(measured, SkyPhase.Night);
        }

        Assert.True(convergedAt > 0 && convergedAt <= 8, $"non converge entro 8 passi (convergedAt={convergedAt})");

        // Dopo la convergenza, con luminosita' costante, la misura non oscilla (ampiezza <= 5% del target).
        var settled = new List<int>();
        var settledSetting = ae.Current;
        for (int step = 0; step < 10; step++)
        {
            settled.Add(Measure(k, ae.Current));
            ae.Update(settled[^1], SkyPhase.Night);
        }

        Assert.True(settled.Max() - settled.Min() <= 0.05 * Target, "la misura oscilla dopo la convergenza");
        Assert.Equal(settledSetting, ae.Current);
    }

    /// <summary>
    /// Rampa di luminosita' di 1000x (in salita o in discesa) su 300 passi, poi stabile per altri 60.
    /// Partenze scelte in modo che il segnale richiesto resti entro i limiti fisici dell'impostazione.
    /// </summary>
    [Theory]
    [InlineData(1000.0, 1000.0, 250.0)]  // cielo che si schiarisce 1000x: il segnale impostato scende
    [InlineData(0.001, 2.0, 150.0)]      // cielo che si oscura 1000x: il segnale impostato sale
    public void Ramp_1000xBrightnessChange_TracksWithoutOscillation(double brightnessFactor, double startExposureMs, double startGain)
    {
        var start = new ExposureSetting(startExposureMs, startGain);
        double k0 = KFor(start, Target); // partenza gia' sul target
        var ae = new AutoExposure(Options, MaxAdu, start);
        // Direzione attesa del segnale impostato: se la luminosita' sale, il segnale deve scendere.
        double expectedSign = brightnessFactor > 1.0 ? -1.0 : 1.0;

        const int rampSteps = 300;
        const int settleSteps = 60;
        double finalK = k0 * brightnessFactor;
        double previousSignal = LogSignal(ae.Current);
        double maxDeviationDuringRamp = 0.0;

        for (int i = 0; i < rampSteps + settleSteps; i++)
        {
            double k = i < rampSteps ? k0 * Math.Pow(brightnessFactor, (double)i / rampSteps) : finalK;
            int measured = Measure(k, ae.Current);
            if (i >= 20 && i < rampSteps)
            {
                maxDeviationDuringRamp = Math.Max(maxDeviationDuringRamp, Math.Abs(measured / Target - 1.0));
            }

            ae.Update(measured, SkyPhase.Night);

            // Il segnale impostato si muove sempre nella stessa direzione: nessuna inversione (oscillazione).
            double signal = LogSignal(ae.Current);
            Assert.True(expectedSign * (signal - previousSignal) >= -1e-9, $"inversione di direzione al passo {i}");
            previousSignal = signal;
        }

        Assert.True(maxDeviationDuringRamp <= 0.15, $"inseguimento troppo lento: {maxDeviationDuringRamp:P1}");

        // Dopo la rampa (k stabile) la misura e' nella zona morta e l'impostazione non cambia piu'.
        Assert.True(Math.Abs(Measure(finalK, ae.Current) / Target - 1.0) <= 0.10, "non rientra nel target dopo la rampa");
        var before = ae.Current;
        for (int i = 0; i < 20; i++)
        {
            ae.Update(Measure(finalK, ae.Current), SkyPhase.Night);
        }

        Assert.Equal(before, ae.Current);
    }

    [Fact]
    public void Night_RaisesExposureBeforeGain()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        ExposureSetting result = ae.Update(1000, SkyPhase.Night);

        Assert.True(result.ExposureMs > 100);
        Assert.Equal(250.0, result.Gain);
    }

    [Fact]
    public void Night_RaisesGainOnlyWhenExposureIsAtMaximum()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(30000, 250));

        ExposureSetting result = ae.Update(1000, SkyPhase.Night);

        Assert.Equal(30000.0, result.ExposureMs);
        Assert.True(result.Gain > 250);
    }

    [Fact]
    public void Day_OnlyDescends_NeverRaises()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        ExposureSetting result = ae.Update(1000, SkyPhase.Day);

        Assert.Equal(new ExposureSetting(100, 250), result);
    }

    [Fact]
    public void Day_ReducesGainFirstThenExposure_UntilMinimum()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(1000, 300));

        ExposureSetting first = ae.Update(60000, SkyPhase.Day);
        Assert.Equal(1000.0, first.ExposureMs);
        Assert.True(first.Gain < 300);

        for (int i = 0; i < 200 && ae.Current != new ExposureSetting(2, 0); i++)
        {
            ae.Update(60000, SkyPhase.Day);
        }

        Assert.Equal(new ExposureSetting(2, 0), ae.Current);
    }

    [Fact]
    public void Saturated_UsesWideStepLimit()
    {
        // Gain gia' al minimo: tutto il passo va sull'esposizione. Rapporto 0.25 e' oltre il limite normale (0.5).
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(1000, 0));

        ExposureSetting result = ae.Update(MaxAdu, SkyPhase.Night);

        Assert.Equal(1000 * Math.Pow(0.25, 0.8), result.ExposureMs, 6);
    }

    [Fact]
    public void NearZero_UsesWideStepLimit()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(2, 0));

        ExposureSetting result = ae.Update(0, SkyPhase.Night);

        Assert.Equal(2 * Math.Pow(10.0, 0.8), result.ExposureMs, 6);
    }

    [Fact]
    public void NormalStep_IsLimitedToTwoPerStep()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        ExposureSetting result = ae.Update((int)(Target / 3.0), SkyPhase.Night);

        Assert.Equal(100 * Math.Pow(2.0, 0.8), result.ExposureMs, 6);
    }

    [Fact]
    public void Deadband_NoChangeWithinTenPercent()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        ExposureSetting result = ae.Update((int)(Target * 1.05), SkyPhase.Night);

        Assert.Equal(new ExposureSetting(100, 250), result);
    }

    [Fact]
    public void InitialSetting_IsClampedToLimits()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(1e6, -5));

        Assert.Equal(new ExposureSetting(30000, 0), ae.Current);
    }

    [Fact]
    public void Update_RejectsMeasureOutOfRange()
    {
        var ae = new AutoExposure(Options, MaxAdu, new ExposureSetting(100, 250));

        Assert.Throws<ArgumentOutOfRangeException>(() => ae.Update(-1, SkyPhase.Night));
        Assert.Throws<ArgumentOutOfRangeException>(() => ae.Update(MaxAdu + 1, SkyPhase.Night));
    }

    [Fact]
    public void Constructor_InvalidExposure_MessageIncludesReceivedValues()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(0, 30000, 0, 500), MaxAdu, new ExposureSetting(100, 250)));
        Assert.StartsWith("Esposizione: serve 0 < min <= max.", ex.Message);
        Assert.Contains("min=0 max=30000", ex.Message);

        var inverted = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(2, 1, 0, 500), MaxAdu, new ExposureSetting(100, 250)));
        Assert.Contains("min=2 max=1", inverted.Message);
    }

    [Fact]
    public void Constructor_InvalidGain_MessageIncludesReceivedValues()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(2, 30000, 500, 0), MaxAdu, new ExposureSetting(100, 250)));
        Assert.StartsWith("Gain: serve min <= max.", ex.Message);
        Assert.Contains("min=500 max=0", ex.Message);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(0, 30000, 0, 500), MaxAdu, new ExposureSetting(100, 250)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(2, 1, 0, 500), MaxAdu, new ExposureSetting(100, 250)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(new AutoExposureOptions(2, 30000, 0, 500) { TargetFraction = 1.5 }, MaxAdu, new ExposureSetting(100, 250)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoExposure(Options, 1, new ExposureSetting(100, 250)));
    }
}
