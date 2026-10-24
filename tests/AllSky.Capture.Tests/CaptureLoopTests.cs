using AllSky.Core;
using AllSky.Capture;
using Xunit;

namespace AllSky.Capture.Tests;

public class CaptureLoopTests
{
    // 23:00 UTC a 45.07N 7.69E: notte. 10:00 UTC: giorno (Sole alto).
    private static readonly DateTimeOffset NightUtc = new(2026, 6, 21, 23, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayUtc = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static CaptureLoopOptions Options(bool dayCapture = true)
    {
        return new CaptureLoopOptions(30, dayCapture, 45.07, 7.69);
    }

    private static CaptureLoop Build(
        ICameraSource camera,
        IClock clock,
        ICaptureDelay delay,
        Func<RawFrame, CancellationToken, Task> onFrame,
        CaptureLoopOptions? options = null,
        AutoExposure? autoExposure = null)
    {
        autoExposure ??= new AutoExposure(new AutoExposureOptions(2, 30000, 0, 500), 65535, new ExposureSetting(10, 250));
        return new CaptureLoop(camera, clock, onFrame, delay, autoExposure, options ?? Options());
    }

    private static Task NoOp(RawFrame frame, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task Interval_IsRespectedBetweenCaptureStarts()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var starts = new List<DateTimeOffset>();
        var camera = new FakeCamera((n, s, ct) =>
        {
            lock (starts) { starts.Add(clock.UtcNow); }
            return Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow));
        });
        var delay = new FakeDelay(clock, n => { if (n == 4) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp);

        await loop.RunAsync(cts.Token);

        DateTimeOffset[] times = starts.ToArray();
        Assert.Equal(4, times.Length);
        for (int i = 1; i < times.Length; i++)
        {
            Assert.Equal(Interval, times[i] - times[i - 1]);
        }
        Assert.Equal(new[] { Interval, Interval, Interval, Interval }, delay.Calls);
        Assert.Equal(CaptureState.Idle, loop.State);
    }

