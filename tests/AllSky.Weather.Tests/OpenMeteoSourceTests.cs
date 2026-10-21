using System.Net;
using AllSky.Weather;
using Xunit;

namespace AllSky.Weather.Tests;

/// <summary>
/// Test della sorgente Open-Meteo con handler HTTP finto e orologio finto. Mai la rete vera.
/// </summary>
public sealed class OpenMeteoSourceTests
{
    private readonly FakeOpenMeteoHandler _handler = new();
    private readonly FakeWeatherClock _clock = new();

    private OpenMeteoSource Crea(TimeSpan? minInterval = null) =>
        new(new HttpClient(_handler), 52.52, 13.41, _clock, minInterval);

    private static void Uguale(double attesa, double? effettivo)
    {
        Assert.NotNull(effettivo);
        Assert.Equal(attesa, effettivo!.Value, 6);
    }

    [Fact]
    public async Task RichiestaConParametriDocumentati_EMappaturaDeiCampi()
    {
        OpenMeteoSource fonte = Crea();

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(1, _handler.Richieste.Count);
        string url = _handler.Richieste[0].ToString();
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", url);
        Assert.Contains("latitude=52.52&longitude=13.41", url);
        Assert.Contains("current=temperature_2m,relative_humidity_2m,cloud_cover,wind_speed_10m,wind_gusts_10m,wind_direction_10m,surface_pressure", url);
        Assert.Contains("wind_speed_unit=ms", url);
        Assert.StartsWith("AllSkyBrain/", _handler.UserAgent[0]);

        Assert.Equal("Open-Meteo", snap!.Source);
        Assert.Equal(_clock.UtcNow, snap.TimestampUtc);
        Uguale(12.3, snap.TemperatureC);
        Uguale(81, snap.HumidityPercent);
        Uguale(45, snap.CloudCoverPercent);
        Uguale(3.2, snap.WindSpeedMs);
        Uguale(6.1, snap.WindGustMs);
        Uguale(240, snap.WindDirectionDeg);
        Uguale(1012.4, snap.PressureHpa);
        // Campi non richiesti a Open-Meteo: sempre null, mai 0.
        Assert.Null(snap.DewPointC);
        Assert.Null(snap.RainRateMmH);
        Assert.Null(snap.SkyTemperatureC);
        Assert.Null(snap.SkyQualityMagArcsec2);
    }

    [Fact]
    public void Costruzione_NonEsegueRichieste()
    {
        _ = Crea();

        Assert.Empty(_handler.Richieste);
    }

