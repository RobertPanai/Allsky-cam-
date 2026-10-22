using System.Text.Json;
using AllSky.Archive;
using AllSky.Cameras.Sim;
using AllSky.Core;
using AllSky.Core.Passes;
using AllSky.Engine;
using AllSky.Satellites;
using AllSky.Weather;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>
/// Meteo e satelliti nel sidecar dei frame, attraverso il motore reale (cattura simulata, nessuna rete).
/// Il fornitore meteo e' sostituito con uno finto, e la previsione usa TLE e propagatori finti.
/// </summary>
public sealed class WeatherSatelliteSidecarTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan FramePacing = TimeSpan.FromMilliseconds(50);

    // 20:00 UTC = 22:00 locali: notte, tutti i frame appartengono alla notte del 19 ottobre.
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImpostazioniSpente_CampiMeteoESatellitiSonoNull()
    {
        using var h = new Harness();

        List<FrameSidecar> sidecars = await CatturaAsync(h, frames: 3);

        Assert.True(sidecars.Count >= 3);
        Assert.All(sidecars, s =>
        {
            Assert.Null(s.Weather);
            Assert.Null(s.Satellites);
        });
    }

    [Fact]
    public async Task FornitoreMeteo_IlSidecarRiportaLaLetturaConLaSorgente()
    {
        using var h = new Harness();
        var lettura = new WeatherSnapshot(Start, 9.5, 55, 1013, null, 20, 2, 4, 270, 0, null, 20.5, "Open-Meteo");

        List<FrameSidecar> sidecars = await CatturaAsync(h, frames: 3, meteo: new FixedWeatherProvider(lettura));

        Assert.True(sidecars.Count >= 3);
        Assert.All(sidecars, s => Assert.Equal(lettura, s.Weather));
        Assert.All(sidecars, s => Assert.Equal("Open-Meteo", s.Weather!.Source));
    }

    [Fact]
    public async Task ErroreDelFornitoreMeteo_NonBloccaLaCattura()
    {
        using var h = new Harness();

        List<FrameSidecar> sidecars = await CatturaAsync(h, frames: 3, meteo: new ThrowingWeatherProvider());

        // La cattura arriva comunque al numero di frame richiesto: il sidecar esiste, senza dati meteo.
        Assert.True(sidecars.Count >= 3);
        Assert.All(sidecars, s => Assert.Null(s.Weather));
    }

    [Fact]
    public async Task SatellitiAttivi_SopraLOrizzonteConNomeNoradEAngoli_SottoLOrizzonteEscluso()
    {
        using var h = new Harness();
        // La ISS ha un arco che copre tutta la cattura; NOAA 19 non ha archi, quindi resta sotto l'orizzonte.
        var propagatori = new ScriptedPropagators(new Dictionary<int, IOrbitPropagator>
        {
            [25544] = new ArcPropagator((Start.AddHours(-1), TimeSpan.FromHours(3), 40.0)),
            [33591] = new ArcPropagator(),
        });

        List<FrameSidecar> sidecars = await CatturaAsync(
            h,
            frames: 3,
            satelliti: new[] { (25544, "ISS (ZARYA)"), (33591, "NOAA 19") },
            tle: new FixedTleFetcher(),
            propagatori: propagatori);

        Assert.True(sidecars.Count >= 3);
        foreach (FrameSidecar sidecar in sidecars)
        {
            SatelliteInView visibile = Assert.Single(sidecar.Satellites!);
            Assert.Equal("ISS (ZARYA)", visibile.Name);
            Assert.Equal(25544, visibile.NoradId);
            Assert.True(visibile.AltDeg > 0);
            Assert.NotNull(visibile.AzDeg);
        }
    }

    [Fact]
    public async Task SatellitiAttivi_NessunoSopraLOrizzonte_ListaVuotaNonNull()
    {
        using var h = new Harness();
        var propagatori = new ScriptedPropagators(new Dictionary<int, IOrbitPropagator>
        {
            [25544] = new ArcPropagator(),
        });

        List<FrameSidecar> sidecars = await CatturaAsync(
            h,
            frames: 3,
            satelliti: new[] { (25544, "ISS (ZARYA)") },
            tle: new FixedTleFetcher(),
            propagatori: propagatori);

        Assert.True(sidecars.Count >= 3);
        Assert.All(sidecars, s =>
        {
            Assert.NotNull(s.Satellites);
            Assert.Empty(s.Satellites!);
        });
    }

    [Fact]
    public async Task CambioDiConfigurazione_RicreaIlFornitoreSenzaRiavvio()
    {
        using var h = new Harness();
        var clock = new FakeClock(Start);
        h.SaveConfig(Config(h.ArchiveFolder));
        var camera = new TestCamera(new SimCamera(SmallSim(), clock));
        var fabbrica = new WeatherTestFactory();
        EngineDependencies dipendenze = h.Dependencies(clock, new TestCameraProvider(_ => camera), new ScriptDelay(clock))
            with { ObservingConditions = fabbrica };
        await using var engine = new AllSkyEngine(dipendenze);
        Assert.Null(engine.WeatherProvider);

        ConfigUpdateResult ascom = engine.UpdateConfig(engine.GetConfig() with
        {
            Weather = new AppConfig.WeatherConfig
            {
                Source = AppConfig.WeatherConfig.Sources.Ascom,
                AscomProgId = "ASCOM.Test.ObservingConditions",
            },
        });
        Assert.True(ascom.Ok);
        Assert.NotNull(engine.WeatherProvider);

        ConfigUpdateResult spento = engine.UpdateConfig(engine.GetConfig() with { Weather = new AppConfig.WeatherConfig() });
        Assert.True(spento.Ok);
        Assert.Null(engine.WeatherProvider);
    }

    /// <summary>
    /// Esegue la cattura per <paramref name="frames"/> frame e restituisce i sidecar della notte, letti da disco.
    /// I satelliti, se indicati, sono attivi con la previsione calcolata sui TLE e propagatori finti.
    /// </summary>
    private static async Task<List<FrameSidecar>> CatturaAsync(
        Harness h,
        int frames,
        IWeatherSnapshotProvider? meteo = null,
        (int NoradId, string Name)[]? satelliti = null,
        ITleFetcher? tle = null,
        IOrbitPropagatorFactory? propagatori = null)
    {
        var clock = new FakeClock(Start);
        AppConfig config = Config(h.ArchiveFolder);
        if (satelliti is not null)
        {
            config = config with
            {
                Satellites = new AppConfig.SatellitesConfig
                {
                    Enabled = true,
                    Targets = satelliti
                        .Select(t => new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = t.NoradId, Name = t.Name })
                        .ToArray(),
                },
            };
        }

        h.SaveConfig(config);
        var camera = new TestCamera(new SimCamera(SmallSim(), clock));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int produced = 0;
        AllSkyEngine? engine = null;
        var delay = new ScriptDelay(clock, async (_, ct) =>
        {
            await WaitForConsumer(camera, () => Volatile.Read(ref produced), ct);
            await Task.Delay(FramePacing, ct);

            // La previsione si calcola in un ciclo a parte: il primo frame con satelliti deve trovarla pronta.
            if (satelliti is not null && engine is not null)
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
            tle: tle,
            propagators: propagatori));
        if (meteo is not null)
        {
            engine.WeatherProvider = meteo;
        }

        engine.FrameProduced += _ => Interlocked.Increment(ref produced);
        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();
        await engine.DisposeAsync();

        string framesDir = Path.Combine(h.ArchiveFolder, "nights", "2026-10-19", "frames");
        return Directory.EnumerateFiles(framesDir, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<FrameSidecar>(File.ReadAllText(f), FrameSidecars.JsonOptions)!)
            .ToList();
    }

    private static async Task WaitForConsumer(TestCamera camera, Func<int> produced, CancellationToken ct)
    {
        while (camera.Captures - produced() > 1)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static SimCameraOptions SmallSim() =>
        new() { Width = 128, Height = 96, Latitude = 45.07, Longitude = 7.69 };

    private static AppConfig Config(string folder) => new()
    {
        Camera = new AppConfig.CameraConfig { Id = "sim-test" },
        Capture = new AppConfig.CaptureConfig { IntervalSeconds = 60 },
        Storage = new AppConfig.StorageConfig { Folder = folder },
    };
}

/// <summary>Fornitore meteo finto: restituisce sempre la stessa lettura.</summary>
internal sealed class FixedWeatherProvider : IWeatherSnapshotProvider
{
    private readonly WeatherSnapshot _lettura;

    public FixedWeatherProvider(WeatherSnapshot lettura)
    {
        _lettura = lettura;
    }

    public Task<WeatherSnapshot?> GetAsync(CancellationToken ct) => Task.FromResult<WeatherSnapshot?>(_lettura);
}

/// <summary>Fornitore meteo che lancia a ogni lettura: il motore deve registrarlo e andare avanti.</summary>
internal sealed class ThrowingWeatherProvider : IWeatherSnapshotProvider
{
    public Task<WeatherSnapshot?> GetAsync(CancellationToken ct) =>
        throw new InvalidOperationException("sorgente meteo in errore di prova");
}

/// <summary>Fabbrica ASCOM finta per i test dell'Engine: il driver viene creato solo alla prima lettura.</summary>
internal sealed class WeatherTestFactory : IObservingConditionsFactory
{
    public IObservingConditionsReader Create(string progId) => new FactoryTestReader();
}
