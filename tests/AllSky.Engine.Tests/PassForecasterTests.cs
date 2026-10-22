using AllSky.Core;
using AllSky.Core.Passes;
using AllSky.Engine.Forecasting;
using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using AllSky.Transits;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>Propagatore finto: archi di elevazione a forma di seno, azimut fisso a est. Nessuna orbita reale.</summary>
internal sealed class ArcPropagator : IOrbitPropagator
{
    private readonly (DateTimeOffset Start, TimeSpan Duration, double MaxElevationDeg)[] _arcs;

    public ArcPropagator(params (DateTimeOffset Start, TimeSpan Duration, double MaxElevationDeg)[] arcs)
    {
        _arcs = arcs;
    }

    public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)
    {
        double elevation = -30.0;
        foreach ((DateTimeOffset start, TimeSpan duration, double maxElevation) in _arcs)
        {
            double dt = (utc - start).TotalSeconds;
            if (dt >= 0 && dt <= duration.TotalSeconds)
            {
                elevation = Math.Max(elevation, maxElevation * Math.Sin(Math.PI * dt / duration.TotalSeconds));
            }
        }

        return new SatObservation(90.0, elevation, 500.0, 7000.0, 0.0, 0.0);
    }
}

/// <summary>Sorgente TLE finta: conta le richieste e risponde come configurato. Nessuna rete.</summary>
internal sealed class ScriptedTleFetcher : ITleFetcher
{
    public int Calls { get; private set; }

    public Exception? Throw { get; set; }

    public Func<IReadOnlyList<SatelliteTarget>, TleFetchResult>? Respond { get; set; }

    public Task<TleFetchResult> FetchAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)
    {
        Calls++;
        if (Throw is not null)
        {
            return Task.FromException<TleFetchResult>(Throw);
        }

        if (Respond is not null)
        {
            return Task.FromResult(Respond(targets));
        }

        var sets = targets
            .Select(t => new TleSet(t.NoradId, t.Name, "1 line", "2 line", DateTimeOffset.UnixEpoch))
            .ToList();
        return Task.FromResult(new TleFetchResult(sets, TleFetchStatus.Fresh, null));
    }
}

/// <summary>Fabbrica finta: un propagatore per numero NORAD.</summary>
internal sealed class ScriptedPropagators : IOrbitPropagatorFactory
{
    private readonly Dictionary<int, IOrbitPropagator> _byNorad;

    public ScriptedPropagators(Dictionary<int, IOrbitPropagator> byNorad)
    {
        _byNorad = byNorad;
    }

    public IOrbitPropagator Create(TleSet tle) => _byNorad[tle.NoradId];
}

