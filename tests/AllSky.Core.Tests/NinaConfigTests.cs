using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class NinaConfigTests
{
    [Fact]
    public void Defaults_AreOffLoopbackApi()
    {
        AppConfig config = new();

        Assert.False(config.Nina.Enabled);
        Assert.Equal(1, config.Nina.Version);
        Assert.Equal("127.0.0.1", config.Nina.Host);
        Assert.Equal(1888, config.Nina.Port);
        Assert.Equal(5, config.Nina.ConnectTimeoutSeconds);
        Assert.Equal(60, config.Nina.ReconnectMaxBackoffSeconds);
        Assert.Equal(AppConfig.NinaConfig.Sources.Api, config.Nina.Source);
        Assert.Null(config.Nina.LogFolder);
        Assert.Equal(0, config.Nina.ClockOffsetSeconds);
        Assert.Empty(new ConfigStore().Validate(config).Where(e => e.Path.StartsWith("nina", StringComparison.Ordinal)));
    }

    [Fact]
    public void RoundTrip_KeepsEveryNinaField()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-nina-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new AppConfig
            {
                Nina = new AppConfig.NinaConfig
                {
                    Enabled = true,
                    Host = "localhost",
                    Port = 1999,
                    ConnectTimeoutSeconds = 9,
                    ReconnectMaxBackoffSeconds = 120,
                    Source = AppConfig.NinaConfig.Sources.Log,
                    LogFolder = @"D:\NINA\logs",
                    ClockOffsetSeconds = -12.5,
                },
            };

            var store = new ConfigStore();
            store.Save(path, original);
            AppConfig loaded = store.Load(path);

            Assert.Equal(original.Nina, loaded.Nina);
            string text = File.ReadAllText(path);
            Assert.Contains("\"nina\"", text);
            Assert.Contains("\"connectTimeoutSeconds\": 9", text);
            Assert.Contains("\"clockOffsetSeconds\": -12.5", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyFileWithoutNinaSection_LoadsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-nina-legacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"camera\":{\"id\":\"sim-1\"}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.False(loaded.Nina.Enabled);
            Assert.Equal("127.0.0.1", loaded.Nina.Host);
            Assert.Equal(1888, loaded.Nina.Port);
            Assert.Equal(AppConfig.NinaConfig.Sources.Api, loaded.Nina.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PartialNinaSection_KeepsDefaultsForMissingKeys()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-nina-partial-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"nina\":{\"enabled\":true,\"port\":2000}}");

            AppConfig loaded = new ConfigStore().Load(path);

            Assert.True(loaded.Nina.Enabled);
            Assert.Equal(2000, loaded.Nina.Port);
            Assert.Equal(5, loaded.Nina.ConnectTimeoutSeconds);
            Assert.Equal(0, loaded.Nina.ClockOffsetSeconds);
            Assert.Null(loaded.Nina.LogFolder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledWithNonLoopbackHost_IsValidationError()
    {
        var config = new AppConfig { Nina = new AppConfig.NinaConfig { Enabled = true, Host = "192.168.1.10" } };

        var errors = new ConfigStore().Validate(config);

        Assert.Contains(errors, e => e.Path == "nina.host" && e.Message.Contains("127.0.0.1"));
    }

    [Fact]
    public void PortZero_IsErrorWhenEnabled_AndOnlyWarningWhenDisabled()
    {
        var store = new ConfigStore();

        var enabled = store.Validate(new AppConfig { Nina = new AppConfig.NinaConfig { Enabled = true, Port = 0 } });
        Assert.Contains(enabled, e => e.Path == "nina.port");

        var disabled = new AppConfig { Nina = new AppConfig.NinaConfig { Enabled = false, Port = 0 } };
        Assert.Empty(store.Validate(disabled).Where(e => e.Path.StartsWith("nina", StringComparison.Ordinal)));
        Assert.Contains(store.ValidateNinaWarnings(disabled), w => w.Path == "nina.port");
    }

    [Fact]
    public void UnknownSource_IsValidationError()
    {
        var config = new AppConfig { Nina = new AppConfig.NinaConfig { Enabled = true, Source = "xml" } };

        Assert.Contains(new ConfigStore().Validate(config), e => e.Path == "nina.source");
    }

    [Fact]
    public void ClockOffsetOutOfRange_IsValidationError()
    {
        var config = new AppConfig { Nina = new AppConfig.NinaConfig { Enabled = true, ClockOffsetSeconds = 1000 } };

        Assert.Contains(new ConfigStore().Validate(config), e => e.Path == "nina.clockOffsetSeconds");
    }

    [Fact]
    public void UnsupportedVersion_IsValidationErrorLikeOtherSections()
    {
        var config = new AppConfig { Nina = new AppConfig.NinaConfig { Version = 99 } };

        Assert.Contains(new ConfigStore().Validate(config), e => e.Path == "nina.version");
    }
}
