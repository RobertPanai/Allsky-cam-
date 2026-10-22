using AllSky.Astro;
using AllSky.Capture;
using AllSky.Cameras.Sim;
using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Core.Passes;
using AllSky.Detection;
using AllSky.Engine;
using AllSky.Engine.Detecting;
using AllSky.Events;
using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using AllSky.Transits;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>Elaboratore di prova: luminanza del frame grezzo (bit alti) replicata sui tre canali. Il cielo e' notturno a quell'ora.</summary>
internal sealed class LumaProcessor : IFrameProcessor
{
    private readonly AppConfig _config;

    public LumaProcessor(AppConfig config)
    {
        _config = config;
    }

    public ProcessedFrame Process(RawFrame raw)
    {
        double sun = SolarPosition.Altitude(raw.Timestamp, _config.Site.Latitude, _config.Site.Longitude);
        string phase = SkyPhases.Simplify(SkyPhases.Classify(sun));
        var rgb = new byte[raw.Width * raw.Height * 3];
        for (int i = 0; i < raw.Pixels.Length; i++)
        {
            byte v = (byte)(raw.Pixels[i] >> 8);
            rgb[3 * i] = v;
            rgb[3 * i + 1] = v;
            rgb[3 * i + 2] = v;
        }

        return new ProcessedFrame(
            raw.Width,
            raw.Height,
            rgb,
            [0xFF, 0xD8, 0x00, 0xFF, 0xD9],
            [0xFF, 0xD8, 0x01, 0xFF, 0xD9],
            phase,
            sun,
            1000);
    }

    public byte[] ProcessRawPreview(RawFrame raw) => [0xFF, 0xD8, 0x02, 0xFF, 0xD9];

    public void UpdateConfig(AppConfig config)
    {
    }
}

/// <summary>
/// Orbita finta con azimut che scorre da 90 a 270 gradi (da est a ovest) e archo di elevazione: la traccia nei pixel
/// va da un lato all'altro del cielo, quasi orizzontale.
/// </summary>
internal sealed class SweepPropagator : IOrbitPropagator
{
    private readonly DateTimeOffset _start;
    private readonly TimeSpan _duration;
    private readonly double _maxElevationDeg;

    public SweepPropagator(DateTimeOffset start, TimeSpan duration, double maxElevationDeg)
    {
        _start = start;
        _duration = duration;
        _maxElevationDeg = maxElevationDeg;
    }

    public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)
    {
        double f = (utc - _start).TotalSeconds / _duration.TotalSeconds;
        if (f < 0 || f > 1)
        {
            return new SatObservation(0.0, -30.0, 500.0, 7000.0, 0.0, 0.0);
        }

        return new SatObservation(90.0 + 180.0 * f, _maxElevationDeg * Math.Sin(Math.PI * f), 500.0, 7000.0, 0.0, 0.0);
    }
}

internal sealed class FixedTleFetcher : ITleFetcher
{
    public Task<TleFetchResult> FetchAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)
    {
        var sets = targets.Select(t => new TleSet(t.NoradId, t.Name, "1 line", "2 line", DateTimeOffset.UnixEpoch)).ToList();
        return Task.FromResult(new TleFetchResult(sets, TleFetchStatus.Fresh, null));
    }
}

internal sealed class FixedPropagators : IOrbitPropagatorFactory
{
    private readonly IOrbitPropagator _propagator;

    public FixedPropagators(IOrbitPropagator propagator)
    {
        _propagator = propagator;
    }

    public IOrbitPropagator Create(TleSet tle) => _propagator;
}