    [Fact]
    public async Task DayCaptureDisabled_SkipsDaylightWithoutCapturing()
    {
        var clock = new FakeClock(DayUtc);
        using var cts = new CancellationTokenSource();
        var camera = new FakeCamera((n, s, ct) => Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        var delay = new FakeDelay(clock, n => { if (n == 3) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp, Options(dayCapture: false));

        await loop.RunAsync(cts.Token);

        Assert.Equal(0, camera.CaptureCalls);
        Assert.Equal(new[] { Interval, Interval, Interval }, delay.Calls);
        Assert.Equal(CaptureState.Idle, loop.State);
    }

    [Fact]
    public async Task DayCaptureEnabled_CapturesDuringDay()
    {
        var clock = new FakeClock(DayUtc);
        using var cts = new CancellationTokenSource();
        var camera = new FakeCamera((n, s, ct) => Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        var delay = new FakeDelay(clock, n => { if (n == 1) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp, Options(dayCapture: true));

        await loop.RunAsync(cts.Token);

        Assert.Equal(1, camera.CaptureCalls);
    }

    [Fact]
    public async Task ErrorsBackOffExponentiallyUpToSixtySeconds_ThenRecover()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var errors = new List<Exception>();
        var camera = new FakeCamera((n, s, ct) => n <= 8
            ? Task.FromException<RawFrame>(new IOException("guasto " + n))
            : Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        var delay = new FakeDelay(clock, n => { if (n == 9) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp);
        loop.OnError += e => { lock (errors) { errors.Add(e); } };

        await loop.RunAsync(cts.Token);

        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60),
                Interval,
            },
            delay.Calls);
        Assert.Equal(8, errors.Count);
        Assert.All(errors, e => Assert.IsType<IOException>(e));
        Assert.Equal(9, camera.CaptureCalls);
        Assert.Equal(0, loop.ConsecutiveErrors);
    }

    [Fact]
    public async Task AfterFiveConsecutiveErrors_ReconnectsBeforeNextAttempt()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var camera = new FakeCamera((n, s, ct) => n <= 5
            ? Task.FromException<RawFrame>(new IOException("guasto " + n))
            : Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        var delay = new FakeDelay(clock, n => { if (n == 6) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp);

        await loop.RunAsync(cts.Token);

        Assert.Equal(1, camera.ConnectCalls);
        Assert.Equal(
            new[] { "capture:1", "capture:2", "capture:3", "capture:4", "capture:5", "connect", "capture:6" },
            camera.Log);
    }

    [Fact]
    public async Task FailedReconnect_IsReportedAndLoopContinues()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var errors = new List<Exception>();
        var camera = new FakeCamera(
            (n, s, ct) => n <= 5
                ? Task.FromException<RawFrame>(new IOException("guasto " + n))
                : Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)),
            connectFailures: 1);
        var delay = new FakeDelay(clock, n => { if (n == 6) cts.Cancel(); });
        CaptureLoop loop = Build(camera, clock, delay, NoOp);
        loop.OnError += e => { lock (errors) { errors.Add(e); } };

        await loop.RunAsync(cts.Token);

        Assert.Equal(6, errors.Count); // 5 errori di cattura + 1 di connessione
        Assert.IsType<InvalidOperationException>(errors[^1]);
        Assert.Equal(6, camera.CaptureCalls);
        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16), Interval,
            },
            delay.Calls);
    }

    [Fact]
    public async Task SlowConsumer_DropsOldestFrames()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();
        var fifthWritten = new TaskCompletionSource();
        var received = new List<int>();

        // Il produttore aspetta che il consumer sia bloccato sul frame 1, poi cattura 2..5 senza attendere.
        var camera = new FakeCamera(async (n, s, ct) =>
        {
            if (n == 2)
            {
                await gate2.Task.WaitAsync(WaitLimit, ct);
            }
            return Frames.Uniform((ushort)n, s, clock.UtcNow);
        });
        var delay = new FakeDelay(clock, n =>
        {
            if (n == 5)
            {
                fifthWritten.TrySetResult();
                cts.Cancel();
            }
        });

        Task OnFrame(RawFrame frame, CancellationToken ct)
        {
            int value = frame.Pixels[0];
            lock (received) { received.Add(value); }
            if (value == 1)
            {
                entered.TrySetResult();
                return gate1.Task;
            }
            return Task.CompletedTask;
        }

        CaptureLoop loop = Build(camera, clock, delay, OnFrame);
        Task run = loop.RunAsync(cts.Token);

        await entered.Task.WaitAsync(WaitLimit);
        gate2.SetResult();
        await fifthWritten.Task.WaitAsync(WaitLimit);
        gate1.SetResult();
        await run.WaitAsync(WaitLimit);

        // Con capacita' 2 e DropOldest restano in coda i frame 4 e 5.
        int[] processed;
        lock (received) { processed = received.ToArray(); }
        Assert.Equal(new[] { 1, 4, 5 }, processed);
    }

    [Fact]
    public async Task OnFrameThrowing_DoesNotStopTheLoop()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var firstError = new TaskCompletionSource();
        var attempts = new List<int>();
        var errors = new List<Exception>();

        // Il frame 2 aspetta che l'errore del frame 1 sia stato notificato, cosi' il conteggio e' deterministico.
        var camera = new FakeCamera(async (n, s, ct) =>
        {
            if (n == 2)
            {
                await firstError.Task.WaitAsync(WaitLimit, ct);
            }
            return Frames.Uniform((ushort)n, s, clock.UtcNow);
        });
        var delay = new FakeDelay(clock, n => { if (n == 4) cts.Cancel(); });

        Task OnFrame(RawFrame frame, CancellationToken ct)
        {
            lock (attempts) { attempts.Add(frame.Pixels[0]); }
            throw new InvalidOperationException("errore del callback");
        }

        CaptureLoop loop = Build(camera, clock, delay, OnFrame);
        loop.OnError += e =>
        {
            lock (errors) { errors.Add(e); }
            firstError.TrySetResult();
            throw new InvalidOperationException("il gestore errori non deve fermare il loop");
        };

        await loop.RunAsync(cts.Token).WaitAsync(WaitLimit);

        Assert.Equal(4, camera.CaptureCalls);
        int[] tried;
        lock (attempts) { tried = attempts.ToArray(); }
        Assert.Contains(4, tried); // l'ultimo frame non viene mai scartato
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.IsType<InvalidOperationException>(e));
    }

    [Fact]
    public async Task Cancellation_CompletesRunAsyncWithoutPendingWork()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var camera = new FakeCamera((n, s, ct) => Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        CaptureLoop loop = Build(camera, clock, new BlockingDelay(), NoOp);

        Task run = loop.RunAsync(cts.Token);
        cts.Cancel();

        await run.WaitAsync(WaitLimit); // non lancia: l'annullamento e' normale
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(CaptureState.Idle, loop.State);
    }

    [Fact]
    public async Task RunAsync_CannotRunTwiceAtOnce()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var camera = new FakeCamera((n, s, ct) => Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        CaptureLoop loop = Build(camera, clock, new BlockingDelay(), NoOp);

        Task first = loop.RunAsync(cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(cts.Token));

        cts.Cancel();
        await first.WaitAsync(WaitLimit);
    }

    [Fact]
    public async Task FrameMedian_UpdatesExposureForNextCapture()
    {
        var clock = new FakeClock(NightUtc);
        using var cts = new CancellationTokenSource();
        var firstProcessed = new TaskCompletionSource();
        var seen = 0;

        // Il frame 2 parte solo dopo che il consumer ha aggiornato l'esposizione dal frame 1.
        var camera = new FakeCamera(async (n, s, ct) =>
        {
            if (n == 2)
            {
                await firstProcessed.Task.WaitAsync(WaitLimit, ct);
            }
            return Frames.Uniform(2000, s, clock.UtcNow); // ~3% di MaxAdu: scuro
        });
        // Il produttore legge l'impostazione corrente PRIMA di chiamare la camera: per rendere il test deterministico
        // la prima attesa dopo il frame 1 aspetta che il consumer abbia aggiornato l'esposizione (corsa vista su CI Windows).
        var delay = new GatedDelay(new FakeDelay(clock, n => { if (n == 2) cts.Cancel(); }), firstProcessed.Task);

        Task OnFrame(RawFrame frame, CancellationToken ct)
        {
            if (Interlocked.Increment(ref seen) == 1)
            {
                firstProcessed.TrySetResult();
            }
            return Task.CompletedTask;
        }

        CaptureLoop loop = Build(camera, clock, delay, OnFrame);
        await loop.RunAsync(cts.Token).WaitAsync(WaitLimit);

        ExposureSetting[] requests = camera.Requests;
        Assert.Equal(10.0, requests[0].ExposureMs);
        Assert.True(requests[1].ExposureMs > 10.0, "l'esposizione dovrebbe salire di notte con misura scura");
        Assert.Equal(250.0, requests[1].Gain);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        var clock = new FakeClock(NightUtc);
        var camera = new FakeCamera((n, s, ct) => Task.FromResult(Frames.Uniform(1000, s, clock.UtcNow)));
        var ae = new AutoExposure(new AutoExposureOptions(2, 30000, 0, 500), 65535, new ExposureSetting(10, 250));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CaptureLoop(camera, clock, NoOp, new BlockingDelay(), ae, new CaptureLoopOptions(0, true, 45, 7)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CaptureLoop(camera, clock, NoOp, new BlockingDelay(), ae, new CaptureLoopOptions(30, true, 91, 7)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CaptureLoop(camera, clock, NoOp, new BlockingDelay(), ae, new CaptureLoopOptions(30, true, 45, 181)));
    }

    /// <summary>Ritardo che, alla prima chiamata, attende un segnale prima di delegare al ritardo finto.</summary>
    private sealed class GatedDelay : ICaptureDelay
    {
        private readonly ICaptureDelay _inner;
        private readonly Task _gate;
        private int _calls;

        public GatedDelay(ICaptureDelay inner, Task gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await _gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            await _inner.DelayAsync(delay, cancellationToken);
        }
    }
}
