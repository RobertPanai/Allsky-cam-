using AllSky.Weather;
using Xunit;

namespace AllSky.Weather.Tests;

/// <summary>
/// Test della sorgente ASCOM ObservingConditions con lettore finto. Nessun driver COM reale.
/// </summary>
public sealed class AscomObservingConditionsTests
{
    private const string ProgId = "ASCOM.Test.ObservingConditions";

    /// <summary>Lettore con tutte le proprietà valorizzate.</summary>
    private static FakeObservingConditionsReader Completo()
    {
        var lettore = new FakeObservingConditionsReader();
        lettore.Valori["Temperature"] = 12.3;
        lettore.Valori["DewPoint"] = 4.5;
        lettore.Valori["Humidity"] = 81;
        lettore.Valori["Pressure"] = 1013.25;
        lettore.Valori["RainRate"] = 0;
        lettore.Valori["SkyQuality"] = 19.8;
        lettore.Valori["SkyTemperature"] = -35.2;
        lettore.Valori["CloudCover"] = 45;
        lettore.Valori["WindDirection"] = 240;
        lettore.Valori["WindGust"] = 6.1;
        lettore.Valori["WindSpeed"] = 3.2;
        return lettore;
    }

    /// <summary>Confronto con tolleranza, con il valore nullable atteso non nullo.</summary>
    private static void Uguale(double attesa, double? effettivo)
    {
        Assert.NotNull(effettivo);
        Assert.Equal(attesa, effettivo!.Value, 6);
    }

    [Fact]
    public async Task ProprietaNonImplementate_DiventanoNull_SenzaEccezioni()
    {
        FakeObservingConditionsReader lettore = Completo();
        lettore.NonImplementate.Add("SkyTemperature");
        lettore.NonImplementate.Add("WindGust");
        lettore.NonImplementate.Add("CloudCover");
        lettore.NonImplementate.Add("DewPoint");
        var fabbrica = new FakeObservingConditionsFactory { Lettore = lettore };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Null(snap!.SkyTemperatureC);
        Assert.Null(snap.WindGustMs);
        Assert.Null(snap.CloudCoverPercent);
        Assert.Null(snap.DewPointC);
        Uguale(12.3, snap.TemperatureC);
        Uguale(3.2, snap.WindSpeedMs);
        Assert.Null(fonte.LastMessage);
    }

    [Fact]
    public async Task TuttoValido_MappaOgniCampoSenzaConversioni()
    {
        var clock = new FakeWeatherClock();
        var fabbrica = new FakeObservingConditionsFactory { Lettore = Completo() };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, clock);

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(clock.UtcNow, snap!.TimestampUtc);
        Assert.Equal("ASCOM:" + ProgId, snap.Source);
        Assert.Equal("ASCOM:" + ProgId, fonte.Name);
        Uguale(12.3, snap.TemperatureC);
        Uguale(81, snap.HumidityPercent);
        Uguale(1013.25, snap.PressureHpa);
        Uguale(4.5, snap.DewPointC);
        Uguale(45, snap.CloudCoverPercent);
        Uguale(3.2, snap.WindSpeedMs);
        Uguale(6.1, snap.WindGustMs);
        Uguale(240, snap.WindDirectionDeg);
        Uguale(0, snap.RainRateMmH);
        Uguale(-35.2, snap.SkyTemperatureC);
        Uguale(19.8, snap.SkyQualityMagArcsec2);
    }

    [Fact]
    public async Task ValoriFuoriIntervalloONonFiniti_DiventanoNull_MaloZeroReale_Resta()
    {
        FakeObservingConditionsReader lettore = Completo();
        lettore.Valori["Temperature"] = 0.0;      // zero reale: resta 0
        lettore.Valori["Humidity"] = 120;         // fuori 0..100
        lettore.Valori["CloudCover"] = -1;        // fuori 0..100
        lettore.Valori["WindDirection"] = 360;    // limite incluso
        lettore.Valori["Pressure"] = double.NaN;  // non finito
        lettore.Valori["WindSpeed"] = -2;         // velocità negativa
        var fabbrica = new FakeObservingConditionsFactory { Lettore = lettore };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        WeatherSnapshot? snap = await fonte.ReadAsync(CancellationToken.None);

        Assert.NotNull(snap);
        Uguale(0.0, snap!.TemperatureC);
        Assert.Null(snap.HumidityPercent);
        Assert.Null(snap.CloudCoverPercent);
        Uguale(360, snap.WindDirectionDeg);
        Assert.Null(snap.PressureHpa);
        Assert.Null(snap.WindSpeedMs);
    }

    [Fact]
    public async Task NessunDriver_RestituisceNullSenzaEccezioni_EUnSoloTentativoPerChiamata()
    {
        var fabbrica = new FakeObservingConditionsFactory { Errore = new InvalidOperationException("driver non registrato") };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        WeatherSnapshot? primo = await fonte.ReadAsync(CancellationToken.None);
        Assert.Null(primo);
        Assert.Equal(1, fabbrica.Chiamate);
        Assert.NotNull(fonte.LastMessage);

        // Nessun ciclo di retry interno: ogni ReadAsync fa al più un tentativo.
        WeatherSnapshot? secondo = await fonte.ReadAsync(CancellationToken.None);
        Assert.Null(secondo);
        Assert.Equal(2, fabbrica.Chiamate);
    }

    [Fact]
    public async Task TuttiNonImplementati_RestituisceNull_ConMessaggio()
    {
        FakeObservingConditionsReader lettore = Completo();
        foreach (string nome in new[]
                 {
                     "CloudCover", "DewPoint", "Humidity", "Pressure", "RainRate", "SkyQuality",
                     "SkyTemperature", "Temperature", "WindDirection", "WindGust", "WindSpeed",
                 })
        {
            lettore.NonImplementate.Add(nome);
        }

        var fabbrica = new FakeObservingConditionsFactory { Lettore = lettore };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        Assert.Null(await fonte.ReadAsync(CancellationToken.None));
        Assert.NotNull(fonte.LastMessage);
    }

    [Fact]
    public async Task LettoreCreatoUnaSolaVolta_PoiRiusato()
    {
        var fabbrica = new FakeObservingConditionsFactory { Lettore = Completo() };
        await using var fonte = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        await fonte.ReadAsync(CancellationToken.None);
        await fonte.ReadAsync(CancellationToken.None);

        Assert.Equal(1, fabbrica.Chiamate);
    }

    [Fact]
    public void Costruzione_NonToccaIlDriver()
    {
        var fabbrica = new FakeObservingConditionsFactory { Lettore = Completo() };

        _ = new AscomObservingConditionsSource(ProgId, fabbrica, new FakeWeatherClock());

        Assert.Equal(0, fabbrica.Chiamate);
    }
}
