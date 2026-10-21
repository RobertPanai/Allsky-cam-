using System.Net;
using AllSky.Satellites;
using Xunit;

namespace AllSky.Satellites.Tests;

/// <summary>
/// Test della sorgente CelesTrak: solo handler finti e orologio finto, mai la rete vera.
/// </summary>
public sealed class CelesTrakTleSourceTests : IDisposable
{
    private static readonly SatelliteTarget Iss = new(25544, "ISS (ZARYA)");

    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "allsky-sat-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCelesTrakHandler _handler = new();
    private readonly FakeCelesTrakClock _clock = new();

    public void Dispose()
    {
        if (Directory.Exists(_cartella))
        {
            Directory.Delete(_cartella, recursive: true);
        }
    }

    /// <summary>Nuova istanza sulla stessa cartella: simula un riavvio.</summary>
    private CelesTrakTleSource Crea(TimeSpan? minInterval = null) =>
        new(new HttpClient(_handler), _clock, _cartella, minInterval);

    [Fact]
    public async Task Risposta200Valida_E_Fresh_E_SalvaCache()
    {
        CelesTrakTleSource source = Crea();

        TleFetchResult result = await source.FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Fresh, result.Status);
        Assert.Null(result.Message);
        TleSet set = Assert.Single(result.Sets);
        Assert.Equal(25544, set.NoradId);
        Assert.Equal("ISS (ZARYA)", set.Name);
        Assert.Equal(_clock.UtcNow, set.FetchedUtc);
        Assert.Equal(1, _handler.Richieste.Count);
        Assert.True(File.Exists(Path.Combine(_cartella, "25544.tle")));
        Assert.True(File.Exists(Path.Combine(_cartella, "25544.json")));
    }

    [Fact]
    public async Task SecondaChiamataEntroDueOre_NonFaRete()
    {
        await Crea().FetchAsync([Iss], CancellationToken.None);
        _clock.Avanza(TimeSpan.FromHours(1));

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.FromCache, result.Status);
        Assert.Single(result.Sets);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task DopoDueOre_NuovaRichiesta()
    {
        await Crea().FetchAsync([Iss], CancellationToken.None);
        _clock.Avanza(TimeSpan.FromHours(2));

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Fresh, result.Status);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Risposta404_Blocca24Ore_ConIstanzaNuova_SenzaRete()
    {
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.NotFound);

        TleFetchResult primo = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Blocked, primo.Status);
        Assert.Empty(primo.Sets);
        Assert.NotNull(primo.Message);
        Assert.Contains("404", primo.Message!);
        Assert.Equal(1, _handler.Richieste.Count);

        // Istanza nuova, stesso orologio: il blocco è su disco, nessuna richiesta.
        _handler.Risposta = _ => FakeCelesTrakHandler.Ok(TleFixtures.Tre(25544));
        TleFetchResult secondo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Blocked, secondo.Status);
        Assert.Equal(1, _handler.Richieste.Count);

        // Ancora dentro le 24 ore: nessuna richiesta.
        _clock.Avanza(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        TleFetchResult terzo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Blocked, terzo.Status);
        Assert.Equal(1, _handler.Richieste.Count);

        // Passate le 24 ore: si riprova.
        _clock.Avanza(TimeSpan.FromMinutes(1));
        TleFetchResult quarto = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Fresh, quarto.Status);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Risposta503_Blocca24Ore()
    {
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.ServiceUnavailable);

        TleFetchResult primo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Blocked, primo.Status);
        Assert.Equal(1, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromHours(12));
        TleFetchResult secondo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Blocked, secondo.Status);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task EccezioneDiRete_Blocca24Ore()
    {
        _handler.Errore = new HttpRequestException("rete non raggiungibile");

        TleFetchResult primo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Blocked, primo.Status);
        Assert.NotNull(primo.Message);
        Assert.Equal(1, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromHours(23));
        await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Errore_FermaIlGiroDeiBersagli_SenzaAltreRichieste()
    {
        var altro = new SatelliteTarget(48274, "CSS (Tianhe)");
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.ServiceUnavailable);

        TleFetchResult result = await Crea().FetchAsync([Iss, altro], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Blocked, result.Status);
        Assert.Empty(result.Sets);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Risposta404_UsaLaCacheVecchia()
    {
        await Crea().FetchAsync([Iss], CancellationToken.None);
        _clock.Avanza(TimeSpan.FromHours(3));
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.NotFound);

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Blocked, result.Status);
        Assert.Single(result.Sets);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Redirect301_Blocca()
    {
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.MovedPermanently);

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Blocked, result.Status);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task RedirectSeguitoAutomaticamente_Blocca()
    {
        // L'handler "segue" un reindirizzamento: la risposta finale ha un URL diverso da quello richiesto.
        _handler.Risposta = _ =>
        {
            HttpResponseMessage response = FakeCelesTrakHandler.Ok(TleFixtures.Tre(25544));
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://celestrak.org/NORAD/elements/altro.php");
            return response;
        };

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Blocked, result.Status);
        Assert.Empty(result.Sets);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task ChecksumErrato_NonSalvato_EFormatoNonBlocca()
    {
        string riga1 = TleFixtures.Linea1(25544);
        char ultimo = riga1[68];
        char errato = (char)('0' + ((ultimo - '0' + 1) % 10));
        string corpoErrato = riga1[..68] + errato;
        _handler.Risposta = _ => FakeCelesTrakHandler.Ok($"ISS (ZARYA)\n{corpoErrato}\n{TleFixtures.Linea2(25544)}\n");

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Failed, result.Status);
        Assert.Empty(result.Sets);
        Assert.Contains("checksum", result.Message!);
        Assert.False(File.Exists(Path.Combine(_cartella, "25544.tle")));

        // Un errore di formato non è un blocco: la richiesta successiva parte subito.
        _handler.Risposta = _ => FakeCelesTrakHandler.Ok(TleFixtures.Tre(25544));
        TleFetchResult dopo = await Crea().FetchAsync([Iss], CancellationToken.None);
        Assert.Equal(TleFetchStatus.Fresh, dopo.Status);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task NoradDiverso_Rifiutato_NonSalvato()
    {
        _handler.Risposta = _ => FakeCelesTrakHandler.Ok(TleFixtures.Tre(12345));

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Failed, result.Status);
        Assert.Empty(result.Sets);
        Assert.Contains("12345", result.Message!);
        Assert.False(File.Exists(Path.Combine(_cartella, "25544.tle")));
    }

    [Fact]
    public async Task UserAgent_Presente()
    {
        await Crea().FetchAsync([Iss], CancellationToken.None);

        string agente = Assert.Single(_handler.UserAgent);
        Assert.StartsWith("AllSkyBrain/", agente);
    }

    [Fact]
    public async Task SoloURLGpPhpConCatnr()
    {
        await Crea().FetchAsync([Iss], CancellationToken.None);
        _handler.Risposta = _ => FakeCelesTrakHandler.Stato(HttpStatusCode.NotFound);
        _clock.Avanza(TimeSpan.FromHours(3));
        await Crea().FetchAsync([Iss, new SatelliteTarget(48274, "CSS (Tianhe)")], CancellationToken.None);

        Assert.NotEmpty(_handler.Richieste);
        foreach (Uri url in _handler.Richieste)
        {
            Assert.StartsWith("https://celestrak.org/NORAD/elements/gp.php?CATNR=", url.AbsoluteUri);
        }

        Assert.Equal("?CATNR=25544&FORMAT=TLE", _handler.Richieste[0].Query);
    }

    [Fact]
    public async Task CacheCorrotta_Viene_Ignorata()
    {
        Directory.CreateDirectory(_cartella);
        File.WriteAllText(Path.Combine(_cartella, "25544.tle"), "spazzatura\n");
        File.WriteAllText(Path.Combine(_cartella, "25544.json"), "{ non json");

        TleFetchResult result = await Crea().FetchAsync([Iss], CancellationToken.None);

        Assert.Equal(TleFetchStatus.Fresh, result.Status);
        Assert.Equal(1, _handler.Richieste.Count);
    }

    [Fact]
    public async Task GetAsync_RestituisceSoloIDati()
    {
        IReadOnlyList<TleSet> sets = await Crea().GetAsync([Iss], CancellationToken.None);

        Assert.Single(sets);
        Assert.Equal(25544, sets[0].NoradId);
    }

    [Fact]
    public void DefaultTargets_ContieneSoloLISS_VerificataSuCelesTrak()
    {
        IReadOnlyList<SatelliteTarget> targets = SatelliteCatalog.DefaultTargets;

        SatelliteTarget unico = Assert.Single(targets);
        Assert.Equal(25544, unico.NoradId);
    }
}
