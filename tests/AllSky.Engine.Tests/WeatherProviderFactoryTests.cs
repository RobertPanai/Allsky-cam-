using System.Net;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Weather;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>Lettura meteo finta del driver ASCOM: valori fissi, o un'eccezione per ogni proprieta'.</summary>
internal sealed class FactoryTestReader : IObservingConditionsReader
{
    private readonly bool _guasto;

    public FactoryTestReader(bool guasto = false)
    {
        _guasto = guasto;
    }

    private double Valore(double v) => _guasto ? throw new InvalidOperationException("driver in errore") : v;

    public double CloudCover() => Valore(20);

    public double DewPoint() => Valore(3);

    public double Humidity() => Valore(55);

    public double Pressure() => Valore(1013);

    public double RainRate() => Valore(0);

    public double SkyQuality() => Valore(20.5);

    public double SkyTemperature() => Valore(-40);

    public double Temperature() => Valore(9.5);

    public double WindDirection() => Valore(270);

    public double WindGust() => Valore(4);

    public double WindSpeed() => Valore(2);
}

/// <summary>Fabbrica finta dei driver ASCOM: restituisce il lettore configurato, o lancia se il driver non esiste.</summary>
internal sealed class FactoryTestObservingFactory : IObservingConditionsFactory
{
    private readonly Func<IObservingConditionsReader> _crea;

    public FactoryTestObservingFactory(Func<IObservingConditionsReader> crea)
    {
        _crea = crea;
    }

    public int Creations { get; private set; }

    public IObservingConditionsReader Create(string progId)
    {
        Creations++;
        return _crea();
    }
}

/// <summary>Orologio fisso per i test del meteo.</summary>
internal sealed class FactoryTestClock : IWeatherClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);
}

/// <summary>Risposta HTTP finta: restituisce sempre lo stesso corpo, senza rete.</summary>
internal sealed class FactoryTestHandler : HttpMessageHandler
{
    private readonly string _corpo;

    public FactoryTestHandler(string corpo)
    {
        _corpo = corpo;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_corpo, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

public sealed class WeatherProviderFactoryTests
{
    private const string OpenMeteoCorpo =
        "{\"current\":{\"temperature_2m\":12.5,\"relative_humidity_2m\":60,\"cloud_cover\":20,\"wind_speed_10m\":3.1," +
        "\"wind_gusts_10m\":5,\"wind_direction_10m\":270,\"surface_pressure\":1012}," +
        "\"current_units\":{\"temperature_2m\":\"°C\",\"relative_humidity_2m\":\"%\",\"cloud_cover\":\"%\"," +
        "\"wind_speed_10m\":\"m/s\",\"wind_gusts_10m\":\"m/s\",\"wind_direction_10m\":\"°\",\"surface_pressure\":\"hPa\"}}";

    private static AppConfig Con(AppConfig.WeatherConfig weather) => new() { Weather = weather };

    [Fact]
    public void SourceNone_BuildsNothingAndNoMessage()
    {
        WeatherBuild build = WeatherProviderFactory.Create(Con(new AppConfig.WeatherConfig()), new FactoryTestClock(), null, null);

        Assert.Null(build.Provider);
        Assert.Null(build.Source);
        Assert.Null(build.Attribution);
        Assert.Null(build.Message);
    }

    [Fact]
    public void OpenMeteoWithoutAcknowledgement_BuildsNothingWithReason()
    {
        var weather = new AppConfig.WeatherConfig { Source = AppConfig.WeatherConfig.Sources.OpenMeteo };
        using var http = new HttpClient(new FactoryTestHandler(OpenMeteoCorpo));

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), http, null);

        Assert.Null(build.Provider);
        Assert.Null(build.Source);
        Assert.NotNull(build.Message);
        Assert.Contains("openMeteoAcknowledgedNonCommercial", build.Message);
    }

