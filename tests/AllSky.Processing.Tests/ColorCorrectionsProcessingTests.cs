using AllSky.Archive;
using AllSky.Astro;
using AllSky.ColorMetrics;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using Xunit;

namespace AllSky.Processing.Tests;

/// <summary>Correzioni temporali del colore (P14.2) nel processore: alpha, reset, percorso spento e misura dei cambi di esposizione.</summary>
public class ColorCorrectionsProcessingTests
{
    // Torino: 19/10/2026 00:30 UTC = 02:30 locali (notte); 11:00 UTC = 13:00 locali (giorno).
    private static readonly DateTimeOffset NightUtc = new(2026, 10, 19, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayUtc = new(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);

    private static readonly ColorCorrections BothOn = new() { TimeConstantSmoothing = true, ExposureCompensation = true };

    // Valori effettivi di BothOn con i default di tau (180 s notte, 20 s giorno): usati dai test di TimedAlpha.
    private static readonly ResolvedColorCorrections Both = new(true, 180.0, 20.0, true, 0.1, 1.1);

    private static AppConfig Config(ColorCorrections? corrections, bool overlay = false) => new()
    {
        Color = new AppConfig.ColorConfig { Corrections = corrections },
        Overlay = new AppConfig.OverlayConfig { Enabled = overlay, Label = "Test" },
    };

    private static RawFrame Frame(int w, int h, DateTimeOffset ts, int seed, double exposureMs = 30000, double gain = 250, ushort sky = 3000)
    {
        var rnd = new Random(seed);
        var px = new ushort[w * h];
        for (int k = 0; k < px.Length; k++)
        {
            int v = sky + rnd.Next(-60, 61) + (k % 97 == 0 ? 20000 : 0);
            px[k] = (ushort)Math.Clamp(v, 0, 65535);
        }

        return new RawFrame(w, h, px, BayerPattern.RGGB, ts, exposureMs, gain, 15.0);
    }

    // ---- alpha dipendente da dt e da altezza del Sole (valori a mano; vedi i commenti) ----

    [Fact]
    public void TimedAlpha_Night_IsOneMinusExpOfDtOverTau()
    {
        // Notte: t = 1, tau = 180 s. dt = 30 s: alpha = 1 - exp(-1/6) = 0.1535183.
        double a = FrameProcessor.TimedAlpha(30, -18, Both, 0.5);

        Assert.Equal(0.1535183, a, 6);
    }

    [Fact]
    public void TimedAlpha_Night_GrowsWithDt()
    {
        // dt = 60 s: alpha = 1 - exp(-1/3) = 0.2834687.
        double a = FrameProcessor.TimedAlpha(60, -18, Both, 0.5);

        Assert.Equal(0.2834687, a, 6);
    }

    [Fact]
    public void TimedAlpha_Day_UsesDayTau()
    {
        // Sole a +10 gradi: t = 0, tau = 20 s. dt = 30 s: alpha = 1 - exp(-1.5) = 0.7768698.
        double a = FrameProcessor.TimedAlpha(30, 10, Both, 0.5);

        Assert.Equal(0.7768698, a, 6);
    }

    [Fact]
    public void TimedAlpha_Twilight_InterpolatesTauLinearly()
    {
        // Sole a -6 gradi: t = 0.5, tau = (20 + 180) / 2 = 100 s. dt = 30 s: alpha = 1 - exp(-0.3) = 0.2591818.
        double a = FrameProcessor.TimedAlpha(30, -6, Both, 0.5);

        Assert.Equal(0.2591818, a, 6);
    }

    [Fact]
    public void TimedAlpha_BelowMinimum_IsClampedTo001()
    {
        // dt = 0.001 s di notte: alpha calcolato 5.6e-6, limitato a 0.01 come MinSmoothingAlpha.
        double a = FrameProcessor.TimedAlpha(0.001, -18, Both, 0.5);

        Assert.Equal(0.01, a, 12);
    }

    [Fact]
    public void TimedAlpha_NonFiniteTau_KeepsPreviousAlpha()
    {
        var corr = new ResolvedColorCorrections(true, double.NaN, 20, true, 0.1, 1.1);

        Assert.Equal(0.42, FrameProcessor.TimedAlpha(30, -18, corr, 0.42));
    }

    // ---- percorso spento: identico a prima ----

    [Fact]
    public void CorrectionsOff_NullAndAllFalseSection_GiveIdenticalOutput()
    {
        var nullSection = new FrameProcessor(Config(null), new BaselineJpegEncoder());
        var allFalse = new FrameProcessor(Config(new ColorCorrections()), new BaselineJpegEncoder());

        for (int i = 0; i < 6; i++)
        {
            double exposure = i % 4 < 2 ? 30000 : 60000;
            RawFrame raw = Frame(320, 240, NightUtc.AddSeconds(30 * i), seed: 200 + i, exposureMs: exposure);
            ProcessedFrame a = nullSection.Process(raw);
            ProcessedFrame b = allFalse.Process(raw);

            Assert.True(a.Rgb8.AsSpan().SequenceEqual(b.Rgb8), $"Rgb8 diverso al frame {i}.");
            Assert.True(a.Jpeg.AsSpan().SequenceEqual(b.Jpeg), $"JPEG diverso al frame {i}.");
        }
    }

    // ---- reset ----

    [Fact]
    public void LongGap_ResetsSmoothing_OutputMatchesFreshProcessor()
    {
        // Giorno, poi un frame notturno sette ore dopo: con correzioni accese lo stato si azzera, quindi il frame
        // deve coincidere con quello di un processore nuovo.
        AppConfig cfg = Config(BothOn);
        var used = new FrameProcessor(cfg, new BaselineJpegEncoder());
        used.Process(Frame(320, 240, DayUtc, seed: 300, exposureMs: 2, sky: 9000));
        RawFrame later = Frame(320, 240, DayUtc.AddHours(7), seed: 301);

        ProcessedFrame afterGap = used.Process(later);
        ProcessedFrame fresh = new FrameProcessor(cfg, new BaselineJpegEncoder()).Process(later);

        Assert.True(afterGap.Rgb8.AsSpan().SequenceEqual(fresh.Rgb8), "Dopo un intervallo oltre 6 ore lo stato deve ripartire da zero.");
    }

    [Fact]
    public void BackwardTimestamps_ResetSmoothing_OutputMatchesFreshProcessor()
    {
        AppConfig cfg = Config(BothOn);
        var used = new FrameProcessor(cfg, new BaselineJpegEncoder());
        used.Process(Frame(320, 240, DayUtc, seed: 310, exposureMs: 2, sky: 9000));
        RawFrame earlier = Frame(320, 240, DayUtc.AddMinutes(-10), seed: 311, exposureMs: 2, sky: 9000);

        ProcessedFrame back = used.Process(earlier);
        ProcessedFrame fresh = new FrameProcessor(cfg, new BaselineJpegEncoder()).Process(earlier);

        Assert.True(back.Rgb8.AsSpan().SequenceEqual(fresh.Rgb8), "Con timestamp che vanno indietro lo stato deve ripartire da zero.");
    }

    [Fact]
    public void ShortGap_KeepsSmoothingState_OutputDiffersFromFresh()
    {
        // Controllo: con un intervallo normale lo stato resta, quindi il risultato diverge dal processore nuovo.
        AppConfig cfg = Config(BothOn);
        var used = new FrameProcessor(cfg, new BaselineJpegEncoder());
        used.Process(Frame(320, 240, DayUtc, seed: 320, exposureMs: 2, sky: 9000));
        RawFrame next = Frame(320, 240, DayUtc.AddSeconds(30), seed: 321, exposureMs: 2, sky: 9000);

        ProcessedFrame kept = used.Process(next);
        ProcessedFrame fresh = new FrameProcessor(cfg, new BaselineJpegEncoder()).Process(next);

        Assert.False(kept.Rgb8.AsSpan().SequenceEqual(fresh.Rgb8), "Senza reset lo stato deve pesare sul frame successivo.");
    }

    // ---- configurazione a runtime ----

    [Fact]
    public void UpdateConfig_TogglingCorrections_DoesNotThrow()
    {
        var processor = new FrameProcessor(Config(null), new BaselineJpegEncoder());
        processor.Process(Frame(320, 240, NightUtc, seed: 400));

        processor.UpdateConfig(Config(new ColorCorrections { Preset = "naturale" }));
        ProcessedFrame on = processor.Process(Frame(320, 240, NightUtc.AddSeconds(30), seed: 401));

        processor.UpdateConfig(Config(new ColorCorrections { Preset = "profondo", GainDbPerUnit = 0.3 }));
        ProcessedFrame deeper = processor.Process(Frame(320, 240, NightUtc.AddSeconds(60), seed: 402));

        processor.UpdateConfig(Config(null));
        ProcessedFrame off = processor.Process(Frame(320, 240, NightUtc.AddSeconds(90), seed: 403));

        Assert.Equal(320 * 240 * 3, on.Rgb8.Length);
        Assert.Equal(320 * 240 * 3, deeper.Rgb8.Length);
        Assert.Equal(320 * 240 * 3, off.Rgb8.Length);
    }

    // ---- misura: salto medio di luminanza ai cambi di esposizione ----

    /// <summary>
    /// Soglia dichiarata: con le correzioni accese il salto medio ai cambi di esposizione (<see cref="ExposureBanding"/>)
    /// deve essere inferiore a META' di quello con le correzioni spente. Scena costante e proporzionale all'esposizione
    /// (nessun offset di bias), esposizione 30 s / 60 s alternata ogni 20 frame, frame ogni 30 s, notte.
    /// Criterio di accettazione scelto qui [S], non dalla diagnosi di P14.1. Limiti del criterio: con luminanze vicine a
    /// zero il salto relativo diventa molto grande (per questo il messaggio riporta anche il salto assoluto); con un offset
    /// di bias nella scena le correzioni non bastano (vedi il rapporto di P14.2).
    /// </summary>
    private const double RequiredRatio = 0.5;

    [Fact]
    public void ExposureChanges_CorrectionsHalveTheJumpAtChanges()
    {
        const int w = 320;
        const int h = 240;
        const int frames = 240;
        RawFrame[] scene = BuildConstantScene(w, h, frames);

        ExposureBandingResult off = Measure(Config(null), scene, out double absOff, out FlickerResult offFlicker);
        ExposureBandingResult on = Measure(Config(BothOn), scene, out double absOn, out FlickerResult onFlicker);

        Assert.NotNull(off.MeanAbsRelJumpAtChanges);
        Assert.NotNull(on.MeanAbsRelJumpAtChanges);
        Assert.True(off.Changes >= 10, $"Cambi di esposizione attesi >= 10, trovati {off.Changes}.");

        double jumpOff = off.MeanAbsRelJumpAtChanges!.Value;
        double jumpOn = on.MeanAbsRelJumpAtChanges!.Value;
        string numbers = $"salto medio ai cambi (relativo, ExposureBanding): spente {jumpOff:P2}, accese {jumpOn:P4}; " +
                         $"salto assoluto di luminanza ai cambi (0..1): spente {absOff:F4}, accese {absOn:F5}; " +
                         $"flicker std (LuminanceFlicker, poco informativo qui: trend a zero): spente {offFlicker.StdPercent:F4} %, accese {onFlicker.StdPercent:F4} %; " +
                         $"soglia: accese < {RequiredRatio:P0} delle spente; {off.Changes} cambi";
        Console.WriteLine("[P14.2] " + numbers);

        Assert.True(jumpOn < RequiredRatio * jumpOff, numbers);
    }

    /// <summary>Scena costante: stessi pixel di base, scalati dall'esposizione (lineare). Stelle sparse a parte.</summary>
    private static RawFrame[] BuildConstantScene(int w, int h, int frames)
    {
        var rnd = new Random(777);
        var baseScene = new double[w * h];
        for (int k = 0; k < baseScene.Length; k++)
        {
            baseScene[k] = 3000 + rnd.Next(-60, 61) + (k % 97 == 0 ? 20000 : 0);
        }

        var result = new RawFrame[frames];
        for (int i = 0; i < frames; i++)
        {
            // Esposizione: 30 s per 20 frame, poi 60 s per 20 frame, e cosi' via (un cambio ogni 20 frame).
            double exposureMs = (i / 20) % 2 == 0 ? 30000 : 60000;
            double scale = exposureMs / 30000.0;
            var px = new ushort[w * h];
            for (int k = 0; k < px.Length; k++)
            {
                px[k] = (ushort)Math.Clamp(Math.Round(baseScene[k] * scale), 0, 65535);
            }

            result[i] = new RawFrame(w, h, px, BayerPattern.RGGB, NightUtc.AddSeconds(30 * i), exposureMs, 250, 15.0);
        }

        return result;
    }

    private static ExposureBandingResult Measure(AppConfig cfg, RawFrame[] frames, out double absJumpAtChanges, out FlickerResult flicker)
    {
        var processor = new FrameProcessor(cfg, new BaselineJpegEncoder());
        var samples = new List<FrameSample>(frames.Length);
        foreach (RawFrame raw in frames)
        {
            ProcessedFrame p = processor.Process(raw);
            var sidecar = new FrameSidecar
            {
                TimestampUtc = raw.Timestamp,
                TimestampLocal = raw.Timestamp,
                PhaseOfDay = "night",
                ExposureMs = raw.ExposureMs,
                Gain = raw.Gain,
            };
            samples.Add(FrameSampler.Create(new Rgb8Image(p.Width, p.Height, p.Rgb8), sidecar));
        }

        absJumpAtChanges = 0.0;
        int changes = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].ExposureMs != samples[i - 1].ExposureMs)
            {
                absJumpAtChanges += Math.Abs(samples[i].MeanLuminance - samples[i - 1].MeanLuminance);
                changes++;
            }
        }

        absJumpAtChanges = changes > 0 ? absJumpAtChanges / changes : 0.0;
        flicker = LuminanceFlicker.Measure(samples);
        return ExposureBanding.Measure(samples);
    }
}
