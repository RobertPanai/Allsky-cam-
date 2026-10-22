using AllSky.Core;
using AllSky.Capture;
using Xunit;

namespace AllSky.Capture.Tests;

/// <summary>Sorgente di finestre fissa per i test.</summary>
internal sealed class FixedWindows : ITransitWindowSource
{
    private readonly TransitWindow[] _windows;

    public FixedWindows(params TransitWindow[] windows)
    {
        _windows = windows;
    }

    public TransitWindow? NextWindow(DateTimeOffset utc)
    {
        foreach (TransitWindow window in _windows)
        {
            if (window.End > utc)
            {
                return window;
            }
        }

        return null;
    }
}

public class TransitCadencePolicyTests
{
    // Notte fittizia: l'orologio parte da qui e i secondi sono relativi a questo istante.
    private static readonly DateTimeOffset Origin = new(2026, 6, 21, 23, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Normal = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Exposure = TimeSpan.FromSeconds(10);

    // Minimo fisico = esposizione 10 s + lettura 2 s = 12 s. Transito = metà di 30 s = 15 s. Margine 5 s.
    private static TransitCadenceOptions Enabled(double fraction = 0.5) => new()
    {
        Enabled = true,
        IntervalFraction = fraction,
        RestoreMarginSeconds = 5,
        ReadoutSeconds = 2,
    };

    private static TransitWindow Window(double startS, double endS) =>
        new(Origin.AddSeconds(startS), Origin.AddSeconds(endS));

    /// <summary>Simula il loop con pose istantanee (la posa non dura): restituisce le attese successive.</summary>
    private static TimeSpan[] Simulate(TransitCadencePolicy policy, int count, TimeSpan? exposure = null)
    {
        var waits = new TimeSpan[count];
        DateTimeOffset pose = Origin;
        for (int i = 0; i < count; i++)
        {
            TimeSpan wait = policy.WaitAfterPose(pose, pose, Normal, exposure ?? Exposure);
            waits[i] = wait;
            pose += wait;
        }

        return waits;
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void PassInProgress_ReducesIntervalThenRestoresAfterEndWithMargin()
    {
        // Passaggio 100-200 s. Le pose cadono a 0, 30, 60, 90; poi la prima pose dopo l'inizio è a 102 (minimo).
        var policy = new TransitCadencePolicy(new FixedWindows(Window(100, 200)), Enabled());

        TimeSpan[] waits = Simulate(policy, 12);

        Assert.Equal(
            new[]
            {
                S(30), S(30), S(30),             // prima: normale (le pose a 0, 30, 60, 90)
                S(12),                            // dalla pose a 90 alla pose a 102: inizio passaggio, non sotto il minimo
                S(15), S(15), S(15), S(15), S(15), S(15), // durante (102 -> 192): metà intervallo
                S(15),                            // pose a 192 -> 207: ancora nel margine di 5 s dopo la fine a 200
                S(30),                            // dopo il margine: normale
            },
            waits);
    }

    [Fact]
    public void PassStartingMidInterval_CutsIntervalAtPassStart()
    {
        // Passaggio da 105 s: l'intervallo 90 -> 120 viene tagliato a 105, perché 15 s >= minimo 12 s.
        var policy = new TransitCadencePolicy(new FixedWindows(Window(105, 300)), Enabled());

        TimeSpan[] waits = Simulate(policy, 5);

        Assert.Equal(new[] { S(30), S(30), S(30), S(15), S(15) }, waits);
    }

    [Fact]
    public void PassStartingTooCloseForMinimum_IsDelayedToMinimumNotBelow()
    {
        // Passaggio da 95 s: a 90 s la posa successiva al minimo cade a 102, non a 95 (12 s < minimo rispettato).
        var policy = new TransitCadencePolicy(new FixedWindows(Window(95, 300)), Enabled());

        TimeSpan[] waits = Simulate(policy, 4);

        Assert.Equal(new[] { S(30), S(30), S(30), S(12) }, waits);
    }

    [Fact]
    public void Transit_NeverGoesBelowPhysicalMinimum_EvenWithVerySmallFraction()
    {
        var policy = new TransitCadencePolicy(new FixedWindows(Window(0, 100000)), Enabled(fraction: 0.05));

        TimeSpan[] waits = Simulate(policy, 200);

        Assert.All(waits, w => Assert.True(w >= S(12), "attesa sotto il minimo fisico: " + w));
        Assert.All(waits, w => Assert.True(w <= Normal, "attesa sopra l'intervallo normale: " + w));
        Assert.True(waits.Skip(1).All(w => w == S(12)));
    }

    [Fact]
    public void DisabledMode_IsIdenticalToFixedInterval_EvenWithPassInWindow()
    {
        var disabled = new TransitCadenceOptions { Enabled = false };
        var policy = new TransitCadencePolicy(new FixedWindows(Window(100, 200)), disabled);

        TimeSpan[] waits = Simulate(policy, 10);

        Assert.Equal(Enumerable.Repeat(Normal, 10).ToArray(), waits);
        Assert.False(policy.Enabled);
    }

    [Fact]
    public void DisabledMode_ReturnsRemainingIntervalFromNow()
    {
        var policy = new TransitCadencePolicy(new FixedWindows(), new TransitCadenceOptions { Enabled = false });

        // Dopo una posa di 4 s, restano 26 s.
        TimeSpan wait = policy.WaitAfterPose(Origin, Origin.AddSeconds(4), Normal, Exposure);

        Assert.Equal(S(26), wait);
        // Oltre l'intervallo: attesa zero, mai negativa.
        Assert.Equal(TimeSpan.Zero, policy.WaitAfterPose(Origin, Origin.AddSeconds(45), Normal, Exposure));
    }

    [Fact]
    public void TwoPassesCloseTogether_StayInTransitAcrossTheShortGap()
    {
        // Il secondo passaggio inizia 5 s dopo la fine del primo: il margine di 5 s copre il buco.
        var policy = new TransitCadencePolicy(new FixedWindows(Window(100, 140), Window(145, 190)), Enabled());

        TimeSpan[] waits = Simulate(policy, 12);

        Assert.Equal(
            new[] { S(30), S(30), S(30), S(12), S(15), S(15), S(15), S(15), S(15), S(15), S(15), S(30) },
            waits);
    }

    [Fact]
    public void TwoDistantPasses_EachGetsItsOwnTransitAndRestoreInBetween()
    {
        // Primo passaggio 100-140, secondo 400-440. Tra i due l'intervallo torna normale.
        var policy = new TransitCadencePolicy(new FixedWindows(Window(100, 140), Window(400, 440)), Enabled());

        TimeSpan[] waits = Simulate(policy, 20);

        Assert.Equal(
            new[]
            {
                S(30), S(30), S(30), S(12), S(15), S(15), S(15),   // primo passaggio (pose 0..147)
                S(30), S(30), S(30), S(30), S(30), S(30), S(30), S(30), // normale fra i passaggi (pose 147..387)
                S(13),                                                 // pose 387 -> 400: inizio del secondo, sopra il minimo
                S(15), S(15), S(15),                                   // secondo passaggio, fino a 445 (fine 440 + margine)
                S(30),                                                 // dopo il margine
            },
            waits);
    }

    [Fact]
    public void SequenceBeforeDuringAfter_HasNormalTransitNormalIntervals()
    {
        // Passaggio 200-300. Pose a 0..180 normali; la pose a 180 viene anticipata a 200 (attesa 20 s);
        // durante il passaggio 15 s; dopo 300 + margine di 5 s, di nuovo 30 s.
        var policy = new TransitCadencePolicy(new FixedWindows(Window(200, 300)), Enabled());

        TimeSpan[] waits = Simulate(policy, 15);

        Assert.Equal(
            new[]
            {
                S(30), S(30), S(30), S(30), S(30), S(30),   // prima: 0 -> 180
                S(20),                                       // 180 -> 200: inizio passaggio
                S(15), S(15), S(15), S(15), S(15), S(15), S(15), // durante: 200 -> 305 (fine 300 + margine 5)
                S(30),                                       // dopo: normale
            },
            waits);
    }

    [Fact]
    public void NullPolicyEquivalent_NoWindowsKeepsNormalInterval()
    {
        var policy = new TransitCadencePolicy(new FixedWindows(), Enabled());

        Assert.Equal(Enumerable.Repeat(Normal, 6).ToArray(), Simulate(policy, 6));
    }

    [Fact]
    public void FromConfig_ClampsOutOfRangeValuesInsteadOfThrowing()
    {
        var config = new AppConfig.CaptureConfig.TransitModeConfig
        {
            Enabled = true,
            IntervalFraction = 0,
            RestoreMarginSeconds = -3,
            ReadoutSeconds = double.NaN,
        };

        TransitCadenceOptions options = TransitCadenceOptions.FromConfig(config);

        Assert.True(options.Enabled);
        Assert.Equal(0.05, options.IntervalFraction, 6);
        Assert.Equal(0.0, options.RestoreMarginSeconds, 6);
        Assert.Equal(2.0, options.ReadoutSeconds, 6);
    }

    [Fact]
    public void Constructor_RejectsInvalidFraction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TransitCadencePolicy(new FixedWindows(), new TransitCadenceOptions { Enabled = true, IntervalFraction = 1.5 }));
    }

    [Fact]
    public void Defaults_AreOffAndConservative()
    {
        var config = new AppConfig.CaptureConfig.TransitModeConfig();

        Assert.False(config.Enabled);
        Assert.Equal(0.5, config.IntervalFraction, 6);
        Assert.Equal(1, AppConfig.CaptureConfig.TransitModeConfig.CurrentVersion);
        Assert.False(new AppConfig().Capture.TransitMode.Enabled);
    }
}