public sealed class PassForecasterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    // Due passaggi: uno tra 1 h (10 min, 45 gradi) e uno tra 5 h (8 min, 30 gradi).
    private static readonly (DateTimeOffset, TimeSpan, double)[] TwoPasses =
    [
        (T0 + TimeSpan.FromHours(1), TimeSpan.FromMinutes(10), 45.0),
        (T0 + TimeSpan.FromHours(5), TimeSpan.FromMinutes(8), 30.0),
    ];

    private static PassForecastSettings Settings(bool enabled = true, params int[] norad) => new(
        enabled,
        norad.Select(n => new SatelliteTarget(n, "SAT " + n)).ToArray(),
        new ObserverSite(45.07, 7.69, 240),
        TimeSpan.FromHours(24),
        new PassOptions());

    private static ScriptedPropagators Propagators(params (int Norad, IOrbitPropagator Propagator)[] items) =>
        new(items.ToDictionary(i => i.Norad, i => i.Propagator));

    [Fact]
    public async Task Disabled_MakesNoRequestAndForecastsNothing()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));

        bool updated = await forecaster.RefreshAsync(Settings(enabled: false, 25544), T0, CancellationToken.None);

        Assert.False(updated);
        Assert.Equal(0, fetcher.Calls);
        PassForecastSnapshot snapshot = forecaster.Snapshot(T0);
        Assert.Equal(PassForecastStatus.Disabled, snapshot.Status);
        Assert.Empty(snapshot.Passes);
        Assert.Empty(forecaster.Evaluate(T0));
    }

    [Fact]
    public async Task Enabled_ComputesPassesInOrderWithCulmination()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));

        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);

        Assert.Equal(1, fetcher.Calls);
        PassForecastSnapshot snapshot = forecaster.Snapshot(T0);
        Assert.Equal(PassForecastStatus.Ready, snapshot.Status);
        Assert.Equal(2, snapshot.Passes.Count);
        Assert.True(snapshot.Passes[0].StartUtc < snapshot.Passes[1].StartUtc);
        Assert.Null(snapshot.Current);
        Assert.Equal(snapshot.Passes[0], snapshot.Next);

        PassForecastItem first = snapshot.Passes[0];
        Assert.Equal("SAT 25544", first.Target);
        Assert.InRange(first.MaxElevationDeg, 44.0, 45.01);
        Assert.True(first.CulminationUtc >= first.StartUtc && first.CulminationUtc <= first.EndUtc);
        // La soglia di 10 gradi viene superata dopo circa 43 s dall'inizio dell'arco.
        Assert.InRange((first.StartUtc - (T0 + TimeSpan.FromHours(1))).TotalSeconds, 30.0, 60.0);
    }

    [Fact]
    public async Task TleIsNotRequestedAgainBeforeTwoHours()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));
        PassForecastSettings settings = Settings(true, 25544);

        await forecaster.RefreshAsync(settings, T0, CancellationToken.None);
        await forecaster.RefreshAsync(settings, T0 + TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(1, fetcher.Calls);

        await forecaster.RefreshAsync(settings, T0 + TimeSpan.FromHours(2), CancellationToken.None);
        Assert.Equal(2, fetcher.Calls);
    }

    [Fact]
    public async Task FetchError_DoesNotCrash_ReportsFailure_AndDoesNotRetryAtOnce()
    {
        var fetcher = new ScriptedTleFetcher { Throw = new HttpRequestException("rete giù") };
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));
        PassForecastSettings settings = Settings(true, 25544);

        bool first = await forecaster.RefreshAsync(settings, T0, CancellationToken.None);
        await forecaster.RefreshAsync(settings, T0 + TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.True(first); // il ricalcolo avviene comunque, con lo stato di errore
        PassForecastSnapshot snapshot = forecaster.Snapshot(T0);
        Assert.Equal(PassForecastStatus.Failed, snapshot.Status);
        Assert.Contains("rete giù", snapshot.Message);
        Assert.Equal(1, fetcher.Calls);
    }

    [Fact]
    public async Task BlockedSource_ReportsTheUserMessage()
    {
        var fetcher = new ScriptedTleFetcher
        {
            Respond = _ => new TleFetchResult(
                Array.Empty<TleSet>(),
                TleFetchStatus.Blocked,
                "CelesTrak ha risposto HTTP 503 per il NORAD 25544."),
        };
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));

        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);

        PassForecastSnapshot snapshot = forecaster.Snapshot(T0);
        Assert.Equal(PassForecastStatus.Blocked, snapshot.Status);
        Assert.Contains("HTTP 503", snapshot.Message);
        Assert.Empty(snapshot.Passes);
    }

    [Fact]
    public async Task ChangingTargets_RequestsTleAtOnce()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators(
            (25544, new ArcPropagator(TwoPasses)),
            (48274, new ArcPropagator())));

        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);
        await forecaster.RefreshAsync(Settings(true, 25544, 48274), T0 + TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(2, fetcher.Calls);
    }

    [Fact]
    public async Task NeverVisibleTarget_GivesEmptyForecastWithoutError()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator())));

        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);

        PassForecastSnapshot snapshot = forecaster.Snapshot(T0);
        Assert.Equal(PassForecastStatus.Ready, snapshot.Status);
        Assert.Empty(snapshot.Passes);
        Assert.Null(snapshot.Next);
    }

    [Fact]
    public async Task Evaluate_ReportsStartAndEndOfPass()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));
        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);

        IReadOnlyList<PassChange> initial = forecaster.Evaluate(T0);
        Assert.Contains(initial, c => c.Kind == PassChangeKind.ForecastUpdated);

        IReadOnlyList<PassChange> started = forecaster.Evaluate(T0 + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5));
        PassChange begin = Assert.Single(started, c => c.Kind == PassChangeKind.Started);
        Assert.Equal(25544, begin.Pass!.NoradId);

        IReadOnlyList<PassChange> ended = forecaster.Evaluate(T0 + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(15));
        Assert.Single(ended, c => c.Kind == PassChangeKind.Ended);
    }

    [Fact]
    public async Task Snapshot_ReportsCurrentPassWhileInProgress()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));
        await forecaster.RefreshAsync(Settings(true, 25544), T0, CancellationToken.None);

        PassForecastSnapshot snapshot = forecaster.Snapshot(T0 + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5));

        Assert.NotNull(snapshot.Current);
        Assert.Equal(snapshot.Passes[0], snapshot.Current);
        Assert.Equal(snapshot.Passes[1], snapshot.Next);
    }

    [Fact]
    public async Task Tracks_ProjectOnlyPassesInProgressOrImminent_WhenCalibrated()
    {
        var fetcher = new ScriptedTleFetcher();
        var forecaster = new PassForecaster(fetcher, Propagators((25544, new ArcPropagator(TwoPasses))));
        PassForecastSettings settings = Settings(true, 25544);
        await forecaster.RefreshAsync(settings, T0, CancellationToken.None);

        var model = new FisheyeModel(480, 360, 340, 0, false);
        var calibration = new SkyCalibrationResult(model, 0.5, 0.0, 0, 0, Accepted: true);

        IReadOnlyList<PassTrackPath> early = forecaster.Tracks(settings, calibration, T0, PassForecaster.TrackLead);
        Assert.Empty(early);

        IReadOnlyList<PassTrackPath> during = forecaster.Tracks(
            settings, calibration, T0 + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5), PassForecaster.TrackLead);
        PassTrackPath path = Assert.Single(during);
        Assert.Equal("SAT 25544", path.Target);
        Assert.NotEmpty(path.Runs);
        Assert.True(path.Runs[0].Count >= 2);
    }
}

