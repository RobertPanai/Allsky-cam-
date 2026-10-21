using System.Diagnostics;
using AllSky.Detection;
using AllSky.Sky;
using Xunit;

namespace AllSky.Detection.Tests;

// Test del rilevatore di scie su frame sintetici deterministici (seed fissi), 960x720.
// Il fondo viene riempito con WarmupFrames frame senza scie prima di ogni misura.
public class StreakDetectorTests
{
    private const int WarmupFrames = 8;

    private static LumaFrame Frame(byte[] pixels)
        => new(StreakSyntheticSky.Width, StreakSyntheticSky.Height, pixels, DateTimeOffset.UnixEpoch);

    private static StreakDetector NewDetector() => new(new StreakDetectorOptions());

    private static void Warmup(StreakDetector detector, StreakSyntheticSky sky, int frames = WarmupFrames)
    {
        for (int i = 0; i < frames; i++)
        {
            detector.Detect(Frame(sky.Render()));
        }
    }

    [Fact]
    public void SyntheticStreaks_WithSnrAtLeast5AndLengthAtLeast40_RecallAtLeast90Percent()
    {
        var sky = new StreakSyntheticSky(1001);
        var detector = NewDetector();
        Warmup(detector, sky);

        const int trailCount = 70;
        int hits = 0;
        for (int i = 0; i < trailCount; i++)
        {
            var trail = sky.RandomTrail(minSnr: 5, maxSnr: 10, minLength: 40, maxLength: 200);
            var detections = detector.Detect(Frame(sky.Render(new[] { trail })));
            if (detections.Any(trail.Matches))
            {
                hits++;
            }
        }

        Assert.True(hits >= 0.9 * trailCount, $"Richiamo {hits}/{trailCount} sotto il 90 per cento.");
    }

    [Fact]
    public void NoStreaks_HundredFramesAfterFill_AtMostOneFalsePositive()
    {
        var sky = new StreakSyntheticSky(2002);
        var detector = NewDetector();
        Warmup(detector, sky);

        int falsePositives = 0;
        for (int i = 0; i < 100; i++)
        {
            falsePositives += detector.Detect(Frame(sky.Render())).Count;
        }

        Assert.True(falsePositives <= 1, $"Falsi positivi: {falsePositives} su 100 frame senza scie.");
    }

    [Fact]
    public void FixedStars_WithSlowBrightnessChanges_ProduceNoDetections()
    {
        var sky = new StreakSyntheticSky(3003);
        var detector = NewDetector();
        Warmup(detector, sky);

        int detections = 0;
        for (int i = 0; i < 20; i++)
        {
            // Ogni stella cambia luminosita' di al massimo il 10 per cento a ogni frame.
            detections += detector.Detect(Frame(sky.Render(starScale: sky.RandomStarScale(0.10)))).Count;
        }

        Assert.True(detections == 0, $"Rilevazioni su stelle fisse: {detections}.");
    }

    [Fact]
    public void FrameWithTenPercentIlluminated_IsDiscarded_EvenWithStreak()
    {
        var sky = new StreakSyntheticSky(4004);
        var detector = NewDetector();
        Warmup(detector, sky);

        var trail = sky.RandomTrail(minSnr: 10, maxSnr: 10, minLength: 120, maxLength: 160);
        var discarded = detector.Detect(Frame(sky.Render(new[] { trail }, illuminatedFraction: 0.10, illuminatedBoost: 40)));
        Assert.Empty(discarded);

        // Controllo: la stessa scia in un frame pulito viene trovata, quindi lo scarto dipende dal frame.
        var control = detector.Detect(Frame(sky.Render(new[] { trail })));
        Assert.True(control.Any(trail.Matches), "La scia nel frame pulito di controllo non e' stata rilevata.");
    }

    [Fact]
    public void StreakOutsideSkyMask_IsNotDetected()
    {
        // Cielo: cerchio di raggio 300 centrato in (480, 360). La scia sta fuori dal cerchio.
        var model = new FisheyeModel(480, 360, 300, 0, false, 0);
        var sky = new StreakSyntheticSky(5005);
        var trail = new SyntheticTrail(60, 80, 220, 140, 30);
        var warm = Enumerable.Range(0, WarmupFrames).Select(_ => sky.Render()).ToArray();
        var trailFrame = sky.Render(new[] { trail });

        var withoutMask = NewDetector();
        foreach (var w in warm)
        {
            withoutMask.Detect(Frame(w));
        }
        var found = withoutMask.Detect(Frame(trailFrame));
        Assert.True(found.Any(trail.Matches), "Controllo senza maschera: la scia non e' stata rilevata.");

        var withMask = NewDetector();
        foreach (var w in warm)
        {
            withMask.Detect(Frame(w), (x, y) => model.IsInsideSky(x, y));
        }
        var masked = withMask.Detect(Frame(trailFrame), (x, y) => model.IsInsideSky(x, y));
        Assert.Empty(masked);
    }

