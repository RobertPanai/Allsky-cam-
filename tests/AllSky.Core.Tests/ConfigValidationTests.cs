using Xunit;

namespace AllSky.Core.Tests;

public sealed class ConfigValidationTests
{
    private readonly ConfigStore _store = new();

    /// <summary>
    /// Verifica una singola regola: la configurazione di partenza è valida per ogni regola,
    /// quindi se la regola è violata l'unico errore atteso è quello sul percorso indicato.
    /// </summary>
    private void AssertRule(AppConfig config, string path, bool valid)
    {
        IReadOnlyList<ValidationError> errors = _store.Validate(config);

        if (valid)
        {
            Assert.Empty(errors);
        }
        else
        {
            ValidationError error = Assert.Single(errors);
            Assert.Equal(path, error.Path);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }

    [Fact]
    public void Validate_DefaultConfig_HasNoErrors()
    {
        Assert.Empty(_store.Validate(new AppConfig()));
    }

    [Theory]
    [InlineData(-90.0, true)]
    [InlineData(0.0, true)]
    [InlineData(90.0, true)]
    [InlineData(-90.001, false)]
    [InlineData(90.001, false)]
    [InlineData(double.NaN, false)]
    public void Validate_Latitude(double value, bool valid)
    {
        var config = new AppConfig { Site = new AppConfig.SiteConfig { Latitude = value } };

        AssertRule(config, "site.latitude", valid);
    }

    [Theory]
    [InlineData(-180.0, true)]
    [InlineData(0.0, true)]
    [InlineData(180.0, true)]
    [InlineData(-180.001, false)]
    [InlineData(180.001, false)]
    [InlineData(double.NaN, false)]
    public void Validate_Longitude(double value, bool valid)
    {
        var config = new AppConfig { Site = new AppConfig.SiteConfig { Longitude = value } };

        AssertRule(config, "site.longitude", valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    public void Validate_CaptureIntervalSeconds(int value, bool valid)
    {
        var config = new AppConfig { Capture = new AppConfig.CaptureConfig { IntervalSeconds = value } };

        AssertRule(config, "capture.intervalSeconds", valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    public void Validate_StorageKeepNights(int value, bool valid)
    {
        var config = new AppConfig { Storage = new AppConfig.StorageConfig { KeepNights = value } };

        AssertRule(config, "storage.keepNights", valid);
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(5810, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(0, false)]
    public void Validate_UiPort(int value, bool valid)
    {
        var config = new AppConfig { Ui = new AppConfig.UiConfig { Port = value } };

        AssertRule(config, "ui.port", valid);
    }

    [Theory]
    [InlineData(-0.001, false)]
    [InlineData(0.0, true)]
    [InlineData(0.85, true)]
    [InlineData(1.0, true)]
    [InlineData(1.001, false)]
    [InlineData(double.NaN, false)]
    public void Validate_ColorSmoothing(double value, bool valid)
    {
        var config = new AppConfig { Color = new AppConfig.ColorConfig { Smoothing = value } };

        AssertRule(config, "color.smoothing", valid);
    }
}
