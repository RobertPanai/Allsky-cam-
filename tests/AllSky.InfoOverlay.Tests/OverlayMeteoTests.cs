using AllSky.Core;
using AllSky.InfoOverlay;
using AllSky.Weather;
using Xunit;

namespace AllSky.InfoOverlay.Tests;

public sealed class OverlayMeteoTests
{
    private static readonly WeatherSnapshot Completo = new(
        new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
        TemperatureC: 11.46,
        HumidityPercent: 80.4,
        PressureHpa: 1012.6,
        DewPointC: 4.0,
        CloudCoverPercent: 12.2,
        WindSpeedMs: 3.24,
        WindGustMs: 5.0,
        WindDirectionDeg: 270.2,
        RainRateMmH: 0.0,
        SkyTemperatureC: -20.0,
        SkyQualityMagArcsec2: 20.8,
        Source: "ASCOM:ObservingConditions.Test");

    private static readonly WeatherSnapshot Vuoto = new(
        new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
        TemperatureC: null,
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

    private static readonly OverlayMeteoField[] TuttiICampi =
    [
        OverlayMeteoField.Temperatura,
        OverlayMeteoField.Umidita,
        OverlayMeteoField.Pressione,
        OverlayMeteoField.Vento,
        OverlayMeteoField.Nuvole,
        OverlayMeteoField.Pioggia,
        OverlayMeteoField.LuminositaCielo,
    ];

    [Fact]
    public void Righe_Italiano_UsanoVirgolaEUnita()
    {
        IReadOnlyList<string> righe = OverlayMeteoText.Righe(Completo, TuttiICampi, Language.It);

        Assert.Equal(
            new[]
            {
                "Temperatura: 11,5 °C",
                "Umidità: 80 %",
                "Pressione: 1013 hPa",
                "Vento: 3,2 m/s, da 270°",
                "Nuvole: 12 %",
                "Pioggia: 0,0 mm/h",
                "Luminosità cielo: 20,80 mag/arcsec²",
            },
            righe);
    }

    [Fact]
    public void Righe_Inglese_UsanoPuntoEUnita()
    {
        IReadOnlyList<string> righe = OverlayMeteoText.Righe(Completo, TuttiICampi, Language.En);

        Assert.Equal(
            new[]
            {
                "Temperature: 11.5 °C",
                "Humidity: 80 %",
                "Pressure: 1013 hPa",
                "Wind: 3.2 m/s from 270°",
                "Cloud cover: 12 %",
                "Rain: 0.0 mm/h",
                "Sky brightness: 20.80 mag/arcsec²",
            },
            righe);
    }

    [Fact]
    public void CampiMancanti_DiventanoNdInItalianoEUnaInInglese()
    {
        IReadOnlyList<string> it = OverlayMeteoText.Righe(Vuoto, TuttiICampi, Language.It);
        IReadOnlyList<string> en = OverlayMeteoText.Righe(Vuoto, TuttiICampi, Language.En);

        Assert.Equal("Temperatura: n/d", it[0]);
        Assert.Equal("Vento: n/d", it[3]);
        Assert.Equal("Temperature: n/a", en[0]);
        Assert.Equal("Wind: n/a", en[3]);
        Assert.All(it, r => Assert.Contains("n/d", r));
        Assert.All(en, r => Assert.Contains("n/a", r));
    }

    [Fact]
    public void Vento_SoloDirezione_MostraNdPerLaVelocita()
    {
        var soloDirezione = Vuoto with { WindDirectionDeg = 90 };

        Assert.Equal(
            new[] { "Vento: n/d, da 90°" },
            OverlayMeteoText.Righe(soloDirezione, [OverlayMeteoField.Vento], Language.It));
    }

    [Fact]
    public void SnapshotAssente_DaRigheMancanti()
    {
        IReadOnlyList<string> righe = OverlayMeteoText.Righe(null, [OverlayMeteoField.Pioggia], Language.It);

        Assert.Equal(new[] { "Pioggia: n/d" }, righe);
    }

    [Fact]
    public void ValoreNonFinito_TrattatoComeMancante()
    {
        var nan = Vuoto with { TemperatureC = double.NaN };

        Assert.Equal(
            new[] { "Temperature: n/a" },
            OverlayMeteoText.Righe(nan, [OverlayMeteoField.Temperatura], Language.En));
    }

    [Fact]
    public void Righe_SeguonoL_OrdineScelto()
    {
        IReadOnlyList<string> righe = OverlayMeteoText.Righe(
            Completo,
            [OverlayMeteoField.Pioggia, OverlayMeteoField.Temperatura],
            Language.En);

        Assert.Equal(new[] { "Rain: 0.0 mm/h", "Temperature: 11.5 °C" }, righe);
    }

    [Fact]
    public void ElencoNullOVuoto_NonProduceRighe()
    {
        Assert.Empty(OverlayMeteoText.Righe(Completo, null, Language.It));
        Assert.Empty(OverlayMeteoText.Righe(Completo, [], Language.It));
    }

    [Fact]
    public void Valida_AccettaUnaSelezioneValida()
    {
        Assert.Empty(OverlayMeteoFields.Valida(TuttiICampi));
    }

    [Fact]
    public void Valida_SegnalaElencoAssenteVuotoRipetutoOValoreIgnoto()
    {
        Assert.Single(OverlayMeteoFields.Valida(null));
        Assert.Single(OverlayMeteoFields.Valida([]));
        Assert.Single(OverlayMeteoFields.Valida([OverlayMeteoField.Vento, OverlayMeteoField.Vento]));
        Assert.Single(OverlayMeteoFields.Valida([(OverlayMeteoField)99]));
    }

    [Fact]
    public void Normalizza_TogliRipetutiEValoriIgnotiMantenendoLOrdine()
    {
        IReadOnlyList<OverlayMeteoField> campi = OverlayMeteoFields.Normalizza(
            [OverlayMeteoField.Nuvole, (OverlayMeteoField)99, OverlayMeteoField.Vento, OverlayMeteoField.Nuvole]);

        Assert.Equal(new[] { OverlayMeteoField.Nuvole, OverlayMeteoField.Vento }, campi);
    }

    [Fact]
    public void Predefinito_DelWidgetMeteo_ProduceRigheIt()
    {
        OverlayWidgetFormat formato = OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Meteo);

        IReadOnlyList<string> righe = OverlayMeteoText.Righe(Completo, formato.Campi, Language.It);

        Assert.Equal(3, righe.Count);
        Assert.StartsWith("Temperatura: ", righe[0]);
    }
}
