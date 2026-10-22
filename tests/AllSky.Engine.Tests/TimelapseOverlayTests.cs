using AllSky.Archive;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Timelapse;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>Maschera nel job di timelapse dell'Engine: flag, sidecar per frame e rigenerazione con la configurazione corrente.</summary>
public sealed class TimelapseOverlayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateTimeOffset FirstFrame = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);
    private const string NightId = "2026-10-19";

    [Fact]
    public async Task FlagAttivo_OgniFrameDelTimelapseUsaIlSidecarDelProprioIstante()
    {
        using var h = new Harness();
        var clock = new FakeClock(FirstFrame.AddHours(1));
        SaveNight(h, clock, frameCount: 3, sidecarFor: i => i != 2);
        h.SaveConfig(Config(h.ArchiveFolder, applyToTimelapse: true));
        var overlay = new FactoryRecorder();

        await RunTimelapseAsync(h, clock, overlay.Create);

        TimelapseOptions options = h.Timelapses.LastOptions!;
        Assert.NotNull(options.Overlay);
        Assert.Equal(1, overlay.Calls);
        for (int i = 0; i < 3; i++)
        {
            // Frame N, orario N: anche il frame 2, senza sidecar salvato, ricostruito dal suo istante.
            Assert.Equal(FirstFrame.AddMinutes(i), options.Overlay!.SidecarAt(i)?.TimestampUtc);
        }
    }

    [Fact]
    public async Task FlagSpento_NessunaMascheraENessunaChiamataAllaFabbrica()
    {
        using var h = new Harness();
        var clock = new FakeClock(FirstFrame.AddHours(1));
        SaveNight(h, clock, frameCount: 2, sidecarFor: _ => true);
        h.SaveConfig(Config(h.ArchiveFolder, applyToTimelapse: false));
        var overlay = new FactoryRecorder();

        await RunTimelapseAsync(h, clock, overlay.Create);

        Assert.Null(h.Timelapses.LastOptions!.Overlay);
        Assert.Equal(0, overlay.Calls);
    }

    [Fact]
    public async Task Rigenerazione_RileggeLaConfigurazioneCorrente()
    {
        using var h = new Harness();
        var clock = new FakeClock(FirstFrame.AddHours(1));
        SaveNight(h, clock, frameCount: 2, sidecarFor: _ => true);
        h.SaveConfig(Config(h.ArchiveFolder, applyToTimelapse: false));
        var seen = new List<AppConfig>();

        await using var engine = new AllSkyEngine(h.Dependencies(
            clock,
            new TestCameraProvider(_ => throw new InvalidOperationException("non usata")),
            new ScriptDelay(clock),
            config => { seen.Add(config); return NullFrameOverlay.Instance; }));
        await engine.StartAsync();

        // Prima generazione: maschera spenta, la fabbrica non viene chiamata.
        await RequestAndWaitAsync(engine, h, 1);
        Assert.Null(h.Timelapses.LastOptions!.Overlay);
        Assert.Empty(seen);

        // Si accende la maschera e si rigenera: il job deve usare la configurazione aggiornata.
        AppConfig current = engine.GetConfig();
        ConfigUpdateResult result = engine.UpdateConfig(current with { Overlay = current.Overlay with { ApplyToTimelapse = true } });
        Assert.True(result.Ok, "la configurazione deve essere accettata");

        await RequestAndWaitAsync(engine, h, 2);
        Assert.NotNull(h.Timelapses.LastOptions!.Overlay);
        Assert.Single(seen);
        Assert.True(seen[0].Overlay.ApplyToTimelapse);
        await engine.StopAsync();
    }

    /// <summary>Crea la notte con frame e, dove richiesto, il sidecar. Il frame senza sidecar viene ricostruito dall'indice.</summary>
    private static void SaveNight(Harness h, FakeClock clock, int frameCount, Func<int, bool> sidecarFor)
    {
        var archive = new NightArchive(h.ArchiveFolder, clock, Tz);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var site = new ObservingSite { LatitudeDeg = 45.07, LongitudeDeg = 7.69 };
        for (int i = 0; i < frameCount; i++)
        {
            DateTimeOffset ts = FirstFrame.AddMinutes(i);
            archive.SaveFrame(ts, "night", jpeg, jpeg, new FrameMeta(1, 1, 1));
            if (sidecarFor(i))
            {
                archive.WriteSidecar(FrameSidecars.FromCapture(ts, "night", 1000, 1, null, site, Tz));
            }
        }
    }

    private static async Task RunTimelapseAsync(Harness h, FakeClock clock, Func<AppConfig, IFrameOverlay?> factory)
    {
        await using var engine = new AllSkyEngine(h.Dependencies(
            clock,
            new TestCameraProvider(_ => throw new InvalidOperationException("non usata")),
            new ScriptDelay(clock),
            factory));
        await engine.StartAsync();
        await RequestAndWaitAsync(engine, h, 1);
        await engine.StopAsync();
    }

    /// <summary>Accoda un timelapse e attende che il montaggio n-esimo della sessione sia completato.</summary>
    private static async Task RequestAndWaitAsync(AllSkyEngine engine, Harness h, int jobNumber)
    {
        Assert.True(await engine.RequestTimelapseAsync(NightId));
        await h.Timelapses.WaitForJobsAsync(jobNumber, Timeout);
    }

    private static AppConfig Config(string folder, bool applyToTimelapse)
    {
        return new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = string.Empty },
            Storage = new AppConfig.StorageConfig { Folder = folder, KeepNights = 30 },
            Overlay = new AppConfig.OverlayConfig { ApplyToTimelapse = applyToTimelapse },
        };
    }

    /// <summary>Fabbrica di prova: conta le chiamate e restituisce una maschera nulla.</summary>
    private sealed class FactoryRecorder
    {
        public int Calls { get; private set; }

        public IFrameOverlay Create(AppConfig config)
        {
            Calls++;
            return NullFrameOverlay.Instance;
        }
    }
}
