using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Sky;
using Xunit;

namespace AllSky.Core.Tests;

// Persistenza della calibrazione accettata in config.json: round-trip, retrocompatibilita' e versioni sconosciute (P3.3).
public class OpticalCalibrationConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-optics-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset At = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string PathFor(string name)
    {
        Directory.CreateDirectory(_folder);
        return Path.Combine(_folder, name);
    }

    private static FisheyeModel Model() => new(640.5, 479.25, 310.0, 12.5, Mirrored: true, K2: 0.02);

    [Fact]
    public void FromModel_ThenToModel_RestoresTheSameGeometry()
    {
        OpticalCalibrationConfig optics = OpticalCalibrationConfig.FromModel(
            Model(), 1280, 960, OpticalCalibrationSource.Sun, 0.42, At);

        FisheyeModel? back = optics.ToModel();

        Assert.NotNull(back);
        Assert.Equal(Model(), back);
        Assert.Equal(1280, optics.FrameWidthPx);
        Assert.Equal(OpticalCalibrationSource.Sun, optics.SourceKind);
    }

    [Fact]
    public void RoundTrip_ThroughConfigStore_KeepsOptics()
    {
        string path = PathFor("config.json");
        var store = new ConfigStore();
        OpticalCalibrationConfig optics = OpticalCalibrationConfig.FromModel(
            Model(), 1280, 960, OpticalCalibrationSource.Manual, 0.0, At);
        var config = new AppConfig { Optics = optics };

        store.Save(path, config);
        AppConfig loaded = store.Load(path);

        Assert.Equal(optics, loaded.Optics);
        Assert.Equal(Model(), loaded.Optics!.ToModel());
        Assert.Equal(OpticalCalibrationSource.Manual, loaded.Optics.SourceKind);
    }

    [Fact]
    public void ConfigWithoutOpticsProperty_LoadsAsNotCalibrated()
    {
        // Configurazione precedente a P3.3: nessuna proprieta' "optics".
        string path = PathFor("config.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "site": { "name": "Test", "latitude": 45.0, "longitude": 7.0 } }""");

        AppConfig loaded = new ConfigStore().Load(path);

        Assert.Null(loaded.Optics);
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void UnknownFieldsInOptics_AreIgnored()
    {
        string path = PathFor("config.json");
        File.WriteAllText(path, """
            { "optics": { "version": 1, "centerX": 10, "centerY": 20, "scaleRadiusPx": 100,
                          "northRotationDeg": 0, "mirrored": false, "k2": 0,
                          "source": "sun", "futureField": [1, 2, 3] } }
            """);

        AppConfig loaded = new ConfigStore().Load(path);

        Assert.NotNull(loaded.Optics);
        Assert.NotNull(loaded.Optics!.ToModel());
    }

    [Fact]
    public void FutureVersion_YieldsNoModel()
    {
        var optics = new OpticalCalibrationConfig
        {
            Version = 2,
            ScaleRadiusPx = 100,
        };

        Assert.Null(optics.ToModel());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    public void NonPositiveOrNonFiniteRadius_YieldsNoModel(double radius)
    {
        var optics = new OpticalCalibrationConfig { ScaleRadiusPx = radius };

        Assert.Null(optics.ToModel());
    }

    [Fact]
    public void K2AtOrBelowLimit_YieldsNoModel()
    {
        var optics = new OpticalCalibrationConfig { ScaleRadiusPx = 100, K2 = -0.5 };

        Assert.Null(optics.ToModel());
    }

    [Fact]
    public void UnknownSource_IsReadAsSun()
    {
        var optics = new OpticalCalibrationConfig { ScaleRadiusPx = 100, Source = "stellar" };

        Assert.Equal(OpticalCalibrationSource.Sun, optics.SourceKind);
    }

    [Fact]
    public void FromModel_NonFiniteRms_IsStoredAsZero()
    {
        OpticalCalibrationConfig optics = OpticalCalibrationConfig.FromModel(
            Model(), 0, 0, OpticalCalibrationSource.Manual, double.NaN, At);

        Assert.Equal(0.0, optics.RmsPx);
        Assert.Equal("manual", optics.Source);
    }
}
