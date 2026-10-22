using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class MonitorConfigTests
{
    private static readonly string[] VoiceNames =
    [
        "AllSkyImage", "NightPhase", "SunMoonAltitude", "NinaTarget", "NinaParkState",
        "TargetAltitudeChart", "MoonDistance", "LatestTimelapse", "Keogram", "StartTrails",
    ];

    [Fact]
    public void Defaults_AreOffAndAllVoicesOff()
    {
        AppConfig config = new();

        Assert.False(config.Monitor.Enabled);
        Assert.Equal(1, config.Monitor.Version);
        Assert.False(config.Monitor.Voices.AnyEnabled());
        foreach (string name in VoiceNames)
        {
            Assert.False((bool)typeof(AppConfig.MonitorConfig.MonitorVoices).GetProperty(name)!.GetValue(config.Monitor.Voices)!, name);
        }

        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("monitor", StringComparison.Ordinal)));
        Assert.Empty(new ConfigStore().ValidateMonitorWarnings(config));
    }

    [Fact]
    public void Voices_AreExactlyTheClosedList()
    {
        string[] actual = typeof(AppConfig.MonitorConfig.MonitorVoices)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(VoiceNames.OrderBy(n => n, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void RoundTrip_KeepsEveryMonitorField_InCamelCase()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-monitor-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new AppConfig
            {
                Monitor = new AppConfig.MonitorConfig
                {
                    Enabled = true,
                    Voices = new AppConfig.MonitorConfig.MonitorVoices
                    {
                        AllSkyImage = true,
                        NightPhase = true,
                        SunMoonAltitude = true,
                        NinaTarget = true,
                        NinaParkState = true,
                        TargetAltitudeChart = true,
                        MoonDistance = true,
                        LatestTimelapse = true,
                        Keogram = true,
                        StartTrails = true,
                    },
                },
            };

            var store = new ConfigStore();
            store.Save(path, original);
            AppConfig loaded = store.Load(path);

            Assert.Equal(original.Monitor, loaded.Monitor);
            string text = File.ReadAllText(path);
            Assert.Contains("\"monitor\"", text);
            Assert.Contains("\"sunMoonAltitude\": true", text);
            Assert.Contains("\"startTrails\": true", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyFileWithoutMonitorSection_LoadsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-monitor-legacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"camera\":{\"id\":\"sim-1\"}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.False(loaded.Monitor.Enabled);
            Assert.False(loaded.Monitor.Voices.AnyEnabled());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledWithoutVoices_IsWarningNotError()
    {
        var config = new AppConfig { Monitor = new AppConfig.MonitorConfig { Enabled = true } };
        var store = new ConfigStore();

        Assert.Empty(store.Validate(config).Where(e => e.Path.StartsWith("monitor", StringComparison.Ordinal)));
        Assert.Contains(store.ValidateMonitorWarnings(config), w => w.Path == "monitor.voices");
    }

    [Fact]
    public void EnabledWithOneVoice_HasNoWarning()
    {
        var config = new AppConfig
        {
            Monitor = new AppConfig.MonitorConfig
            {
                Enabled = true,
                Voices = new AppConfig.MonitorConfig.MonitorVoices { Keogram = true },
            },
        };

        Assert.Empty(new ConfigStore().ValidateMonitorWarnings(config));
    }

    [Fact]
    public void DisabledWithoutVoices_HasNoWarning()
    {
        var config = new AppConfig { Monitor = new AppConfig.MonitorConfig { Enabled = false } };

        Assert.Empty(new ConfigStore().ValidateMonitorWarnings(config));
    }

    [Fact]
    public void UnsupportedVersion_IsValidationError()
    {
        var config = new AppConfig { Monitor = new AppConfig.MonitorConfig { Version = 99 } };

        Assert.Contains(new ConfigStore().Validate(config), e => e.Path == "monitor.version");
    }
}
