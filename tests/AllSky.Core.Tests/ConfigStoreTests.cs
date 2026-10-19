using System.Text.Json;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly ConfigStore _store = new();

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "allsky-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FileIn(string name) => Path.Combine(_dir, name);

    private static string Json(AppConfig config) =>
        JsonSerializer.Serialize(config, ConfigStore.SerializerOptions);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new AppConfig
        {
            Language = Language.En,
            Site = new AppConfig.SiteConfig
            {
                Name = "Test",
                Latitude = -33.9,
                Longitude = 151.2,
                ElevationM = 12.5,
                Timezone = "Australia/Sydney",
            },
            Camera = new AppConfig.CameraConfig
            {
                Id = "ascom-1",
                Gain = 120,
                MaxExposureMs = 20000,
                DayExposureMs = 5,
                Binning = 2,
            },
            Color = new AppConfig.ColorConfig
            {
                WhiteBalance = WhiteBalanceMode.Manual,
                ManualGains = [1.1, 0.9, 1.3],
                Smoothing = 0.5,
                GradientRemoval = 0.2,
                Stretch = Stretch.Asinh,
                Saturation = 1.4,
            },
            Mask = new AppConfig.MaskConfig { Enabled = true, ImagePath = @"C:\mask.png" },
            Overlay = new AppConfig.OverlayConfig
            {
                Enabled = false,
                ShowTime = false,
                ShowSensorTemp = false,
                Label = "Cupola nord",
            },
            Capture = new AppConfig.CaptureConfig { IntervalSeconds = 45, DayCapture = false },
            Products = new AppConfig.ProductsConfig
            {
                Timelapse = false,
                Keogram = false,
                Startrails = false,
                TimelapseFps = 30,
            },
            Storage = new AppConfig.StorageConfig { Folder = @"D:\AllSky", KeepNights = 7 },
            Ui = new AppConfig.UiConfig { Theme = Theme.Red, Port = 6000, AllowLan = true },
        };

        string path = FileIn("config.json");
        _store.Save(path, original);
        AppConfig loaded = _store.Load(path);

        Assert.Equal(Json(original), Json(loaded));
        Assert.Equal(original.Color.ManualGains, loaded.Color.ManualGains);
        Assert.Equal(Language.En, loaded.Language);
        Assert.Equal(Theme.Red, loaded.Ui.Theme);
        Assert.Equal(@"C:\mask.png", loaded.Mask.ImagePath);
    }

    [Fact]
    public void Save_WritesCamelCaseKeysAndStringEnums()
    {
        string path = FileIn("config.json");

        _store.Save(path, new AppConfig());

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("it", root.GetProperty("language").GetString());
        Assert.True(root.GetProperty("site").TryGetProperty("elevationM", out _));
        Assert.Equal("auto", root.GetProperty("color").GetProperty("whiteBalance").GetString());
        Assert.Equal("mtf", root.GetProperty("color").GetProperty("stretch").GetString());
        Assert.Equal("auto", root.GetProperty("ui").GetProperty("theme").GetString());
        Assert.Equal(5810, root.GetProperty("ui").GetProperty("port").GetInt32());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutCreatingFile()
    {
        string path = FileIn("does-not-exist.json");

        AppConfig loaded = _store.Load(path);

        Assert.Equal(Json(new AppConfig()), Json(loaded));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Load_CorruptFile_RenamesToBadAndReturnsDefaults()
    {
        string path = FileIn("config.json");
        File.WriteAllText(path, "{ \"site\": { \"latitude\": ");

        AppConfig loaded = _store.Load(path);

        Assert.Equal(Json(new AppConfig()), Json(loaded));
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{ non e' json")]
    [InlineData("{\"ui\":{\"port\":\"abc\"}}")]
    public void Load_InvalidContent_RenamesToBadAndReturnsDefaults(string content)
    {
        string path = FileIn("config.json");
        File.WriteAllText(path, content);

        AppConfig loaded = _store.Load(path);

        Assert.Equal(Json(new AppConfig()), Json(loaded));
        Assert.True(File.Exists(path + ".bad"));
    }
}