    [Fact]
    public void IntervalloSottoQuindiciMinuti_NonAmmesso()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Crea(TimeSpan.FromMinutes(5)); });
    }

    [Fact]
    public async Task CacheDiQuindiciMinuti_NonRichiedeRete()
    {
        OpenMeteoSource fonte = Crea();
        await fonte.ReadAsync(CancellationToken.None);

        _clock.Avanza(TimeSpan.FromMinutes(14));
        WeatherSnapshot? aQuattordici = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(aQuattordici);
        Assert.Equal(1, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromMinutes(1));
        await fonte.ReadAsync(CancellationToken.None);

        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ErroreHttp_NessunRetryImmediato_EBackoffSenzaRichieste(int codice)
    {
        _handler.Risposta = _ => FakeOpenMeteoHandler.Stato((HttpStatusCode)codice);
        OpenMeteoSource fonte = Crea();

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
        Assert.Equal(1, _handler.Richieste.Count);
        Assert.NotNull(fonte.LastMessage);

        _clock.Avanza(TimeSpan.FromMinutes(14));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(1, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromMinutes(1));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task BackoffCresce_15_30_60_120_240_360_E_PoiRestaASeiOre()
    {
        _handler.Risposta = _ => FakeOpenMeteoHandler.Stato(HttpStatusCode.ServiceUnavailable);
        OpenMeteoSource fonte = Crea();
        await fonte.ReadAsync(CancellationToken.None);
        int richieste = 1;

        // Ogni passo: un minuto prima della scadenza non si richiede; alla scadenza sì, e l'errore allunga l'attesa.
        int[] attese = { 15, 30, 60, 120, 240, 360, 360 };
        foreach (int minuti in attese)
        {
            _clock.Avanza(TimeSpan.FromMinutes(minuti - 1));
            await fonte.ReadAsync(CancellationToken.None);
            Assert.Equal(richieste, _handler.Richieste.Count);

            _clock.Avanza(TimeSpan.FromMinutes(1));
            await fonte.ReadAsync(CancellationToken.None);
            richieste++;
            Assert.Equal(richieste, _handler.Richieste.Count);
        }
    }

    [Fact]
    public async Task ErroreDiRete_RestituisceNullSenzaEccezione()
    {
        _handler.Errore = new HttpRequestException("rete non raggiungibile");
        OpenMeteoSource fonte = Crea();

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
        Assert.Equal(1, _handler.Richieste.Count);
        Assert.Contains("Impossibile contattare", fonte.LastMessage!);

        _clock.Avanza(TimeSpan.FromMinutes(15));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Fact]
    public async Task Timeout_RestituisceNullSenzaEccezione()
    {
        _handler.Errore = new TaskCanceledException("timeout del client");
        OpenMeteoSource fonte = Crea();

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
        Assert.Contains("Timeout", fonte.LastMessage!);
    }

    [Fact]
    public async Task JsonMalformato_RestituisceNull_SenzaBackoffCrescente()
    {
        _handler.Risposta = _ => FakeOpenMeteoHandler.Ok("{questo non è json");
        OpenMeteoSource fonte = Crea();

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
        Assert.NotNull(fonte.LastMessage);

        _clock.Avanza(TimeSpan.FromMinutes(14));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(1, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromMinutes(1));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Richieste.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"current\":5}")]
    [InlineData("{\"current\":{\"temperature_2m\":\"caldo\"}}")]
    public async Task JsonSenzaDatiUtilizzabili_RestituisceNull(string corpo)
    {
        _handler.Risposta = _ => FakeOpenMeteoHandler.Ok(corpo);
        OpenMeteoSource fonte = Crea();

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnitaInattesa_ScartaSoloIlValoreInteressato()
    {
        // Raffica dichiarata in km/h: il valore non si usa, il resto sì.
        _handler.Risposta = _ => FakeOpenMeteoHandler.Ok(OpenMeteoJson.Esempio(unitaRaffica: "km/h"));
        OpenMeteoSource fonte = Crea();

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Null(snap!.WindGustMs);
        Uguale(3.2, snap.WindSpeedMs);
        Uguale(12.3, snap.TemperatureC);
    }

    [Fact]
    public async Task ValoreFuoriIntervallo_EScartato()
    {
        string corpo = OpenMeteoJson.Esempio().Replace("\"relative_humidity_2m\":81", "\"relative_humidity_2m\":120", StringComparison.Ordinal);
        _handler.Risposta = _ => FakeOpenMeteoHandler.Ok(corpo);
        OpenMeteoSource fonte = Crea();

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Null(snap!.HumidityPercent);
        Uguale(12.3, snap.TemperatureC);
    }

    [Fact]
    public async Task DopoErrore_RispostaValida_AzzeraIlBackoff()
    {
        _handler.Risposta = _ => FakeOpenMeteoHandler.Stato(HttpStatusCode.ServiceUnavailable);
        OpenMeteoSource fonte = Crea();
        await fonte.ReadAsync(CancellationToken.None);

        _handler.Risposta = _ => FakeOpenMeteoHandler.Ok(OpenMeteoJson.Esempio());
        _clock.Avanza(TimeSpan.FromMinutes(15));
        Assert.NotNull(await fonte.ReadAsync(CancellationToken.None));
        Assert.Null(fonte.LastMessage);

        // Nuovo errore: il backoff riparte da 15 minuti, non da 30.
        _handler.Risposta = _ => FakeOpenMeteoHandler.Stato(HttpStatusCode.ServiceUnavailable);
        _clock.Avanza(TimeSpan.FromMinutes(15));
        await fonte.ReadAsync(CancellationToken.None);
        int richieste = _handler.Richieste.Count;

        _clock.Avanza(TimeSpan.FromMinutes(14));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(richieste, _handler.Richieste.Count);

        _clock.Avanza(TimeSpan.FromMinutes(1));
        await fonte.ReadAsync(CancellationToken.None);
        Assert.Equal(richieste + 1, _handler.Richieste.Count);
    }

    [Fact]
    public void Costanti_AttribuzioneEAvvisoNonCommerciale()
    {
        Assert.Equal("Dati meteo: Open-Meteo.com (CC BY 4.0)", OpenMeteoSource.Attribution);
        Assert.Contains("non commerciale", OpenMeteoSource.NonCommercialNotice);
        Assert.Contains("non-commercial", OpenMeteoSource.NonCommercialNotice);
    }
}