public sealed class PassEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static EngineDependencies Dependencies(Harness h, FakeClock clock, ScriptedTleFetcher fetcher)
    {
        var delay = new ScriptDelay(clock, async (_, ct) => await Task.Delay(System.Threading.Timeout.Infinite, ct));
        return h.Dependencies(
            clock,
            new TestCameraProvider(_ => throw new InvalidOperationException("nessuna camera nei test")),
            delay) with
        {
            TleFetcher = fetcher,
            OrbitPropagators = new ScriptedPropagators(new Dictionary<int, IOrbitPropagator>
            {
                [25544] = new ArcPropagator((T0 + TimeSpan.FromHours(1), TimeSpan.FromMinutes(10), 45.0)),
            }),
        };
    }

    [Fact]
    public async Task ForecastIsOffByDefault_NoTleRequestIsMade()
    {
        using var h = new Harness();
        var clock = new FakeClock(T0);
        var fetcher = new ScriptedTleFetcher();

        await using var engine = new AllSkyEngine(Dependencies(h, clock, fetcher));
        await engine.StartAsync();
        await Task.Delay(300);

        Assert.Equal(0, fetcher.Calls);
        Assert.Equal(PassForecastStatus.Disabled, engine.GetForecast().Status);
        Assert.Equal(PassTrackBlock.ServiceOff, engine.GetTracks().Block);
    }

    [Fact]
    public async Task ForecastEnabledInConfig_ComputesPassesThroughEngine()
    {
        using var h = new Harness();
        h.SaveConfig(new AppConfig { Satellites = new AppConfig.SatellitesConfig { Enabled = true } });
        var clock = new FakeClock(T0);
        var fetcher = new ScriptedTleFetcher();

        await using var engine = new AllSkyEngine(Dependencies(h, clock, fetcher));
        await engine.StartAsync();

        PassForecastSnapshot snapshot = engine.GetForecast();
        for (int i = 0; i < 100 && snapshot.Status != PassForecastStatus.Ready; i++)
        {
            await Task.Delay(50);
            snapshot = engine.GetForecast();
        }

        Assert.Equal(PassForecastStatus.Ready, snapshot.Status);
        Assert.Single(snapshot.Passes);
        Assert.Equal(1, fetcher.Calls);
    }
}