    [Fact]
    public async Task OpenMeteoWithAcknowledgement_ReadsCurrentConditionsAndCarriesAttribution()
    {
        var weather = new AppConfig.WeatherConfig
        {
            Source = AppConfig.WeatherConfig.Sources.OpenMeteo,
            OpenMeteoAcknowledgedNonCommercial = true,
        };
        using var http = new HttpClient(new FactoryTestHandler(OpenMeteoCorpo));

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), http, null);

        Assert.NotNull(build.Provider);
        Assert.NotNull(build.Source);
        Assert.Null(build.Message);
        Assert.Equal(OpenMeteoSource.Attribution, build.Attribution);

        WeatherSnapshot? snapshot = await build.Provider!.GetAsync(CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("Open-Meteo", snapshot!.Source);
        Assert.Equal(12.5, snapshot.TemperatureC);
        Assert.Equal(1012, snapshot.PressureHpa);
        Assert.Null(snapshot.DewPointC);
    }

    [Fact]
    public void OpenMeteoWithoutHttpClient_BuildsNothingWithReason()
    {
        var weather = new AppConfig.WeatherConfig
        {
            Source = AppConfig.WeatherConfig.Sources.OpenMeteo,
            OpenMeteoAcknowledgedNonCommercial = true,
        };

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, null);

        Assert.Null(build.Provider);
        Assert.NotNull(build.Message);
    }

    [Fact]
    public void OpenMeteoWithInvalidSite_DoesNotThrow()
    {
        var weather = new AppConfig.WeatherConfig
        {
            Source = AppConfig.WeatherConfig.Sources.OpenMeteo,
            OpenMeteoAcknowledgedNonCommercial = true,
        };
        var config = new AppConfig { Weather = weather, Site = new AppConfig.SiteConfig { Latitude = 95 } };
        using var http = new HttpClient(new FactoryTestHandler(OpenMeteoCorpo));

        WeatherBuild build = WeatherProviderFactory.Create(config, new FactoryTestClock(), http, null);

        Assert.Null(build.Provider);
        Assert.NotNull(build.Message);
    }

    [Fact]
    public async Task Ascom_WithFactory_ReadsTheDriverThroughTheProvider()
    {
        var weather = new AppConfig.WeatherConfig
        {
            Source = AppConfig.WeatherConfig.Sources.Ascom,
            AscomProgId = "ASCOM.Test.ObservingConditions",
        };
        var fabbrica = new FactoryTestObservingFactory(() => new FactoryTestReader());

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, fabbrica);
        try
        {
            Assert.NotNull(build.Provider);
            Assert.Null(build.Attribution);

            WeatherSnapshot? snapshot = await build.Provider!.GetAsync(CancellationToken.None);

            Assert.NotNull(snapshot);
            Assert.Equal("ASCOM:ASCOM.Test.ObservingConditions", snapshot!.Source);
            Assert.Equal(9.5, snapshot.TemperatureC);
            Assert.Equal(1, fabbrica.Creations);
        }
        finally
        {
            await ((IAsyncDisposable)build.Source!).DisposeAsync();
        }
    }

    [Fact]
    public async Task Ascom_DriverError_ProviderReturnsNullInsteadOfThrowing()
    {
        var weather = new AppConfig.WeatherConfig
        {
            Source = AppConfig.WeatherConfig.Sources.Ascom,
            AscomProgId = "ASCOM.Test.ObservingConditions",
        };
        var fabbrica = new FactoryTestObservingFactory(() => throw new InvalidOperationException("driver non registrato"));

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, fabbrica);
        try
        {
            // Il driver che non si connette non blocca il chiamante: nessuna eccezione, nessun dato.
            WeatherSnapshot? snapshot = await build.Provider!.GetAsync(CancellationToken.None);
            Assert.Null(snapshot);
        }
        finally
        {
            await ((IAsyncDisposable)build.Source!).DisposeAsync();
        }
    }

    [Fact]
    public void Ascom_WithoutProgId_BuildsNothingWithReason()
    {
        var weather = new AppConfig.WeatherConfig { Source = AppConfig.WeatherConfig.Sources.Ascom, AscomProgId = "  " };

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, new FactoryTestObservingFactory(() => new FactoryTestReader()));

        Assert.Null(build.Provider);
        Assert.NotNull(build.Message);
    }

    [Fact]
    public void Ascom_WithoutFactory_BuildsNothingWithReason()
    {
        var weather = new AppConfig.WeatherConfig { Source = AppConfig.WeatherConfig.Sources.Ascom, AscomProgId = "ASCOM.Test.ObservingConditions" };

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, null);

        Assert.Null(build.Provider);
        Assert.NotNull(build.Message);
    }

    [Fact]
    public void UnknownSource_BuildsNothingWithReason()
    {
        var weather = new AppConfig.WeatherConfig { Source = "stazione-x" };

        WeatherBuild build = WeatherProviderFactory.Create(Con(weather), new FactoryTestClock(), null, null);

        Assert.Null(build.Provider);
        Assert.Contains("stazione-x", build.Message);
    }
}