public sealed class DetectionEngineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    // 2026-10-19 22:00 UTC = 00:00 locali (CEST): notte osservativa del 19.
    private static readonly DateTimeOffset NightStart = new(2026, 10, 19, 22, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan FramePacing = TimeSpan.FromMilliseconds(50);

    private const int W = 128;
    private const int H = 96;
    private const int Background = 20;

    [Fact]
    public async Task MeteoraIniettata_SalvaUnEventoMeteoraSuDisco()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true)));
        int index = 0;
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            int k = Interlocked.Increment(ref index) - 1;
            IReadOnlyList<(int X, int Y)>? streak = k == 5 ? Line((16, 30), (60, 30)) : null;
            return Task.FromResult(Frame(clock.UtcNow, streak, 30));
        });

        await RunFramesAsync(h, clock, camera, frames: 12, configure: null, locateFfmpeg: null);

        List<EventRecord> events = new EventArchive(h.ArchiveFolder).List().ToList();
        EventRecord meteor = Assert.Single(events);
        Assert.Equal(EventKind.Meteor, meteor.Kind);
        Assert.Equal(StreakClass.Meteor, meteor.Class);
        Assert.Equal("crop.jpg", meteor.Files.Crop);
        Assert.Equal("clip.mp4", meteor.Files.Clip);
    }

    [Fact]
    public async Task DetectionSpenta_NessunEventoNessunaChiamataAlFfmpeg()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: false)));
        int index = 0;
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            int k = Interlocked.Increment(ref index) - 1;
            return Task.FromResult(Frame(clock.UtcNow, k == 5 ? Line((16, 30), (60, 30)) : null, 30));
        });
        int locatorCalls = 0;

        await RunFramesAsync(h, clock, camera, frames: 12, configure: null, locateFfmpeg: () =>
        {
            Interlocked.Increment(ref locatorCalls);
            return "/opt/test/ffmpeg";
        });

        Assert.Empty(new EventArchive(h.ArchiveFolder).List());
        Assert.Equal(0, Volatile.Read(ref locatorCalls));
        Assert.DoesNotContain(h.Logs, l => l.Contains("ilevamento", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(h.ArchiveFolder, ".transit-staging")));
    }

    [Fact]
    public async Task FfmpegMancante_EventoSalvatoSenzaClipEMotivoNelLog()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true)));
        int index = 0;
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            int k = Interlocked.Increment(ref index) - 1;
            return Task.FromResult(Frame(clock.UtcNow, k == 5 ? Line((16, 30), (60, 30)) : null, 30));
        });

        await RunFramesAsync(h, clock, camera, frames: 12, configure: null, locateFfmpeg: () => null);

        EventRecord meteor = Assert.Single(new EventArchive(h.ArchiveFolder).List());
        Assert.Null(meteor.Files.Clip);
        Assert.Equal("crop.jpg", meteor.Files.Crop);
        Assert.Contains(h.Logs, l => l.Contains("ffmpeg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErroreDelSaver_NonFermaLaCattura()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true)));

        // Un file al posto della cartella "events" della notte: la creazione dell'evento fallisce.
        string night = Path.Combine(h.ArchiveFolder, "nights", "2026-10-19");
        Directory.CreateDirectory(night);
        File.WriteAllText(Path.Combine(night, "events"), "bloccato");

        int index = 0;
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            int k = Interlocked.Increment(ref index) - 1;
            return Task.FromResult(Frame(clock.UtcNow, k == 5 ? Line((16, 30), (60, 30)) : null, 30));
        });

        await using var engine = await RunFramesAsync(h, clock, camera, frames: 12, configure: null, locateFfmpeg: null, keepEngine: true);

        Assert.NotEqual("error", engine.GetStatus().State);
        Assert.Null(engine.GetStatus().Error);
        Assert.True(engine.ListFrames("2026-10-19", 0, int.MaxValue).Count >= 12);
        Assert.Contains(h.Logs, l => l.StartsWith("Rilevamento del frame non riuscito", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SatellitePrevisto_SullaTraccia_NonGeneraUnaMeteora()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true), Calibrated(), Satellites(enabled: true)));

        EventRecord[] events = await RunWithSatelliteStreakAsync(h, clock, satellitesEnabled: true);

        Assert.Empty(events);
    }

    [Fact]
    public async Task StessaScia_SenzaSatellitiPrevisti_GeneraUnaMeteora()
    {
        // Controllo del caso sopra: stessa scia, nessun satellite in configurazione → meteora.
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true), Calibrated(), Satellites(enabled: false)));

        EventRecord[] events = await RunWithSatelliteStreakAsync(h, clock, satellitesEnabled: false);

        EventRecord meteor = Assert.Single(events);
        Assert.Equal(EventKind.Meteor, meteor.Kind);

        // Con calibrazione valida la scia ha alt/az a inizio e fine, nella convenzione del frame ridotto (qui 1:1).
        Assert.NotNull(meteor.StartAltAz);
        Assert.NotNull(meteor.EndAltAz);
        Assert.InRange(meteor.StartAltAz!.AltDeg, 0.0, 90.0);
    }

    [Fact]
    public async Task TransitoIdentificato_SalvaUnEventoDiTransito()
    {
        using var h = new Harness();
        var clock = new FakeClock(NightStart);
        h.SaveConfig(Config(h.ArchiveFolder, Detection(enabled: true), Calibrated(), Satellites(enabled: true)));

        SweepPropagator propagator = Sweep();
        var satellites = new FixedPropagators(propagator);
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            DateTimeOffset ts = clock.UtcNow;
            // Lunga esposizione simulata: in ogni frame del passaggio la scia copre tutta la traccia prevista.
            IReadOnlyList<(int X, int Y)>? streak = InPass(ts) ? TrackLine(propagator) : null;
            return Task.FromResult(Frame(ts, streak, 25));
        });

        await RunFramesAsync(
            h,
            clock,
            camera,
            frames: 13,
            configure: builder => builder.Propagators = satellites,
            locateFfmpeg: null,
            waitForForecast: true);

        List<EventRecord> events = new EventArchive(h.ArchiveFolder).List().ToList();
        EventRecord transit = Assert.Single(events, e => e.Kind == EventKind.Transit);
        Assert.NotNull(transit.TransitInfo);
        Assert.Equal(TransitVerdict.Identificato, transit.TransitInfo!.Verdict);
        Assert.Equal("composite.jpg", transit.Files.Composite);
        Assert.Equal("clip.mp4", transit.Files.Clip);
        Assert.Empty(events.Where(e => e.Kind == EventKind.Meteor));
        Assert.False(Directory.Exists(Path.Combine(h.ArchiveFolder, ".transit-staging")));
    }

    [Fact]
    public void ModalitaTransito_FinestraDelPassaggioRiduceLAttesa()
    {
        // Orologio finto: passaggio tra T0+1 min e T0+11 min. Intervallo normale 60 s, esposizione 5 s, lettura 2 s.
        DateTimeOffset t0 = NightStart;
        var windows = new ForecastTransitWindowSource(() => new[]
        {
            new PassForecastItem("ISS", 25544, t0 + TimeSpan.FromMinutes(1), t0 + TimeSpan.FromMinutes(5), t0 + TimeSpan.FromMinutes(11), 45, 90, 270, 1.0, true, false, false),
        });
        var policy = new TransitCadencePolicy(windows, new TransitCadenceOptions { Enabled = true, IntervalFraction = 0.5, RestoreMarginSeconds = 5, ReadoutSeconds = 2 });
        TimeSpan normal = TimeSpan.FromSeconds(60);
        TimeSpan exposure = TimeSpan.FromSeconds(5);

        // L'attesa si misura da "ora" (fine della posa): inizio della posa successiva meno ora.
        // Posa prima del passaggio, a t0+5 s: la successiva e' a t0+60 s, quindi 55 s.
        Assert.Equal(TimeSpan.FromSeconds(55), policy.WaitAfterPose(t0, t0 + exposure, normal, exposure));

        // Posa durante il passaggio (t0+70 s): intervallo ridotto alla meta', 30 s, quindi la successiva e' a t0+100 s.
        DateTimeOffset inside = t0 + TimeSpan.FromSeconds(70);
        Assert.Equal(TimeSpan.FromSeconds(25), policy.WaitAfterPose(inside, inside + exposure, normal, exposure));

        // Posa dopo la fine del passaggio: di nuovo l'intervallo normale, 55 s dalla fine della posa.
        DateTimeOffset after = t0 + TimeSpan.FromMinutes(12);
        Assert.Equal(TimeSpan.FromSeconds(55), policy.WaitAfterPose(after, after + exposure, normal, exposure));
    }

    // ---- scenari ----

    private static async Task<EventRecord[]> RunWithSatelliteStreakAsync(Harness h, FakeClock clock, bool satellitesEnabled)
    {
        SweepPropagator propagator = Sweep();
        var satellites = new FixedPropagators(propagator);
        bool injected = false;
        var camera = new TestCamera(new SimCamera(SmallSim(), clock), (_, _, _) =>
        {
            DateTimeOffset ts = clock.UtcNow;
            // Una sola scia, a meta' del passaggio: orizzontale, sul centro della traccia prevista (chord est-ovest).
            bool mid = ts >= NightStart.AddMinutes(4.5) && ts < NightStart.AddMinutes(5.5) && !injected;
            if (mid)
            {
                injected = true;
            }

            return Task.FromResult(Frame(ts, mid ? Line((40, 48), (88, 48)) : null, 30));
        });

        await RunFramesAsync(h, clock, camera, frames: 12, configure: builder => builder.Propagators = satellites, locateFfmpeg: null, waitForForecast: satellitesEnabled);
        return new EventArchive(h.ArchiveFolder).List().ToArray();
    }

    /// <summary>Esegue la cattura per <paramref name="frames"/> frame, poi ferma il servizio (che svuota la coda del rilevamento).</summary>
    private static async Task<AllSkyEngine> RunFramesAsync(
        Harness h,
        FakeClock clock,
        TestCamera camera,
        int frames,
        Action<ScenarioBuilder>? configure,
        Func<string?>? locateFfmpeg,
        bool waitForForecast = false,
        bool keepEngine = false)
    {
        var builder = new ScenarioBuilder();
        configure?.Invoke(builder);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int produced = 0;
        AllSkyEngine? engine = null;
        var delay = new ScriptDelay(clock, async (_, ct) =>
        {
            await WaitForConsumer(camera, () => Volatile.Read(ref produced), ct);

            // La coda del rilevamento scarta il frame più vecchio se il worker resta indietro di oltre quattro frame:
            // una pausa reale tra due catture lascia tempo al worker, così il test non dipende dalla velocità della macchina.
            await Task.Delay(FramePacing, ct);
            if (waitForForecast && engine is not null)
            {
                while (engine.GetForecast().Status != PassForecastStatus.Ready)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(5, ct);
                }
            }

            if (Volatile.Read(ref produced) < frames)
            {
                return;
            }

            reached.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        engine = new AllSkyEngine(h.Dependencies(
            clock,
            new TestCameraProvider(_ => camera),
            delay,
            processor: config => new LumaProcessor(config),
            locateFfmpeg: locateFfmpeg,
            tle: builder.Tle ?? new FixedTleFetcher(),
            propagators: builder.Propagators));
        engine.FrameProduced += _ => Interlocked.Increment(ref produced);
        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();
        if (keepEngine)
        {
            return engine;
        }

        await engine.DisposeAsync();
        return engine;
    }

    private sealed class ScenarioBuilder
    {
        public IOrbitPropagatorFactory? Propagators { get; set; }

        public ITleFetcher? Tle { get; set; }
    }

    // ---- frame e geometria ----

    private static RawFrame Frame(DateTimeOffset ts, IReadOnlyList<(int X, int Y)>? streak, int excess)
    {
        var pixels = new ushort[W * H];
        Array.Fill(pixels, (ushort)(Background << 8));
        if (streak is not null)
        {
            foreach ((int x, int y) in streak)
            {
                pixels[y * W + x] = (ushort)((Background + excess) << 8);
            }
        }

        return new RawFrame(W, H, pixels, BayerPattern.None, ts, 30000, 250, null);
    }

    /// <summary>Punti della linea tra due estremi, arrotondati ai pixel.</summary>
    private static IReadOnlyList<(int X, int Y)> Line((int X, int Y) a, (int X, int Y) b)
    {
        int steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) * 2 + 1;
        var points = new List<(int, int)>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            points.Add((
                (int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero)));
        }

        return points.Distinct().ToList();
    }

    /// <summary>Calibrazione accettata coerente con il frame di prova (128x96, zenit al centro, orizzonte a 45 px).</summary>
    private static OpticalCalibrationConfig Calibrated() => new()
    {
        CenterX = W / 2.0,
        CenterY = H / 2.0,
        ScaleRadiusPx = 45.0,
        NorthRotationDeg = 0.0,
        Mirrored = false,
        K2 = 0.0,
        FrameWidthPx = W,
        FrameHeightPx = H,
        Source = OpticalCalibrationConfig.SunSource,
        RmsPx = 0.5,
    };

    private static SweepPropagator Sweep() => new(NightStart.AddMinutes(2), TimeSpan.FromMinutes(6), 60.0);

    private static bool InPass(DateTimeOffset ts) => ts >= NightStart.AddMinutes(2) && ts <= NightStart.AddMinutes(8);

    /// <summary>Traccia del passaggio nei pixel, campionata ogni secondo: la stessa geometria che usa il confermatore.</summary>
    private static IReadOnlyList<(int X, int Y)> TrackLine(IOrbitPropagator propagator)
    {
        var model = Calibrated();
        var fisheye = new FisheyeModel(model.CenterX, model.CenterY, model.ScaleRadiusPx, 0.0, false, 0.0);
        var pixels = new List<(double X, double Y)>();
        for (DateTimeOffset t = NightStart.AddMinutes(2); t <= NightStart.AddMinutes(8); t = t.AddSeconds(1))
        {
            SatObservation obs = propagator.Observe(t, 45.07, 7.69, 240);
            if (obs.ElevationDeg >= 0 && fisheye.TryAltAzToPixel(obs.ElevationDeg, obs.AzimuthDeg, out double x, out double y) && fisheye.IsInsideSky(x, y))
            {
                pixels.Add((x, y));
            }
        }

        var line = new List<(int X, int Y)>();
        for (int i = 1; i < pixels.Count; i++)
        {
            (int X, int Y) a = ((int)Math.Round(pixels[i - 1].X), (int)Math.Round(pixels[i - 1].Y));
            (int X, int Y) b = ((int)Math.Round(pixels[i].X), (int)Math.Round(pixels[i].Y));
            line.AddRange(Line(a, b));
        }

        return line.Distinct().ToList();
    }

    private static AppConfig.DetectionConfig Detection(bool enabled) => new()
    {
        Enabled = enabled,
        ClipFps = 25,
        Streak = new AppConfig.DetectionConfig.StreakSettings { TrackDistancePx = 20.0, TrackAngleDeg = 90.0 },
    };

    private static AppConfig.SatellitesConfig Satellites(bool enabled) => new()
    {
        Enabled = enabled,
        Targets = [new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = 25544, Name = "ISS" }],
    };

    private static AppConfig Config(
        string folder,
        AppConfig.DetectionConfig detection,
        OpticalCalibrationConfig? optics = null,
        AppConfig.SatellitesConfig? satellites = null)
    {
        return new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = "sim-test" },
            Capture = new AppConfig.CaptureConfig { IntervalSeconds = 60 },
            Storage = new AppConfig.StorageConfig { Folder = folder, KeepNights = 30 },
            Detection = detection,
            Optics = optics,
            Satellites = satellites ?? new AppConfig.SatellitesConfig(),
        };
    }

    private static SimCameraOptions SmallSim()
    {
        return new SimCameraOptions { Width = W, Height = H, Latitude = 45.07, Longitude = 7.69 };
    }

    /// <summary>Come in <see cref="AllSkyEngineTests"/>: il simulatore non deve correre avanti al consumer.</summary>
    private static async Task WaitForConsumer(TestCamera camera, Func<int> produced, CancellationToken ct)
    {
        while (camera.Captures - produced() > 1)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