    [Fact]
    public void StreakInsideExclusionCircle_IsNotDetected()
    {
        var sky = new StreakSyntheticSky(6006);
        var trail = new SyntheticTrail(300, 300, 480, 330, 30);
        double length = trail.Length;
        var warm = Enumerable.Range(0, WarmupFrames).Select(_ => sky.Render()).ToArray();
        var trailFrame = sky.Render(new[] { trail });

        var withoutCircle = NewDetector();
        foreach (var w in warm)
        {
            withoutCircle.Detect(Frame(w));
        }
        var found = withoutCircle.Detect(Frame(trailFrame));
        Assert.True(found.Any(trail.Matches), "Controllo senza cerchio: la scia non e' stata rilevata.");

        // Cerchio centrato sul punto medio della scia, con raggio che copre tutta la sua lunghezza.
        var circle = new[] { (X: 390, Y: 315, R: (int)Math.Ceiling(length / 2.0) + 10) };
        var withCircle = NewDetector();
        foreach (var w in warm)
        {
            withCircle.Detect(Frame(w));
        }
        var excluded = withCircle.Detect(Frame(trailFrame), exclusionCircles: circle);
        Assert.Empty(excluded);
    }

    [Fact]
    public void TwoStreaksInOneFrame_ProduceTwoDetections()
    {
        var sky = new StreakSyntheticSky(7007);
        var detector = NewDetector();
        Warmup(detector, sky);

        var first = new SyntheticTrail(100, 150, 290, 160, 30);
        var second = new SyntheticTrail(600, 500, 700, 560, 30);
        var detections = detector.Detect(Frame(sky.Render(new[] { first, second })));

        Assert.True(detections.Count == 2, $"Attese 2 rilevazioni, ottenute {detections.Count}.");
        Assert.True(detections.Any(first.Matches), "Prima scia non rilevata.");
        Assert.True(detections.Any(second.Matches), "Seconda scia non rilevata.");
    }

    [Fact]
    public void ShortStreak_TenPixels_IsIgnored()
    {
        var sky = new StreakSyntheticSky(8008);
        var detector = NewDetector();
        Warmup(detector, sky);

        var shortTrail = new SyntheticTrail(400, 400, 410, 400, 40);
        var detections = detector.Detect(Frame(sky.Render(new[] { shortTrail })));

        Assert.Empty(detections);
    }

    [Fact]
    public void FirstFrameAfterReset_ProducesNoDetections()
    {
        var sky = new StreakSyntheticSky(9009);
        var detector = NewDetector();
        Warmup(detector, sky);
        detector.Reset();

        var trail = new SyntheticTrail(200, 200, 340, 260, 30);
        var afterReset = detector.Detect(Frame(sky.Render(new[] { trail })));
        Assert.Empty(afterReset);

        // Dopo il riempimento del fondo la stessa scia viene rilevata.
        Warmup(detector, sky);
        var later = detector.Detect(Frame(sky.Render(new[] { trail })));
        Assert.True(later.Any(trail.Matches), "Scia non rilevata dopo il riempimento successivo al Reset.");
    }

    [Fact]
    public void Detect_BufferSizeMismatch_ThrowsArgumentException()
    {
        var detector = NewDetector();
        Assert.Throws<ArgumentException>(() =>
        {
            detector.Detect(new LumaFrame(10, 10, new byte[5], DateTimeOffset.UnixEpoch));
        });
    }

    [Fact]
    public void Options_InvalidBackgroundFrames_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new StreakDetector(new StreakDetectorOptions { BackgroundFrames = 0 });
        });
    }

    [Fact]
    public void Frame960x720_AverageDetectTime_IsUnderLooseBudget()
    {
        // Budget di progetto: 300 ms per frame. La soglia del test e' larga (2 s) per non essere fragile.
        var sky = new StreakSyntheticSky(10010);
        var detector = NewDetector();
        Warmup(detector, sky);

        var frames = new byte[20][];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = sky.Render(new[] { sky.RandomTrail(minSnr: 6, maxSnr: 10, minLength: 40, maxLength: 200) });
        }

        var sw = Stopwatch.StartNew();
        foreach (var f in frames)
        {
            detector.Detect(Frame(f));
        }
        sw.Stop();

        double averageMs = sw.Elapsed.TotalMilliseconds / frames.Length;
        Assert.True(averageMs < 2000, $"Tempo medio per frame {averageMs:F1} ms sopra la soglia larga di 2000 ms.");
    }
}
