using AllSky.Core;
using Xunit;

namespace AllSky.Mcp.Tests;

/// <summary>Sezione <c>mcp</c> di AppConfig: default spenti, validazione e salvataggio.</summary>
public sealed class McpConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-mcp-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Predefinito_EMcpSpentoSullaPorta8765()
    {
        var config = new AppConfig();

        Assert.False(config.Mcp.Enabled);
        Assert.Equal(8765, config.Mcp.Port);
        Assert.Equal(AppConfig.McpConfig.CurrentVersion, config.Mcp.Version);
    }

    [Fact]
    public void FileSenzaSezioneMcp_LeggeSpento()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "config.json");
        File.WriteAllText(path, """{"schemaVersion":1,"ui":{"port":5810}}""");

        AppConfig config = new ConfigStore().Load(path);

        Assert.False(config.Mcp.Enabled);
        Assert.Equal(8765, config.Mcp.Port);
    }

    [Fact]
    public void SalvataggioERilettura_ConservanoLaSezione()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "config.json");
        var store = new ConfigStore();
        AppConfig original = new() { Mcp = new AppConfig.McpConfig { Enabled = true, Port = 9123 } };

        store.Save(path, original);
        AppConfig loaded = store.Load(path);

        Assert.True(loaded.Mcp.Enabled);
        Assert.Equal(9123, loaded.Mcp.Port);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(70000)]
    public void PortaFuoriIntervallo_EErroreAncheConServerSpento(int port)
    {
        var errors = new ConfigStore().Validate(new AppConfig { Mcp = new AppConfig.McpConfig { Enabled = false, Port = port } });

        Assert.Contains(errors, e => e.Path == "mcp.port");
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(65535)]
    public void PortaNeiLimiti_EAccettata(int port)
    {
        var errors = new ConfigStore().Validate(new AppConfig { Mcp = new AppConfig.McpConfig { Enabled = true, Port = port } });

        Assert.DoesNotContain(errors, e => e.Path.StartsWith("mcp", StringComparison.Ordinal));
    }

    [Fact]
    public void VersioneSconosciuta_EErrore()
    {
        var errors = new ConfigStore().Validate(new AppConfig { Mcp = new AppConfig.McpConfig { Version = 99 } });

        Assert.Contains(errors, e => e.Path == "mcp.version");
    }
}
