using System.Text.Json;
using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests;

/// <summary>Sotto-oggetto <c>overlay</c>: opzioni della maschera informativa e retrocompatibilita' del JSON.</summary>
public class OverlayConfigTests
{
    [Fact]
    public void Defaults_MascheraSpenta_SuTuttiIPercorsi_ELayoutAssente()
    {
        var overlay = new AppConfig().Overlay;

        Assert.Equal(1, overlay.Version);
        Assert.False(overlay.ApplyToTimelapse);
        Assert.False(overlay.ApplyToLive);
        Assert.False(overlay.ApplyToPublish);
        Assert.Null(overlay.Layout);
    }

    [Fact]
    public void ConfigSenzaSezioneOverlay_LeggeIPredefiniti()
    {
        AppConfig config = Deserialize("""{ "schemaVersion": 1, "language": "it" }""");

        Assert.False(config.Overlay.ApplyToTimelapse);
        Assert.False(config.Overlay.ApplyToPublish);
        Assert.Null(config.Overlay.Layout);
        Assert.True(config.Overlay.Enabled);
    }

    [Fact]
    public void VecchiaSezioneOverlay_SenzaNuoviCampi_MantieneLeVecchieProprieta()
    {
        AppConfig config = Deserialize("""
            { "overlay": { "enabled": false, "showTime": true, "showSensorTemp": false, "label": "Osservatorio" } }
            """);

        Assert.False(config.Overlay.Enabled);
        Assert.False(config.Overlay.ShowSensorTemp);
        Assert.Equal("Osservatorio", config.Overlay.Label);
        Assert.False(config.Overlay.ApplyToTimelapse);
        Assert.Equal(AppConfig.OverlayConfig.CurrentVersion, config.Overlay.Version);
    }

    [Fact]
    public void RoundTrip_ConFlagsELayout_PreservaIValori()
    {
        using JsonDocument layout = JsonDocument.Parse("""{ "versione": 1, "widget": [] }""");
        var original = new AppConfig
        {
            Overlay = new AppConfig.OverlayConfig
            {
                ApplyToTimelapse = true,
                ApplyToLive = false,
                ApplyToPublish = false,
                Layout = layout.RootElement.Clone(),
            },
        };

        string json = JsonSerializer.Serialize(original, ConfigStore.SerializerOptions);
        AppConfig read = Deserialize(json);

        Assert.True(read.Overlay.ApplyToTimelapse);
        Assert.False(read.Overlay.ApplyToPublish);
        Assert.NotNull(read.Overlay.Layout);
        Assert.Equal(1, read.Overlay.Layout!.Value.GetProperty("versione").GetInt32());
    }

    [Fact]
    public void Serializzazione_UsaLeChiaviCamelCase_eNonScriveIlLayoutSeAssente()
    {
        string json = JsonSerializer.Serialize(new AppConfig(), ConfigStore.SerializerOptions);

        Assert.Contains("\"applyToTimelapse\": false", json);
        Assert.Contains("\"applyToLive\": false", json);
        Assert.Contains("\"applyToPublish\": false", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"layout\": null", json);
    }

    private static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.SerializerOptions)!;
}
