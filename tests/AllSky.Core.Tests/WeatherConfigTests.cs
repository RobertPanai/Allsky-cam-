using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class WeatherConfigTests
{
    [Fact]
    public void Defaults_AreOffAndValid()
    {
        AppConfig config = new();

        Assert.Equal(AppConfig.WeatherConfig.Sources.None, config.Weather.Source);
        Assert.Equal(1, config.Weather.Version);
        Assert.Null(config.Weather.AscomProgId);
        Assert.False(config.Weather.OpenMeteoAcknowledgedNonCommercial);
        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("weather", StringComparison.Ordinal)));
    }

    [Fact]
    public void OpenMeteoWithoutAcknowledgement_IsNotAValidationError()
    {
        // Senza conferma la sorgente non parte, ma la configurazione resta salvabile.
        var config = new AppConfig
        {
            Weather = new AppConfig.WeatherConfig { Source = AppConfig.WeatherConfig.Sources.OpenMeteo },
        };

        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("weather", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("weather.source", "xyz")]
    [InlineData("weather.source", null)]
    [InlineData("weather.ascomProgId", "ascom")]
    [InlineData("weather.readTimeoutSeconds", "0")]
    [InlineData("weather.maxAgeMinutes", "0")]
    public void InvalidValues_AreReported(string path, string value)
    {
        AppConfig.WeatherConfig weather = path switch
        {
            "weather.source" => new AppConfig.WeatherConfig { Source = value! },
            "weather.ascomProgId" => new AppConfig.WeatherConfig { Source = AppConfig.WeatherConfig.Sources.Ascom, AscomProgId = " " },
            "weather.readTimeoutSeconds" => new AppConfig.WeatherConfig { ReadTimeoutSeconds = int.Parse(value) },
            _ => new AppConfig.WeatherConfig { MaxAgeMinutes = int.Parse(value) },
        };

        var errors = new ConfigStore().Validate(new AppConfig { Weather = weather });

        Assert.Contains(errors, e => e.Path == path);
    }

    [Fact]
    public void RoundTrip_KeepsEveryWeatherField()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-weather-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new AppConfig
            {
                Weather = new AppConfig.WeatherConfig
                {
                    Source = AppConfig.WeatherConfig.Sources.Ascom,
                    AscomProgId = "ASCOM.Simulator.ObservingConditions",
                    ReadTimeoutSeconds = 8,
                    MaxAgeMinutes = 45,
                    OpenMeteoAcknowledgedNonCommercial = true,
                },
            };

            var store = new ConfigStore();
            store.Save(path, original);
            AppConfig loaded = store.Load(path);

            Assert.Equal(original.Weather, loaded.Weather);
            string text = File.ReadAllText(path);
            Assert.Contains("\"weather\"", text);
            Assert.Contains("\"ascomProgId\": \"ASCOM.Simulator.ObservingConditions\"", text);
            Assert.Contains("\"openMeteoAcknowledgedNonCommercial\": true", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyFileWithoutWeatherSection_LoadsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-weather-legacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"camera\":{\"id\":\"sim-1\"}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.Equal(AppConfig.WeatherConfig.Sources.None, loaded.Weather.Source);
            Assert.Equal(5, loaded.Weather.ReadTimeoutSeconds);
            Assert.Equal(30, loaded.Weather.MaxAgeMinutes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PartialWeatherSection_KeepsDefaultsForMissingKeys()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-weather-partial-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"weather\":{\"source\":\"openmeteo\",\"openMeteoAcknowledgedNonCommercial\":true}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.Equal(AppConfig.WeatherConfig.Sources.OpenMeteo, loaded.Weather.Source);
            Assert.True(loaded.Weather.OpenMeteoAcknowledgedNonCommercial);
            Assert.Equal(30, loaded.Weather.MaxAgeMinutes);
            Assert.Null(loaded.Weather.AscomProgId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
