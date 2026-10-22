using System.Text.Json;
using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class SatellitesConfigTests
{
    [Fact]
    public void Defaults_AreOffAndTrackOnlyTheIss()
    {
        AppConfig config = new();

        Assert.False(config.Satellites.Enabled);
        Assert.Equal(1, config.Satellites.Version);
        Assert.Equal(25544, Assert.Single(config.Satellites.Targets).NoradId);
        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("satellites", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("satellites.windowHours")]
    [InlineData("satellites.minElevationDeg")]
    [InlineData("satellites.sunMaxAltDeg")]
    public void InvalidScalars_AreReported(string path)
    {
        AppConfig.SatellitesConfig bad = path switch
        {
            "satellites.windowHours" => new AppConfig.SatellitesConfig { WindowHours = 0 },
            "satellites.minElevationDeg" => new AppConfig.SatellitesConfig { MinElevationDeg = 90 },
            _ => new AppConfig.SatellitesConfig { SunMaxAltDeg = 5 },
        };

        var errors = new ConfigStore().Validate(new AppConfig { Satellites = bad });

        Assert.Contains(errors, e => e.Path == path);
    }

    [Fact]
    public void NoradOutOfRange_AndDuplicates_AreReported()
    {
        var targets = new[]
        {
            new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = 123456, Name = "troppo" },
            new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = 25544, Name = "ISS" },
            new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = 25544, Name = "ISS bis" },
        };

        var errors = new ConfigStore().Validate(new AppConfig { Satellites = new AppConfig.SatellitesConfig { Targets = targets } });

        Assert.Equal(2, errors.Count(e => e.Path == "satellites.targets"));
    }

    [Fact]
    public void LegacyFileWithoutSection_LoadsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-sat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"camera\":{\"id\":\"sim-1\"}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.False(loaded.Satellites.Enabled);
            Assert.Equal(25544, Assert.Single(loaded.Satellites.Targets).NoradId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledSection_RoundTripsThroughJson()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-sat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var config = new AppConfig
            {
                Satellites = new AppConfig.SatellitesConfig
                {
                    Enabled = true,
                    WindowHours = 12,
                    Targets = [new AppConfig.SatellitesConfig.SatelliteTargetConfig { NoradId = 48274, Name = "CSS" }],
                },
            };
            var store = new ConfigStore();
            store.Save(path, config);

            AppConfig loaded = store.Load(path);

            Assert.True(loaded.Satellites.Enabled);
            Assert.Equal(12, loaded.Satellites.WindowHours);
            Assert.Equal(48274, Assert.Single(loaded.Satellites.Targets).NoradId);
            Assert.Contains("\"satellites\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
