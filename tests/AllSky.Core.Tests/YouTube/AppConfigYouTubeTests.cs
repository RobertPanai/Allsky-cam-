using System.IO;
using AllSky.Core;
using Xunit;

namespace AllSky.Core.Tests.YouTube;

public sealed class AppConfigYouTubeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-yt-cfg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Predefinita_CaricamentoSpento()
    {
        var config = new AppConfig();
        Assert.False(config.YouTube.Enabled);
        Assert.Null(config.YouTube.ClientId);
        Assert.Equal(1, config.YouTube.Version);
        Assert.Equal("private", config.YouTube.Privacy);
    }

    [Fact]
    public void ConfigurazionePrecedente_SenzaSezioneYouTube_SiLeggeConDefault()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "config.json");
        File.WriteAllText(path, "{ \"schemaVersion\": 1, \"site\": { \"name\": \"Test\" } }");

        AppConfig loaded = new ConfigStore().Load(path);

        Assert.Equal("Test", loaded.Site.Name);
        Assert.False(loaded.YouTube.Enabled);
        Assert.Equal("12:30", loaded.YouTube.DailyTime);
    }

    [Fact]
    public void SalvataggioERilettura_ConservaLaSezione()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "config.json");
        var store = new ConfigStore();
        var config = new AppConfig { YouTube = new AppConfig.YouTubeConfig { Enabled = true, ClientId = "cid", Tags = ["x", "y"] } };

        store.Save(path, config);
        AppConfig loaded = store.Load(path);

        Assert.True(loaded.YouTube.Enabled);
        Assert.Equal("cid", loaded.YouTube.ClientId);
        Assert.Equal(["x", "y"], loaded.YouTube.Tags);
    }

    [Fact]
    public void FileSenzaSegreti_NessunCampoSegreto()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "config.json");
        new ConfigStore().Save(path, new AppConfig());

        string json = File.ReadAllText(path).ToLowerInvariant();
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("refresh", json);
    }
}
