using System.Text.Json;
using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class DetectionConfigTests
{
    [Fact]
    public void Defaults_AreOffAndValid()
    {
        AppConfig config = new();

        Assert.False(config.Detection.Enabled);
        Assert.False(config.Detection.SaveAllVisiblePasses);
        Assert.Equal(1, config.Detection.Version);
        Assert.Equal(960, config.Detection.AnalysisLongSidePx);
        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("detection", StringComparison.Ordinal)));
    }

    [Fact]
    public void ConfigWithoutDetectionSection_ReadsDefaults()
    {
        // Un file scritto prima di P5.5 non ha la sezione: deve leggersi con il rilevamento spento.
        string path = Path.Combine(Path.GetTempPath(), "allsky-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new AppConfig { Camera = new AppConfig.CameraConfig { Id = "sim-1" } }, ConfigStore.SerializerOptions)
                .Replace("\"detection\"", "\"noDetection\"", StringComparison.Ordinal));

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.False(loaded.Detection.Enabled);
            Assert.Equal("sim-1", loaded.Camera.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SectionRoundTrips()
    {
        var config = new AppConfig
        {
            Detection = new AppConfig.DetectionConfig
            {
                Enabled = true,
                SaveAllVisiblePasses = true,
                ClipFps = 30,
                Streak = new AppConfig.DetectionConfig.StreakSettings { TrackDistancePx = 15 },
                Transit = new AppConfig.DetectionConfig.TransitSettings { ScoreThreshold = 0.7 },
            },
        };

        string json = JsonSerializer.Serialize(config, ConfigStore.SerializerOptions);
        AppConfig back = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.SerializerOptions)!;

        Assert.True(back.Detection.Enabled);
        Assert.True(back.Detection.SaveAllVisiblePasses);
        Assert.Equal(30, back.Detection.ClipFps);
        Assert.Equal(15, back.Detection.Streak.TrackDistancePx);
        Assert.Equal(0.7, back.Detection.Transit.ScoreThreshold);
    }

    [Theory]
    [InlineData("detection.analysisLongSidePx")]
    [InlineData("detection.clipFps")]
    [InlineData("detection.streak.thresholdSigma")]
    [InlineData("detection.streak.maxChangedFraction")]
    [InlineData("detection.streak.maxFrameGapSeconds")]
    [InlineData("detection.transit.scoreThreshold")]
    [InlineData("detection.version")]
    public void OutOfRangeValues_AreReported(string path)
    {
        AppConfig.DetectionConfig bad = path switch
        {
            "detection.analysisLongSidePx" => new AppConfig.DetectionConfig { AnalysisLongSidePx = 100 },
            "detection.clipFps" => new AppConfig.DetectionConfig { ClipFps = 0 },
            "detection.streak.thresholdSigma" => new AppConfig.DetectionConfig { Streak = new AppConfig.DetectionConfig.StreakSettings { ThresholdSigma = -1 } },
            "detection.streak.maxChangedFraction" => new AppConfig.DetectionConfig { Streak = new AppConfig.DetectionConfig.StreakSettings { MaxChangedFraction = 0 } },
            "detection.streak.maxFrameGapSeconds" => new AppConfig.DetectionConfig { Streak = new AppConfig.DetectionConfig.StreakSettings { MaxFrameGapSeconds = 0 } },
            "detection.transit.scoreThreshold" => new AppConfig.DetectionConfig { Transit = new AppConfig.DetectionConfig.TransitSettings { ScoreThreshold = 1.5 } },
            _ => new AppConfig.DetectionConfig { Version = 99 },
        };

        var errors = new ConfigStore().Validate(new AppConfig { Detection = bad });

        Assert.Contains(errors, e => e.Path == path);
    }

    [Fact]
    public void ValidDetectionValues_AreAccepted()
    {
        var config = new AppConfig
        {
            Detection = new AppConfig.DetectionConfig
            {
                Enabled = true,
                AnalysisLongSidePx = 1280,
                ClipFps = 60,
                Streak = new AppConfig.DetectionConfig.StreakSettings { MaxChangedFraction = 1.0, BolideLengthPx = 500 },
                Transit = new AppConfig.DetectionConfig.TransitSettings { ScoreThreshold = 0.5, SampleSpacingPx = 0.5 },
            },
        };

        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("detection", StringComparison.Ordinal)));
    }
}
