using AllSky.Weather;
using Xunit;

namespace AllSky.Weather.Tests;

/// <summary>Sorgente finta: ogni lettura consuma un esito preparato. Nessuna rete, nessun tempo reale.</summary>
internal sealed class ScriptedWeatherSource : IWeatherSource
{
    private readonly Queue<Func<CancellationToken, Task<WeatherSnapshot?>>> _esiti = new();

    public int Letture { get; private set; }

    public string Name => "finta";

    public ScriptedWeatherSource Riesce(WeatherSnapshot? snapshot) =>
        Accoda(_ => Task.FromResult(snapshot));

    public ScriptedWeatherSource Fallisce(Exception ex) =>
        Accoda(_ => Task.FromException<WeatherSnapshot?>(ex));

    public ScriptedWeatherSource Blocca() =>
        Accoda(async ct => { await Task.Delay(Timeout.Infinite, ct); return null; });

    private ScriptedWeatherSource Accoda(Func<CancellationToken, Task<WeatherSnapshot?>> esito)
    {
        _esiti.Enqueue(esito);
        return this;
    }

    public Task<WeatherSnapshot?> ReadAsync(CancellationToken ct)
    {
        Letture++;
        if (_esiti.Count == 0)
        {
            throw new InvalidOperationException("nessun esito preparato");
        }

        return _esiti.Dequeue()(ct);
    }
}

public sealed class WeatherSnapshotProviderTests
{
    private static readonly DateTimeOffset Inizio = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private static WeatherSnapshot Snap(DateTimeOffset quando, double? temperatura = 10.0) => new(
        quando,
        TemperatureC: temperatura,
        HumidityPercent: null,
        PressureHpa: null,
        DewPointC: null,
        CloudCoverPercent: null,
        WindSpeedMs: null,
        WindGustMs: null,
        WindDirectionDeg: null,
        RainRateMmH: null,
        SkyTemperatureC: null,
        SkyQualityMagArcsec2: null,
        Source: "finta");

    private static CachedWeatherSnapshotProvider Provider(
        ScriptedWeatherSource sorgente,
        FakeWeatherClock clock,
        TimeSpan? maxAge = null,
        TimeSpan? timeout = null,
        List<string>? log = null) =>
        new(sorgente, clock, maxAge, timeout, log is null ? null : (m, _) => log.Add(m));

    [Fact]
    public async Task LetturaRiuscita_RestituisceLoSnapshot()
    {
        var clock = new FakeWeatherClock();
        var snap = Snap(Inizio, 12.5);
        var sorgente = new ScriptedWeatherSource().Riesce(snap);

        WeatherSnapshot? risultato = await Provider(sorgente, clock).GetAsync(CancellationToken.None);

        Assert.Equal(snap, risultato);
        Assert.Equal(1, sorgente.Letture);
    }

    [Fact]
    public async Task Fallimento_UsaUltimoValidoSeRecente()
    {
        var clock = new FakeWeatherClock();
        var buono = Snap(Inizio, 12.5);
        var sorgente = new ScriptedWeatherSource()
            .Riesce(buono)
            .Fallisce(new InvalidOperationException("driver assente"));
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock, maxAge: TimeSpan.FromMinutes(30));

        await provider.GetAsync(CancellationToken.None);
        clock.Avanza(TimeSpan.FromMinutes(10));
        WeatherSnapshot? risultato = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(buono, risultato);
    }

    [Fact]
    public async Task Cache_Scaduta_RestituisceNull()
    {
        var clock = new FakeWeatherClock();
        var sorgente = new ScriptedWeatherSource()
            .Riesce(Snap(Inizio))
            .Riesce(null);
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock, maxAge: TimeSpan.FromMinutes(30));

        await provider.GetAsync(CancellationToken.None);
        clock.Avanza(TimeSpan.FromMinutes(31));
        WeatherSnapshot? risultato = await provider.GetAsync(CancellationToken.None);

        Assert.Null(risultato);
    }

    [Fact]
    public async Task Cache_NonAncoraScaduta_AlLimiteEsatto_RestaValida()
    {
        var clock = new FakeWeatherClock();
        var buono = Snap(Inizio);
        var sorgente = new ScriptedWeatherSource().Riesce(buono).Riesce(null);
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock, maxAge: TimeSpan.FromMinutes(30));

        await provider.GetAsync(CancellationToken.None);
        clock.Avanza(TimeSpan.FromMinutes(30));

        Assert.Equal(buono, await provider.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NessunDato_NonSostituisceUltimoValido()
    {
        var clock = new FakeWeatherClock();
        var buono = Snap(Inizio, 9.0);
        var sorgente = new ScriptedWeatherSource()
            .Riesce(buono)
            .Riesce(Snap(Inizio.AddMinutes(1), temperatura: null));
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock);

        await provider.GetAsync(CancellationToken.None);
        clock.Avanza(TimeSpan.FromMinutes(1));

        Assert.Equal(buono, await provider.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SorgenteConEccezione_NonLancia_E_SenzaCacheRestituisceNull()
    {
        var clock = new FakeWeatherClock();
        var log = new List<string>();
        var sorgente = new ScriptedWeatherSource().Fallisce(new IOException("porta COM occupata"));

        WeatherSnapshot? risultato = await Provider(sorgente, clock, log: log).GetAsync(CancellationToken.None);

        Assert.Null(risultato);
        Assert.Single(log);
    }

    [Fact]
    public async Task Timeout_UsaUltimoValidoSenzaAttendere()
    {
        var clock = new FakeWeatherClock();
        var buono = Snap(Inizio, 7.0);
        var sorgente = new ScriptedWeatherSource().Riesce(buono).Blocca();
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock, timeout: TimeSpan.FromMilliseconds(50));

        await provider.GetAsync(CancellationToken.None);
        WeatherSnapshot? risultato = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(buono, risultato);
    }

    [Fact]
    public async Task Log_ScriveSoloAllaTransizione()
    {
        var clock = new FakeWeatherClock();
        var log = new List<string>();
        var sorgente = new ScriptedWeatherSource()
            .Fallisce(new IOException("a"))
            .Fallisce(new IOException("b"))
            .Riesce(Snap(Inizio))
            .Riesce(Snap(Inizio.AddSeconds(5)))
            .Fallisce(new IOException("c"));
        CachedWeatherSnapshotProvider provider = Provider(sorgente, clock, log: log);

        for (int i = 0; i < 5; i++)
        {
            await provider.GetAsync(CancellationToken.None);
        }

        // Errore (1 messaggio), ripresa (1), nuovo errore (1): tre messaggi in tutto, non uno per lettura fallita.
        Assert.Equal(3, log.Count);
        Assert.Contains(log, m => m.Contains("di nuovo disponibile"));
    }

    [Fact]
    public async Task Annullamento_SiPropaga()
    {
        var clock = new FakeWeatherClock();
        var sorgente = new ScriptedWeatherSource().Blocca();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Provider(sorgente, clock).GetAsync(cts.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ScadenzaNonPositiva_Rifiutata(int minuti)
    {
        var clock = new FakeWeatherClock();
        var sorgente = new ScriptedWeatherSource();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CachedWeatherSnapshotProvider(sorgente, clock, maxAge: TimeSpan.FromMinutes(minuti)));
    }
}
