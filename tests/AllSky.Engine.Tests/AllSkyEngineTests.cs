using AllSky.Archive;
using AllSky.Cameras.Sim;
using AllSky.Core;
using AllSky.Engine;
using Xunit;

namespace AllSky.Engine.Tests;

public sealed class AllSkyEngineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task QuattroOreAlTramonto_FrameFinisconoNellaNotteGiusta()
    {
        using var h = new Harness();
        var start = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero); // 17:00 locali (CEST)
        DateTimeOffset end = start.AddHours(4); // il cielo e' notturno solo dopo le 20:00 locali
        var clock = new FakeClock(start);
        h.SaveConfig(Config(h.ArchiveFolder, intervalSeconds: 60));

        var camera = new TestCamera(new SimCamera(SmallSim(), clock));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int produced = 0;
        var delay = new ScriptDelay(clock, async (_, ct) =>
        {
            await WaitForConsumer(camera, () => Volatile.Read(ref produced), ct);
            if (clock.UtcNow < end)
            {
                return;
            }

            reached.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        await using var engine = new AllSkyEngine(h.Dependencies(clock, new TestCameraProvider(_ => camera), delay));
        engine.FrameProduced += _ => Interlocked.Increment(ref produced);
        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();

        NightDto night = Assert.Single(engine.ListNights(), n => n.Id == "2026-10-19");
        Assert.Equal(1, engine.ListNights().Count);
        Assert.True(night.Frames > 100);

        IReadOnlyList<FrameDto> frames = engine.ListFrames("2026-10-19", 0, int.MaxValue);
        Assert.Equal(night.Frames, frames.Count);
        Assert.True(frames.All(f => f.Timestamp >= start && f.Timestamp <= end));

        List<string> phases = frames.Select(f => f.Phase).ToList();
        Assert.Contains("day", phases);
        Assert.Contains("twilight", phases);
        Assert.Contains("night", phases);
        Assert.NotNull(engine.GetLatestJpeg(false));
    }

    [Fact]
    public async Task CambioNotteAMezzogiorno_ScriveProdottiAccodaTimelapseEApplicaRetention()
    {
        using var h = new Harness();
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 18, 18, 0, 0, TimeSpan.Zero)); // 20:00 locali del 18
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

        // Notti precedenti e una frame gia' archiviata della notte successiva (retention: keepNights = 3).
        var preexisting = new NightArchive(h.ArchiveFolder, clock, tz);
        foreach (int day in new[] { 14, 15, 16, 17 })
        {
            // 22:00 UTC del giorno indicato = 00:00 locali del giorno dopo: appartiene alla notte "day".
            preexisting.SaveFrame(new DateTimeOffset(2026, 10, day, 22, 0, 0, TimeSpan.Zero), "night", jpeg, jpeg, new FrameMeta(1, 1, 1));
        }

        preexisting.SaveFrame(new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero), "day", jpeg, jpeg, new FrameMeta(1, 1, 1));
        h.SaveConfig(Config(h.ArchiveFolder, intervalSeconds: 300, keepNights: 3));

        // Ferma il simulatore dopo il cambio notte (13:00 locali), attendendo che il timelapse sia finito.
        DateTimeOffset stopAt = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);
        var camera = new TestCamera(new SimCamera(SmallSim(), clock));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int produced = 0;
        var delay = new ScriptDelay(clock, async (_, ct) =>
        {
            await WaitForConsumer(camera, () => Volatile.Read(ref produced), ct);
            if (clock.UtcNow < stopAt)
            {
                return;
            }

            await h.Timelapses.Done.WaitAsync(TimeSpan.FromSeconds(60), ct);
            reached.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        await using var engine = new AllSkyEngine(h.Dependencies(clock, new TestCameraProvider(_ => camera), delay));
        engine.FrameProduced += _ => Interlocked.Increment(ref produced);
        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();

        int framesNight = engine.ListFrames("2026-10-18", 0, int.MaxValue).Count;
        Assert.True(framesNight > 100);

        // Prodotti della notte chiusa.
        string keogramPath = engine.GetProductPath("2026-10-18", NightProduct.Keogram);
        string startrailsPath = engine.GetProductPath("2026-10-18", NightProduct.Startrails);
        Assert.True(File.Exists(keogramPath));
        Assert.True(File.Exists(startrailsPath));
        Assert.True(File.Exists(engine.GetProductPath("2026-10-18", NightProduct.Timelapse)));

        // Il keogram usa una colonna per frame; al cambio notte possono mancare i frame ancora in coda nel ciclo.
        byte[] keogram = File.ReadAllBytes(keogramPath);
        int keogramWidth = (keogram[2] << 8) | keogram[3];
        Assert.InRange(keogramWidth, framesNight - 2, framesNight);
        byte[] startrails = File.ReadAllBytes(startrailsPath);
        Assert.Equal(FakeProcessor.Width, (startrails[2] << 8) | startrails[3]);

        // Il timelapse legge l'archivio al momento del job: i frame ancora in coda a mezzogiorno possono mancare.
        Assert.InRange(h.Timelapses.LastFrameCount, framesNight - 2, framesNight);
        NightDto closed = Assert.Single(engine.ListNights(), n => n.Id == "2026-10-18");
        Assert.Equal("ready", closed.Timelapse);

        // Retention: si conservano le ultime tre notti (19, 18, 17); 14, 15 e 16 vengono cancellate.
        string nightsRoot = Path.Combine(h.ArchiveFolder, "nights");
        Assert.True(Directory.Exists(Path.Combine(nightsRoot, "2026-10-17")));
        Assert.False(Directory.Exists(Path.Combine(nightsRoot, "2026-10-16")));
        Assert.False(Directory.Exists(Path.Combine(nightsRoot, "2026-10-15")));
        Assert.False(Directory.Exists(Path.Combine(nightsRoot, "2026-10-14")));
    }

    [Fact]
    public void UpdateConfig_ConErroreDiValidazione_NonCambiaNullaENonScrive()
    {
        using var h = new Harness();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        var engine = new AllSkyEngine(h.Dependencies(
            clock,
            new TestCameraProvider(_ => throw new InvalidOperationException("non usata")),
            new ScriptDelay(clock)));

        AppConfig before = engine.GetConfig();
        AppConfig invalid = before with { Capture = before.Capture with { IntervalSeconds = 0 } };

        ConfigUpdateResult rejected = engine.UpdateConfig(invalid);

        Assert.False(rejected.Ok);
        Assert.Contains(rejected.Errors, e => e.Path == "capture.intervalSeconds");
        Assert.Same(before, engine.GetConfig());
        Assert.False(File.Exists(h.ConfigPath));

        AppConfig valid = before with { Capture = before.Capture with { IntervalSeconds = 45 } };
        ConfigUpdateResult accepted = engine.UpdateConfig(valid);

        Assert.True(accepted.Ok);
        Assert.Empty(accepted.Errors);
        Assert.Equal(45, engine.GetConfig().Capture.IntervalSeconds);
        Assert.True(File.Exists(h.ConfigPath));
    }

    [Fact]
    public async Task CameraCheLancia_StatoError_EventoEmessoEStopLasciaIdle()
    {
        using var h = new Harness();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        h.SaveConfig(Config(h.ArchiveFolder, intervalSeconds: 60));

        var camera = new TestCamera(
            new SimCamera(SmallSim(), clock),
            (_, _, _) => Task.FromException<RawFrame>(new InvalidOperationException("Il sensore non risponde.")));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ScriptDelay(clock, (index, ct) =>
        {
            if (index < 3)
            {
                return Task.CompletedTask;
            }

            reached.TrySetResult();
            return Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        await using var engine = new AllSkyEngine(h.Dependencies(clock, new TestCameraProvider(_ => camera), delay));
        var states = new List<string>();
        engine.StatusChanged += s =>
        {
            lock (states)
            {
                states.Add(s.State);
            }
        };

        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);

        StatusDto status = engine.GetStatus();
        Assert.Equal("error", status.State);
        Assert.NotNull(status.Error);
        Assert.Contains("sensore", status.Error!);
        lock (states)
        {
            Assert.Contains("error", states);
        }

        await engine.StopAsync();

        Assert.Equal(1, camera.Disposals);
        StatusDto stopped = engine.GetStatus();
        Assert.Equal("idle", stopped.State);
        Assert.Null(stopped.Error);
        Assert.False(stopped.Camera.Connected);
    }

    [Fact]
    public async Task StopPulito_EmetteEventiERilasciaLaCamera()
    {
        using var h = new Harness();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        h.SaveConfig(Config(h.ArchiveFolder, intervalSeconds: 60));

        var camera = new TestCamera(new SimCamera(SmallSim(), clock));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ScriptDelay(clock, (index, ct) =>
        {
            if (index < 4)
            {
                return Task.CompletedTask;
            }

            reached.TrySetResult();
            return Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        await using var engine = new AllSkyEngine(h.Dependencies(clock, new TestCameraProvider(_ => camera), delay));
        var frames = new List<FrameEvent>();
        int statusEvents = 0;
        engine.FrameProduced += f =>
        {
            lock (frames)
            {
                frames.Add(f);
            }
        };
        engine.StatusChanged += _ => Interlocked.Increment(ref statusEvents);

        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();

        lock (frames)
        {
            Assert.NotEmpty(frames);
            Assert.Equal(16, frames[0].Id.Length);
        }

        Assert.True(Volatile.Read(ref statusEvents) > 0);
        Assert.NotNull(engine.GetLatestJpeg(false));
        Assert.NotNull(engine.GetLatestJpeg(true));
        Assert.Equal(1, camera.Disposals);
        Assert.Equal("idle", engine.GetStatus().State);
        Assert.False(engine.GetStatus().Camera.Connected);
    }

    /// <summary>
    /// Il simulatore non deve correre avanti al consumer: il canale del ciclo ha capacita' 2 e scarterebbe frame.
    /// Attende finche' al piu' un frame catturato non e' ancora stato elaborato.
    /// </summary>
    private static async Task WaitForConsumer(TestCamera camera, Func<int> produced, CancellationToken ct)
    {
        while (camera.Captures - produced() > 1)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static SimCameraOptions SmallSim()
    {
        return new SimCameraOptions { Width = 128, Height = 96, Latitude = 45.07, Longitude = 7.69 };
    }

    private static AppConfig Config(string folder, int intervalSeconds, int keepNights = 30)
    {
        return new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = "sim-test" },
            Capture = new AppConfig.CaptureConfig { IntervalSeconds = intervalSeconds },
            Storage = new AppConfig.StorageConfig { Folder = folder, KeepNights = keepNights },
        };
    }
}
