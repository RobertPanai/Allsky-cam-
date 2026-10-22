using AllSky.Core;
using AllSky.Capture;
using Xunit;

namespace AllSky.Capture.Tests;

public class CaptureLoopTransitTests
{
    private static readonly DateTimeOffset NightUtc = new(2026, 6, 21, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Loop_WithTransitPolicy_UsesReducedWaitsDuringPass()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var starts = new List<DateTimeOffset>();
        var policy = new TransitCadencePolicy(
            new FixedWindows(new TransitWindow(NightUtc.AddSeconds(100), NightUtc.AddSeconds(200))),
            new TransitCadenceOptions { Enabled = true });

        // Frame con mediana uguale al target: l'esposizione resta 10 ms, il minimo è 10 ms + 2 s.
        var camera = new FakeCamera((n, s, ct) =>
        {
            lock (starts) { starts.Add(clock.UtcNow); }
            return Task.FromResult(Frames.Uniform(16384, s, clock.UtcNow));
        });
        var delay = new FakeDelay(clock, n => { if (n == 12) cts.Cancel(); });
        var autoExposure = new AutoExposure(new AutoExposureOptions(2, 30000, 0, 500), 65535, new ExposureSetting(10, 250));
        var loop = new CaptureLoop(
            camera,
            clock,
            (frame, ct) => Task.CompletedTask,
            delay,
            autoExposure,
            new CaptureLoopOptions(30, true, 45.07, 7.69),
            policy);

        await loop.RunAsync(cts.Token);

        // Esposizione 10 ms: minimo fisico 10 ms + 2 s. Il passaggio inizia a 100 s, quindi la pose a 90 s
        // viene anticipata a 100 s (attesa 10 s). Dentro il passaggio 15 s; fine a 200 s, margine 5 s: 205 s torna normale.
        TimeSpan[] expected =
        {
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        };
        Assert.Equal(expected, delay.Calls);

        DateTimeOffset[] times = starts.ToArray();
        for (int i = 1; i < times.Length; i++)
        {
            Assert.Equal(expected[i - 1], times[i] - times[i - 1]);
        }
    }
}
