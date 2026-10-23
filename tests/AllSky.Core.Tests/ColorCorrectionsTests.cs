using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

/// <summary>Correzioni temporali del colore (P14.2): default spenti, preset, validazione, round-trip e configurazioni precedenti.</summary>
public sealed class ColorCorrectionsTests : IDisposable
{
    private readonly string _dir;
    private readonly ConfigStore _store = new();

    public ColorCorrectionsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "allsky-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static AppConfig WithCorrections(ColorCorrections? corrections) =>
        new() { Color = new AppConfig.ColorConfig { Corrections = corrections } };

    [Fact]
    public void Defaults_AreAllOff_AndValid()
    {
        AppConfig config = new();

        Assert.NotNull(config.Color.Corrections);
        Assert.False(config.Color.Corrections!.TimeConstantSmoothing);
        Assert.False(config.Color.Corrections.ExposureCompensation);
        Assert.Equal(180.0, config.Color.Corrections.TauNightSeconds);
        Assert.Equal(20.0, config.Color.Corrections.TauDaySeconds);
        Assert.Equal(0.1, config.Color.Corrections.GainDbPerUnit);
        Assert.Equal("personalizzato", config.Color.Corrections.Preset);

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(config.Color.Corrections, config.Color.Saturation);
        Assert.False(r.Enabled);
        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("color.corrections", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resolve_NullSection_IsOffAndKeepsConfiguredSaturation()
    {
        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(null, 1.37);

        Assert.False(r.Enabled);
        Assert.False(r.TimeConstantSmoothing);
        Assert.False(r.ExposureCompensation);
        Assert.Equal(1.37, r.Saturation);
    }

    [Fact]
    public void Preset_Naturale_TurnsBothOnWithDefaultTaus_AndKeepsSaturation()
    {
        var c = new ColorCorrections { Preset = "naturale" };

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(c, 1.1);

        Assert.True(r.TimeConstantSmoothing);
        Assert.True(r.ExposureCompensation);
        Assert.Equal(180.0, r.TauNightSeconds);
        Assert.Equal(20.0, r.TauDaySeconds);
        Assert.Equal(1.1, r.Saturation);
    }

    [Fact]
    public void Preset_Neutro_UsesLongerTausAndSaturationOne()
    {
        var c = new ColorCorrections { Preset = "neutro" };

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(c, 1.6);

        Assert.True(r.Enabled);
        Assert.Equal(300.0, r.TauNightSeconds);
        Assert.Equal(40.0, r.TauDaySeconds);
        Assert.Equal(1.0, r.Saturation);
    }

    [Fact]
    public void Preset_Profondo_UsesSaturation125()
    {
        var c = new ColorCorrections { Preset = "profondo" };

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(c, 1.1);

        Assert.True(r.TimeConstantSmoothing);
        Assert.True(r.ExposureCompensation);
        Assert.Equal(180.0, r.TauNightSeconds);
        Assert.Equal(20.0, r.TauDaySeconds);
        Assert.Equal(1.25, r.Saturation);
    }

    [Fact]
    public void Preset_OverridesFlagsSetToOff()
    {
        var c = new ColorCorrections { Preset = "naturale", TimeConstantSmoothing = false, ExposureCompensation = false };

        Assert.True(ColorCorrectionPresets.Resolve(c, 1.1).Enabled);
    }

    [Fact]
    public void Preset_Personalizzato_UsesFieldsAsWritten()
    {
        var c = new ColorCorrections
        {
            Preset = "personalizzato",
            TimeConstantSmoothing = true,
            TauNightSeconds = 600,
            TauDaySeconds = 5,
            ExposureCompensation = false,
            GainDbPerUnit = 0.25,
        };

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(c, 1.3);

        Assert.True(r.TimeConstantSmoothing);
        Assert.False(r.ExposureCompensation);
        Assert.Equal(600.0, r.TauNightSeconds);
        Assert.Equal(5.0, r.TauDaySeconds);
        Assert.Equal(0.25, r.GainDbPerUnit);
        Assert.Equal(1.3, r.Saturation);
    }

    [Fact]
    public void Preset_Unknown_ResolvesAsCustom_AndValidationReportsIt()
    {
        var c = new ColorCorrections { Preset = "veloce", TimeConstantSmoothing = true };

        ResolvedColorCorrections r = ColorCorrectionPresets.Resolve(c, 1.1);

        Assert.True(r.TimeConstantSmoothing);
        Assert.False(r.ExposureCompensation);
        Assert.Contains(new ConfigStore().Validate(WithCorrections(c)), e => e.Path == "color.corrections.preset");
    }

    [Fact]
    public void Validate_TausAtBoundaries_AreAccepted()
    {
        var c = new ColorCorrections { TauNightSeconds = 3600, TauDaySeconds = 0.5, GainDbPerUnit = 0 };

        Assert.Empty(new ConfigStore().Validate(WithCorrections(c)));
    }

    [Theory]
    [InlineData(0.0, "color.corrections.tauNightSeconds")]
    [InlineData(-1.0, "color.corrections.tauNightSeconds")]
    [InlineData(3600.5, "color.corrections.tauNightSeconds")]
    [InlineData(double.NaN, "color.corrections.tauNightSeconds")]
    public void Validate_TauNight_OutOfRange_IsError(double tau, string path)
    {
        var c = new ColorCorrections { TauNightSeconds = tau };

        Assert.Contains(new ConfigStore().Validate(WithCorrections(c)), e => e.Path == path);
    }

    [Theory]
    [InlineData(0.0, "color.corrections.tauDaySeconds")]
    [InlineData(4000.0, "color.corrections.tauDaySeconds")]
    [InlineData(double.NaN, "color.corrections.tauDaySeconds")]
    public void Validate_TauDay_OutOfRange_IsError(double tau, string path)
    {
        var c = new ColorCorrections { TauDaySeconds = tau };

        Assert.Contains(new ConfigStore().Validate(WithCorrections(c)), e => e.Path == path);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Validate_GainDbPerUnit_OutOfRange_IsError(double db)
    {
        var c = new ColorCorrections { GainDbPerUnit = db };

        Assert.Contains(new ConfigStore().Validate(WithCorrections(c)), e => e.Path == "color.corrections.gainDbPerUnit");
    }

    [Fact]
    public void Validate_ErrorMessages_AreInItalian()
    {
        var c = new ColorCorrections { TauNightSeconds = -5, Preset = "x" };

        IReadOnlyList<ValidationError> errors = new ConfigStore().Validate(WithCorrections(c));

        Assert.All(errors.Where(e => e.Path.StartsWith("color.corrections", StringComparison.Ordinal)), e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
        Assert.Contains(errors, e => e.Message.Contains("costante di tempo di notte", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Message.Contains("Preset sconosciuto", StringComparison.Ordinal));
    }

    [Fact]
    public void RoundTrip_KeepsEveryCorrectionField_InCamelCase()
    {
        string path = Path.Combine(_dir, "config.json");
        var original = new AppConfig
        {
            Color = new AppConfig.ColorConfig
            {
                Corrections = new ColorCorrections
                {
                    TimeConstantSmoothing = true,
                    TauNightSeconds = 300,
                    TauDaySeconds = 40,
                    ExposureCompensation = true,
                    GainDbPerUnit = 0.2,
                    Preset = "personalizzato",
                },
            },
        };

        _store.Save(path, original);
        string json = File.ReadAllText(path);
        AppConfig loaded = _store.Load(path);

        Assert.True(json.Contains("\"timeConstantSmoothing\": true", StringComparison.Ordinal), "\"timeConstantSmoothing\": true");
        Assert.True(json.Contains("\"tauNightSeconds\": 300", StringComparison.Ordinal), "\"tauNightSeconds\": 300");
        Assert.True(json.Contains("\"exposureCompensation\": true", StringComparison.Ordinal), "\"exposureCompensation\": true");
        Assert.True(json.Contains("\"preset\": \"personalizzato\"", StringComparison.Ordinal), "\"preset\": \"personalizzato\"");
        Assert.Equal(original.Color.Corrections, loaded.Color.Corrections);
    }

    [Fact]
    public void OldConfig_WithoutCorrectionsSection_LoadsAsAllOff()
    {
        string path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "color": { "smoothing": 0.85, "saturation": 1.1 } }""");

        AppConfig loaded = _store.Load(path);

        Assert.False(File.Exists(path + ".bad"));
        Assert.Equal(0.85, loaded.Color.Smoothing);
        Assert.False(ColorCorrectionPresets.Resolve(loaded.Color.Corrections, loaded.Color.Saturation).Enabled);
        Assert.Empty(_store.Validate(loaded).Where(e => e.Path.StartsWith("color.corrections", StringComparison.Ordinal)));
    }

    [Fact]
    public void ExplicitNullCorrections_LoadsAsAllOff()
    {
        string path = Path.Combine(_dir, "null.json");
        File.WriteAllText(path, """{ "color": { "corrections": null } }""");

        AppConfig loaded = _store.Load(path);

        Assert.Null(loaded.Color.Corrections);
        Assert.False(ColorCorrectionPresets.Resolve(loaded.Color.Corrections, loaded.Color.Saturation).Enabled);
        Assert.Empty(_store.Validate(loaded).Where(e => e.Path.StartsWith("color.corrections", StringComparison.Ordinal)));
    }
}
